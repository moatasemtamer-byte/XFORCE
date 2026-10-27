using System.Globalization;
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
///
/// The report body is laid out from the data, not from the template's fixed row boxes:
/// column widths are measured from the content, row heights grow with wrapped lines, cells
/// are clipped, and the column-header band and page header repeat on every page.
/// </summary>
public static class ReportRenderer
{
    private static readonly Dictionary<string, TrdpReport?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opt-in trace of every string drawn (filled only when <see cref="Trace"/> is true).</summary>
    public static bool Trace;
    public static readonly List<string> TracedText = new();
    private static void T(string s) { if (Trace && s.Length > 0) TracedText.Add(s); }

    /// <summary>Opt-in geometry trace of the layout (one TSV row per text/cell/row/band/page).</summary>
    public static bool LayoutTrace;
    public static readonly List<string> LayoutDump = new();
    private static void L(Ctx? ctx, string kind, double x, double y, double w, double h, string text, XFont? font)
    {
        if (!LayoutTrace) return;
        string t = (text ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        string fs = font != null ? font.Size.ToString("0.##", CultureInfo.InvariantCulture) : "";
        LayoutDump.Add(string.Join("\t",
            (ctx?.CurrentPage ?? 0).ToString(CultureInfo.InvariantCulture), kind,
            Fmt(x), Fmt(y), Fmt(w), Fmt(h), font?.Name ?? "", fs, t));
    }
    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static List<string> DumpText() => TracedText;
    public static List<string> DumpLayout() => LayoutDump;

    /// <summary>Renders a single report template to its own document.</summary>
    public static void Write(TrdpReport template, ReportData data, string outPath, string? reportFilter = null)
    {
        var doc = new PdfDocument();
        doc.Info.Title = template.Name;
        var ctx = new Ctx(doc, template, data);
        RenderReport(ctx, template);
        doc.Save(outPath);
        PdfPostProcess.NormalizeFontSubsetTags(outPath);
    }

    /// <summary>Renders the standard user-program report suite; each report starts on a new page.</summary>
    public static void WriteSuite(ReportData data, string outPath, string? reportFilter = null)
    {
        var doc = new PdfDocument();
        RenderInto(doc, data, reportFilter);
        doc.Save(outPath);
        PdfPostProcess.NormalizeFontSubsetTags(outPath);
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
        ctx.ResetReport();
        int startPage = ctx.Doc.PageCount;
        int page = -1;
        ctx.StartPage(ref page);
        ctx.BuildPlan();
        ctx.DrawPageHeader();
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
        public double PageLeft, PageTop, MarginRight, MarginBottom, ContentW, ContentH;
        public double BandScale = 1;
        public double HeaderHeight;
        public double DetailTop;
        public double LastRowBottom;

        public int CurrentPage = -1;
        public PdfPage PdfPage = null!;
        public XGraphics Gfx = null!;
        public bool PageClosed;

        // Detail flow state.
        public int RowCursor;
        public TablePlan? Plan;
        public readonly HashSet<TrdpItem> Suppress = new(ReferenceEqualityComparer.Instance);
        private bool _finished;

        public readonly List<Stamp> Stamps = new();

        public Ctx(PdfDocument doc, TrdpReport? template, ReportData data)
        {
            Doc = doc;
            Template = template;
            Data = data;
        }

        public void ResetReport()
        {
            RowSets ??= new();
            Plan = null;
            Suppress.Clear();
            Stamps.Clear();
            RowCursor = 0;
            HeaderHeight = 0;
            LastRowBottom = 0;
            PageClosed = false;
            _finished = false;
            _layoutGuard = 0;
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

        public List<Dictionary<string, string>> SubRows(TrdpSubReport sr)
        {
            if (sr.Uri.Contains("ProgramExecutionList", StringComparison.OrdinalIgnoreCase)) return Data.ProgramExecutionList;
            if (sr.Uri.Contains("GeneralParameters", StringComparison.OrdinalIgnoreCase)) return Data.GeneralParameters;
            if (sr.Uri.Contains("SequenceOfEvents", StringComparison.OrdinalIgnoreCase)) return Data.SequenceOfEvents;
            if (sr.Uri.Contains("CrossTab", StringComparison.OrdinalIgnoreCase)) return new List<Dictionary<string, string>>();
            return sr.Report != null ? RowsFor(sr.Report, Data) : new List<Dictionary<string, string>>();
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
            PageClosed = false;

            PageW = w;
            PageH = hgt;
            PageLeft = t?.MarginLeft ?? 0;
            PageTop = t?.MarginTop ?? 0;
            MarginRight = t?.MarginRight ?? 0;
            MarginBottom = t?.MarginBottom ?? 0;
            ContentW = Math.Max(72, w - PageLeft - MarginRight);
            ContentH = Math.Max(72, hgt - PageTop - MarginBottom);

            double design = t != null && t.DesignWidth > 0 ? t.DesignWidth : ContentW;
            BandScale = Math.Clamp(ContentW / Math.Max(72, design), 0.85, 1.25);

            L(this, "page", PageLeft, PageTop, ContentW, ContentH, "", null);
        }

        public double BodyBottom()
        {
            double footer = Template?.Sections.FirstOrDefault(s => s.Kind == "PageFooter")?.Height ?? 0;
            return PageH - MarginBottom - footer;
        }

        public void NewPage()
        {
            FinishPage();
            int page = -1;
            StartPage(ref page);
            DrawPageHeader();
            DetailTop = PageTop + HeaderHeight;
            LastRowBottom = DetailTop;
        }

        public void DrawPageHeader()
        {
            var sec = Template?.Sections.FirstOrDefault(s => s.Kind == "PageHeader");
            DrawSection(this, sec, PageTop, header: true);
            HeaderHeight = sec?.Height ?? 0;
        }

        public void BuildPlan()
        {
            var detail = Template?.Sections.FirstOrDefault(s => s.Kind == "Detail");
            var table = detail?.Items.OfType<TrdpTable>().FirstOrDefault();
            if (table == null) { Plan = null; return; }
            var header = Template?.Sections.FirstOrDefault(s => s.Kind == "PageHeader");
            double left = PageLeft + table.Left * BandScale;
            double avail = Math.Max(96, ContentW - table.Left * BandScale);
            Plan = TablePlan.Build(this, table, left, avail, header, null);
            foreach (var tb in Plan.LabelByColumn.Values) Suppress.Add(tb);
        }

        public void FinishPage()
        {
            if (PageClosed) return;
            var footer = Template?.Sections.FirstOrDefault(s => s.Kind == "PageFooter");
            if (footer != null)
                DrawSection(this, footer, PageH - MarginBottom - footer.Height, header: false);
            Gfx.Dispose();
            PageClosed = true;
        }

        public void FinishLastPage()
        {
            if (_finished) return;
            var rf = Template?.Sections.FirstOrDefault(s => s.Kind == "ReportFooter");
            if (rf != null && rf.Height > 0)
            {
                double y = Math.Min(LastRowBottom, PageH - MarginBottom - rf.Height);
                if (y >= PageTop) DrawSection(this, rf, y, header: false);
            }
            FinishPage();
            _finished = true;
        }

        private int _layoutGuard;

        public void FlowDetail()
        {
            var t = Template;
            if (t == null) return;
            var detail = t.Sections.FirstOrDefault(s => s.Kind == "Detail");
            if (detail == null) return;

            _finished = false;
            RowCursor = 0;
            _layoutGuard = 0;

            if (Plan == null)
            {
                var subs = detail.Items.OfType<TrdpSubReport>().ToList();
                if (subs.Count > 0) FlowSubReports(subs);
                else DrawSection(this, detail, DetailTop, header: false);
                FinishLastPage();
                return;
            }

            int[]? offsets = null;
            while (true)
            {
                if (++_layoutGuard > 5000) break;
                double bottom = BodyBottom();
                double y = Plan.DrawHeader(this, DetailTop);
                bool drewAny = false;

                while (RowCursor < RowSets.Count)
                {
                    if (offsets == null)
                    {
                        double rowH = Plan.RowHeights[RowCursor];
                        if (y + rowH <= bottom + 0.01)
                        {
                            DrawRow(this, Plan, RowCursor, RowSets[RowCursor], y);
                            y += rowH;
                            RowCursor++;
                            drewAny = true;
                            continue;
                        }
                        if (drewAny) break;
                        if (!Plan.IsMatrix && y > DetailTop + 0.5 && rowH <= bottom - DetailTop + 0.01)
                        {
                            // Absolute cell layout cannot be split line by line; start this row
                            // on a fresh page when it does not fit the remaining space.
                            break;
                        }
                        if (!Plan.IsMatrix)
                        {
                            DrawRow(this, Plan, RowCursor, RowSets[RowCursor], y);
                            y += rowH;
                            RowCursor++;
                            drewAny = true;
                            break;
                        }
                        if (rowH <= bottom - y + 0.01)
                        {
                            DrawRow(this, Plan, RowCursor, RowSets[RowCursor], y);
                            y += rowH;
                            RowCursor++;
                            drewAny = true;
                            continue;
                        }
                        // Taller than a whole page: split it across pages, line by line.
                        offsets = new int[Plan.Cells.Count];
                    }

                    bool complete = DrawRowPart(this, Plan, RowCursor, RowSets[RowCursor], offsets, y, bottom,
                        offsets.All(o => o == 0), out double partH);
                    y += partH;
                    drewAny = true;
                    if (complete) { RowCursor++; offsets = null; }
                    else break;
                }

                LastRowBottom = y;
                if (RowCursor >= RowSets.Count) break;
                if (!drewAny && y <= DetailTop + 0.5) break;
                NewPage();
            }

            FinishLastPage();
        }

        /// <summary>Flows the ProjectImplementation sub-reports one after another, breaking
        /// pages when a sub-report does not fit and repeating its table header when it spans.</summary>
        private void FlowSubReports(List<TrdpSubReport> subs)
        {
            double bottom = BodyBottom();
            double y = DetailTop;

            foreach (var sr in subs)
            {
                var rpt = sr.Report;
                if (rpt == null) continue;
                var sdetail = rpt.Sections.FirstOrDefault(s => s.Kind == "Detail");
                if (sdetail == null) continue;

                var rows = SubRows(sr);
                var dataTable = sdetail.Items.OfType<TrdpTable>().FirstOrDefault(t => !t.IsCrosstab);
                var cross = sdetail.Items.OfType<TrdpTable>().FirstOrDefault(t => t.IsCrosstab);
                var statics = sdetail.Items
                    .Where(i => !ReferenceEquals(i, dataTable) && !ReferenceEquals(i, cross))
                    .ToList();

                double ox = PageLeft + sr.Left * BandScale;
                double availW = Math.Max(60, sr.Width * BandScale);
                double staticH = 0;
                foreach (var s in statics) staticH = Math.Max(staticH, s.Top + (s.Height > 0 ? s.Height : 12));
                double tableTop = dataTable?.Top ?? (cross?.Top ?? 0);

                TablePlan? plan = null;
                if (dataTable != null)
                    plan = TablePlan.Build(this, dataTable, ox + dataTable.Left * BandScale,
                        availW - dataTable.Left * BandScale, null, rows);

                double headH = plan?.GroupHeaderH ?? 0;
                double firstRowH = plan != null && rows.Count > 0 ? plan.RowHeights[0] : 0;
                double need = Math.Max(staticH, tableTop) + headH + firstRowH + 2;
                if (y + need > bottom && y > DetailTop + 1)
                {
                    NewPage();
                    y = DetailTop;
                }

                DrawStatics(statics, ox, y, rows.FirstOrDefault());
                double curY;

                if (dataTable != null && plan != null)
                {
                    int idx = 0;
                    bool firstBlock = true;
                    while (true)
                    {
                        if (!firstBlock)
                        {
                            DrawStatics(statics, ox, y, idx < rows.Count ? rows[idx] : null);
                            curY = y + tableTop;
                        }
                        else curY = y + tableTop;

                        curY = plan.DrawHeader(this, curY);
                        while (idx < rows.Count && curY + plan.RowHeights[idx] <= bottom + 0.01)
                        {
                            DrawRow(this, plan, idx, rows[idx], curY);
                            curY += plan.RowHeights[idx];
                            idx++;
                        }
                        if (idx >= rows.Count) break;
                        if (curY <= y + tableTop + 0.5) break; // defensive: no progress
                        NewPage();
                        y = DetailTop;
                        firstBlock = false;
                    }
                }
                else if (cross != null)
                {
                    var (tags, blocks) = CrosstabData(this);
                    double cellH = CrosstabCellH(cross);
                    int r = 0;
                    bool firstBlock = true;
                    curY = y + tableTop;
                    while (true)
                    {
                        if (!firstBlock)
                        {
                            DrawStatics(statics, ox, y, null);
                            curY = y + tableTop;
                        }
                        curY = DrawCrosstabHeader(this, cross, ox, curY, blocks);
                        int drawn = 0;
                        while (r < tags.Count && curY + cellH <= bottom + 0.01)
                        {
                            DrawCrosstabRow(this, cross, ox, curY, blocks, tags[r], r);
                            curY += cellH;
                            r++;
                            drawn++;
                        }
                        if (r >= tags.Count) break;
                        if (drawn == 0 && !firstBlock) break;
                        NewPage();
                        y = DetailTop;
                        firstBlock = false;
                    }
                }
                else
                {
                    curY = y + Math.Max(staticH, tableTop);
                }

                y = curY + 8;
                LastRowBottom = y;
            }
        }

        private void DrawStatics(List<TrdpItem> statics, double ox, double y, Dictionary<string, string>? row)
        {
            foreach (var s in statics)
                DrawItem(this, s, ox + s.Left * BandScale, y + s.Top, s.Width * BandScale, s.Height, row);
        }
    }

    // ---- table layout -------------------------------------------------------

    private sealed class CellStyle
    {
        public XFont Font = null!;
        public double Size;
        public double LineHeight;
        public XBrush Brush = XBrushes.Black;
        public double PadL = 3, PadR = 3, PadT = 2, PadB = 2;
        public string Align = "Left";
        public string VAlign = "Top";
        public string Background = "";
        public string BorderStyle = "";
        public string BorderColor = "";
        public double BorderWidth;
    }

    private sealed class CellContent
    {
        public string Text = "";
        public List<string> Lines = new();
        public double Height;
    }

    private sealed class TablePlan
    {
        public TrdpTable Table = null!;
        public List<TrdpTableCell> Cells = new();
        public double Left;
        public double Available;
        public double[] Widths = Array.Empty<double>();
        public double TotalWidth;
        public double MinRowHeight = 17;
        public bool IsMatrix;
        public CellStyle?[] Styles = Array.Empty<CellStyle?>();
        public List<string[]> RowTexts = new();
        public List<CellContent[]> RowContent = new();
        public List<double> RowHeights = new();
        public Dictionary<int, TrdpTextBox> LabelByColumn = new();
        public double LabelTop;
        public double GroupHeaderH;

        public double ColX(int col)
        {
            double x = Left;
            for (int i = 0; i < col && i < Widths.Length; i++) x += Widths[i];
            return x;
        }

        public double ColW(int col, int span)
        {
            double w = 0;
            for (int i = 0; i < span && col + i < Widths.Length; i++) w += Widths[col + i];
            return w;
        }

        public static TablePlan Build(Ctx ctx, TrdpTable t, double left, double available,
            TrdpSection? headerSec, List<Dictionary<string, string>>? rowsOverride)
        {
            var rows = rowsOverride ?? ctx.RowSets;
            var plan = new TablePlan
            {
                Table = t,
                Left = left,
                Available = Math.Max(96, available),
                Cells = t.Cells,
            };

            int n = ColumnCount(t);
            plan.IsMatrix = t.Cells.Any(c => c.Item != null) &&
                            t.Cells.Where(c => c.Item != null).All(c => c.Item is TrdpTextBox);
            plan.MinRowHeight = Math.Max(8, TableRowHeight(t));
            var template = TemplateWidths(t, n, plan.Available);
            plan.Styles = BuildStyles(t, plan.IsMatrix);

            if (!plan.IsMatrix)
            {
                // Authored absolute layout (e.g. the Tagnames two-column cell): keep the design
                // proportions, only scale them with the page band.
                plan.Widths = template.Select(w => w * ctx.BandScale).ToArray();
                plan.TotalWidth = plan.Widths.Sum();
                plan.RowHeights = new List<double>(rows.Count);
                for (int r = 0; r < rows.Count; r++)
                {
                    double need = plan.MinRowHeight;
                    for (int i = 0; i < plan.Cells.Count; i++)
                    {
                        var cell = plan.Cells[i];
                        if (cell.Item == null) continue;
                        double cw = plan.ColW(cell.ColumnIndex, cell.ColumnSpan);
                        need = Math.Max(need, cell.Item.Top + MeasureContent(ctx, cell.Item, cw, plan.MinRowHeight, rows[r]));
                    }
                    plan.RowHeights.Add(need);
                }
                return plan;
            }

            if (headerSec != null) plan.MapLabels(template, headerSec);

            foreach (var row in rows)
            {
                var texts = new string[plan.Cells.Count];
                for (int i = 0; i < plan.Cells.Count; i++)
                {
                    var tb = plan.Cells[i].Item as TrdpTextBox;
                    texts[i] = tb == null ? "" : Evaluate(tb.Value, ctx, row);
                }
                plan.RowTexts.Add(texts);
            }

            plan.AllocateWidths(ctx, template, n);
            plan.BuildContent(ctx);
            plan.GroupHeaderH = plan.ComputeGroupHeaderHeight();
            return plan;
        }

        private static int ColumnCount(TrdpTable t)
        {
            int n = t.Columns.Count;
            foreach (var c in t.Cells) n = Math.Max(n, c.ColumnIndex + c.ColumnSpan);
            return Math.Max(1, n);
        }

        private static double[] TemplateWidths(TrdpTable t, int n, double available)
        {
            var w = new double[n];
            for (int i = 0; i < t.Columns.Count && i < n; i++) w[i] = t.Columns[i].Width;
            foreach (var c in t.Cells)
                if (c.Item != null && c.ColumnIndex < n && w[c.ColumnIndex] <= 0)
                    w[c.ColumnIndex] = c.Item.Width;
            double fallback = available / Math.Max(1, n);
            for (int i = 0; i < n; i++) if (w[i] <= 0) w[i] = fallback;
            return w;
        }

        private static CellStyle?[] BuildStyles(TrdpTable t, bool matrix)
        {
            var arr = new CellStyle?[t.Cells.Count];
            for (int i = 0; i < t.Cells.Count; i++)
            {
                if (t.Cells[i].Item is not TrdpTextBox tb) continue;
                arr[i] = StyleOf(tb, matrix);
            }
            return arr;
        }

        private static CellStyle StyleOf(TrdpTextBox tb, bool tableData)
        {
            var font = tableData ? DataFont(tb) : Font(tb);
            return new CellStyle
            {
                Font = font,
                Size = font.Size,
                LineHeight = font.Size * 1.25,
                Brush = Brush(tb.Color.Length > 0 ? tb.Color : "Black"),
                PadL = Math.Max(tableData ? 3 : 2, tb.PadLeft),
                PadR = Math.Max(tableData ? 3 : 2, tb.PadRight),
                PadT = Math.Max(tableData ? 2 : 0, tb.PadTop),
                PadB = Math.Max(tableData ? 2 : 0, tb.PadBottom),
                Align = tb.TextAlign.Length > 0 ? tb.TextAlign : "Left",
                VAlign = tb.VerticalAlign.Length > 0 ? tb.VerticalAlign : "Top",
                Background = tb.BackgroundColor,
                BorderStyle = tb.LineStyle,
                BorderColor = tb.LineColor,
                BorderWidth = tb.BorderWidth,
            };
        }

        /// <summary>
        /// Finds the static column labels in the page header: the lowest row of literal text
        /// boxes whose left edges line up with the table's designed column edges. Those labels
        /// are then drawn in a repeating band instead of once in the page header.
        /// </summary>
        private void MapLabels(double[] templateWidths, TrdpSection header)
        {
            var lefts = new double[templateWidths.Length];
            double x = Table.Left;
            for (int i = 0; i < lefts.Length; i++) { lefts[i] = x; x += templateWidths[i]; }

            var candidates = Walk(header.Items).OfType<TrdpTextBox>()
                .Where(tb => tb.Value.Length > 0 && !tb.Value.TrimStart().StartsWith("="))
                .Where(tb => tb.Top >= header.Height * 0.5)
                .ToList();

            var groups = candidates
                .GroupBy(tb => Math.Round(tb.Top, 0))
                .Where(g => g.Count() >= 2)
                .OrderByDescending(g => g.Key);

            foreach (var g in groups)
            {
                var map = new Dictionary<int, (TrdpTextBox Tb, double Dist)>();
                foreach (var tb in g)
                {
                    int bestCol = -1;
                    double best = 16;
                    for (int i = 0; i < lefts.Length; i++)
                    {
                        double d = Math.Abs(tb.Left - lefts[i]);
                        if (d < best) { best = d; bestCol = i; }
                    }
                    if (bestCol < 0) continue;
                    if (!map.TryGetValue(bestCol, out var cur) || best < cur.Dist) map[bestCol] = (tb, best);
                }

                if (map.Count < Math.Max(2, (int)Math.Ceiling(lefts.Length * 0.6))) continue;
                LabelByColumn = map.ToDictionary(kv => kv.Key, kv => kv.Value.Tb);
                LabelTop = g.Key;
                return;
            }
        }

        private void AllocateWidths(Ctx ctx, double[] template, int n)
        {
            var min = new double[n];
            var demand = new double[n];
            double padX = 6;

            for (int i = 0; i < n; i++)
            {
                int ci = ColumnCell(i);
                var font = ci >= 0 && Styles[ci] != null ? Styles[ci]!.Font : GetFont("Arial", 9, XFontStyle.Regular);
                double minTok = 0, maxLine = 0;
                for (int r = 0; r < RowTexts.Count; r++)
                {
                    string s = RowTexts[r][i];
                    if (s.Length == 0) continue;
                    foreach (var tok in s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        minTok = Math.Max(minTok, ctx.Gfx.MeasureString(tok, font).Width);
                    foreach (var line in s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                        maxLine = Math.Max(maxLine, ctx.Gfx.MeasureString(line, font).Width);
                }
                if (LabelByColumn.TryGetValue(i, out var lb))
                {
                    var lf = HeaderStyle(lb).Font;
                    double lw = ctx.Gfx.MeasureString(Evaluate(lb.Value, ctx, null), lf).Width;
                    minTok = Math.Max(minTok, lw);
                    maxLine = Math.Max(maxLine, lw);
                }
                min[i] = Math.Max(32, minTok + padX);
                demand[i] = Math.Max(min[i], Math.Min(maxLine + padX, Available * 0.52));
                min[i] = Math.Min(min[i], Available * 0.33);
            }

            double sumMin = min.Sum();
            if (sumMin <= 0)
            {
                Widths = template;
                TotalWidth = Widths.Sum();
                return;
            }

            var w = new double[n];
            if (sumMin >= Available)
            {
                for (int i = 0; i < n; i++) w[i] = min[i] * Available / sumMin;
            }
            else
            {
                var weight = new double[n];
                double sw = 0;
                for (int i = 0; i < n; i++) { weight[i] = Math.Max(0, demand[i] - min[i]); sw += weight[i]; }
                if (sw <= 0)
                {
                    for (int i = 0; i < n; i++) weight[i] = template[i];
                    sw = weight.Sum();
                }
                if (sw <= 0)
                {
                    for (int i = 0; i < n; i++) weight[i] = 1;
                    sw = n;
                }
                double extra = Available - sumMin;
                for (int i = 0; i < n; i++) w[i] = min[i] + extra * weight[i] / sw;
            }

            double total = w.Sum();
            if (total > 0)
            {
                double f = Available / total;
                if (Math.Abs(f - 1) > 1e-6)
                    for (int i = 0; i < n; i++) w[i] *= f;
            }
            Widths = w;
            TotalWidth = w.Sum();
        }

        private int ColumnCell(int col)
        {
            for (int i = 0; i < Cells.Count; i++)
                if (Cells[i].ColumnIndex == col && Cells[i].ColumnSpan == 1 && Cells[i].Item != null)
                    return i;
            return -1;
        }

        private void BuildContent(Ctx ctx)
        {
            for (int r = 0; r < RowTexts.Count; r++)
            {
                var contents = new CellContent[Cells.Count];
                double rowH = MinRowHeight;
                for (int i = 0; i < Cells.Count; i++)
                {
                    var cell = Cells[i];
                    if (cell.Item is not TrdpTextBox) { contents[i] = new CellContent(); continue; }
                    var st = Styles[i]!;
                    double w = ColW(cell.ColumnIndex, cell.ColumnSpan) - st.PadL - st.PadR;
                    var lines = WrapText(ctx, RowTexts[r][i], st.Font, w);
                    double h = ContentHeight(lines.Count, st);
                    contents[i] = new CellContent { Text = RowTexts[r][i], Lines = lines, Height = h };
                    rowH = Math.Max(rowH, h);
                }
                RowContent.Add(contents);
                RowHeights.Add(rowH);
            }

            if (RowHeights.Count == 0) RowHeights.Add(MinRowHeight);
        }

        /// <summary>Height an absolutely laid-out cell needs for the given data row. Used to grow
        /// the Tagnames-style rows so wrapped descriptions cannot run into the next row.</summary>
        private static double MeasureContent(Ctx ctx, TrdpItem item, double w, double h,
            Dictionary<string, string>? row)
        {
            double s = ctx.BandScale;
            switch (item)
            {
                case TrdpPanel p:
                    double need = h;
                    foreach (var c in p.Items)
                    {
                        if (!c.Visible) continue;
                        need = Math.Max(need, c.Top + MeasureContent(ctx, c, c.Width * s, c.Height, row));
                    }
                    return need;
                case TrdpTextBox tb:
                    if (!tb.CanGrow) return h;
                    string text = Evaluate(tb.Value, ctx, row);
                    if (text.Length == 0) return h;
                    var font = Font(tb);
                    var lines = CollapseBlank(WrapBox(ctx, text, font, w,
                        Math.Max(2, tb.PadLeft), Math.Max(2, tb.PadRight)));
                    double lineH = font.Size * 1.25;
                    double block = (lines.Count - 1) * lineH + font.Size * 1.12;
                    return tb.PadTop + block + tb.PadBottom;
                default:
                    return h;
            }
        }

        private double ComputeGroupHeaderHeight()
        {
            double h = 0;
            foreach (var g in Table.ColumnGroups)
                if (g.Header != null) h = Math.Max(h, g.Header.Height > 0 ? g.Header.Height : 17.28);
            return h > 0 ? Math.Max(16, h) : 0;
        }

        public bool HasLabelHeader => LabelByColumn.Count > 0;
        public bool HasGroupHeader => GroupHeaderH > 0;

        /// <summary>Draws the repeating header band; returns the y where data rows start.</summary>
        public double DrawHeader(Ctx ctx, double rowsTop)
        {
            if (HasLabelHeader)
            {
                double bandTop = Math.Max(ctx.PageTop + LabelTop, rowsTop - 24);
                if (bandTop >= rowsTop) bandTop = rowsTop - 1;
                double h = rowsTop - bandTop;
                DrawBand(ctx, bandTop, h);
                foreach (var (col, tb) in LabelByColumn)
                {
                    var st = HeaderStyle(tb);
                    string txt = Evaluate(tb.Value, ctx, null);
                    DrawCellLines(ctx, st, new[] { txt }, 0, 1, ColX(col), bandTop, ColW(col, 1), h, true);
                }
                return rowsTop;
            }

            if (HasGroupHeader)
            {
                double h = GroupHeaderH;
                DrawBand(ctx, rowsTop, h);
                int gi = 0;
                foreach (var g in Table.ColumnGroups)
                {
                    if (g.Header != null)
                    {
                        var tb = g.Header as TrdpTextBox;
                        var st = HeaderStyle(tb ?? new TrdpTextBox { Value = " " });
                        string txt = tb != null ? Evaluate(tb.Value, ctx, null) : "";
                        DrawCellLines(ctx, st, new[] { txt }, 0, 1, ColX(gi), rowsTop, ColW(gi, 1), h, true);
                    }
                    gi++;
                }
                return rowsTop + h;
            }

            return rowsTop;
        }

        private void DrawBand(Ctx ctx, double top, double h)
        {
            var g = ctx.Gfx;
            g.DrawRectangle(_headerBg, Left, top, TotalWidth, h);
            for (int i = 1; i < Widths.Length; i++)
                g.DrawLine(_headerSep, ColX(i), top, ColX(i), top + h);
            g.DrawLine(_headerRule, Left, top, Left + TotalWidth, top);
            g.DrawLine(_headerRule, Left, top + h, Left + TotalWidth, top + h);
            L(ctx, "band", Left, top, TotalWidth, h, "", null);
        }
    }

    // ---- table drawing ------------------------------------------------------

    private static readonly XBrush _headerBg = new XSolidBrush(XColor.FromArgb(0xE8, 0xED, 0xF2));
    private static readonly XBrush _bandBg = new XSolidBrush(XColor.FromArgb(0xF7, 0xF9, 0xFB));
    private static readonly XPen _headerRule = new(XColor.FromArgb(0x8A, 0x98, 0xA6), 0.9);
    private static readonly XPen _headerSep = new(XColor.FromArgb(0xC9, 0xD2, 0xDA), 0.5);
    private static readonly XPen _cellPen = new(XColor.FromArgb(0xD3, 0xDA, 0xE1), 0.5);

    private static void DrawRow(Ctx ctx, TablePlan plan, int rowIdx, Dictionary<string, string> data, double y)
    {
        double h = plan.RowHeights[rowIdx];
        for (int i = 0; i < plan.Cells.Count; i++)
        {
            var cell = plan.Cells[i];
            if (cell.Item == null) continue;
            double x = plan.ColX(cell.ColumnIndex);
            double w = plan.ColW(cell.ColumnIndex, cell.ColumnSpan);
            var rect = new XRect(x, y, w, h * cell.RowSpan);

            if (plan.IsMatrix && plan.Styles[i] != null)
            {
                DrawCellChrome(ctx, plan.Styles[i]!, rect, rowIdx);
                var content = plan.RowContent[rowIdx][i];
                DrawCellLines(ctx, plan.Styles[i]!, content.Lines, 0, content.Lines.Count,
                    rect.X, rect.Y, rect.Width, rect.Height, true);
            }
            else
            {
                DrawItem(ctx, cell.Item, rect.X, rect.Y, rect.Width, rect.Height, data, rect.Height);
            }
        }
        L(ctx, "row", plan.Left, y, plan.TotalWidth, h, "", null);
    }

    /// <summary>Draws one page-slice of a row that is taller than the free space. Returns true
    /// when all of the row's lines have been drawn.</summary>
    private static bool DrawRowPart(Ctx ctx, TablePlan plan, int rowIdx, Dictionary<string, string> data,
        int[] taken, double y, double bottom, bool firstPart, out double height)
    {
        int k = int.MaxValue;
        for (int i = 0; i < plan.Cells.Count; i++)
        {
            var st = plan.Styles[i];
            if (st == null) continue;
            var content = plan.RowContent[rowIdx][i];
            int remaining = content.Lines.Count - taken[i];
            if (remaining <= 0) continue;
            double avail = bottom - y - st.PadT - st.PadB - st.Size * 1.12;
            int maxLines = avail >= 0
                ? (int)Math.Floor(avail / st.LineHeight) + 1
                : 1;
            maxLines = Math.Max(0, maxLines);
            k = Math.Min(k, Math.Min(remaining, maxLines));
        }

        if (k == int.MaxValue || k <= 0)
        {
            height = 0;
            return false;
        }

        double h = firstPart ? plan.MinRowHeight : 8;
        for (int i = 0; i < plan.Cells.Count; i++)
        {
            var st = plan.Styles[i];
            if (st == null) continue;
            int drawn = Math.Min(k, plan.RowContent[rowIdx][i].Lines.Count - taken[i]);
            if (drawn > 0) h = Math.Max(h, ContentHeight(drawn, st));
        }
        if (y + h > bottom + 0.5) h = Math.Max(6, bottom - y);

        for (int i = 0; i < plan.Cells.Count; i++)
        {
            var cell = plan.Cells[i];
            if (cell.Item == null || plan.Styles[i] == null) continue;
            double x = plan.ColX(cell.ColumnIndex);
            double w = plan.ColW(cell.ColumnIndex, cell.ColumnSpan);
            var rect = new XRect(x, y, w, h);
            DrawCellChrome(ctx, plan.Styles[i]!, rect, rowIdx);

            int remaining = plan.RowContent[rowIdx][i].Lines.Count - taken[i];
            int drawn = Math.Min(k, remaining);
            DrawCellLines(ctx, plan.Styles[i]!, plan.RowContent[rowIdx][i].Lines, taken[i], drawn,
                x, y, w, h, true);
            taken[i] += drawn;
        }

        height = h;
        for (int i = 0; i < plan.Cells.Count; i++)
            if (plan.Styles[i] != null && taken[i] < plan.RowContent[rowIdx][i].Lines.Count)
                return false;
        return true;
    }

    private static void DrawCellChrome(Ctx ctx, CellStyle st, XRect r, int rowIdx)
    {
        var g = ctx.Gfx;
        bool custom = st.Background.Length > 0 &&
                      !st.Background.Equals("White", StringComparison.OrdinalIgnoreCase);
        if (custom) g.DrawRectangle(new XSolidBrush(ColorOf(st.Background)), r);
        else if ((rowIdx & 1) == 1) g.DrawRectangle(_bandBg, r);

        XPen pen;
        if (st.BorderStyle.Length > 0 && !st.BorderStyle.Equals("None", StringComparison.OrdinalIgnoreCase) &&
            !st.BorderStyle.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            pen = new XPen(st.BorderColor.Length > 0 ? ColorOf(st.BorderColor) : XColors.Black,
                st.BorderWidth > 0 ? Math.Min(st.BorderWidth, 1.2) : 0.75);
        }
        else
        {
            pen = _cellPen;
        }
        g.DrawRectangle(pen, r);
        L(ctx, "cell", r.X, r.Y, r.Width, r.Height, "", null);
    }

    private static void DrawCellLines(Ctx ctx, CellStyle st, IReadOnlyList<string> lines,
        int start, int count, double x, double y, double w, double h, bool clip)
    {
        if (count <= 0) return;
        var g = ctx.Gfx;
        var state = g.Save();
        if (clip) g.IntersectClip(new XRect(x, y, Math.Max(1, w), Math.Max(1, h)));

        double blockH = (count - 1) * st.LineHeight + st.Size * 1.12;
        double top = st.VAlign.Equals("Middle", StringComparison.OrdinalIgnoreCase)
            ? y + (h - blockH) / 2
            : st.VAlign.Equals("Bottom", StringComparison.OrdinalIgnoreCase)
                ? y + h - st.PadB - blockH
                : y + st.PadT;

        double baseline = top + st.Size * 0.9;
        for (int k = 0; k < count; k++)
        {
            string ln = lines[start + k];
            if (ln.Length > 0)
            {
                double tw = g.MeasureString(ln, st.Font).Width;
                double dx = st.Align.Equals("Center", StringComparison.OrdinalIgnoreCase)
                    ? x + (w - tw) / 2
                    : st.Align.Equals("Right", StringComparison.OrdinalIgnoreCase)
                        ? x + w - st.PadR - tw
                        : x + st.PadL;
                T(ln);
                L(ctx, "celltext", dx, baseline - st.Size * 0.9, tw, st.Size * 1.12, ln, st.Font);
                g.DrawString(ln, st.Font, st.Brush, dx, baseline);
            }
            baseline += st.LineHeight;
        }
        g.Restore(state);
    }

    // ---- section / item drawing --------------------------------------------

    private static void DrawSection(Ctx ctx, TrdpSection? sec, double top, bool header)
    {
        if (sec == null) return;
        if (header) ctx.HeaderHeight = sec.Height;
        double s = ctx.BandScale;
        foreach (var item in sec.Items)
        {
            if (ctx.Suppress.Contains(item)) continue;
            double limit = sec.Height;
            foreach (var other in sec.Items)
            {
                if (other.Top >= item.Top + item.Height - 0.5 && other.Top < limit) limit = other.Top;
            }
            double maxH = Math.Max(item.Height, limit - item.Top);
            DrawItem(ctx, item, ctx.PageLeft + item.Left * s, top + item.Top, item.Width * s, item.Height, null, maxH);
        }
    }

    private static void DrawItem(Ctx ctx, TrdpItem item, double x, double y, double w, double h,
        Dictionary<string, string>? row, double maxH = 0)
    {
        if (!item.Visible) return;
        if (item.Width > 0 && w <= 0) w = item.Width;
        if (item.Height > 0 && h <= 0) h = item.Height;
        double s = ctx.BandScale;

        switch (item)
        {
            case TrdpPanel p:
                double bottom = y + (maxH > 0 ? Math.Min(h, maxH) : h);
                foreach (var c in p.Items)
                {
                    if (!c.Visible) continue;
                    double limit = bottom;
                    foreach (var o in p.Items)
                        if (o.Visible && o.Top >= c.Top + c.Height - 0.5) limit = Math.Min(limit, y + o.Top);
                    DrawItem(ctx, c, x + c.Left * s, y + c.Top, c.Width * s, c.Height, row,
                        Math.Max(c.Height, limit - (y + c.Top)));
                }
                break;
            case TrdpTextBox tb:
                DrawTextBox(ctx, tb, x, y, w, h, row, maxH);
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
                DrawTable(ctx, t, x, y, w, row);
                break;
        }
    }

    private static void DrawTextBox(Ctx ctx, TrdpTextBox tb, double x, double y, double w, double h,
        Dictionary<string, string>? row, double maxH = 0)
    {
        if (w <= 0) w = 60;
        if (h <= 0) h = 12;

        string text = Evaluate(tb.Value, ctx, row);
        bool deferred = text.Contains('\u0001');   // {PageCount} known only after layout
        if (deferred) text = "";                    // stamped later
        var font = Font(tb);
        var brush = Brush(tb.Color.Length > 0 ? tb.Color : "Black");
        double padL = Math.Max(2, tb.PadLeft);
        double padR = Math.Max(2, tb.PadRight);
        double padT = tb.PadTop;
        double padB = tb.PadBottom;

        if (tb.BackgroundColor.Length > 0 && !tb.BackgroundColor.Equals("White", StringComparison.OrdinalIgnoreCase))
            ctx.Gfx.DrawRectangle(new XSolidBrush(ColorOf(tb.BackgroundColor)), x, y, w, h);

        if (tb.LineStyle.Length > 0 && !tb.LineStyle.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            var pen = new XPen(tb.LineColor.Length > 0 ? ColorOf(tb.LineColor) : XColors.Black, tb.BorderWidth > 0 ? tb.BorderWidth : 0.5);
            ctx.Gfx.DrawRectangle(pen, x, y, w, h);
        }

        var lines = WrapBox(ctx, text, font, w, padL, padR);
        double lineH = font.Size * 1.25;
        double blockH = (lines.Count - 1) * lineH + font.Size * 1.12;
        double availH = Math.Max(4, (maxH > 0 ? maxH : h) - padT - padB);
        if (blockH > availH + 0.01)
        {
            var compact = CollapseBlank(lines);
            if (compact.Count < lines.Count)
            {
                lines = compact;
                blockH = (lines.Count - 1) * lineH + font.Size * 1.12;
            }

            // Still too tall for the fixed box (a two-paragraph project description in a
            // one-line header, for example): keep only the lines that fit.
            if (blockH > availH && lines.Count > 1)
            {
                int fit = (int)Math.Floor((availH - font.Size * 1.12) / lineH) + 1;
                fit = Math.Max(1, Math.Min(fit, lines.Count));
                if (fit < lines.Count)
                {
                    L(ctx, "trunc", x, y, w, h, text, font);
                    lines = lines.Take(fit).ToList();
                    blockH = (lines.Count - 1) * lineH + font.Size * 1.12;
                }
            }
        }

        // Never grow upward over the fields above: when the wrapped block is taller than
        // the authored box, anchor it at the top and let it use the space below.
        bool overflow = blockH > Math.Max(4, h - padT - padB);
        double top = overflow || tb.VerticalAlign.Length == 0 ||
                     tb.VerticalAlign.Equals("Top", StringComparison.OrdinalIgnoreCase)
            ? y + padT
            : tb.VerticalAlign.Equals("Middle", StringComparison.OrdinalIgnoreCase)
                ? y + (h - blockH) / 2
                : y + h - padB - blockH;
        double baseline = top + font.Size * 0.9;
        double firstBaseline = baseline;

        bool center = tb.TextAlign.Equals("Center", StringComparison.OrdinalIgnoreCase);
        bool right = tb.TextAlign.Equals("Right", StringComparison.OrdinalIgnoreCase);

        var clipState = ctx.Gfx.Save();
        if (maxH > 0) ctx.Gfx.IntersectClip(new XRect(x, y, Math.Max(1, w), Math.Max(1, maxH)));
        foreach (var ln in lines)
        {
            if (ln.Length == 0) { baseline += lineH; continue; }
            double tw = ctx.Gfx.MeasureString(ln, font).Width;
            double drawX = center ? x + (w - tw) / 2
                         : right ? x + w - padR - tw
                         : x + padL;
            T(ln);
            L(ctx, "text", drawX, baseline - font.Size * 0.9, tw, font.Size * 1.12, ln, font);
            ctx.Gfx.DrawString(ln, font, brush, drawX, baseline);
            baseline += lineH;
        }
        ctx.Gfx.Restore(clipState);

        if (deferred || tb.Value.Contains("{PageNumber}"))
        {
            string stampText = tb.Value.TrimStart('=').Trim();
            ctx.Stamps.Add(new Stamp(ctx.CurrentPage, x, firstBaseline, w, stampText, font, brush, tb.TextAlign));
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
        var detail = rpt.Sections.FirstOrDefault(s => s.Kind == "Detail");
        if (detail == null) return;

        var rows = ctx.SubRows(sr);
        var dataTable = detail.Items.OfType<TrdpTable>().FirstOrDefault(t => !t.IsCrosstab);
        var cross = detail.Items.OfType<TrdpTable>().FirstOrDefault(t => t.IsCrosstab);
        double s = ctx.BandScale;

        foreach (var item in detail.Items)
        {
            if (ReferenceEquals(item, dataTable) || ReferenceEquals(item, cross)) continue;
            DrawItem(ctx, item, x + item.Left * s, y + item.Top, item.Width * s, item.Height, rows.FirstOrDefault());
        }

        if (dataTable != null)
        {
            var plan = TablePlan.Build(ctx, dataTable, x + dataTable.Left * s,
                Math.Max(60, w - dataTable.Left * s), null, rows);
            double dy = plan.DrawHeader(ctx, y + dataTable.Top);
            for (int i = 0; i < rows.Count; i++)
            {
                DrawRow(ctx, plan, i, rows[i], dy);
                dy += plan.RowHeights[i];
            }
        }
    }

    private static void DrawTable(Ctx ctx, TrdpTable t, double x, double y, double w, Dictionary<string, string>? row)
    {
        var rows = new List<Dictionary<string, string>> { row ?? new Dictionary<string, string>() };
        var plan = TablePlan.Build(ctx, t, x + t.Left * ctx.BandScale,
            Math.Max(60, w - t.Left * ctx.BandScale), null, rows);
        double dy = plan.DrawHeader(ctx, y);
        DrawRow(ctx, plan, 0, rows[0], dy);
    }

    // ---- SOE cross-tab ------------------------------------------------------

    private static (List<string> Tags, List<string> Blocks) CrosstabData(Ctx ctx)
    {
        var assigns = ctx.Data.SoeAssignments;
        var tags = assigns.Select(a => a.Tagname).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        var blocks = assigns.Select(a => a.BlockNum).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => int.TryParse(b, out var n) ? n : 0).ToList();
        return (tags, blocks);
    }

    private static double DrawCrosstabHeader(Ctx ctx, TrdpTable ct, double x, double y, List<string> blocks)
    {
        var corner = ct.CornerText.Length > 0 ? ct.CornerText : "Tagname";
        var (rowLabelW, colW) = CrosstabMetrics(ctx, ct);
        double h = CrosstabCellH(ct);
        DrawCrosstabCell(ctx, corner, x, y, rowLabelW, h, true, true, true, true);
        for (int c = 0; c < blocks.Count; c++)
            DrawCrosstabCell(ctx, blocks[c], x + rowLabelW + c * colW, y, colW, h, true, true, true, true);
        if (HasCrosstabTotal(ct))
            DrawCrosstabCell(ctx, "Total", x + rowLabelW + blocks.Count * colW, y, colW, h, true, true, true, true);
        return y + h;
    }

    private static void DrawCrosstabRow(Ctx ctx, TrdpTable ct, double x, double y, List<string> blocks, string tag, int rowIdx)
    {
        var (rowLabelW, colW) = CrosstabMetrics(ctx, ct);
        double h = CrosstabCellH(ct);
        int rowTotal = 0;
        for (int c = 0; c < blocks.Count; c++)
        {
            int count = ctx.Data.SoeAssignments.Count(a =>
                a.Tagname.Equals(tag, StringComparison.OrdinalIgnoreCase) &&
                a.BlockNum.Equals(blocks[c], StringComparison.OrdinalIgnoreCase));
            rowTotal += count;
            DrawCrosstabCell(ctx, count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "",
                x + rowLabelW + c * colW, y, colW, h, true, true, true, true);
        }
        DrawCrosstabCell(ctx, tag, x, y, rowLabelW, h, true, true, true, true);
        if (HasCrosstabTotal(ct))
            DrawCrosstabCell(ctx, rowTotal > 0 ? rowTotal.ToString(CultureInfo.InvariantCulture) : "",
                x + rowLabelW + blocks.Count * colW, y, colW, h, true, true, true, true);
    }

    private static bool HasCrosstabTotal(TrdpTable ct) => ct.ColumnGroups.Count > 1;

    private static double CrosstabCellH(TrdpTable ct) => Math.Max(TableRowHeight(ct), 10);

    private static (double RowLabelW, double ColW) CrosstabMetrics(Ctx ctx, TrdpTable ct)
    {
        double colW = ct.Columns.Count > 1 ? ct.Columns[1].Width * ctx.BandScale : 24;
        colW = Math.Max(18, colW);

        double labelW = 60;
        foreach (var t in ctx.Data.SoeAssignments.Select(a => a.Tagname).Distinct(StringComparer.OrdinalIgnoreCase))
            labelW = Math.Max(labelW, ctx.Gfx.MeasureString(t, _crosstabFont).Width + 8);
        labelW = Math.Min(Math.Max(60, labelW), 220);
        return (labelW, colW);
    }

    private static void DrawCrosstabCell(Ctx ctx, string text, double x, double y, double w, double h, bool left, bool top, bool right, bool bottom)
    {
        var gfx = ctx.Gfx;
        var pen = new XPen(XColors.Black, 0.5);
        if (left) gfx.DrawLine(pen, x, y, x, y + h);
        if (right) gfx.DrawLine(pen, x + w, y, x + w, y + h);
        if (top) gfx.DrawLine(pen, x, y, x + w, y);
        if (bottom) gfx.DrawLine(pen, x, y + h, x + w, y + h);
        L(ctx, "cell", x, y, w, h, "", null);
        if (text.Length == 0) return;
        double tw = gfx.MeasureString(text, _crosstabFont).Width;
        double tx = w >= tw ? x + (w - tw) / 2 : x + 1;
        T(text);
        L(ctx, "celltext", tx, y + h - 3 - _crosstabFont.Size * 0.9, tw,
            _crosstabFont.Size * 1.12, text, _crosstabFont);
        gfx.DrawString(text, _crosstabFont, XBrushes.Black, tx, y + h - 3);
    }

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

        string f2 = ExtractField(s);
        if (f2.Length > 0)
        {
            if (row != null) return GetField(row, f2);
            return ctx.Data.Header.TryGetValue(f2, out var hv) ? hv : "";
        }

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
        int end = 0;
        while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] == '_')) end++;
        return rest.Substring(0, end);
    }

