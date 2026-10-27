using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Pt2Conv;

/// <summary>
/// Reads a Telerik Reporting <c>.trdp</c> package (a zip containing <c>definition.xml</c>
/// and an <c>Images/</c> folder) into the <see cref="TrdpReport"/> model. Only the subset
/// of the Telerik schema the TriStation report suite actually uses is parsed; anything
/// else is ignored.
/// </summary>
public static class TrdpReader
{
    private static readonly XNamespace Ns = "http://schemas.telerik.com/reporting/2018/3.0";

    /// <summary>Loads a template from bytes of a .trdp zip package.</summary>
    public static TrdpReport Load(byte[] package)
    {
        using var ms = new MemoryStream(package);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var def = zip.GetEntry("definition.xml")
            ?? zip.Entries.FirstOrDefault(e => e.Name.Equals("definition.xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(".trdp has no definition.xml");
        XDocument doc;
        using (var s = def.Open()) doc = XDocument.Load(s);
        var report = ParseReport(doc.Root ?? throw new InvalidDataException("empty definition.xml"));
        AttachImages(report, zip);
        return report;
    }

    /// <summary>Reads a .trdp from the embedded-resource set; returns null if absent.</summary>
    public static TrdpReport? LoadEmbedded(string fileName, Dictionary<string, TrdpReport?> cache)
    {
        if (cache.TryGetValue(fileName, out var r)) return r;
        var asm = typeof(TrdpReader).Assembly;
        string resName = "Pt2Conv.Resources.reports." + fileName;
        using var s = asm.GetManifestResourceStream(resName);
        TrdpReport? loaded = null;
        if (s != null)
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            loaded = Load(ms.ToArray());
        }
        cache[fileName] = loaded;
        return loaded;
    }

    private static void AttachImages(TrdpReport report, ZipArchive zip)
    {
        var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.StartsWith("Images/", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            images["/Images/" + e.Name] = ms.ToArray();
            images[e.Name] = ms.ToArray();
        }
        if (images.Count == 0) return;
        foreach (var item in Walk(report.Sections.SelectMany(s => s.Items)))
            if (item is TrdpPicture p && p.Image.Length > 0 &&
                (images.TryGetValue(p.Image, out var bytes) || images.TryGetValue("/Images/" + Path.GetFileName(p.Image), out bytes)))
                p.ImageBytes = bytes;
    }

    private static IEnumerable<TrdpItem> Walk(IEnumerable<TrdpItem> items)
    {
        foreach (var it in items)
        {
            yield return it;
            if (it is TrdpPanel p) foreach (var c in Walk(p.Items)) yield return c;
        }
    }

    private static TrdpReport ParseReport(XElement root)
    {
        var report = new TrdpReport
        {
            Name = Attr(root, "Name"),
            Width = Unit(Attr(root, "Width"), 612),
            DataSourceName = Attr(root, "DataSourceName"),
        };
        report.DesignWidth = report.Width;
        report.Height = 792;

        var ps = root.Element(Ns + "PageSettings");
        if (ps != null)
        {
            report.PaperKind = Attr(ps, "PaperKind");
            report.Landscape = BoolAttr(ps, "Landscape", false);

            // Margins are authored as <Margins><MarginsU Left="0.5in" .../></Margins>.
            var m = ps.Element(Ns + "Margins");
            var mu = m?.Element(Ns + "MarginsU") ?? m;
            if (mu != null)
            {
                report.MarginLeft = Unit(Attr(mu, "Left"), 0);
                report.MarginTop = Unit(Attr(mu, "Top"), 0);
                report.MarginRight = Unit(Attr(mu, "Right"), 0);
                report.MarginBottom = Unit(Attr(mu, "Bottom"), 0);
            }

            double paperW = Unit(Attr(ps, "PaperWidth"), 0);
            double paperH = Unit(Attr(ps, "PaperHeight"), 0);
            if (paperW <= 0 || paperH <= 0)
            {
                var (w, h) = PaperSize(report.PaperKind);
                paperW = w;
                paperH = h;
                if (report.Landscape && paperW < paperH) (paperW, paperH) = (paperH, paperW);
            }

            // The template's body must fit between the margins; enlarge the sheet when the
            // margins were omitted or a custom width is wider than the nominal paper.
            double minW = report.DesignWidth + report.MarginLeft + report.MarginRight;
            if (paperW < minW) paperW = minW;
            report.Width = paperW;
            report.Height = paperH > 0 ? paperH : 792;
        }
        else
        {
            // No page settings: assume Letter and derive the width from the designed body.
            report.Width = report.DesignWidth;
        }

        ParseStyles(root, report.Styles);

        var items = root.Element(Ns + "Items");
        if (items != null)
            foreach (var el in items.Elements())
            {
                var sec = ParseSection(el);
                if (sec != null) report.Sections.Add(sec);
            }
        return report;
    }

    private static void ParseStyles(XElement root, TrdpStyleSheet sheet)
    {
        var ss = root.Element(Ns + "StyleSheet");
        if (ss == null) return;
        foreach (var rule in ss.Elements(Ns + "StyleRule"))
        {
            var style = ParseStyleElement(rule.Element(Ns + "Style"), null);
            var selectors = rule.Element(Ns + "Selectors");
            if (selectors == null) continue;
            foreach (var sel in selectors.Elements())
                sheet.ByName.Add((sel.Name.LocalName, Attr(sel, "Type") + "|" + Attr(sel, "Name"), style));
        }
    }

    private static TrdpSection? ParseSection(XElement el)
    {
        string kind = el.Name.LocalName switch
        {
            "PageHeaderSection" => "PageHeader",
            "DetailSection" => "Detail",
            "ReportFooterSection" => "ReportFooter",
            "PageFooterSection" => "PageFooter",
            _ => "",
        };
        if (kind.Length == 0) return null;

        var sec = new TrdpSection
        {
            Kind = kind,
            Height = Unit(Attr(el, "Height"), 0),
            PrintOnFirstPage = BoolAttr(el, "PrintOnFirstPage", true),
            PrintOnLastPage = BoolAttr(el, "PrintOnLastPage", true),
            PageBreakAfter = string.Equals(Attr(el, "PageBreak"), "After", StringComparison.OrdinalIgnoreCase),
            PageBreakBefore = string.Equals(Attr(el, "PageBreak"), "Before", StringComparison.OrdinalIgnoreCase),
        };
        var items = el.Element(Ns + "Items");
        if (items != null)
            foreach (var it in items.Elements())
            {
                var parsed = ParseItem(it);
                if (parsed != null) sec.Items.Add(parsed);
            }
        return sec;
    }

    private static TrdpItem? ParseItem(XElement el)
    {
        switch (el.Name.LocalName)
        {
            case "Panel":
            {
                var p = new TrdpPanel
                {
                    Name = Attr(el, "Name"),
                    Left = Unit(Attr(el, "Left"), 0),
                    Top = Unit(Attr(el, "Top"), 0),
                    Width = Unit(Attr(el, "Width"), 0),
                    Height = Unit(Attr(el, "Height"), 0),
                    StyleName = Attr(el, "StyleName"),
                };
                var items = el.Element(Ns + "Items");
                if (items != null)
                    foreach (var c in items.Elements())
                    {
                        var pc = ParseItem(c);
                        if (pc != null) p.Items.Add(pc);
                    }
                return p;
            }
            case "TextBox":
            {
                var tb = new TrdpTextBox
                {
                    Name = Attr(el, "Name"),
                    Left = Unit(Attr(el, "Left"), 0),
                    Top = Unit(Attr(el, "Top"), 0),
                    Width = Unit(Attr(el, "Width"), 0),
                    Height = Unit(Attr(el, "Height"), 0),
                    StyleName = Attr(el, "StyleName"),
                    Value = el.Attribute("Value")?.Value ?? el.Element(Ns + "Value")?.Value ?? "",
                    CanGrow = BoolAttr(el, "CanGrow", false),
                    Format = Attr(el, "Format"),
                };
                ApplyStyle(tb, ParseStyleElement(el.Element(Ns + "Style"), tb));
                return tb;
            }
            case "Shape":
            {
                var sh = new TrdpShape
                {
                    Name = Attr(el, "Name"),
                    Left = Unit(Attr(el, "Left"), 0),
                    Top = Unit(Attr(el, "Top"), 0),
                    Width = Unit(Attr(el, "Width"), 0),
                    Height = Unit(Attr(el, "Height"), 0),
                    StyleName = Attr(el, "StyleName"),
                };
                var st = el.Element(Ns + "Style");
                if (st != null)
                {
                    sh.LineColor = Attr(st, "Color");
                    sh.BackgroundColor = Attr(st, "BackgroundColor");
                    if (double.TryParse(Attr(st, "LineWidth").Replace("pt", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var lw)) sh.LineWidth = lw;
                }
                var stype = el.Element(Ns + "ShapeType");
                if (stype != null)
                {
                    var line = stype.Element(Ns + "LineShape");
                    if (line != null) { sh.Kind = "Line"; sh.Direction = Attr(line, "Direction"); }
                    else if (stype.Element(Ns + "EllipseShape") != null) sh.Kind = "Ellipse";
                }
                return sh;
            }
            case "PictureBox":
            {
                var pic = new TrdpPicture
                {
                    Name = Attr(el, "Name"),
                    Left = Unit(Attr(el, "Left"), 0),
                    Top = Unit(Attr(el, "Top"), 0),
                    Width = Unit(Attr(el, "Width"), 0),
                    Height = Unit(Attr(el, "Height"), 0),
                    StyleName = Attr(el, "StyleName"),
                    Image = Attr(el, "Image"),
                    Sizing = Attr(el, "Sizing"),
                };
                return pic;
            }
            case "Crosstab":
            case "Table":
            {
                var t = new TrdpTable
                {
                    IsCrosstab = el.Name.LocalName == "Crosstab",
                    Name = Attr(el, "Name"),
                    Left = Unit(Attr(el, "Left"), 0),
                    Top = Unit(Attr(el, "Top"), 0),
                    Width = Unit(Attr(el, "Width"), 0),
                    Height = Unit(Attr(el, "Height"), 0),
                    StyleName = Attr(el, "StyleName"),
                    DataSourceName = Attr(el, "DataSourceName"),
                    RowHeadersEveryPage = BoolAttr(el, "RowHeadersPrintOnEveryPage", false),
                    ColumnHeadersEveryPage = BoolAttr(el, "ColumnHeadersPrintOnEveryPage", false),
                };
                var body = el.Element(Ns + "Body");
                if (body != null)
                {
                    var cells = body.Element(Ns + "Cells");
                    if (cells != null)
                        foreach (var c in cells.Elements(Ns + "TableCell"))
                        {
                            var ri = c.Element(Ns + "ReportItem");
                            t.Cells.Add(new TrdpTableCell
                            {
                                RowIndex = IntAttr(c, "RowIndex"),
                                ColumnIndex = IntAttr(c, "ColumnIndex"),
                                RowSpan = Math.Max(1, IntAttr(c, "RowSpan")),
                                ColumnSpan = Math.Max(1, IntAttr(c, "ColumnSpan")),
                                Item = ri != null ? ParseItem(ri.Elements().FirstOrDefault() ?? ri) : null,
                            });
                        }
                    var cols = body.Element(Ns + "Columns");
                    if (cols != null)
                        foreach (var c in cols.Elements(Ns + "Column"))
                            t.Columns.Add(new TrdpTableColumn { Width = Unit(Attr(c, "Width"), 0) });
                    var rows = body.Element(Ns + "Rows");
                    if (rows != null)
                        foreach (var r in rows.Elements(Ns + "Row"))
                            t.Rows.Add(new TrdpTableRow { Height = Unit(Attr(r, "Height"), 0) });
                }
                var corner = el.Element(Ns + "Corner");
                if (corner != null)
                {
                    var ri = corner.Element(Ns + "ReportItem");
                    if (ri != null)
                    {
                        t.Corner = new TrdpTableRow();
                        var citem = ParseItem(ri.Elements().FirstOrDefault() ?? ri);
                        if (citem is TrdpTextBox ctb) t.CornerText = ctb.Value;
                    }
                    // Crosstab corners wrap cells: <Corner><Cells><TableCell><ReportItem>...
                    var ccells = corner.Element(Ns + "Cells");
                    if (ccells != null)
                        foreach (var cc in ccells.Elements(Ns + "TableCell"))
                        {
                            var cri = cc.Element(Ns + "ReportItem");
                            if (cri == null) continue;
                            var citem = ParseItem(cri.Elements().FirstOrDefault() ?? cri);
                            if (citem is TrdpTextBox ctb && t.CornerText.Length == 0) t.CornerText = ctb.Value;
                        }
                }
                ParseGroups(el.Element(Ns + "RowGroups"), t.RowGroups);
                ParseGroups(el.Element(Ns + "ColumnGroups"), t.ColumnGroups);
                return t;
            }
            case "SubReport":
            {
                var sr = new TrdpSubReport
                {
                    Name = Attr(el, "Name"),
                    Left = Unit(Attr(el, "Left"), 0),
                    Top = Unit(Attr(el, "Top"), 0),
                    Width = Unit(Attr(el, "Width"), 0),
                    Height = Unit(Attr(el, "Height"), 0),
                    StyleName = Attr(el, "StyleName"),
                    Uri = Attr(el.Element(Ns + "ReportSource")?.Element(Ns + "UriReportSource"), "Uri"),
                };
                return sr;
            }
            default:
                return null;
        }
    }

    private static void ParseGroups(XElement? groups, List<TrdpTableGroup> into)
    {
        if (groups == null) return;
        foreach (var g in groups.Elements(Ns + "TableGroup"))
        {
            var grp = new TrdpTableGroup { Name = Attr(g, "Name") };
            var ri = g.Element(Ns + "ReportItem");
            if (ri != null)
                grp.Header = ParseItem(ri.Elements().FirstOrDefault() ?? ri);
            var groupings = g.Element(Ns + "Groupings");
            var grouping = groupings?.Element(Ns + "Grouping");
            grp.Grouping = grouping != null ? Attr(grouping, "Expression") : "";
            into.Add(grp);
        }
    }

    private static void ApplyStyle(TrdpTextBox tb, TrdpStyle? st)
    {
        if (st == null) return;
        if (st.FontName.Length > 0) tb.FontName = st.FontName;
        if (st.FontSize > 0) tb.FontSize = st.FontSize;
        tb.Bold = st.Bold;
        tb.Italic = st.Italic;
        if (st.Color.Length > 0) tb.Color = st.Color;
        if (st.BackgroundColor.Length > 0) tb.BackgroundColor = st.BackgroundColor;
    }

    private static TrdpStyle? ParseStyleElement(XElement? style, TrdpTextBox? tb)
    {
        if (style == null) return null;
        var st = new TrdpStyle
        {
            Color = Attr(style, "Color"),
            BackgroundColor = Attr(style, "BackgroundColor"),
        };
        if (tb != null)
        {
            tb.TextAlign = Attr(style, "TextAlign") is { Length: > 0 } ta ? ta : "Left";
            tb.VerticalAlign = Attr(style, "VerticalAlign") is { Length: > 0 } va ? va : "Top";
        }
        var font = style.Element(Ns + "Font");
        if (font != null)
        {
            st.FontName = Attr(font, "Name");
            if (double.TryParse(Attr(font, "Size").Replace("pt", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var fs)) st.FontSize = fs;
            st.Bold = BoolAttr(font, "Bold", false);
            st.Italic = BoolAttr(font, "Italic", false);
        }
        var border = style.Element(Ns + "BorderStyle");
        if (border != null && tb != null)
            tb.LineStyle = Attr(border, "Default") is { Length: > 0 } bd ? bd : Attr(border, "Left");
        var bc = style.Element(Ns + "BorderColor");
        if (bc != null && tb != null)
            tb.LineColor = Attr(bc, "Default") is { Length: > 0 } b2 ? b2 : Attr(bc, "Left");
        var pad = style.Element(Ns + "Padding");
        if (pad != null && tb != null)
        {
            tb.PadLeft = Unit(Attr(pad, "Left"), 0);
            tb.PadTop = Unit(Attr(pad, "Top"), 0);
            tb.PadRight = Unit(Attr(pad, "Right"), 0);
            tb.PadBottom = Unit(Attr(pad, "Bottom"), 0);
        }
        return st;
    }

    /// <summary>Portrait dimensions in points for the paper kinds TriStation emits.</summary>
    private static (double W, double H) PaperSize(string kind)
    {
        switch (kind.Trim().ToLowerInvariant())
        {
            case "a3": return (841.89, 1190.55);
            case "a4": return (595.28, 841.89);
            case "a5": return (419.53, 595.28);
            case "b5": return (498.90, 708.66);
            case "legal": return (612, 1008);
            case "executive": return (522, 756);
            case "statement": return (396, 612);
            case "tabloid": return (792, 1224);
            case "ledger": return (1224, 792);
            default: return (612, 792);
        }
    }

    // ---- attribute helpers --------------------------------------------------

    private static string Attr(XElement? el, string name)
        => el?.Attribute(name)?.Value ?? "";

    private static bool BoolAttr(XElement el, string name, bool dflt)
    {
        string v = Attr(el, name);
        if (v.Length == 0) return dflt;
        return v.Equals("True", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static int IntAttr(XElement el, string name)
        => int.TryParse(Attr(el, name), out var v) ? v : 0;

    /// <summary>Parses a Telerik length. Units seen in these templates: in, pt, px.
    /// A bare number is treated as points. px is converted at 96 dpi (1px = 0.75pt).</summary>
    public static double Unit(string s, double dflt)
    {
        if (string.IsNullOrWhiteSpace(s)) return dflt;
        s = s.Trim();
        double n;
        if (s.EndsWith("in", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(s[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n * 72 : dflt;
        if (s.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(s[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n : dflt;
        if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(s[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n * 0.75 : dflt;
        if (s.EndsWith("cm", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(s[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n * 72 / 2.54 : dflt;
        if (s.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(s[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n * 72 / 25.4 : dflt;
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n : dflt;
    }
}
