using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Pt2Conv;

/// <summary>
/// Renders a TriStation Telerik <c>.trdp</c> template against decoded project data
/// (<see cref="ReportData"/>) to a PDF, using PdfSharpCore only. Reproduces the template's
/// page setup, page header/footer repetition, the detail table's row flow across pages,
/// sub-reports and the SOE cross-tab. Expression evaluation covers the <c>Fields.[X]</c>
/// data references, literals with <c>{PageNumber}</c>/<c>{PageCount}</c>/<c>{ExecutionTime}</c>,
/// and (as a passthrough) the <c>TCXReportFieldCalulator</c> helper calls the templates use.
/// </summary>
public static class ReportRenderer
{
    private static readonly Dictionary<string, TrdpReport?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opt-in trace of every string drawn (filled only when <see cref="Trace"/> is true).</summary>
    public static bool Trace;
    public static readonly List<string> TracedText = new();
    private static void T(string s) { if (Trace && s.Length > 0) TracedText.Add(s); }

    /// <summary>Renders a single report template to its own document.</summary>
    public static void Write(TrdpReport template, ReportData data, string outPath, string? reportFilter = null)
    {
        var doc = new PdfDocument();
        doc.Info.Title = template.Name;
        var ctx = new Ctx(doc, template, data);
        RenderReport(ctx, template);
        doc.Save(outPath);
    }

    /// <summary>Renders the standard user-program report suite; each report starts on a new page.</summary>
    public static void WriteSuite(ReportData data, string outPath, string? reportFilter = null)
    {
        var doc = new PdfDocument();
        RenderInto(doc, data, reportFilter);
        doc.Save(outPath);
    }

    /// <summary>
    /// Renders the report suite into an existing document, appending after whatever pages it
    /// already holds. Used to combine FBD sheets and reports into a single PDF.
    /// </summary>
    public static void RenderInto(PdfDocument doc, ReportData data, string? reportFilter = null)
    {
        if (string.IsNullOrWhiteSpace(doc.Info.Title))
            doc.Info.Title = data.ProjectName.Length > 0 ? data.ProjectName : "TriStation Reports";
        var ctx = new Ctx(doc, null, data);
        foreach (var t in SuiteTemplates(reportFilter))
            RenderReport(ctx, t);
        if (doc.PageCount == 0) doc.AddPage();
    }

    /// <summary>Renders one report into an existing document (new page), stamping its page numbers.</summary>
    private static void RenderReport(Ctx ctx, TrdpReport t)
    {
        ctx.Template = t;
        ctx.RowSets = Ctx.RowsFor(t, ctx.Data);
        int startPage = ctx.Doc.PageCount;
        int page = -1;
        ctx.StartPage(ref page);
        DrawSection(ctx, t.Sections.FirstOrDefault(s => s.Kind == "PageHeader"), ctx.PageTop, header: true);
        ctx.DetailTop = ctx.PageTop + ctx.HeaderHeight;
        ctx.FlowDetail();
        ctx.StampPageNumbers(startPage);
    }