    private static string GetField(Dictionary<string, string> row, string field)
        => row.TryGetValue(field, out var v) ? v : "";

    private static string Substitute(string literal, Ctx ctx)
    {
        if (!literal.Contains('{')) return literal;
        string outText = literal;
        outText = outText.Replace("{PageNumber}", (ctx.CurrentPage + 1).ToString(CultureInfo.InvariantCulture));
        outText = outText.Replace("{ExecutionTime}", DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        outText = outText.Replace("{PageCount}", "\u0001"); // placeholder stamped later
        return outText;
    }

    // ---- text / font helpers ------------------------------------------------

    private static readonly Dictionary<string, XFont> _fonts = new(StringComparer.OrdinalIgnoreCase);

    private static XFont GetFont(string name, double size, XFontStyle style)
    {
        string key = name + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + (int)style;
        if (_fonts.TryGetValue(key, out var f)) return f;
        try { f = new XFont(name, size, style); }
        catch { f = new XFont("Arial", size, style); }
        _fonts[key] = f;
        return f;
    }

    private static XFont Font(TrdpTextBox tb)
    {
        string name = tb.FontName.Length > 0 ? tb.FontName : "Arial";
        double size = tb.FontSize > 0 ? tb.FontSize : 10;
        var style = XFontStyle.Regular;
        if (tb.Bold && tb.Italic) style = XFontStyle.BoldItalic;
        else if (tb.Bold) style = XFontStyle.Bold;
        else if (tb.Italic) style = XFontStyle.Italic;
        return GetFont(name, size, style);
    }

    /// <summary>Data cells use one technical typeface so tables align regardless of the
    /// template's per-cell font.</summary>
    private static XFont DataFont(TrdpTextBox tb)
    {
        var style = tb.Bold && tb.Italic ? XFontStyle.BoldItalic
            : tb.Bold ? XFontStyle.Bold
            : tb.Italic ? XFontStyle.Italic
            : XFontStyle.Regular;
        return GetFont("Arial", 9, style);
    }

    private static CellStyle HeaderStyle(TrdpTextBox tb)
    {
        var font = GetFont("Arial", 9, tb.Italic ? XFontStyle.BoldItalic : XFontStyle.Bold);
        return new CellStyle
        {
            Font = font,
            Size = font.Size,
            LineHeight = font.Size * 1.25,
            Brush = Brush(tb.Color.Length > 0 ? tb.Color : "#1C2733"),
            PadL = 4,
            PadR = 4,
            PadT = 2,
            PadB = 2,
            Align = tb.TextAlign.Length > 0 ? tb.TextAlign : "Left",
            VAlign = "Middle",
        };
    }

    private static IEnumerable<TrdpItem> Walk(IEnumerable<TrdpItem> items)
    {
        foreach (var it in items)
        {
            yield return it;
            if (it is TrdpPanel p)
                foreach (var c in Walk(p.Items)) yield return c;
        }
    }

    /// <summary>Wraps to the padded text area, but keeps a short label on one line when it
    /// fits the authored box and only the padding would have broken it.</summary>
    private static List<string> WrapBox(Ctx ctx, string text, XFont font, double w,
        double padL, double padR)
    {
        var lines = WrapText(ctx, text, font, Math.Max(2, w - padL - padR));
        if (lines.Count > 1 && text.IndexOf('\n') < 0 &&
            ctx.Gfx.MeasureString(text, font).Width <= w - 1)
            return new List<string> { text };
        return lines;
    }

    private static List<string> WrapText(Ctx ctx, string text, XFont font, double maxW)
    {
        var result = new List<string>();
        if (maxW < 4) maxW = 4;
        var g = ctx.Gfx;

        foreach (var para in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string cur = "";
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string trial = cur.Length == 0 ? word : cur + " " + word;
                if (g.MeasureString(trial, font).Width <= maxW) { cur = trial; continue; }
                if (cur.Length > 0) { result.Add(cur); cur = ""; }
                if (g.MeasureString(word, font).Width <= maxW) { cur = word; continue; }
                var parts = BreakWord(g, word, font, maxW);
                for (int i = 0; i < parts.Count - 1; i++) result.Add(parts[i]);
                cur = parts[^1];
            }
            result.Add(cur);
        }

        return result.Count > 0 ? result : new List<string> { "" };
    }

