using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;

namespace TriconexToTia.Mapper;

public sealed record TypeMappingResult(
    string Mapped,
    ConversionStatus Status,
    string? Note,
    bool IsUserDefined);

/// <summary>Maps Triconex primitive and array type names onto Siemens SCL types.</summary>
public sealed class TypeMapper
{
    private static readonly Regex ArrayPattern = new(
        @"^\s*ARRAY\s*\[(?<bounds>[^\]]+)\]\s*OF\s+(?<element>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StringPattern = new(
        @"^\s*STRING\s*(?:\[(?<len>\d+)\]\s*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Mappings _mappings;

    public TypeMapper(Mappings mappings) => _mappings = mappings;

    public Mappings Mappings => _mappings;

    public TypeMappingResult Map(string sourceType, IReadOnlyCollection<string> userDefinedTypes)
    {
        var raw = (sourceType ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return new TypeMappingResult("Bool", ConversionStatus.Manual, "Empty type name; defaulted to Bool.", false);
        }

        var arrayMatch = ArrayPattern.Match(raw);
        if (arrayMatch.Success)
        {
            var element = Map(arrayMatch.Groups["element"].Value, userDefinedTypes);
            var bounds = arrayMatch.Groups["bounds"].Value.Trim();
            var mapped = $"Array[{bounds}] of {element.Mapped}";
            return new TypeMappingResult(
                mapped,
                element.Status,
                element.Note,
                element.IsUserDefined);
        }

        var stringMatch = StringPattern.Match(raw);
        if (stringMatch.Success)
        {
            if (stringMatch.Groups["len"].Success)
            {
                return new TypeMappingResult($"String[{stringMatch.Groups["len"].Value}]", ConversionStatus.Converted, null, false);
            }
            return new TypeMappingResult("String[254]", ConversionStatus.Approximated,
                "String length may need adjustment.", false);
        }

        if (_mappings.Types.TryGetValue(raw, out var rule))
        {
            return new TypeMappingResult(
                rule.Siemens ?? raw,
                Mappings.ParseStatus(rule.Status),
                rule.Note,
                false);
        }

        if (userDefinedTypes.Contains(raw, StringComparer.OrdinalIgnoreCase))
        {
            return new TypeMappingResult($"\"{raw}\"", ConversionStatus.Converted, null, true);
        }

        // A TriStation library block can also appear as a variable type ("VAR x : UNPACK16"), and
        // the declared type must resolve to the block that will exist in the target project: the
        // Siemens instruction (TDD_I -> TOF) or the stand-in SupportBlockGenerator emits
        // (UNPACK16 -> Triconex_UNPACK16). The validator resolves the same rule for the same
        // reason, before mapping runs. Functions cannot be a variable's type, so only block-kind
        // rules apply; a rule without a Siemens name stays unmapped like any other unknown type.
        if (_mappings.LibraryFunctions.TryGetValue(raw, out var libraryRule)
            && !string.Equals(libraryRule.Kind, "function", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(libraryRule.Siemens))
        {
            return new TypeMappingResult(
                libraryRule.Siemens!,
                Mappings.ParseStatus(libraryRule.Status),
                libraryRule.Note,
                false);
        }

        if (raw.Contains("STRUCT", StringComparison.OrdinalIgnoreCase))
        {
            return new TypeMappingResult(raw, ConversionStatus.Manual,
                "Inline STRUCT types are not converted automatically; declare a UDT.", false);
        }

        return new TypeMappingResult(raw, ConversionStatus.Manual,
            "No Siemens mapping is known for this type.", false);
    }
}
