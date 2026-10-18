using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Results;

/// <summary>Options that steer mapping and generation.</summary>
public sealed class ConverterOptions
{
    /// <summary>TIA Portal major version used by the generated import script.</summary>
    public int TiaVersion { get; set; } = 20;

    /// <summary>How FBD-language blocks are emitted; defaults to today's SCL-only output.</summary>
    public FbdOutputMode FbdOutput { get; set; } = FbdOutputMode.Scl;

    /// <summary>
    /// Which V16 box-template dialect the FBD generator targets. Defaults to the genuine V16 Update 3
    /// spelling; set to <see cref="FbdBoxDialect.V16Ga"/> when the importer is the V16.0 GA build.
    /// </summary>
    public FbdBoxDialect FbdDialect { get; set; } = FbdBoxDialect.V16Update3;

    /// <summary>CPU order number for the import script's hardware step.</summary>
    public string? PlcCpu { get; set; }

    /// <summary>Path to an external mappings.json, or null to use the embedded one.</summary>
    public string? MappingsPath { get; set; }

    /// <summary>When true, run validation only and generate nothing.</summary>
    public bool ValidateOnly { get; set; }

    /// <summary>
    /// Value written to <c>DocumentInfo/Created</c>. Leave null for the current UTC time; set it to
    /// make output byte-reproducible so generated files can be diffed against real exports.
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    public int ClampTiaVersion()
    {
        // V16 is the oldest supported target: the SCL and tag-table dialects used by the
        // generators are all valid there.
        if (TiaVersion < 16) return 16;
        if (TiaVersion > 21) return 21;
        return TiaVersion;
    }
}