    /// <summary>Drops empty paragraphs so a multi-paragraph value still fits a box that
    /// cannot grow (e.g. the page-header project description).</summary>
    private static List<string> CollapseBlank(IReadOnlyList<string> lines)
    {
        var outp = new List<string>(lines.Count);
        foreach (var l in lines)
            if (l.Length > 0) outp.Add(l);
        return outp.Count > 0 ? outp : new List<string> { "" };
    }

    private static List<string> BreakWord(XGraphics g, string word, XFont font, double maxW)
    {
        var parts = new List<string>();
        int start = 0;
        while (start < word.Length)
        {
            int lo = 1, hi = word.Length - start, best = 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (g.MeasureString(word.Substring(start, mid), font).Width <= maxW)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }

            int take = best;
            if (best < word.Length - start && best > 1)
            {
                int dot = word.LastIndexOf('.', start + best - 1, best);
                if (dot > start) take = dot - start + 1;
            }

            parts.Add(word.Substring(start, take));
            start += take;
        }
        return parts.Count > 0 ? parts : new List<string> { "" };
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

    // ---- helpers ------------------------------------------------------------

    private static double ContentHeight(int lines, CellStyle st)
        => st.PadT + st.PadB + (lines - 1) * st.LineHeight + st.Size * 1.12;

