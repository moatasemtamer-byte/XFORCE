namespace Pt2Conv;

/// <summary>
/// Flat, renderer-friendly view of a decompiled TriStation project.
///
/// <see cref="FromPt2"/> already knows how to decode the container into FBD sheets
/// (blocks, terminals, wires, comments with raw geometry) and Structured Text. This
/// model is the same data with no nesting the renderer has to walk: enough to draw
/// the sheets and lay out the title block, and nothing tied to the web viewer.
/// </summary>
public sealed class PdfProject
{
    public string Name = "";
    public string Description = "";
    public List<PdfPou> Pous = new();
    public List<PdfTag> Tags = new();
}

public sealed class PdfPou
{
    public string Name = "";
    public string PouType = "";
    public string? ReturnType;
    public string Title = "";
    public PdfDrawing Drawing = new();
    public List<PdfVar> Vars = new();
    public List<string> ReferencedGlobals = new();
    public string? St;
    public List<PdfSheet> Sheets = new();
}

public sealed class PdfDrawing
{
    public string Number = "";
    public string Title = "";
    public string Size = "D";
    public string SheetTemplate = "";
    public string Rev = "";
    public int HZones = 16;
    public int VZones = 8;
}

public sealed class PdfVar
{
    public string Name = "";
    public string DataType = "";
    public string Init = "";
    public string Desc = "";
    public string Section = "";
    public bool IsInstance;
}

public sealed class PdfTag
{
    public string Name = "";
    public string Type = "";
    public string Desc = "";
    public string Address = "";
}

public sealed class PdfSheet
{
    public int Number;
    public string Title = "";
    public List<PdfElem> Elems = new();
    public List<PdfWire> Wires = new();
}

public abstract class PdfElem
{
    public int Id;
    public double X, Y, W, H;
}

public sealed class PdfBlockElem : PdfElem
{
    public string TypeName = "";
    public string Instance = "";
    public bool IsFunction;
    public List<PdfPin> Inputs = new();
    public List<string> Outputs = new();
}

public sealed class PdfVarElem : PdfElem
{
    public string Name = "";
    public string VarType = "";
    public string Annotation = "";
    public bool Annotate;
    public bool HasInWire;
    public bool HasOutWire;
}

public sealed class PdfCommentElem : PdfElem
{
    public string Text = "";
    public string Border = "Single";
    public string Align = "Left";
    public double TextSize = 10;
    public bool BehindLogic;
}

public sealed class PdfUnknownElem : PdfElem
{
    public string RawKind = "unknown";
}

public sealed class PdfPin
{
    public string Name = "";
    public int Wire = -1;
}

public sealed class PdfWire
{
    public int Id;
    public List<(double X, double Y)> Positions = new();
}
