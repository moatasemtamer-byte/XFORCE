using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Models;

/// <summary>Base class for every converted Triconex code unit.</summary>
public abstract class PlcBlock
{
    public string Name { get; set; } = string.Empty;

    public PlcLanguage Language { get; set; } = PlcLanguage.Unknown;

    public int SourceLine { get; set; } = -1;

    public List<Variable> Variables { get; } = new();

    public string Code { get; set; } = string.Empty;

    /// <summary>Return type for a FUNCTION; null for FB/OB.</summary>
    public string? ReturnType { get; set; }

    /// <summary>Result of mapping the return type (FUNCTION only).</summary>
    public string? MappedReturnType { get; set; }

    public ConversionStatus ReturnTypeStatus { get; set; } = ConversionStatus.Converted;

    /// <summary>Original XML element name, e.g. "FUNCTION_BLOCK".</summary>
    public string SourceElementName { get; set; } = string.Empty;

    public abstract BlockType BlockType { get; }

    public string? Comment { get; set; }
}
