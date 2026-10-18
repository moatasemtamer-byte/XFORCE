using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Models;

/// <summary>
/// A variable declared inside a block (input/output/in-out/local) or a member
/// of a data type.
/// </summary>
public class Variable
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Original Triconex type name, e.g. "REAL" or "ARRAY[1..8] OF BOOL".</summary>
    public string TypeName { get; set; } = string.Empty;

    public VariableDirection Direction { get; set; } = VariableDirection.Local;

    public int SourceLine { get; set; } = -1;

    public string? Comment { get; set; }

    /// <summary>Result of applying the type mapping table. Null until mapped.</summary>
    public string? MappedTypeName { get; set; }

    public ConversionStatus TypeStatus { get; set; } = ConversionStatus.Converted;

    public string? MappingNote { get; set; }

    public string EffectiveTypeName => MappedTypeName ?? TypeName;

    /// <summary>Declared inside a constant group (<c>localVars/@constant</c>).</summary>
    public bool Constant { get; set; }

    /// <summary>PLCopen address element, when the source supplies one.</summary>
    public string? Address { get; set; }

    /// <summary>Initial value text from <c>simpleValue/@value</c>.</summary>
    public string? InitialValue { get; set; }

    /// <summary>Documentation text; also used as the emitted comment.</summary>
    public string? Documentation { get; set; }

    /// <summary>Type came from <c>&lt;derived name="..."/&gt;</c>: an FB instance or a data type.</summary>
    public bool IsDerivedType { get; set; }

    public bool IsArray { get; set; }

    /// <summary>Array bounds as "lower..upper", comma separated per dimension.</summary>
    public string? ArrayBounds { get; set; }
}
