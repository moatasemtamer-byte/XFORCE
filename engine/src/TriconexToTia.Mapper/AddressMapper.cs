using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Mapper;

/// <summary>
/// Assigns Siemens logical addresses to I/O points and global variables.
///
/// Triconex addresses (card/slot/channel or Modbus registers) do not map onto
/// Siemens addresses automatically, so when the source has no usable address a
/// placeholder is assigned and flagged in the report.
/// </summary>
public sealed class AddressMapper
{
    public void Resolve(PlcProject project, MappingResult mapping, ValidationResult validation)
    {
        var diByte = 0; var diBit = 0;
        var dqByte = 0; var dqBit = 0;
        var dmByte = 0; var dmBit = 0;
        var analogWord = 0; var analogDword = 0; var analogByte = 0;
        var globalWord = 100; var globalDword = 100; var globalByte = 100;
        var placeholders = 0;

        foreach (var point in project.IoPoints)
        {
            if (!string.IsNullOrWhiteSpace(point.LogicalAddress))
            {
                point.AddressResolved = true;
                continue;
            }

            string address;
            if (point.Signal == IoSignal.Digital)
            {
                if (point.Direction == IoDirection.Output)
                {
                    address = NextDigital('Q', ref dqByte, ref dqBit);
                }
                else if (point.Direction == IoDirection.Input)
                {
                    address = NextDigital('I', ref diByte, ref diBit);
                }
                else
                {
                    address = NextDigital('M', ref dmByte, ref dmBit);
                }
            }
            else
            {
                address = NextMemory(point.EffectiveTypeName, ref analogWord, ref analogDword, ref analogByte);
            }

            point.LogicalAddress = address;
            point.AddressResolved = false;
            placeholders++;
            mapping.Record(new MappingRecord
            {
                Category = "Address",
                Source = point.Name,
                Target = address,
                Status = ConversionStatus.Approximated,
                Note = "Address could not be determined from the source; placeholder assigned.",
                SourceLine = point.SourceLine,
            });
        }

        foreach (var variable in project.GlobalVariables)
        {
            if (!string.IsNullOrWhiteSpace(variable.LogicalAddress))
            {
                variable.AddressResolved = true;
                continue;
            }

            var address = NextMemory(variable.EffectiveTypeName, ref globalWord, ref globalDword, ref globalByte);
            variable.LogicalAddress = address;
            variable.AddressResolved = false;
            placeholders++;
            mapping.Record(new MappingRecord
            {
                Category = "Address",
                Source = variable.Name,
                Target = address,
                Status = ConversionStatus.Approximated,
                Note = "Global variable has no source address; placeholder assigned.",
                SourceLine = variable.SourceLine,
            });
        }

        if (placeholders > 0)
        {
            validation.Add(ValidationIssue.Warning(
                $"{placeholders} tag address(es) could not be resolved from the source and were assigned placeholder memory addresses. Assign the correct addresses in TIA Portal."));
        }
    }

    private static string NextDigital(char area, ref int byteCounter, ref int bitCounter)
    {
        var address = $"%{area}{byteCounter}.{bitCounter}";
        bitCounter++;
        if (bitCounter > 7)
        {
            bitCounter = 0;
            byteCounter++;
        }
        return address;
    }

    private static string NextMemory(string mappedType, ref int word, ref int dword, ref int @byte)
    {
        var size = SizeOf(mappedType);
        if (size <= 1)
        {
            var address = $"%MB{@byte}";
            @byte += 1;
            return address;
        }
        if (size <= 2)
        {
            var address = $"%MW{word}";
            word += 2;
            return address;
        }
        var dwordAddress = $"%MD{dword}";
        dword += 4;
        return dwordAddress;
    }

    private static int SizeOf(string type)
    {
        var normalized = (type ?? string.Empty).Trim().Trim('"').ToUpperInvariant();
        if (normalized.StartsWith("ARRAY", StringComparison.Ordinal)) return 2;
        return normalized switch
        {
            "BOOL" or "BYTE" or "SINT" or "USINT" => 1,
            "INT" or "UINT" or "WORD" or "DATE" or "TOD" => 2,
            "LINT" or "ULINT" or "LREAL" => 8,
            "DINT" or "UDINT" or "DWORD" or "REAL" or "TIME" or "DT" or "DTL" => 4,
            _ => 2,
        };
    }
}
