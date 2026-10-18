using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Models;

/// <summary>Base class for one item of an FBD/LD network.</summary>
public abstract class LogicItem
{
    /// <summary>PLCopen <c>localId</c>; connections reference items by this value.</summary>
    public string LocalId { get; set; } = string.Empty;

    public int SheetNumber { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Value of <c>ts:TEXT</c> when the item carries an annotation.</summary>
    public string? Annotation { get; set; }

    /// <summary>
    /// TriStation data type of a variable item, taken from <c>ts:FBDITEM_EXT/ts:DATATYPE</c>. The
    /// same signal name can be wired with different types in different diagrams, so the type the
    /// diagram expects at this site is carried next to the name.
    /// </summary>
    public string? SourceTypeName { get; set; }

    /// <summary>
    /// Identity of the item inside a diagram. TriStation restarts <c>localId</c> numbering on every
    /// sheet, so a <c>localId</c> is only unique when combined with its sheet.
    /// </summary>
    public string SheetKey => $"{SheetNumber}:{LocalId}";
}

/// <summary>
/// A call to a library function or to a user function block instance. When
/// <see cref="InstanceName"/> is set the call is an FB instance invocation; otherwise
/// it is a function call whose outputs are returned rather than stored.
/// </summary>
public sealed class BlockCall : LogicItem
{
    public string TypeName { get; set; } = string.Empty;

    public string? InstanceName { get; set; }

    /// <summary>Siemens block/library name resolved from <see cref="TypeName"/>.</summary>
    public string? MappedTypeName { get; set; }

    /// <summary>Status of <see cref="MappedTypeName"/>; null while unmapped.</summary>
    public ConversionStatus? TypeStatus { get; set; }

    public string? MappingNote { get; set; }

    /// <summary>
    /// True when the call is a stateless function (its result is used as a value) rather than a
    /// block instance. Resolved by the mapping stage, not guessed by the generator.
    /// </summary>
    public bool IsFunction { get; set; }

    /// <summary>
    /// SCL expression that replaces this call entirely, with <c>{Formal}</c> placeholders. Set for
    /// TriStation functions that have no Siemens name but do have an exact arithmetic form.
    /// </summary>
    public string? InlineExpression { get; set; }

    /// <summary>
    /// Infix operator that replaces this call entirely (<c>AND</c>, <c>OR</c>, <c>XOR</c>), with the
    /// wired inputs as operands. TIA SCL accepts the operator form but rejects keyword-style calls
    /// such as <c>OR(IN1 := ..., IN2 := ...)</c>. Set by the mapping stage from the rule.
    /// </summary>
    public string? Operator { get; set; }

    /// <summary>
    /// Declared type of the value this call produces, when the mapping rule states it (e.g. the
    /// boolean result of an inlined <c>NOT</c>). Null when the type is not known.
    /// </summary>
    public string? ResultType { get; set; }

    /// <summary>
    /// TriStation formal name to Siemens formal name for this call's outputs. Output formals are
    /// referenced through <see cref="SignalSource.RefFormalParameter"/> on the consuming side, so the
    /// rename cannot live on the output <see cref="CallArgument"/> alone.
    /// </summary>
    public Dictionary<string, string> MappedOutputFormals { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the call has no user-visible input arguments at all.</summary>
    public bool IsStateless =>
        Inputs.Count == 0 && InOuts.Count == 0 && Outputs.Count == 0;

    public List<CallArgument> Inputs { get; } = new();

    public List<CallArgument> InOuts { get; } = new();

    public List<CallArgument> Outputs { get; } = new();
}

/// <summary>One formal parameter of a call, with every signal wired into it.</summary>
public sealed class CallArgument
{
    public string? FormalParameter { get; set; }

    /// <summary>
    /// Siemens formal name when the mapped instruction spells it differently; null keeps
    /// <see cref="FormalParameter"/>.
    /// </summary>
    public string? MappedFormalParameter { get; set; }

    /// <summary>
    /// Siemens conversion function to wrap the incoming value in (e.g. <c>DINT_TO_TIME</c>); null
    /// passes it through unchanged.
    /// </summary>
    public string? ValueConversion { get; set; }

    /// <summary>
    /// The callee's declared type for this formal (its FBD pin type), when known. The emitter
    /// converts a wired signal of a different type into this one. For an output formal it is the
    /// type the block produces, used to convert a result before it reaches its target.
    /// </summary>
    public string? ExpectedType { get; set; }

    public List<SignalSource> Sources { get; } = new();

    public bool IsOptional => Sources.Count == 0;
}

/// <summary>Where one input signal comes from.</summary>
public sealed class SignalSource
{
    /// <summary>"expression" for a variable/literal, "blockOutput" for a call result.</summary>
    public string Kind { get; set; } = "expression";

    /// <summary>Variable name or literal text when <see cref="Kind"/> is "expression".</summary>
    public string? Expression { get; set; }

    /// <summary>localId of the driving block when <see cref="Kind"/> is "blockOutput".</summary>
    public string? RefLocalId { get; set; }

    public string? RefFormalParameter { get; set; }

    /// <summary>Sheet of the driving block; localId numbering restarts on every sheet.</summary>
    public int RefSheetNumber { get; set; }

    /// <summary>
    /// TriStation data type the diagram wired at this site (from the driving variable item), when
    /// known. Used to coerce a signal whose global tag has a different declared type.
    /// </summary>
    public string? TypeName { get; set; }

    public bool IsBlockOutput => Kind == "blockOutput";

    public static SignalSource FromExpression(string expression, string? typeName = null) =>
        new() { Kind = "expression", Expression = expression, TypeName = typeName };

    public static SignalSource FromBlockOutput(string localId, string? formalParameter, int sheetNumber) =>
        new()
        {
            Kind = "blockOutput",
            RefLocalId = localId,
            RefFormalParameter = formalParameter,
            RefSheetNumber = sheetNumber,
        };
}

/// <summary>A variable used by the network as a signal source (<c>inVariable</c>).</summary>
public sealed class VariableReference : LogicItem
{
    /// <summary>Variable name or literal text taken from <c>expression</c>.</summary>
    public string Expression { get; set; } = string.Empty;
}

/// <summary>An assignment produced by an <c>outVariable</c> / <c>inOutVariable</c> item.</summary>
public sealed class VariableAssignment : LogicItem
{
    public string Target { get; set; } = string.Empty;

    public string? Expression { get; set; }

    /// <summary>localId of the driving block, when the value comes from a call result.</summary>
    public string? RefLocalId { get; set; }

    public string? RefFormalParameter { get; set; }

    /// <summary>Sheet of the driving block; localId numbering restarts on every sheet.</summary>
    public int RefSheetNumber { get; set; }

    public bool IsBlockOutput => RefLocalId is not null;
}

/// <summary>A network comment (<c>comment/content</c>).</summary>
public sealed class LogicComment : LogicItem
{
    public string Text { get; set; } = string.Empty;
}