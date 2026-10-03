using System.Text.Json.Serialization;
using TriconexToTia.Core.Enums;

namespace TriconexToTia.Mapper;

/// <summary>Type or library-function rule loaded from mappings.json.</summary>
public sealed class MappingRule
{
    public string? Siemens { get; set; }

    public string Status { get; set; } = "Exact";

    public string? Note { get; set; }
}

public sealed class ManualPattern
{
    public string Prefix { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
}

/// <summary>In-memory form of mappings.json. Dictionaries are case-insensitive.</summary>
public sealed class Mappings
{
    public string Version { get; set; } = "1.0";

    public Dictionary<string, MappingRule> Types { get; set; } = new();

    public Dictionary<string, string> BlockTypes { get; set; } = new();

    public Dictionary<string, MappingRule> LibraryFunctions { get; set; } = new();

    public List<ManualPattern> ManualPatterns { get; set; } = new();

    [JsonIgnore]
    public string Source { get; set; } = "embedded";

    /// <summary>Re-keys every dictionary with an ordinal-ignore-case comparer.</summary>
    public Mappings Normalize()
    {
        Types = new Dictionary<string, MappingRule>(Types, StringComparer.OrdinalIgnoreCase);
        BlockTypes = new Dictionary<string, string>(BlockTypes, StringComparer.OrdinalIgnoreCase);
        LibraryFunctions = new Dictionary<string, MappingRule>(LibraryFunctions, StringComparer.OrdinalIgnoreCase);
        return this;
    }

    public static ConversionStatus ParseStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "approximated" or "approximate" or "approx" => ConversionStatus.Approximated,
            "manual" => ConversionStatus.Manual,
            "failed" or "fail" => ConversionStatus.Failed,
            _ => ConversionStatus.Converted,
        };

    public static ConversionStatus Worst(ConversionStatus a, ConversionStatus b) =>
        (ConversionStatus)Math.Max((int)a, (int)b);
}
