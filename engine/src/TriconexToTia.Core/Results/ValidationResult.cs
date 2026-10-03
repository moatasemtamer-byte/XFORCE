using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Results;

public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = new();

    public IEnumerable<ValidationIssue> Fatal => Issues.Where(i => i.Severity == Severity.Fatal);

    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == Severity.Warning);

    public IEnumerable<ValidationIssue> Infos => Issues.Where(i => i.Severity == Severity.Info);

    public bool HasFatal => Issues.Any(i => i.Severity == Severity.Fatal);

    public void Add(ValidationIssue issue) => Issues.Add(issue);
}