    private static double TableRowHeight(TrdpTable t)
    {
        if (t.Rows.Count > 0) return t.Rows[0].Height;
        var first = t.Cells.FirstOrDefault();
        if (first?.Item != null && first.Item.Height > 0) return first.Item.Height;
        return 18;
    }

    private sealed record Stamp(int PageIndex, double X, double Y, double W, string Text,
        XFont Font, XBrush Brush, string Align);

    // ---- deferred page numbers ----------------------------------------------

    private static void StampPageNumbers(this Ctx ctx, int startPage)
    {
        int total = ctx.Doc.PageCount - startPage;
        if (total <= 0) return;

        foreach (var stamp in ctx.Stamps)
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
            double drawX = stamp.Align.Equals("Center", StringComparison.OrdinalIgnoreCase)
                    ? stamp.X + (stamp.W - sz.Width) / 2
                    : stamp.Align.Equals("Right", StringComparison.OrdinalIgnoreCase)
                        ? stamp.X + stamp.W - sz.Width - 2
                        : stamp.X + 2;
            int savedPage = ctx.CurrentPage;
            ctx.CurrentPage = stamp.PageIndex;
            L(ctx, "stamp", drawX, stamp.Y - stamp.Font.Size * 0.9, sz.Width, stamp.Font.Size * 1.12,
                text, stamp.Font);
            ctx.CurrentPage = savedPage;
            gfx.DrawString(text, stamp.Font, stamp.Brush, drawX, stamp.Y);
        }
        ctx.Stamps.Clear();
    }
}