    private static List<TrdpReport> SuiteTemplates(string? filter)
    {
        var names = new (string File, string[] Keys)[]
        {
            ("_POUReport.trdp", new[] { "pou", "pouvariables", "variables" }),
            ("_InstVarReport.trdp", new[] { "inst", "instance" }),
            ("ProgramVariableListing.trdp", new[] { "programvariable", "pvlisting" }),
            ("TagnameCompleteListing.trdp", new[] { "tag", "tagnames" }),
            ("_POUXRefReport.trdp", new[] { "xref", "cross" }),
            ("_SharedLibReport.trdp", new[] { "shared", "lib" }),
            ("ProjectImplementation.trdp", new[] { "projectimpl", "implementation" }),
        };
        var list = new List<TrdpReport>();
        foreach (var (file, keys) in names)
        {
            if (filter != null && !keys.Any(k => file.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                                k.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                continue;
            var t = TrdpReader.LoadEmbedded(file, _cache);
            if (t != null)
            {
                if (file.StartsWith("ProjectImplementation"))
                    ResolveSubReports(t);
                list.Add(t);
            }
        }
        return list;
    }

    private static void ResolveSubReports(TrdpReport t)
    {
        foreach (var item in t.Sections.SelectMany(s => s.Items))
            ResolveSubReports(item);
    }

    private static void ResolveSubReports(TrdpItem item)
    {
        if (item is TrdpSubReport sr && sr.Uri.Length > 0)
            sr.Report = TrdpReader.LoadEmbedded(sr.Uri, _cache);
        else if (item is TrdpPanel p)
            foreach (var c in p.Items) ResolveSubReports(c);
    }

    // ---- rendering context --------------------------------------------------

    private sealed class Ctx
    {
        public readonly PdfDocument Doc;
        public TrdpReport? Template;
        public readonly ReportData Data;
        public List<Dictionary<string, string>> RowSets = new();

        public double PageW, PageH;
        public double PageLeft, PageTop, ContentW, ContentH;
        public double HeaderHeight;
        public double DetailTop;

        public int CurrentPage = -1;
        public PdfPage PdfPage = null!;
        public XGraphics Gfx = null!;

        // Rows already emitted (detail flow state lives on the ctx so sub-reports can be drawn anywhere).
        public int RowCursor;

        // Deferred page-number stamps: (pageIndex, x, y, template text).
        public readonly List<(int PageIndex, double X, double Y, string Text, XFont Font, XBrush Brush, string Align)> StampScript = new();

        public Ctx(PdfDocument doc, TrdpReport? template, ReportData data)
        {
            Doc = doc;
            Template = template;
            Data = data;
        }

        public static List<Dictionary<string, string>> RowsFor(TrdpReport t, ReportData d)
        {
            if (t.Name.Contains("POU Variable", StringComparison.OrdinalIgnoreCase)) return d.PouReport;
            if (t.Name.Contains("Instance Variable", StringComparison.OrdinalIgnoreCase)) return d.InstVarReport;
            if (t.Name.Contains("Program Variable", StringComparison.OrdinalIgnoreCase)) return d.ProgramVariableListing;
            if (t.Name.Contains("Tagnames", StringComparison.OrdinalIgnoreCase)) return d.TagnameCompleteListing;
            if (t.Name.Contains("Cross-Reference", StringComparison.OrdinalIgnoreCase)) return d.PouXRefReport;
            if (t.Name.Contains("Shared Librar", StringComparison.OrdinalIgnoreCase)) return d.SharedLibReport;
            if (t.Name.Contains("Program Execution", StringComparison.OrdinalIgnoreCase)) return d.ProgramExecutionList;
            if (t.Name.Contains("General Parameters", StringComparison.OrdinalIgnoreCase)) return d.GeneralParameters;
            if (t.Name.Contains("Sequence of Event", StringComparison.OrdinalIgnoreCase)) return d.SequenceOfEvents;
            return new List<Dictionary<string, string>>();
        }

        public void StartPage(ref int page)
        {
            var t = Template;
            double w = t?.Width ?? 612, hgt = t?.Height ?? 792;
            if (w <= 0) w = 612;
            if (hgt <= 0) hgt = 792;
            PdfPage = Doc.AddPage();
            PdfPage.Width = XUnit.FromPoint(w);
            PdfPage.Height = XUnit.FromPoint(hgt);
            Gfx = XGraphics.FromPdfPage(PdfPage);
            CurrentPage = Doc.PageCount - 1;
            page = CurrentPage;

            PageW = w;
            PageH = hgt;
            PageLeft = t?.MarginLeft ?? 0;
            PageTop = t?.MarginTop ?? 0;
            ContentW = w - (t?.MarginLeft ?? 0) - (t?.MarginRight ?? 0);
            ContentH = hgt - (t?.MarginTop ?? 0) - (t?.MarginBottom ?? 0);
        }

        public void FinishPage()
        {
            DrawSection(this, Template?.Sections.FirstOrDefault(s => s.Kind == "PageFooter"), PageH - (Template?.MarginBottom ?? 0) - (Template?.Sections.FirstOrDefault(s => s.Kind == "PageFooter")?.Height ?? 0), header: false);
            Gfx.Dispose();
        }

        private bool _finished = false;
        public void FinishLastPage()
        {
            if (_finished) return;
            // ReportFooter (rarely has content) prints at the end of the report.
            var rf = Template?.Sections.FirstOrDefault(s => s.Kind == "ReportFooter");
            if (rf != null && rf.Height > 0)
                DrawSection(this, rf, DetailTop, header: false);
            FinishPage();
            _finished = true;
        }

        public void FlowDetail()
        {
            var t = Template;
            if (t == null) return;
            var detail = t.Sections.FirstOrDefault(s => s.Kind == "Detail");
            if (detail == null) return;

            _finished = false;
            RowCursor = 0;

            var primaryTable = detail.Items.OfType<TrdpTable>().FirstOrDefault();
            if (primaryTable == null)
            {
                // No data table -- draw the detail section once and finish.
                DrawSection(this, detail, DetailTop, header: false);
                FinishLastPage();
                return;
            }

            double rowH = Math.Max(TableRowHeight(primaryTable), 4);
            bool headersEvery = primaryTable.ColumnHeadersEveryPage;
            bool first = true;

            while (true)
            {
                if (first || headersEvery)
                    DrawSection(this, t.Sections.FirstOrDefault(s => s.Kind == "PageHeader"), PageTop, header: true);

                double y = DetailTop;
                double bottom = PageH - t.MarginBottom -
                                (t.Sections.FirstOrDefault(s => s.Kind == "PageFooter")?.Height ?? 0);

                y = DrawTableHeader(this, primaryTable, rowH, y);

                bool drewAny = false;
                while (RowCursor < RowSets.Count)
                {
                    if (y + rowH > bottom && drewAny) break;
                    DrawTableRow(this, primaryTable, RowSets[RowCursor], y, rowH);
                    y += rowH;
                    RowCursor++;
                    drewAny = true;
                }

                first = false;
                FinishLastPage();
                if (RowCursor >= RowSets.Count) break;

                _finished = false;
                int page = -1;
                StartPage(ref page);
            }
        }
    }

    /// <summary>Draws the table's column-header band (from ColumnGroups) and returns the new Y.</summary>
    private static double DrawTableHeader(Ctx ctx, TrdpTable t, double rowH, double y)
    {
        if (t.ColumnGroups.Count == 0 || t.ColumnGroups.All(g => g.Header == null)) return y;
        double hh = rowH * 0.9;
        foreach (var g in t.ColumnGroups)
            if (g.Header != null) hh = Math.Max(hh, g.Header.Height > 0 ? g.Header.Height : rowH * 0.9);
        double x = t.Left > 0 ? ctx.PageLeft + t.Left : ctx.PageLeft;
        foreach (var g in t.ColumnGroups)
        {
            double cw = ColumnWidth(t, g);
            if (g.Header != null) DrawItem(ctx, g.Header, x, y, cw, hh, null);
            x += cw;
        }
        return y + hh;
    }

    // ---- layout helpers -----------------------------------------------------

    private static double TableRowHeight(TrdpTable t)
    {
        if (t.Rows.Count > 0) return t.Rows[0].Height;
        var first = t.Cells.FirstOrDefault();
        if (first?.Item != null && first.Item.Height > 0) return first.Item.Height;
        return 18;
    }

    private static double TableWidth(TrdpTable t)
    {
        if (t.Columns.Count > 0) return t.Columns.Sum(c => c.Width);
        double max = 0;
        foreach (var c in t.Cells)
            max = Math.Max(max, c.ColumnIndex + c.ColumnSpan);
        if (max > 0)
        {
            var widths = new double[(int)max];
            foreach (var c in t.Cells)
                if (c.Item != null) widths[c.ColumnIndex] = Math.Max(widths[c.ColumnIndex], c.Item.Width);
            return widths.Sum();
        }
        return t.Width > 0 ? t.Width : 400;
    }

    private static double ColumnWidth(TrdpTable t, TrdpTableGroup g)
    {
        // Column groups are laid out left-to-right at the table's column widths.
        int idx = t.ColumnGroups.IndexOf(g);
        if (t.Columns.Count > 0 && idx < t.Columns.Count) return t.Columns[idx].Width;
        return t.Cells.FirstOrDefault(c => c.ColumnIndex == idx)?.Item?.Width ?? (t.Width / Math.Max(1, t.ColumnGroups.Count));
    }

    private static double[] ColumnWidths(TrdpTable t)
    {
        int n = 0;
        foreach (var c in t.Cells) n = Math.Max(n, c.ColumnIndex + c.ColumnSpan);
        n = Math.Max(n, t.Columns.Count);
        var w = new double[Math.Max(1, n)];
        for (int i = 0; i < t.Columns.Count && i < w.Length; i++) w[i] = t.Columns[i].Width;
        foreach (var c in t.Cells)
            if (c.Item != null && c.ColumnIndex < w.Length && w[c.ColumnIndex] <= 0) w[c.ColumnIndex] = c.Item.Width;
        return w;
    }

    private static void DrawTableRow(Ctx ctx, TrdpTable t, Dictionary<string, string> row, double y, double rowH)
    {
        var widths = ColumnWidths(t);
        double tableLeft = t.Left > 0 ? ctx.PageLeft + t.Left : ctx.PageLeft;
        foreach (var cell in t.Cells)
        {
            if (cell.Item == null) continue;
            double x = tableLeft;
            for (int i = 0; i < cell.ColumnIndex && i < widths.Length; i++) x += widths[i];
            double w = 0;
            for (int i = 0; i < cell.ColumnSpan && cell.ColumnIndex + i < widths.Length; i++) w += widths[cell.ColumnIndex + i];
            double h = rowH * cell.RowSpan;
            DrawItem(ctx, cell.Item, x, y, w, h, row);
        }
    }

    // ---- section / item drawing --------------------------------------------

    private static void DrawSection(Ctx ctx, TrdpSection? sec, double top, bool header)
    {
        if (sec == null) return;
        if (header) ctx.HeaderHeight = sec.Height;
        foreach (var item in sec.Items)
            DrawItem(ctx, item, ctx.PageLeft + item.Left, top + item.Top, item.Width, item.Height, null);
    }

    private static void DrawItem(Ctx ctx, TrdpItem item, double x, double y, double w, double h, Dictionary<string, string>? row)
    {
        if (!item.Visible) return;
        if (item.Width > 0 && w <= 0) w = item.Width;
        if (item.Height > 0 && h <= 0) h = item.Height;

        switch (item)
        {
            case TrdpPanel p:
                foreach (var c in p.Items)
                    DrawItem(ctx, c, x + c.Left, y + c.Top, c.Width, c.Height, row);
                break;
            case TrdpTextBox tb:
                DrawTextBox(ctx, tb, x, y, w, h, row);
                break;
            case TrdpShape sh:
                DrawShape(ctx, sh, x, y, w, h);
                break;
            case TrdpPicture pic:
                DrawPicture(ctx, pic, x, y, w, h);
                break;
            case TrdpSubReport sr:
                DrawSubReport(ctx, sr, x, y, w, h);
                break;
            case TrdpTable t:
                DrawTable(ctx, t, x, y, row);
                break;
        }
    }

    private static void DrawTextBox(Ctx ctx, TrdpTextBox tb, double x, double y, double w, double h, Dictionary<string, string>? row)
    {
        if (w <= 0) w = 60;
        if (h <= 0) h = 12;

        string text = Evaluate(tb.Value, ctx, row);
        bool deferred = text.Contains("\u0001");   // {PageCount} known only after layout
        if (deferred) text = "";                    // stamped later
        var font = Font(tb);
        var brush = Brush(tb.Color.Length > 0 ? tb.Color : "Black");
        double padL = tb.PadLeft > 0 ? tb.PadLeft : 2;
        double padR = tb.PadRight > 0 ? tb.PadRight : 2;
        double padT = tb.PadTop;
        double padB = tb.PadBottom;

        if (tb.BackgroundColor.Length > 0 && !tb.BackgroundColor.Equals("White", StringComparison.OrdinalIgnoreCase))
            ctx.Gfx.DrawRectangle(new XSolidBrush(ColorOf(tb.BackgroundColor)), x, y, w, h);

        if (tb.LineStyle.Length > 0 && !tb.LineStyle.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            var pen = new XPen(tb.LineColor.Length > 0 ? ColorOf(tb.LineColor) : XColors.Black, tb.BorderWidth > 0 ? tb.BorderWidth : 0.5);
            ctx.Gfx.DrawRectangle(pen, x, y, w, h);
        }

        double textW = Math.Max(2, w - padL - padR);
        var lines = WrapText(ctx, text, font, textW);
        double lineH = font.Size * 1.2;
        double totalH = lines.Count * lineH;
        double startY = y + padT;
        if (tb.VerticalAlign.Equals("Middle", StringComparison.OrdinalIgnoreCase)) startY = y + (h - totalH) / 2;
        else if (tb.VerticalAlign.Equals("Bottom", StringComparison.OrdinalIgnoreCase)) startY = y + h - padB - totalH;
        startY += font.Size * 0.9;

        bool center = tb.TextAlign.Equals("Center", StringComparison.OrdinalIgnoreCase);
        bool right = tb.TextAlign.Equals("Right", StringComparison.OrdinalIgnoreCase);
        double clipBottom = y + h + 0.5;

        var state = ctx.Gfx.Save();
        foreach (var ln in lines)
        {
            if (startY > clipBottom && !tb.CanGrow) break;
            double drawX = center ? x + w / 2 - ctx.Gfx.MeasureString(ln, font).Width / 2
                         : right ? x + w - padR - ctx.Gfx.MeasureString(ln, font).Width
                         : x + padL;
            T(ln);
            var fmt = center ? XStringFormats.Default : XStringFormats.Default;
            ctx.Gfx.DrawString(ln, font, brush, drawX, startY);
            startY += lineH;
        }
        ctx.Gfx.Restore(state);

        if (deferred || text.Contains("{PageNumber}"))
        {
            string stampText = tb.Value.TrimStart('=').Trim();
            ctx.StampScript.Add((ctx.CurrentPage, x + padL, startY - font.Size * 0.9, stampText, font, brush, tb.TextAlign));
        }
    }

    private static void DrawShape(Ctx ctx, TrdpShape sh, double x, double y, double w, double h)
    {
        var pen = new XPen(sh.LineColor.Length > 0 ? ColorOf(sh.LineColor) : XColors.Black, sh.LineWidth > 0 ? sh.LineWidth : 1);
        if (sh.Kind == "Line")
        {
            double cy = y + h / 2;
            ctx.Gfx.DrawLine(pen, x, cy, x + w, cy);
        }
        else if (sh.Kind == "Ellipse")
        {
            ctx.Gfx.DrawEllipse(pen, x, y, w, h);
        }
        else
        {
            if (sh.BackgroundColor.Length > 0 && !sh.BackgroundColor.Equals("White", StringComparison.OrdinalIgnoreCase))
                ctx.Gfx.DrawRectangle(pen, new XSolidBrush(ColorOf(sh.BackgroundColor)), x, y, w, h);
            else
                ctx.Gfx.DrawRectangle(pen, x, y, w, h);
        }
    }

    private static void DrawPicture(Ctx ctx, TrdpPicture pic, double x, double y, double w, double h)
    {
        if (pic.ImageBytes == null || pic.ImageBytes.Length == 0) return;
        try
        {
            using var ms = new MemoryStream(pic.ImageBytes);
            var img = XImage.FromStream(() => new MemoryStream(pic.ImageBytes));
            if (img == null) return;
            ctx.Gfx.DrawImage(img, x, y, w > 0 ? w : img.PointWidth, h > 0 ? h : img.PointHeight);
        }
        catch { /* image decode failed -- skip */ }
    }

    private static void DrawSubReport(Ctx ctx, TrdpSubReport sr, double x, double y, double w, double h)
    {
        var rpt = sr.Report;
        if (rpt == null) return;

        var rows = Ctx.RowsFor(rpt, ctx.Data);
        // Sub-reports for ProjectImplementation: use the specific row sets by URI.
        if (sr.Uri.Contains("ProgramExecutionList", StringComparison.OrdinalIgnoreCase)) rows = ctx.Data.ProgramExecutionList;
        else if (sr.Uri.Contains("GeneralParameters", StringComparison.OrdinalIgnoreCase)) rows = ctx.Data.GeneralParameters;
        else if (sr.Uri.Contains("SequenceOfEvents", StringComparison.OrdinalIgnoreCase)) rows = ctx.Data.SequenceOfEvents;
        else if (sr.Uri.Contains("CrossTab", StringComparison.OrdinalIgnoreCase)) rows = new List<Dictionary<string, string>>();

        var detail = rpt.Sections.FirstOrDefault(s => s.Kind == "Detail");
        if (detail == null) return;

        // Title / static text and the crosstab / data table, in the sub-report's own coordinates.
        foreach (var item in detail.Items)
        {
            if (item is TrdpTable tbl && tbl.IsCrosstab)
            {
                DrawCrosstab(ctx, tbl, x + tbl.Left, y + tbl.Top, w, h);
                continue;
            }
            if (item is TrdpTable dt)
            {
                DrawSubReportTable(ctx, dt, rows, x, y);
                continue;
            }
            DrawItem(ctx, item, x + item.Left, y + item.Top, item.Width, item.Height, rows.FirstOrDefault());
        }
    }

    private static void DrawSubReportTable(Ctx ctx, TrdpTable table, List<Dictionary<string, string>> rows, double x, double y)
    {
        double rowH = TableRowHeight(table);
        double dy = y + (table.Top > 0 ? table.Top : 0);
        dy = DrawSubReportHeader(ctx, table, x, rowH, dy);
        foreach (var row in rows)
        {
            DrawTableRow(ctx, table, row, dy, rowH);
            dy += rowH;
        }
    }

    private static double DrawSubReportHeader(Ctx ctx, TrdpTable table, double x, double rowH, double dy)
    {
        if (table.ColumnGroups.Count == 0 || table.ColumnGroups.All(g => g.Header == null)) return dy;
        double hh = rowH * 0.9;
        foreach (var g in table.ColumnGroups)
            if (g.Header != null) hh = Math.Max(hh, g.Header.Height > 0 ? g.Header.Height : rowH * 0.9);
        double cx = x + table.Left;
        foreach (var g in table.ColumnGroups)
        {
            double cw = ColumnWidth(table, g);
            if (g.Header != null) DrawItem(ctx, g.Header, cx, dy, cw, hh, null);
            cx += cw;
        }
        return dy + hh;
    }

    private static void DrawTable(Ctx ctx, TrdpTable t, double x, double y, Dictionary<string, string>? row)
    {
        // Non-data table (e.g. a static layout table) -- draw its cells once.
        double rowH = TableRowHeight(t);
        DrawTableRow(ctx, t, row ?? new Dictionary<string, string>(), y, rowH);
    }

    /// <summary>Renders the SOE cross-tab using the template's own corner text, row group
    /// (tag names) and column group (block numbers), plus a trailing "Total" column.</summary>
    private static void DrawCrosstab(Ctx ctx, TrdpTable ct, double x, double y, double w, double h)
    {
        var assigns = ctx.Data.SoeAssignments;
        var tags = ctx.Data.SoeAssignments.Select(a => a.Tagname)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        var blocks = assigns.Select(a => a.BlockNum).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => int.TryParse(b, out var n) ? n : 0).ToList();

        var corner = ct.CornerText.Length > 0 ? ct.CornerText : "Tagname";
        var rowGroup = ct.RowGroups.FirstOrDefault();
        var colGroup = ct.ColumnGroups.FirstOrDefault();
        bool hasTotal = ct.ColumnGroups.Count > 1;

        var rowHeader = rowGroup?.Header as TrdpTextBox;
        var colHeader = colGroup?.Header as TrdpTextBox;

        double cellH = Math.Max(TableRowHeight(ct), 10);
        double rowLabelW = rowHeader != null && rowHeader.Width > 0 ? rowHeader.Width : 60;
        double colW = colHeader != null && colHeader.Width > 0 ? colHeader.Width : 24;

        double gfxX = x, gfxY = y;
        // Corner
        DrawCrosstabCell(ctx, corner, gfxX, gfxY, rowLabelW, cellH, true, true, true, true);
        // Column headers
        for (int c = 0; c < blocks.Count; c++)
            DrawCrosstabCell(ctx, blocks[c], gfxX + rowLabelW + c * colW, gfxY, colW, cellH, true, true, true, true);
        if (hasTotal)
            DrawCrosstabCell(ctx, "Total", gfxX + rowLabelW + blocks.Count * colW, gfxY, colW, cellH, true, true, true, true);

        for (int r = 0; r < tags.Count; r++)
        {
            double ry = gfxY + cellH * (r + 1);
            int rowTotal = 0;
            for (int c = 0; c < blocks.Count; c++)
            {
                int count = assigns.Count(a => a.Tagname.Equals(tags[r], StringComparison.OrdinalIgnoreCase)
                                               && a.BlockNum.Equals(blocks[c], StringComparison.OrdinalIgnoreCase));
                rowTotal += count;
                DrawCrosstabCell(ctx, count > 0 ? count.ToString() : "", gfxX + rowLabelW + c * colW, ry, colW, cellH, true, true, true, true);
            }
            DrawCrosstabCell(ctx, tags[r], gfxX, ry, rowLabelW, cellH, true, true, true, true);
            if (hasTotal)
                DrawCrosstabCell(ctx, rowTotal > 0 ? rowTotal.ToString() : "", gfxX + rowLabelW + blocks.Count * colW, ry, colW, cellH, true, true, true, true);
        }
    }

    private static void DrawCrosstabCell(Ctx ctx, string text, double x, double y, double w, double h, bool left, bool top, bool right, bool bottom)
    {
        var gfx = ctx.Gfx;
        var pen = new XPen(XColors.Black, 0.5);
        if (left) gfx.DrawLine(pen, x, y, x, y + h);
        if (right) gfx.DrawLine(pen, x + w, y, x + w, y + h);
        if (top) gfx.DrawLine(pen, x, y, x + w, y);
        if (bottom) gfx.DrawLine(pen, x, y + h, x + w, y + h);
        if (text.Length == 0) return;
        var font = _crosstabFont;
        double tw = gfx.MeasureString(text, font).Width;
        double tx = w >= tw ? x + (w - tw) / 2 : x + 1;
        T(text);
        gfx.DrawString(text, font, XBrushes.Black, tx, y + h - 3);
    }

    public static List<string> DumpText() => TracedText;

    private static readonly XFont _crosstabFont = new("Arial", 8);

    // ---- expression evaluation ----------------------------------------------

    private static string Evaluate(string expr, Ctx ctx, Dictionary<string, string>? row)
    {
        if (string.IsNullOrEmpty(expr)) return "";
        string s = expr.Trim();
        if (!s.StartsWith("="))
        {
            // Literal (e.g. "Page {PageNumber} of {PageCount}"): resolve tokens now,
            // except page count which is only known after the report is laid out.
            return Substitute(s, ctx);
        }

        s = s.Substring(1).Trim();

        // Strip a trailing calculator/whitespace and function wrapping.
        string result = s;

        // TCXReportFieldCalulator.FieldCalculator.<fn>(FIELDS.[X]) --> passthrough of inner field.
        int paren = s.IndexOf('(');
        if (s.Contains("FieldCalculat", StringComparison.OrdinalIgnoreCase) && paren > 0)
        {
            int close = s.LastIndexOf(')');
            string inner = close > paren ? s.Substring(paren + 1, close - paren - 1) : "";
            string field = ExtractField(inner);
            if (field.Length > 0 && row != null) return GetField(row, field);
            return inner.Trim().Trim('[', ']');
        }

        if (s.StartsWith("Count(", StringComparison.OrdinalIgnoreCase))
            return (row != null ? 1 : 0).ToString(CultureInfo.InvariantCulture);

        if (s.StartsWith("RowNumber", StringComparison.OrdinalIgnoreCase))
            return (ctx.RowCursor + 1).ToString(CultureInfo.InvariantCulture);

        // A bare field reference.
        string f2 = ExtractField(s);
        if (f2.Length > 0)
        {
            if (row != null) return GetField(row, f2);
            return ctx.Data.Header.TryGetValue(f2, out var hv) ? hv : "";
        }

        // Literal with tokens.
        return Substitute(expr, ctx);
    }

    private static string ExtractField(string s)
    {
        s = s.Trim();
        int f = s.IndexOf("Fields", StringComparison.OrdinalIgnoreCase);
        if (f < 0) return "";
        string rest = s.Substring(f + "Fields".Length).Trim();
        if (rest.StartsWith(".")) rest = rest.Substring(1).Trim();
        rest = rest.Trim();
        if (rest.StartsWith("[")) return rest.Substring(1).TrimEnd(']', ' ', ')', '\r', '\n', ';').Trim();
        // bare identifier
        int end = 0;
        while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] == '_')) end++;
        return rest.Substring(0, end);
    }

    private static string GetField(Dictionary<string, string> row, string field)
    {
        if (row.TryGetValue(field, out var v)) return v;
        // Header fallback for header-only fields.
        return "";
    }

    private static string Substitute(string literal, Ctx ctx)
    {
        if (!literal.Contains('{')) return literal;
        string outText = literal;
        outText = outText.Replace("{PageNumber}", (ctx.CurrentPage + 1).ToString(CultureInfo.InvariantCulture));
        outText = outText.Replace("{ExecutionTime}", DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        outText = outText.Replace("{PageCount}", "\u0001"); // placeholder stamped later
        return outText;
    }

    private static string GetHeader(Ctx ctx, string field)
        => ctx.Data.Header.TryGetValue(field, out var v) ? v : "";

    // ---- text / font helpers ------------------------------------------------

    private static readonly Dictionary<string, XFont> _fonts = new(StringComparer.OrdinalIgnoreCase);

    private static XFont Font(TrdpTextBox tb)
    {
        string name = tb.FontName.Length > 0 ? tb.FontName : "Arial";
        double size = tb.FontSize > 0 ? tb.FontSize : 10;
        var style = XFontStyle.Regular;
        if (tb.Bold && tb.Italic) style = XFontStyle.BoldItalic;
        else if (tb.Bold) style = XFontStyle.Bold;
        else if (tb.Italic) style = XFontStyle.Italic;
        string key = name + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + (int)style;
        if (_fonts.TryGetValue(key, out var f)) return f;
        try { f = new XFont(name, size, style); }
        catch { f = new XFont("Arial", size, style); }
        _fonts[key] = f;
        return f;
    }

    private static List<string> WrapText(Ctx ctx, string text, XFont font, double maxW)
    {
        var result = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (ctx.Gfx.MeasureString(raw, font).Width <= maxW || raw.Length == 0)
            {
                result.Add(raw);
                continue;
            }
            string cur = "";
            foreach (var word in raw.Split(' '))
            {
                string trial = cur.Length == 0 ? word : cur + " " + word;
                if (ctx.Gfx.MeasureString(trial, font).Width <= maxW) cur = trial;
                else { if (cur.Length > 0) result.Add(cur); cur = word; }
            }
            if (cur.Length > 0) result.Add(cur);
        }
        return result.Count > 0 ? result : new List<string> { "" };
    }

    private static readonly Dictionary<string, XColor> _colors = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, XBrush> _brushes = new(StringComparer.OrdinalIgnoreCase);

    private static XColor ColorOf(string name)
    {
        if (_colors.TryGetValue(name, out var c)) return c;
        c = name.ToLowerInvariant() switch
        {
            "black" => XColors.Black,
            "white" => XColors.White,
            "red" => XColors.Red,
            "blue" => XColors.Blue,
            "green" => XColors.Green,
            "gray" or "grey" => XColors.Gray,
            "silver" => XColors.Silver,
            _ => TryHex(name) ?? XColors.Black,
        };
        _colors[name] = c;
        return c;
    }

    private static XColor? TryHex(string s)
    {
        s = s.TrimStart('#');
        if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return XColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        if (s.Length == 8 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            return XColor.FromArgb((argb >> 24) & 0xFF, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
        return null;
    }

    private static XBrush Brush(string name)
    {
        if (_brushes.TryGetValue(name, out var b)) return b;
        b = new XSolidBrush(ColorOf(name));
        _brushes[name] = b;
        return b;
    }

    // ---- deferred page numbers ----------------------------------------------

    private static void StampPageNumbers(this Ctx ctx, int startPage)
    {
        // Count pages belonging to this report.
        int total = ctx.Doc.PageCount - startPage;
        if (total <= 0) return;

        foreach (var stamp in ctx.StampScript)
        {
            if (stamp.PageIndex < startPage) continue;
            var page = ctx.Doc.Pages[stamp.PageIndex];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            int pageNumber = stamp.PageIndex - startPage + 1;
            string text = stamp.Text
                .Replace("{PageNumber}", pageNumber.ToString(CultureInfo.InvariantCulture))
                .Replace("{PageCount}", total.ToString(CultureInfo.InvariantCulture))
                .Replace("{ExecutionTime}", DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture))
                .Replace("\u0001", total.ToString(CultureInfo.InvariantCulture));
            var sz = gfx.MeasureString(text, stamp.Font);
            double drawX = stamp.Align.Equals("Center", StringComparison.OrdinalIgnoreCase) ? stamp.X + (ctx.PageW - 2 * ctx.PageLeft) / 2 - sz.Width / 2
                         : stamp.Align.Equals("Right", StringComparison.OrdinalIgnoreCase) ? stamp.X + ctx.ContentW - sz.Width
                         : stamp.X;
            gfx.DrawString(text, stamp.Font, stamp.Brush, drawX, stamp.Y + stamp.Font.Size * 0.9);
        }
        ctx.StampScript.Clear();
    }
}
