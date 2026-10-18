using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Mapper;

public sealed partial class TriconexAddressTranslator
{
    private const string ReadAliasSuffix = "R";
    private const string ReadWriteAliasSuffix = "W";

    [GeneratedRegex(@"\A\s*(?<type>MB|MI|MR)(?<alias>[NRW])\s*\.\s*(?<index>\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

public string? Translate(string? triconexAddress, out string? note) =>
        Translate(triconexAddress, null, out note, out _);

    /// <summary>
    /// Maps a TriStation memory address to a Siemens absolute address. The Siemens memory area is
    /// chosen from the tag's data type, not from the TriStation register mnemonic: a 32-bit tag must
    /// sit at a double-word address (<c>%MD</c>), and TIA rejects a 32-bit type on a word address
    /// ("the entered address does not match the data type"). <paramref name="widthAdjusted"/> reports
    /// a source where the register width and the data type disagree.
    /// </summary>
    public string? Translate(string? triconexAddress, string? dataType, out string? note, out bool widthAdjusted)
    {
        note = null;
        widthAdjusted = false;
        if (string.IsNullOrWhiteSpace(triconexAddress)) return null;
        var match = AddressPattern().Match(triconexAddress);
        if (!match.Success)
        {
            note = $"Unrecognised TriStation address \"{triconexAddress}\".";
            return null;
        }
        var type = match.Groups["type"].Value.ToUpperInvariant();
        var alias = match.Groups["alias"].Value.ToUpperInvariant();
        var index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
        if (alias is ReadAliasSuffix or ReadWriteAliasSuffix)
        {
            note = $"Triconex {(alias == ReadAliasSuffix ? "read" : "read/write")}-aliased address ({triconexAddress}); Siemens has no alias, verify the memory image.";
        }
        return ComposeAddress(type, index, dataType, ref note, out widthAdjusted);
    }

    /// <summary>Siemens memory area for a TriStation register, driven by the data type width.</summary>
    private static string ComposeAddress(string registerType, int index, string? dataType, ref string? note, out bool widthAdjusted)
    {
        widthAdjusted = false;
        var expectedWidth = registerType switch { "MB" => 1, "MI" => 16, _ => 32 };
        var width = BitWidthOf(dataType);
        if (width == 0)
        {
            // No data type to rely on, so keep the register's own area.
            return registerType switch
            {
                "MB" => $"%M{index / 8}.{index % 8}",
                "MI" => $"%MW{index}",
                _ => $"%MD{index}",
            };
        }

        var address = width switch
        {
            1 => $"%M{index / 8}.{index % 8}",
            8 => $"%MB{index}",
            16 => $"%MW{index}",
            _ => $"%MD{index}",
        };

        if (width != expectedWidth)
        {
            widthAdjusted = true;
            var widthNote =
                $"The source gives \"{dataType}\" ({width}-bit) a {expectedWidth}-bit TriStation {registerType} register "
                + $"({registerType}.{index}); it was mapped to {address}. Verify the memory image against the hardware.";
            note = note is null ? widthNote : note + " " + widthNote;
        }

        return address;
    }

    /// <summary>Bit width of an SCL elementary type, or 0 when it is unknown or composite.</summary>
    private static int BitWidthOf(string? dataType) => (dataType ?? string.Empty).Trim().Trim('"').ToUpperInvariant() switch
    {
        "BOOL" => 1,
        "SINT" or "USINT" or "BYTE" or "CHAR" => 8,
        "INT" or "UINT" or "WORD" or "S5TIME" or "DATE" => 16,
        "DINT" or "UDINT" or "DWORD" or "REAL" or "TIME" or "TIME_OF_DAY" or "TOD" or "DATE_AND_TIME" => 32,
        _ => 0,
    };

public void Apply(PlcProject project, MappingResult mapping, ValidationResult validation)
    {
        var translated = 0;
        var unresolved = 0;
        var aliased = 0;
        var widthAdjusted = 0;
        foreach (var variable in project.GlobalVariables)
        {
            var sourceAddress = variable.Metadata?.PhysicalAddress ?? variable.Address;
            if (string.IsNullOrWhiteSpace(sourceAddress)) continue;
            var address = Translate(sourceAddress, variable.EffectiveTypeName, out var note, out var adjusted);
            if (address is null)
            {
                address = TryTranslateRawAddress(sourceAddress, variable.EffectiveTypeName, out var rawAdjusted);
                if (address is null)
                {
                    unresolved++;
                    mapping.Record(new MappingRecord
                    {
                        Category = "Address",
                        Source = variable.Name,
                        Target = string.Empty,
                        Status = ConversionStatus.Manual,
                        Note = note ?? "Address could not be translated.",
                        SourceLine = variable.SourceLine,
                    });
                    continue;
                }
                adjusted = rawAdjusted;
                note = null;
            }
            translated++;
            if (adjusted) widthAdjusted++;
            variable.LogicalAddress = address;
            variable.AddressResolved = true;
            var isAliased = note is not null && note.Contains("aliased", StringComparison.OrdinalIgnoreCase);
            if (isAliased) aliased++;
            var status = note is null ? ConversionStatus.Converted : ConversionStatus.Approximated;
            mapping.Record(new MappingRecord
            {
                Category = "Address",
                Source = variable.Name,
                Target = address,
                Status = status,
                Note = note ?? $"TriStation {sourceAddress} mapped to {address}.",
                SourceLine = variable.SourceLine,
            });
        }
        if (aliased > 0)
        {
            validation.Add(ValidationIssue.Warning($"{aliased} tag(s) use TriStation aliasing, which has no Siemens equivalent."));
        }
        if (widthAdjusted > 0)
        {
            validation.Add(ValidationIssue.Warning(
                $"{widthAdjusted} tag(s) assign a data type whose width does not match the TriStation register; "
                + "the Siemens address was widened to fit the type. Verify the memory image."));
        }
        if (unresolved > 0)
        {
            validation.Add(ValidationIssue.Fatal($"{unresolved} tag(s) have an address that could not be translated."));
        }

        var reassigned = ReassignDuplicateAddresses(project, mapping);
        if (reassigned > 0)
        {
            validation.Add(ValidationIssue.Warning(
                $"{reassigned} tag(s) share an absolute address with another tag; TriStation allows aliased tags "
                + "but Siemens does not and reports an ambiguous-address warning on every access. Unique placeholder "
                + "memory addresses were assigned. Verify the memory image."));
        }
    }

    /// <summary>
    /// TriStation lets several tags alias one physical point, so their translated Siemens addresses
    /// collide. Siemens has no tag aliasing: every access to a shared address raises
    /// "Access to ... with ambiguous address". The first tag keeps the translated address and each
    /// duplicate gets a unique placeholder from an unused memory region so the tag table stays
    /// unambiguous. The placeholder range never overlaps a translated address.
    /// </summary>
    private static int ReassignDuplicateAddresses(PlcProject project, MappingResult mapping)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in project.IoPoints)
        {
            if (!string.IsNullOrWhiteSpace(point.LogicalAddress)) used.Add(point.LogicalAddress!);
        }

        // Populate up front so a placeholder can never land on an address a later tag still owns.
        foreach (var variable in project.GlobalVariables)
        {
            if (!string.IsNullOrWhiteSpace(variable.LogicalAddress)) used.Add(variable.LogicalAddress!);
        }

        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextByte = 4096;
        var bitByte = 12288;
        var bitIndex = 0;
        var reassigned = 0;

        foreach (var variable in project.GlobalVariables)
        {
            var address = variable.LogicalAddress;
            if (string.IsNullOrWhiteSpace(address)) continue;
            if (kept.Add(address!)) continue;

            var isBool = string.Equals(variable.EffectiveTypeName, "BOOL", StringComparison.OrdinalIgnoreCase);
            string placeholder;
            do
            {
                placeholder = isBool ? AllocateBit(ref bitByte, ref bitIndex) : AllocateMemory(variable.EffectiveTypeName, ref nextByte);
            } while (!used.Add(placeholder));

            mapping.Record(new MappingRecord
            {
                Category = "Address",
                Source = variable.Name,
                Target = placeholder,
                Status = ConversionStatus.Approximated,
                Note = $"Source address {address} is shared with another tag; Siemens cannot alias tags, so unique address {placeholder} was assigned.",
                SourceLine = variable.SourceLine,
            });
            variable.LogicalAddress = placeholder;
            reassigned++;
        }

        return reassigned;
    }

