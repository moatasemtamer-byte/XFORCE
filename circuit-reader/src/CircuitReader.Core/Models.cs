// Core data model — mirrors the Python ``circuit_reader.ladder`` types so
// the parsing algorithms map 1:1.

namespace CircuitReader.Core;

public enum ElementKind
{
    NormallyOpen,        // -| |-   contact, active when bit = 1
    NormallyClosed,      // -|/|-   contact, active when bit = 0
    Coil,                // -( )-   output coil
    Latch,               // -(L)-   set-only coil
    Unlatch,             // -(U)-   reset-only coil
    Timer,               // -(T)-   timer accumulator
    ClippedCoil,         // synthesized placeholder for off-page coils
    Compare,             // -[>= 5000]- numeric comparison (IF block)
    LetAssign,           // -[LET S=expr]- math/scale assignment (LET block)
    Short,               // SHORT annotation (always-true rung)
    Open,                // OPEN annotation (always-false rung)
}

public sealed record Element(
    ElementKind Kind,
    string Address,
    string Label,
    double X,
    double Y)
{
    public bool IsInput =>
        Kind is ElementKind.NormallyOpen or ElementKind.NormallyClosed
             or ElementKind.Compare;

    public bool IsOutput =>
        Kind is ElementKind.Coil or ElementKind.Latch or ElementKind.Unlatch
             or ElementKind.Timer or ElementKind.ClippedCoil
             or ElementKind.LetAssign;
}

public sealed class Row
{
    public double Y { get; set; }
    public List<Element> Elements { get; } = new();

    public Row(double y) { Y = y; }
}

public sealed class Rung
{
    public int Number { get; set; }
    public List<Row> Rows { get; } = new();
    public Element? Output { get; set; }
    public List<Element> Inputs { get; } = new();
    public List<Row> Branches { get; } = new();
    public string BooleanEquation { get; set; } = string.Empty;
    public string Minimized { get; set; } = string.Empty;
    /// <summary>
    /// Function-style equation: ``LHS = AND(A, !B, OR(C, D))`` derived
    /// from <see cref="BooleanEquation"/>.  Useful for users who want to
    /// paste it into Excel formulas, scripts, or anywhere & / | / ~
    /// would be parsed as something other than boolean ops.
    /// </summary>
    public string FunctionEquation { get; set; } = string.Empty;
    public string? Note { get; set; }
    /// <summary>Free-form descriptive comment for the rung / network
    /// (e.g. the Triconex <c>+===+</c> comment box text).</summary>
    public string? Comment { get; set; }
    /// <summary>Structured-text statements when the network is an ST block
    /// (Triconex <c>+---+</c> box) rather than a relay-ladder rung.</summary>
    public List<string> Statements { get; } = new();
}

public sealed class VerticalLine
{
    public double X { get; init; }
    public double YTop { get; init; }
    public double YBottom { get; init; }
    public double Height => YBottom - YTop;
}

public sealed class PageLadder
{
    public int PageNumber { get; set; }
    public List<Rung> Rungs { get; } = new();
    public Dictionary<string, string> AddressLabels { get; } = new();
}
