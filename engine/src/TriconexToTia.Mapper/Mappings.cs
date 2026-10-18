using System.Text.Json.Serialization;
using TriconexToTia.Core.Enums;

namespace TriconexToTia.Mapper;

/// <summary>Type or library-function rule loaded from mappings.json.</summary>
public sealed class MappingRule
{
    public string? Siemens { get; set; }

    public string Status { get; set; } = "Exact";

    public string? Note { get; set; }

    /// <summary>What to replace an action in the source with.</summary>
    public string? Action { get; set; }

    /// <summary>
    /// "function" for a stateless IEC function, "block" for an instance-based block. Only
    /// meaningful for library functions; the emitter needs it to choose between an inline call and
    /// an instance declaration.
    /// </summary>
    public string Kind { get; set; } = "block";

    /// <summary>
    /// SCL expression that replaces the whole call, with <c>{FormalName}</c> placeholders for the
    /// call's inputs. Used for TriStation functions that have no Siemens counterpart but do have an
    /// exact arithmetic form. The emitter substitutes the connected signals, so the expression needs
    /// no separate renaming rules.
    /// </summary>
    public string? Inline { get; set; }

    /// <summary>
    /// SCL infix operator that replaces the whole call (<c>AND</c>, <c>OR</c>, <c>XOR</c>) with the
    /// wired inputs as operands. TIA rejects keyword-style calls such as <c>OR(IN1 := ..., IN2 :=
    /// ...)</c>, so n-ary logic functions are rendered as operator expressions instead.
    /// </summary>
    public string? Operator { get; set; }

    /// <summary>
    /// Declared type of the value the mapped instruction produces, when it cannot be inferred from
    /// the pins (e.g. an inlined <c>NOT</c> returns Bool). Null when the type is not known.
    /// </summary>
    public string? ResultType { get; set; }

    /// <summary>
    /// Type every input pin expects when the rule does not name individual formals. Used for
    /// instructions whose operands are all the same type, e.g. the boolean inputs of PACK16/PACK32.
    /// </summary>
    public string? InputType { get; set; }

    /// <summary>
    /// Per-formal-parameter rules, keyed by the TriStation formal name. Only needed when the Siemens
    /// instruction spells its formals differently or expects a converted value.
    /// </summary>
    public Dictionary<string, ParameterRule>? Parameters { get; set; }
}

/// <summary>How one formal parameter of a TriStation call maps onto the Siemens instruction.</summary>
public sealed class ParameterRule
{
    /// <summary>Siemens formal name; null keeps the TriStation name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Siemens conversion function wrapped around the incoming value, e.g. <c>DINT_TO_TIME</c> for a
    /// millisecond count. Null passes the value through unchanged.
    /// </summary>
    public string? Convert { get; set; }

    /// <summary>
    /// Siemens type this pin expects (its FBD pin type). A wired signal of another type is converted
    /// into it. Null means the pin's type is not constrained by the rule.
    /// </summary>
    public string? Type { get; set; }
}

public sealed class ManualPattern
{
    public string Prefix { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
}

/// <summary>In-memory form of mappings.json. Dictionaries are case-insensitive.</summary>
public sealed class Mappings
{
    public string Version { get; set; } = "1.0";

    public Dictionary<string, MappingRule> Types { get; set; } = new();

    public Dictionary<string, string> BlockTypes { get; set; } = new();

    public Dictionary<string, MappingRule> LibraryFunctions { get; set; } = new();

    public List<ManualPattern> ManualPatterns { get; set; } = new();

    [JsonIgnore]
    public string Source { get; set; } = "embedded";

    /// <summary>Re-keys every dictionary with an ordinal-ignore-case comparer.</summary>
    public Mappings Normalize()
    {
        Types = new Dictionary<string, MappingRule>(Types, StringComparer.OrdinalIgnoreCase);
        BlockTypes = new Dictionary<string, string>(BlockTypes, StringComparer.OrdinalIgnoreCase);
        LibraryFunctions = new Dictionary<string, MappingRule>(LibraryFunctions, StringComparer.OrdinalIgnoreCase);
        return this;
    }

    public static ConversionStatus ParseStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "approximated" or "approximate" or "approx" => ConversionStatus.Approximated,
            "manual" => ConversionStatus.Manual,
            "failed" or "fail" => ConversionStatus.Failed,
            _ => ConversionStatus.Converted,
        };

    public static ConversionStatus Worst(ConversionStatus a, ConversionStatus b) =>
        (ConversionStatus)Math.Max((int)a, (int)b);
}
