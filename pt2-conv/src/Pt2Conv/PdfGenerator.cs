using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Pt2Conv;

/// <summary>
/// Draws a decompiled TriStation FBD project to a PDF.
///
/// The sheet geometry comes from the container's own sheet template
/// (<c>Current/Elements/SheetTemplate*&#47;*.BKG</c>): the template holds the title block
/// (a full and a compact variant) and the frame band, so the sheet size is the
/// template's extent plus the 30 unit frame. Everything is drawn in those sheet
/// units (y down, like the container) and scaled onto a Letter page, the way
/// TriStation prints: a size D sheet is 3300 x 2100 units.
///
/// Pure managed drawing (PdfSharpCore): no TriStation, no licence, no native deps,
/// so it runs anywhere the tool runs -- including Linux on the engine host.
/// </summary>
public static class PdfGenerator
{
    // ---- palette (matches the viewer's SVG colours) --------------------------
    private static readonly XColor Ink = XColor.FromArgb(0x1F, 0x29, 0x37);
    private static readonly XColor WireColor = XColor.FromArgb(0x37, 0x41, 0x51);
    private static readonly XColor FrameColor = XColor.FromArgb(0x4B, 0x55, 0x63);
    private static readonly XColor PinDot = XColor.FromArgb(0x1F, 0x29, 0x37);

    // TriStation draws every line 0.5 pt wide on the sheet (1.6 sheet units).
    private static readonly XPen Ink1 = new(Ink, 1.6);
    private static readonly XPen Ink075 = new(Ink, 1.2);
    private static readonly XPen Ink15 = new(Ink, 1.6);
    private static readonly XPen WirePen = new(WireColor, 1.6) { LineJoin = XLineJoin.Round, LineCap = XLineCap.Round };
    private static readonly XPen Frame1 = new(FrameColor, 1.6);
    private static readonly XPen Frame15 = new(FrameColor, 1.6);
    private static readonly XBrush WhiteBrush = new XSolidBrush(XColors.White);
    private static readonly XBrush InkBrush = new XSolidBrush(Ink);
    private static readonly XBrush PinBrush = new XSolidBrush(PinDot);
    private static readonly XBrush FrameBrush = new XSolidBrush(FrameColor);

    private const string FontFamily = "Arial";
    // Points -> user units (the SVG uses the same 96/72 factor).
    private const double Pt = 96.0 / 72.0;

    // The frame band between the sheet's edge and the drawing area.
    private const double FrameMargin = 30;
    // The page: US Letter, landscape for a landscape sheet.
    private const double PageW = 792, PageH = 612;
    private const double PageMargin = 9;

    /// <summary>The row letters of the frame, from the bottom up. TriStation doubles the
    /// last letter once the sequence runs out (8 rows: A..G and HH).</summary>
    private const string RowLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";

    private sealed record SheetSize(double W, double H, double TitleW, double TitleH);

