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

    /// <summary>POU version attribute from the source, e.g. "1.19".</summary>
    public string? Version { get; set; }

    /// <summary>Body of the block: verbatim text or a resolved FBD graph.</summary>
    public LogicModel? Logic { get; set; }

    /// <summary>ts:POU_EXT/@ApplicationType, e.g. "SAFETY and CONTROL".</summary>
    public string? ApplicationType { get; set; }

    /// <summary>Instance names of the function blocks called from this block.</summary>
    public List<string> CalledInstances { get; } = new();
}
