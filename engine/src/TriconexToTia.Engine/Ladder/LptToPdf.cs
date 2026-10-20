using System.Text;
using System.Text.RegularExpressions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace TriconexToTia.Engine.Ladder;

public static class LptToPdf
{
    private static readonly Regex PageHeaderRe = new(@"TRISTATION.*Page\s+\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool HasTristationPageHeaders(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        var text = Decode(bytes);
        return PageHeaderRe.IsMatch(text);
    }

    public static byte[] ConvertToPdf(byte[] bytes)
    {
        var text = Decode(bytes);
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var blocks = SplitByPageHeaders(lines);
        if (blocks.Count == 0)
            blocks = new List<string[]> { lines };

        var doc = new PdfDocument();
        doc.Info.Title = "LPT";
        doc.Info.Creator = "XforceMsw";

        foreach (var block in blocks)
        {
            AddPage(doc, block);
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static List<string[]> SplitByPageHeaders(string[] lines)
    {
        var blocks = new List<string[]>();
        var current = new List<string>();
        foreach (var line in lines)
        {
            if (PageHeaderRe.IsMatch(line) && current.Count > 0)
            {
                blocks.Add(current.ToArray());
                current = new List<string>();
            }
            current.Add(line);
        }
        if (current.Count > 0)
            blocks.Add(current.ToArray());
        return blocks;
    }

    private static void AddPage(PdfDocument doc, string[] lines)
    {
        var page = doc.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;
        page.Orientation = PdfSharpCore.PageOrientation.Landscape;

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Courier New", 6.2, XFontStyle.Regular);
        double lineHeight = font.GetHeight();
        double x = 18;
        double y = 22;
        double maxY = page.Height - 22;

        foreach (var line in lines)
        {
            if (y > maxY)
                break;
            gfx.DrawString(line.Replace("\t", "        "), font, XBrushes.Black, x, y);
            y += lineHeight;
        }
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return Encoding.ASCII.GetString(bytes); }
    }
}
