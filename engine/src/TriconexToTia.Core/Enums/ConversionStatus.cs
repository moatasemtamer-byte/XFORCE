namespace TriconexToTia.Core.Enums;

/// <summary>
/// Outcome of a single mapping or conversion decision. Every decision is
/// recorded so nothing is silently dropped (constraint C4).
/// </summary>
public enum ConversionStatus
{
    /// <summary>Direct, exact equivalence.</summary>
    Converted,

    /// <summary>An equivalent was produced, but semantics may differ.</summary>
    Approximated,

    /// <summary>No automatic equivalent exists; an engineer must act.</summary>
    Manual,

    /// <summary>The item could not be converted at all.</summary>
    Failed,
}
