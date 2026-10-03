namespace TriconexToTia.Core.Models;

/// <summary>
/// An XML element the parser did not recognise. Kept so nothing is silently
/// dropped (constraint C4); surfaced in the conversion report.
/// </summary>
public sealed class UnrecognizedElement
{
    public string ElementName { get; set; } = string.Empty;

    public string ParentPath { get; set; } = string.Empty;

    public int SourceLine { get; set; } = -1;

    /// <summary>Truncated raw XML, for human review.</summary>
    public string Snippet { get; set; } = string.Empty;
}
