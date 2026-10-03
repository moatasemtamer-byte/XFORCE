using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Results;

public sealed class ValidationIssue
{
    public Severity Severity { get; init; }

    public string Message { get; init; } = string.Empty;

    public int SourceLine { get; init; } = -1;

    /// <summary>Optional context, e.g. the block or variable name.</summary>
    public string? Context { get; init; }

    public static ValidationIssue Fatal(string message, int sourceLine = -1, string? context = null) =>
        new() { Severity = Severity.Fatal, Message = message, SourceLine = sourceLine, Context = context };

    public static ValidationIssue Warning(string message, int sourceLine = -1, string? context = null) =>
        new() { Severity = Severity.Warning, Message = message, SourceLine = sourceLine, Context = context };

    public static ValidationIssue Info(string message, int sourceLine = -1, string? context = null) =>
        new() { Severity = Severity.Info, Message = message, SourceLine = sourceLine, Context = context };
}
