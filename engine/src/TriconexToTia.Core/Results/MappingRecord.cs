using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Results;

/// <summary>One recorded mapping decision, for the conversion report.</summary>
public sealed class MappingRecord
{
    public string Category { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string? Target { get; init; }

    public ConversionStatus Status { get; init; } = ConversionStatus.Converted;

    public string? Note { get; init; }

    public int SourceLine { get; init; } = -1;
}
