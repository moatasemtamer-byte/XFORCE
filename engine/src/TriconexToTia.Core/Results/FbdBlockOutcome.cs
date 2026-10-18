namespace TriconexToTia.Core.Results;

/// <summary>
/// How one FBD-language block was emitted during an FBD output run, for the report.
/// </summary>
public sealed class FbdBlockOutcome
{
    /// <summary>Block name.</summary>
    public string Block { get; init; } = string.Empty;

    /// <summary>"FBD" when rendered as SimaticML, "SCL" when it fell back.</summary>
    public string Rendered { get; init; } = string.Empty;

    /// <summary>Why the block was kept as SCL, when it was.</summary>
    public string? Reason { get; init; }

    /// <summary>Approximated constructs the FBD rendering needed (helpers, conversions).</summary>
    public List<string> Approximations { get; } = new();
}