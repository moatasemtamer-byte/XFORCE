namespace TriconexToTia.Core.Results;

/// <summary>Options that steer mapping and generation.</summary>
public sealed class ConverterOptions
{
    /// <summary>TIA Portal major version used by the generated import script.</summary>
    public int TiaVersion { get; set; } = 20;

    /// <summary>CPU order number for the import script's hardware step.</summary>
    public string? PlcCpu { get; set; }

    /// <summary>Path to an external mappings.json, or null to use the embedded one.</summary>
    public string? MappingsPath { get; set; }

    /// <summary>When true, run validation only and generate nothing.</summary>
    public bool ValidateOnly { get; set; }

    public int ClampTiaVersion()
    {
        if (TiaVersion < 17) return 17;
        if (TiaVersion > 21) return 21;
        return TiaVersion;
    }
}
