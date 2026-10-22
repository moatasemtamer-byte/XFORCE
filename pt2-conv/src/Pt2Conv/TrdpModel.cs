namespace Pt2Conv;

/// <summary>
/// In-memory model of a Telerik Reporting <c>.trdp</c> template: the report sections
/// (page header, detail, footer), their report items (bands/panels/text boxes/shapes/
/// pictures/tables), and the report/table style sheet. Deliberately a loose model -- a
/// template that uses an item kind or attribute this renderer does not know degrades
/// gracefully (item skipped, text drawn unstyled) rather than throwing.
/// </summary>
public sealed class TrdpReport
{
    public string Name = "";
    public double Width = 612;   // points
    public double Height = 792;  // points
    public double MarginLeft, MarginTop, MarginRight, MarginBottom;
    public string DataSourceName = "";
    public TrdpStyleSheet Styles = new();
    public List<TrdpSection> Sections = new();
}

/// <summary>One of PageHeaderSection / DetailSection / ReportFooterSection / PageFooterSection.</summary>
public sealed class TrdpSection
{
    public string Kind = "";     // "PageHeader", "Detail", "ReportFooter", "PageFooter"
    public double Height;
    public bool PrintOnFirstPage = true;
    public bool PrintOnLastPage = true;
    public bool PageBreakBefore;
    public bool PageBreakAfter;
    public List<TrdpItem> Items = new();
}

/// <summary>A style-sheet text style (font family/size/bold/italic, colour).</summary>
public sealed class TrdpStyle
{
    public string FontName = "";
    public double FontSize = 0;
    public bool Bold;
    public bool Italic;
    public string Color = "";
    public string BackgroundColor = "";
}

public sealed class TrdpStyleSheet
{
    public List<(string Type, string Name, TrdpStyle? Style)> ByName = new();
}

public abstract class TrdpItem
{
    public string Name = "";
    public double Left, Top, Width, Height;
    public string StyleName = "";
    public bool Visible = true;
    public string Condition = "";
}

public sealed class TrdpPanel : TrdpItem
{
    public List<TrdpItem> Items = new();
}

public sealed class TrdpTextBox : TrdpItem
{
    public string Value = "";
    public bool CanGrow;
    public string Format = "";
    public double FontSize;
    public bool Bold;
    public bool Italic;
    public string FontName = "";
    public string Color = "";
    public string BackgroundColor = "";
    public string TextAlign = "Left";
    public string VerticalAlign = "Top";
    public string LineStyle = "";   // BorderStyle (None/Solid/...)
    public string LineColor = "";
    public double BorderWidth;
    public double PadLeft, PadTop, PadRight, PadBottom;
}

public sealed class TrdpShape : TrdpItem
{
    public string Kind = "Rectangle";  // "Rectangle" | "Line" | "Ellipse"
    public string Direction = "EW";
    public string LineColor = "";
    public string BackgroundColor = "";
    public double LineWidth = 1;
}

public sealed class TrdpPicture : TrdpItem
{
    public string Image = "";
    public string Sizing = "Stretch";
    public byte[]? ImageBytes;
}

public sealed class TrdpTable : TrdpItem
{
    public bool IsCrosstab;
    public string DataSourceName = "";
    public bool RowHeadersEveryPage;
    public bool ColumnHeadersEveryPage;
    public string CornerText = "";
    public List<TrdpTableCell> Cells = new();
    public List<TrdpTableColumn> Columns = new();
    public List<TrdpTableRow> Rows = new();
    public List<TrdpTableGroup> RowGroups = new();
    public List<TrdpTableGroup> ColumnGroups = new();
    public TrdpTableRow? Corner;
}

public sealed class TrdpTableCell
{
    public int RowIndex, ColumnIndex, RowSpan = 1, ColumnSpan = 1;
    public TrdpItem? Item;
}

public sealed class TrdpTableColumn { public double Width; }
public sealed class TrdpTableRow { public double Height; }

/// <summary>A TableGroup -- its ReportItem is the column/row header template.</summary>
public sealed class TrdpTableGroup
{
    public string Name = "";
    public TrdpItem? Header;
    public string Grouping = "";
}

public sealed class TrdpSubReport : TrdpItem
{
    public string Uri = "";
    public TrdpReport? Report;
}
