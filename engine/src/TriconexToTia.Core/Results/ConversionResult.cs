using TriconexToTia.Core.Models;

namespace TriconexToTia.Core.Results;

/// <summary>Overall result of a conversion run, ready for the CLI or a server.</summary>
public sealed class ConversionResult
{
    public bool Success { get; init; }

    /// <summary>Set when the run failed before or during parsing.</summary>
    public string? FatalError { get; init; }

    public PlcProject Project { get; init; } = new();

    public ValidationResult Validation { get; init; } = new();

    public MappingResult Mapping { get; init; } = new();

    public IReadOnlyList<GeneratedFile> Files { get; init; } = Array.Empty<GeneratedFile>();

    /// <summary>Convenience accessor for the generated conversion report text.</summary>
    public string? Report => Files
        .FirstOrDefault(f => f.RelativePath == "conversion_report.txt")?.Content;

    /// <summary>Convenience accessor for the generated machine-readable report (GUI evidence).</summary>
    public string? ReportJson => Files
        .FirstOrDefault(f => f.RelativePath == "report.json")?.Content;

    public bool ValidateOnly { get; init; }
}