    // The sheet sizes of the standard templates (the frame band included).
    private static readonly Dictionary<string, SheetSize> SheetSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = new(1020, 762, 318, 96),
        ["B"] = new(1606, 1006, 296, 96),
        ["C"] = new(2100, 1600, 423, 156),
        ["D"] = new(3300, 2100, 1014, 280),
    };

    private static SheetSize SizeOf(PdfDrawing d)
    {
        var letter = string.IsNullOrWhiteSpace(d.Size) ? "D" : d.Size.ToUpperInvariant();
        return SheetSizes.TryGetValue(letter, out var s) ? s : SheetSizes["D"];
    }

    // ---- public entry -------------------------------------------------------

    public static void Write(PdfProject project, string outPath)
    {
        var doc = new PdfDocument();
        DrawInto(doc, project);
        doc.Save(outPath);
    }

    /// <summary>
    /// Draws the project's FBD sheets (or the ST/text fallback) into an existing document,
    /// appending after whatever pages it already holds. Used to combine FBD sheets and the
    /// report suite into a single PDF without a page-import/merge step.
    /// </summary>
    public static void DrawInto(PdfDocument doc, PdfProject project)
    {
        if (string.IsNullOrWhiteSpace(doc.Info.Title))
            doc.Info.Title = string.IsNullOrWhiteSpace(project.Name) ? "TriStation FBD" : project.Name;
        doc.Info.Creator = "pt2conv";

        bool any = false;
        foreach (var pou in project.Pous)
        {
            foreach (var sheet in pou.Sheets)
            {
                DrawSheet(doc, project, pou, sheet);
                any = true;
            }
        }

        // A Structured Text POU (or a project with no FBD sheets at all) still deserves
        // a readable PDF, so fall back to text pages.
        if (!any)
        {
            DrawTextPages(doc, project);
        }
    }

    // ---- one FBD sheet ------------------------------------------------------

    private static void DrawSheet(PdfDocument doc, PdfProject project, PdfPou pou, PdfSheet sheet)
    {
        // The template of the drawing drives the sheet geometry; the built-in table is
        // the fallback for containers without the template streams.
        var tpl = TemplateFor(project, pou);
        var fallback = SizeOf(pou.Drawing);
        double sheetW = tpl != null && tpl.SheetW > 0 ? tpl.SheetW : fallback.W;
        double sheetH = tpl != null && tpl.SheetH > 0 ? tpl.SheetH : fallback.H;

        bool portrait = sheetH > sheetW;
        double pageW = portrait ? PageH : PageW;
        double pageH = portrait ? PageW : PageH;
        double scale = Math.Min((pageW - 2 * PageMargin) / sheetW, (pageH - 2 * PageMargin) / sheetH);
        double offsetX = (pageW - sheetW * scale) / 2;
        double offsetY = (pageH - sheetH * scale) / 2;

        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(pageW);
        page.Height = XUnit.FromPoint(pageH);

        using var gfx = XGraphics.FromPdfPage(page);
        gfx.TranslateTransform(offsetX, offsetY);
        gfx.ScaleTransform(scale, scale);

        DrawFrame(gfx, pou.Drawing, sheetW, sheetH);
        DrawTitleBlock(gfx, project, pou, sheet, tpl);

        // The FBD content lives in the drawing area, which starts one frame band
        // below the top edge of the sheet.
        gfx.Save();
        gfx.TranslateTransform(0, FrameMargin);

        var behind = new List<PdfCommentElem>();
        var front = new List<PdfCommentElem>();
        foreach (var c in sheet.Elems.OfType<PdfCommentElem>())
            (c.BehindLogic ? behind : front).Add(c);

        foreach (var c in behind) DrawComment(gfx, c, false);
        foreach (var w in sheet.Wires) DrawWire(gfx, w);
        foreach (var e in sheet.Elems)
        {
            switch (e)
            {
                case PdfBlockElem b: DrawBlock(gfx, b); break;
                case PdfVarElem v: DrawTerminal(gfx, v); break;
            }
        }
        foreach (var c in front) DrawComment(gfx, c, true);

        gfx.Restore();
    }

    /// <summary>The sheet template of a drawing (e.g. "SheetTemplateD").</summary>
    private static SheetTemplate? TemplateFor(PdfProject project, PdfPou pou)
    {
        string name = pou.Drawing.SheetTemplate;
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (project.Templates.TryGetValue(name, out var tpl)) return tpl;
        // The drawing may name the size instead of the template ("SheetTemplateD" vs "D").
        foreach (var (key, value) in project.Templates)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase)) return value;
            if (key.EndsWith(pou.Drawing.Size ?? "", StringComparison.OrdinalIgnoreCase)
                && (pou.Drawing.Size ?? "").Length > 0) return value;
        }
        return null;
    }

    // ---- frame --------------------------------------------------------------

    private static void DrawFrame(XGraphics gfx, PdfDrawing drawing, double sheetW, double sheetH)
    {
        double m = FrameMargin;
        int hz = Math.Max(1, drawing.HZones <= 0 ? 16 : drawing.HZones);
        int vz = Math.Max(1, drawing.VZones <= 0 ? 8 : drawing.VZones);
        double innerW = sheetW - 2 * m;
        double innerH = sheetH - 2 * m;
        // TriStation insets the column grid by 4 units on each side, so the two outer
        // columns are a little wider than the ones in between.
        double colW = (innerW - 8) / hz;
        double rowH = innerH / vz;

        gfx.DrawRectangle(Frame15, WhiteBrush, 0, 0, sheetW, sheetH);
        gfx.DrawRectangle(Frame1, m, m, innerW, innerH);

        for (int i = 1; i < hz; i++)
        {
            double x = m + 4 + i * colW;
            gfx.DrawLine(Frame1, x, 0, x, m);
            gfx.DrawLine(Frame1, x, sheetH - m, x, sheetH);
        }
        for (int i = 1; i < vz; i++)
        {
            double y = m + i * rowH;
            gfx.DrawLine(Frame1, 0, y, m, y);
            gfx.DrawLine(Frame1, sheetW - m, y, sheetW, y);
        }

        // The zone numbers run from right to left (1 in the rightmost column) and are
        // centred in their column, in the top and the bottom band.
        var font = new XFont(FontFamily, LabelFontSize);
        for (int i = 0; i < hz; i++)
        {
            double cx = (ColumnStart(i, m, colW) + ColumnStart(i + 1, m, colW)) / 2;
            string number = (hz - i).ToString();
            DrawCentered(gfx, number, font, FrameBrush, cx, m / 2);
            DrawCentered(gfx, number, font, FrameBrush, cx, sheetH - m / 2);
        }

        // The row letters run from the bottom up. TriStation doubles the letter once
        // the plain sequence runs out (8 rows: A..G and HH) and leaves the top cell of
        // the right hand band empty.
        for (int i = 0; i < vz; i++)
        {
            double y = m + (i + 0.5) * rowH;   // i = 0 is the top row
            string ch = RowLetter(vz - 1 - i);
            if (ch.Length == 0) continue;
            DrawRowLabel(gfx, ch, font, FrameBrush, m / 2, y);
            if (i > 0) DrawRowLabel(gfx, ch, font, FrameBrush, sheetW - m / 2, y);
        }
    }

    /// <summary>The left edge of a frame column (the outer columns are 4 units wider).</summary>
    private static double ColumnStart(int column, double margin, double colW)
        => margin + (column == 0 ? 0 : 4 + column * colW);

    /// <summary>The row label of a row, counted from the bottom. TriStation doubles the
    /// letter when the row count exceeds the plain letter sequence.</summary>
    private static string RowLetter(int rowFromBottom)
    {
        if (rowFromBottom < 0) return "";
        string ch = RowLetters[Math.Min(rowFromBottom, RowLetters.Length - 1)].ToString();
        return rowFromBottom < 7 ? ch : ch + ch;
    }

    /// <summary>The size of the frame labels (the zone numbers and the row letters).</summary>
    private const double LabelFontSize = 19.03;

    // ---- title block --------------------------------------------------------

    /// <summary>The font size of a template text, in sheet units. TriStation's template
    /// sizes are points on the printed sheet; the sizes seen in a TriStation print are
    /// 13, 16, 19, 25 and 33 units for 10, 12, 14, 18 and 24 points.</summary>
    private static double FontUnits(double templateSize)
    {
        if (FontUnitSizes.TryGetValue(templateSize, out double units)) return units;
        return Math.Max(4, 1.5 * templateSize - 2);
    }

    private static readonly Dictionary<double, double> FontUnitSizes = new()
    {
        [10] = 13, [12] = 16, [14] = 19, [18] = 25, [24] = 33,
    };

    /// <summary>Draws the title block of the sheet template. The first sheet of the print
    /// carries the full variant, the others the compact one (the way TriStation prints).</summary>
    private static void DrawTitleBlock(XGraphics gfx, PdfProject project, PdfPou pou, PdfSheet sheet, SheetTemplate? tpl)
    {
        if (tpl == null || tpl.Backgrounds.Count == 0)
        {
            DrawFallbackTitleBlock(gfx, project, pou, sheet);
            return;
        }

        int bg = sheet.GlobalNumber <= 1 ? 0 : 1;
        if (bg >= tpl.Backgrounds.Count) bg = tpl.Backgrounds.Count - 1;
        var elems = tpl.Backgrounds[bg];

        foreach (var e in elems)
            if (e.Box) gfx.DrawRectangle(Ink1, e.X, e.Y, e.W, e.H);

        foreach (var e in elems)
        {
            if (e.Text.Length == 0) continue;
            string text = e.ExpandMacros ? ExpandMacros(e.Text, sheet) : e.Text;
            if (text.Trim().Length == 0) continue;
            DrawTemplateText(gfx, e, text);
        }
    }

    /// <summary>Draws one template text. TriStation prints the title block texts top
    /// aligned: the baseline sits about one ascent below the top of the text box. A
    /// text that does not fit the cell is wrapped over several lines (TriStation never
    /// scales the font down).</summary>
    private static void DrawTemplateText(XGraphics gfx, TemplateElem e, string text)
    {
        double fontUnits = FontUnits(e.Size);
        var font = new XFont(FontFamily, fontUnits);
        double maxWidth = Math.Max(10, e.W - 10);

        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            WrapLine(gfx, raw.Trim(), font, maxWidth, lines);

        double baseline = e.Y + 0.966 * fontUnits;
        double step = 1.17 * fontUnits;
        foreach (var line in lines)
        {
            if (line.Length > 0)
            {
                var size = gfx.MeasureString(line, font);
                double x = e.Align == "Center" ? e.X + e.W / 2 - size.Width / 2
                         : e.Align == "Right" ? e.X + e.W - 5 - size.Width
                         : e.X + 5;
                gfx.DrawString(line, font, InkBrush, x, baseline);
            }
            baseline += step;
        }
    }

    /// <summary>Splits one line of a template text into the lines that fit the cell.</summary>
    private static void WrapLine(XGraphics gfx, string text, XFont font, double maxWidth, List<string> lines)
    {
        if (text.Length == 0) { lines.Add(""); return; }
        string current = "";
        foreach (var word in text.Split(' '))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && gfx.MeasureString(candidate, font).Width > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else current = candidate;
        }
        lines.Add(current);
    }

    /// <summary>Expands the title block macros of a sheet.</summary>
    private static string ExpandMacros(string text, PdfSheet sheet)
    {
        foreach (var (key, value) in sheet.Macros) text = text.Replace(key, value);
        return System.Text.RegularExpressions.Regex.Replace(text, "%[A-Z0-9_]+", "").Trim();
    }

    // The title block of a size D sheet, used when the container has no template.
    private static readonly (double x, double y, double w, double h)[] TbBoxes =
    {
        (0, 0, 395, 280),
        (0, 70, 1014, 70),
        (198, 70, 197, 210),
        (0, 105, 395, 70),
        (395, 140, 50, 55),
        (914, 140, 100, 105),
        (395, 195, 619, 50),
        (0, 210, 395, 35),
    };

    private static readonly (double x, double y, double w, double h, string align, double size, string text)[] TbLabels =
    {
        (395, 2, 75, 20, "Left", 10, "PROJECT"),
        (404, 22, 610, 45, "Left", 24, "%PROJECT_NAME"),
        (45, 80, 105, 15, "Center", 10, "ACTIVITY"),
        (245, 80, 110, 15, "Center", 10, "DATE/TIME"),
        (395, 72, 125, 20, "Left", 10, "DRAWING TITLE"),
        (404, 92, 610, 45, "Left", 24, "%DRAWING_TITLE"),
        (0, 107, 198, 19, "Left", 10, "CREATED BY"),
        (198, 120, 197, 20, "Center", 12, "%DATE_CREATED %TIME_CREATED"),
        (0, 142, 198, 19, "Left", 10, "MODIFIED BY"),
        (0, 155, 198, 20, "Left", 12, "%MODIFIED_BY"),
        (198, 155, 197, 20, "Center", 12, "%DATE_MODIFIED %TIME_MODIFIED"),
        (0, 177, 198, 20, "Left", 10, "PRINTED BY"),
        (0, 190, 198, 20, "Left", 12, "%PRINTED_BY"),
        (198, 190, 197, 20, "Center", 12, "%DATE_PRINTED %TIME_PRINTED"),
        (0, 212, 198, 21, "Left", 10, "APPROVED BY"),
        (0, 225, 198, 20, "Left", 12, "%APPROVED_NAME"),
        (198, 225, 197, 20, "Center", 12, "%APPROVED_TIME"),
        (0, 247, 198, 21, "Left", 10, "SHEET TEMPLATE"),
        (0, 260, 198, 20, "Left", 12, "%SHEET_TEMPLATE"),
        (394, 142, 45, 15, "Left", 10, "SIZE"),
        (400, 153, 40, 40, "Center", 24, "%DRAWING_SIZE"),
        (446, 142, 110, 15, "Left", 10, "DRAWING NO."),
        (455, 160, 460, 35, "Left", 18, "%DRAWING_NUMBER"),
        (915, 142, 40, 20, "Left", 10, "REV."),
        (919, 160, 95, 35, "Center", 18, "%REVISION"),
        (395, 197, 125, 20, "Left", 10, "SHEET TITLE"),
        (401, 214, 510, 30, "Left", 18, "%SHEET_TITLE"),
        (915, 197, 60, 15, "Left", 10, "SHEET"),
        (919, 214, 95, 30, "Center", 14, "%SHEET_NUMBER of %SHEET_COUNT"),
        (395, 247, 105, 20, "Left", 10, "ELEMENT"),
        (397, 257, 615, 23, "Left", 14, "%ELEMENT_NAME"),
    };

    /// <summary>The synthesised title block used when the container has no template.</summary>
    private static void DrawFallbackTitleBlock(XGraphics gfx, PdfProject project, PdfPou pou, PdfSheet sheet)
    {
        var size = SizeOf(pou.Drawing);
        double bx = size.W - FrameMargin - size.TitleW;
        double by = size.H - FrameMargin - size.TitleH;

        gfx.DrawRectangle(WhiteBrush, bx, by, size.TitleW, size.TitleH);
        foreach (var (x, y, w, h) in TbBoxes)
            gfx.DrawRectangle(Ink1, bx + x, by + y, w, h);

        foreach (var (x, y, w, h, align, sz, text) in TbLabels)
        {
            string expanded = ExpandMacros(text, sheet);
            if (expanded.Length == 0) continue;
            var elem = new TemplateElem { X = bx + x, Y = by + y, W = w, H = h, Align = align, Size = sz };
            DrawTemplateText(gfx, elem, expanded);
        }
    }

    // ---- wires --------------------------------------------------------------

    private static void DrawWire(XGraphics gfx, PdfWire w)
    {
        if (w.Positions.Count < 2) return;
        for (int i = 1; i < w.Positions.Count; i++)
        {
            var a = w.Positions[i - 1];
            var b = w.Positions[i];
            gfx.DrawLine(WirePen, a.X, a.Y, b.X, b.Y);
        }
        var first = w.Positions[0];
        var last = w.Positions[^1];
        DrawDot(gfx, first.X, first.Y, 2.4);
        DrawDot(gfx, last.X, last.Y, 2.4);
    }

    // ---- blocks -------------------------------------------------------------

    private static double BlockPinY(PdfBlockElem e, int i) => e.Y + (e.IsFunction ? 40 : 60) + i * 20;

    private static void DrawBlock(XGraphics gfx, PdfBlockElem e)
    {
        double header = e.IsFunction ? 40 : 60;
        string label = e.IsFunction ? e.TypeName : (string.IsNullOrEmpty(e.Instance) ? e.TypeName : e.Instance);
        string sub = e.IsFunction ? "" : e.TypeName;

        gfx.DrawRectangle(Ink15, WhiteBrush, e.X, e.Y, e.W, e.H);
        if (!e.IsFunction)
            gfx.DrawLine(Ink075, e.X, e.Y + header - 4, e.X + e.W, e.Y + header - 4);

        var nameFont = new XFont(FontFamily, 16, XFontStyle.Bold);
        DrawCentered(gfx, label, nameFont, InkBrush, e.X + e.W / 2, e.Y + (e.IsFunction ? 26 : 22));

        if (!string.IsNullOrEmpty(sub))
        {
            var subFont = new XFont(FontFamily, 13);
            DrawCentered(gfx, sub, subFont, InkBrush, e.X + e.W / 2, e.Y + 46);
        }

        var pinFont = new XFont(FontFamily, 11);
        for (int i = 0; i < e.Inputs.Count; i++)
        {
            double py = BlockPinY(e, i);
            gfx.DrawString(e.Inputs[i].Name, pinFont, InkBrush, e.X + 6, py + 4);
            gfx.DrawLine(Ink1, e.X, py, e.X - 10, py);
            DrawDot(gfx, e.X - 10, py, 2.2);
        }
        for (int i = 0; i < e.Outputs.Count; i++)
        {
            double py = BlockPinY(e, i);
            var sz = gfx.MeasureString(e.Outputs[i], pinFont);
            gfx.DrawString(e.Outputs[i], pinFont, InkBrush, e.X + e.W - 6 - sz.Width, py + 4);
            gfx.DrawLine(Ink1, e.X + e.W, py, e.X + e.W + 10, py);
            DrawDot(gfx, e.X + e.W + 10, py, 2.2);
        }
    }

    // ---- terminals ----------------------------------------------------------

    private static void DrawTerminal(XGraphics gfx, PdfVarElem e)
    {
        gfx.DrawRectangle(Ink1, WhiteBrush, e.X, e.Y, e.W, e.H);

        if (e.HasOutWire)
        {
            gfx.DrawLine(Ink075, e.X + e.W, e.Y + 4, e.X + e.W, e.Y + e.H - 4);
            gfx.DrawLine(Ink1, e.X + e.W, e.Y + 10, e.X + e.W + 10, e.Y + 10);
            DrawDot(gfx, e.X + e.W + 10, e.Y + 10, 2.2);
        }
        if (e.HasInWire)
        {
            gfx.DrawLine(Ink1, e.X, e.Y + 10, e.X - 10, e.Y + 10);
            DrawDot(gfx, e.X - 10, e.Y + 10, 2.2);
        }

        var nameFont = new XFont(FontFamily, 13, XFontStyle.Bold);
        DrawCentered(gfx, e.Name, nameFont, InkBrush, e.X + e.W / 2, e.Y + e.H / 2);

        if (e.Annotate && !string.IsNullOrWhiteSpace(e.Annotation))
        {
            var annFont = new XFont(FontFamily, 11);
            DrawCentered(gfx, e.Annotation, annFont, InkBrush, e.X + e.W / 2, e.Y + e.H + 13);
        }
    }

    // ---- comments -----------------------------------------------------------

    private static void DrawComment(XGraphics gfx, PdfCommentElem e, bool front)
    {
        bool dbl = e.Border == "Double";
        bool single = e.Border == "Single" || dbl;

        if (single)
        {
            if (front) gfx.DrawRectangle(Ink1, WhiteBrush, e.X, e.Y, e.W, e.H);
            else gfx.DrawRectangle(Ink1, e.X, e.Y, e.W, e.H);
        }
        if (dbl)
            gfx.DrawRectangle(Ink1, e.X + 3, e.Y + 3, Math.Max(0, e.W - 6), Math.Max(0, e.H - 6));

        double fontSize = FontUnits(e.TextSize <= 0 ? 10 : e.TextSize);
        var font = new XFont(FontFamily, fontSize);
        var lines = WrapComment(e.Text, Math.Max(20, e.W - 8), fontSize);
        bool center = e.Align == "Center";
        double tx = center ? e.X + e.W / 2 : e.X + 4;
        double lineHeight = fontSize * 1.18;
        double y = e.Y + 3 + fontSize * 0.85;
        for (int i = 0; i < lines.Count; i++)
        {
            gfx.DrawString(lines[i], font, InkBrush, tx, y, center ? XStringFormats.Center : XStringFormats.Default);
            y += lineHeight;
        }
    }

    private static List<string> WrapComment(string text, double maxWidth, double fontSize)
    {
        double approxChar = fontSize * 0.56;
        int maxChars = Math.Max(4, (int)Math.Floor(maxWidth / approxChar));
        var lines = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            if (rawLine.Length <= maxChars)
            {
                lines.Add(rawLine);
                continue;
            }
            string current = "";
            foreach (var word in rawLine.Split(' '))
            {
                if (current.Length == 0) current = word;
                else if (current.Length + 1 + word.Length <= maxChars) current += " " + word;
                else { lines.Add(current); current = word; }
            }
            if (current.Length > 0) lines.Add(current);
        }
        return lines.Count > 0 ? lines : new List<string> { "" };
    }

    // ---- text-only fallback -------------------------------------------------

    private static void DrawTextPages(PdfDocument doc, PdfProject project)
    {
        const double pageW = 595, pageH = 842, margin = 40;
        foreach (var pou in project.Pous)
        {
            var text = pou.St;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var titleFont = new XFont(FontFamily, 12, XFontStyle.Bold);
            var bodyFont = new XFont("Courier New", 8);
            double lineHeight = bodyFont.Size * 1.25;

            PdfPage page = doc.AddPage();
            page.Width = XUnit.FromPoint(pageW);
            page.Height = XUnit.FromPoint(pageH);
            XGraphics gfx = XGraphics.FromPdfPage(page);

            double y = margin;
            gfx.DrawString($"{pou.Name} ({pou.PouType})", titleFont, InkBrush, margin, y);
            y += 18;

            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (y > pageH - margin)
                {
                    gfx.Dispose();
                    page = doc.AddPage();
                    page.Width = XUnit.FromPoint(pageW);
                    page.Height = XUnit.FromPoint(pageH);
                    gfx = XGraphics.FromPdfPage(page);
                    y = margin;
                }
                gfx.DrawString(line, bodyFont, InkBrush, margin, y);
                y += lineHeight;
            }
            gfx.Dispose();
        }
    }

    // ---- helpers ------------------------------------------------------------

    private static void DrawDot(XGraphics gfx, double cx, double cy, double r)
        => gfx.DrawEllipse(PinBrush, cx - r, cy - r, r * 2, r * 2);

    /// <summary>Draws a text centred in a point (both axes).</summary>
    private static void DrawCentered(XGraphics gfx, string text, XFont font, XBrush brush, double cx, double cy)
    {
        var sz = gfx.MeasureString(text, font);
        gfx.DrawString(text, font, brush, cx - sz.Width / 2, cy + 0.35 * font.Size);
    }

    /// <summary>Draws a row label in a frame band. TriStation centres a doubled letter
    /// as if it were a single one, so the second letter sticks out of the band.</summary>
    private static void DrawRowLabel(XGraphics gfx, string text, XFont font, XBrush brush, double cx, double cy)
    {
        var sz = gfx.MeasureString(text.Length > 1 ? text.Substring(0, 1) : text, font);
        gfx.DrawString(text, font, brush, cx - sz.Width / 2, cy + 0.35 * font.Size);
    }
}
