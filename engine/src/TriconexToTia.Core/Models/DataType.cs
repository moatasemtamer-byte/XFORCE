namespace TriconexToTia.Core.Models;

/// <summary>Base for a named data type (struct/UDT).</summary>
public abstract class DataType
{
    public string Name { get; set; } = string.Empty;

    public int SourceLine { get; set; } = -1;

    public string? Comment { get; set; }

    /// <summary>Original XML element name, e.g. "DataType".</summary>
    public string SourceElementName { get; set; } = string.Empty;
}