    private static string AllocateBit(ref int byteCounter, ref int bitCounter)
    {
        var address = $"%M{byteCounter}.{bitCounter}";
        bitCounter++;
        if (bitCounter > 7)
        {
            bitCounter = 0;
            byteCounter++;
        }
        return address;
    }

    private static string AllocateMemory(string? type, ref int byteCounter)
    {
        var bits = BitWidthOf(type);
        var size = bits switch { 8 => 1, 16 => 2, 32 or 64 => 4, _ => 2 };
        if (byteCounter % size != 0) byteCounter += size - (byteCounter % size);
        var offset = byteCounter;
        byteCounter += size;
        return size switch
        {
            1 => $"%MB{offset}",
            2 => $"%MW{offset}",
            _ => $"%MD{offset}",
        };
    }

    public string? TryTranslateRawAddress(string raw) => TryTranslateRawAddress(raw, null, out _);

    public string? TryTranslateRawAddress(string raw, string? dataType, out bool widthAdjusted)
    {
        widthAdjusted = false;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var memMatch = Regex.Match(raw, @"^M([BIR])([NRW])\.\s*(\d+)$", RegexOptions.IgnoreCase);
        if (memMatch.Success)
        {
            var t = memMatch.Groups[1].Value.ToUpperInvariant();
            var idx = int.Parse(memMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            string? unused = null;
            return ComposeAddress(t, idx, dataType, ref unused, out widthAdjusted);
        }
        return null;
    }
}
