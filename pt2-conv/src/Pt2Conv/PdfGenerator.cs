using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Pt2Conv;

/// <summary>
/// Draws a decompiled TriStation FBD project to a PDF.
///
/// This is a straight port of the geometry in the web viewer's FBD sheet
/// (<c>src/components/engine/fbd-sheet.tsx</c>): the drawing frame with zone ticks,
/// the synthesised title block, then blocks / terminals / wires / comments at the
/// raw export coordinates. Sheet units are the container's own units (1 unit = 1 pt),
/// so a size-D sheet is a 3270 x 2070 pt page.
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

    private static readonly XPen Ink1 = new(Ink, 1.0);
    private static readonly XPen Ink075 = new(Ink, 0.75);
    private static readonly XPen Ink15 = new(Ink, 1.5);
    private static readonly XPen WirePen = new(WireColor, 1.5) { LineJoin = XLineJoin.Round, LineCap = XLineCap.Round };
    private static readonly XPen Frame1 = new(FrameColor, 1.0);
    private static readonly XPen Frame15 = new(FrameColor, 1.5);
    private static readonly XBrush WhiteBrush = new XSolidBrush(XColors.White);
    private static readonly XBrush InkBrush = new XSolidBrush(Ink);
    private static readonly XBrush PinBrush = new XSolidBrush(PinDot);
    private static readonly XBrush FrameBrush = new XSolidBrush(FrameColor);

    private const string FontFamily = "Arial";
    // Points -> user units (the SVG uses the same 96/72 factor).
    private const double Pt = 96.0 / 72.0;

    private sealed record SheetSize(double W, double H, double TitleW, double TitleH);

    private static readonly Dictionary<string, SheetSize> SheetSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = new(990, 732, 318, 96),
        ["B"] = new(1576, 976, 296, 96),
        ["C"] = new(2070, 1570, 423, 156),
        ["D"] = new(3270, 2070, 1014, 280),
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

        doc.Save(outPath);
    }

    // ---- one FBD sheet ------------------------------------------------------

    private static void DrawSheet(PdfDocument doc, PdfProject project, PdfPou pou, PdfSheet sheet)
    {
        var size = SizeOf(pou.Drawing);
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(size.W);
        page.Height = XUnit.FromPoint(size.H);

        using var gfx = XGraphics.FromPdfPage(page);

        DrawFrame(gfx, pou.Drawing, size);
        DrawTitleBlock(gfx, project, pou, sheet, size);

        var behind = new List<PdfCommentElem>();
        var front = new List<PdfCommentElem>();
        foreach (var c in sheet.Elems.OfType<PdfCommentElem>())
            (c.BehindLogic ? behind : front).Add(c);

        foreach (var c in behind) DrawComment(gfx, c, front: false);
        foreach (var w in sheet.Wires) DrawWire(gfx, w);
        foreach (var e in sheet.Elems)
        {
            switch (e)
            {
                case PdfBlockElem b: DrawBlock(gfx, b); break;
                case PdfVarElem v: DrawTerminal(gfx, v); break;
            }
        }
        foreach (var c in front) DrawComment(gfx, c, front: true);
    }

    // ---- frame --------------------------------------------------------------

    private static void DrawFrame(XGraphics gfx, PdfDrawing drawing, SheetSize size)
    {
        double m = Math.Round(size.W * (30.0 / 3270.0));
        int hz = Math.Max(1, drawing.HZones <= 0 ? 16 : drawing.HZones);
        int vz = Math.Max(1, drawing.VZones <= 0 ? 8 : drawing.VZones);
        double innerW = size.W - 2 * m;
        double innerH = size.H - 2 * m;
        double colW = innerW / hz;
        double rowH = innerH / vz;
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        double labelSize = m * 0.6;

        gfx.DrawRectangle(Frame15, WhiteBrush, 0.75, 0.75, size.W - 1.5, size.H - 1.5);
        gfx.DrawRectangle(Frame1, m, m, innerW, innerH);

        for (int i = 1; i < hz; i++)
        {
            double x = m + i * colW;
            gfx.DrawLine(Frame1, x, 0, x, m);
            gfx.DrawLine(Frame1, x, size.H - m, x, size.H);
        }
        for (int i = 1; i < vz; i++)
        {
            double y = m + i * rowH;
            gfx.DrawLine(Frame1, 0, y, m, y);
            gfx.DrawLine(Frame1, size.W - m, y, size.W, y);
        }

        var font = new XFont(FontFamily, labelSize);
        for (int i = 0; i < hz; i++)
        {
            double x = m + (i + 0.5) * colW;
            DrawCentered(gfx, (i + 1).ToString(), font, FrameBrush, x, m / 2);
            DrawCentered(gfx, (i + 1).ToString(), font, FrameBrush, x, size.H - m / 2);
        }
        for (int i = 0; i < vz; i++)
        {
            double y = m + (i + 0.5) * rowH;
            string ch = i < letters.Length ? letters[i].ToString() : "";
            DrawCentered(gfx, ch, font, FrameBrush, m / 2, y);
            DrawCentered(gfx, ch, font, FrameBrush, size.W - m / 2, y);
        }
    }

    // ---- title block --------------------------------------------------------

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

    private static void DrawTitleBlock(XGraphics gfx, PdfProject project, PdfPou pou, PdfSheet sheet, SheetSize size)
    {
        double bx = size.W - size.TitleW;
        double by = size.H - size.TitleH;
        double sx = size.TitleW / 1014.0;
        double sy = size.TitleH / 280.0;

        var vars = new Dictionary<string, string>
        {
            ["%PROJECT_NAME"] = project.Name,
            ["%DRAWING_TITLE"] = pou.Drawing.Title,
            ["%DRAWING_NUMBER"] = pou.Drawing.Number,
            ["%DRAWING_SIZE"] = pou.Drawing.Size,
            ["%REVISION"] = pou.Drawing.Rev,
            ["%SHEET_TEMPLATE"] = pou.Drawing.SheetTemplate,
            ["%SHEET_TITLE"] = string.IsNullOrWhiteSpace(sheet.Title) ? pou.Title : sheet.Title,
            ["%SHEET_NUMBER"] = sheet.Number.ToString(),
            ["%SHEET_COUNT"] = pou.Sheets.Count.ToString(),
            ["%ELEMENT_NAME"] = pou.Name,
        };

        gfx.DrawRectangle(WhiteBrush, bx, by, size.TitleW, size.TitleH);

        foreach (var (x, y, w, h) in TbBoxes)
            gfx.DrawRectangle(Ink1, bx + x * sx, by + y * sy, w * sx, h * sy);

        foreach (var (x, y, w, h, align, sz, text) in TbLabels)
        {
            string expanded = ExpandTb(text, vars);
            if (string.IsNullOrWhiteSpace(expanded)) continue;
            double fs = sz * Pt * sx;
            var font = new XFont(FontFamily, fs);
            bool center = align == "Center";
            double cx = bx + (center ? x + w / 2 : x + 3) * sx;
            double cy = by + (y + h / 2) * sy;
            DrawTextBaseline(gfx, expanded, font, InkBrush, cx, cy, center);
        }
    }

    private static string ExpandTb(string text, Dictionary<string, string> vars)
    {
        string outText = text;
        foreach (var (k, v) in vars) outText = outText.Replace(k, v);
        outText = System.Text.RegularExpressions.Regex.Replace(outText, "%[A-Z0-9_]+", "");
        outText = System.Text.RegularExpressions.Regex.Replace(outText, "\\s{2,}", " ");
        return outText.Trim();
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

        var nameFont = new XFont(FontFamily, 13, XFontStyle.Bold);
        DrawCentered(gfx, label, nameFont, InkBrush, e.X + e.W / 2, e.Y + (e.IsFunction ? 26 : 22));

        if (!string.IsNullOrEmpty(sub))
        {
            var subFont = new XFont(FontFamily, 12);
            DrawCentered(gfx, sub, subFont, InkBrush, e.X + e.W / 2, e.Y + 46);
        }

        var pinFont = new XFont(FontFamily, 12);
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

        var nameFont = new XFont(FontFamily, 12, XFontStyle.Bold);
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

        double fontSize = Math.Max(6, (e.TextSize <= 0 ? 10 : e.TextSize) * Pt);
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

    private static void DrawCentered(XGraphics gfx, string text, XFont font, XBrush brush, double cx, double cy)
    {
        var sz = gfx.MeasureString(text, font);
        gfx.DrawString(text, font, brush, cx - sz.Width / 2, cy + sz.Height * 0.35);
    }

    private static void DrawTextBaseline(XGraphics gfx, string text, XFont font, XBrush brush, double x, double cy, bool center)
    {
        var sz = gfx.MeasureString(text, font);
        double drawX = center ? x - sz.Width / 2 : x;
        gfx.DrawString(text, font, brush, drawX, cy + sz.Height * 0.35);
    }
}
