// HTML exporter — a self-contained, styled report (no external CSS/JS) that
// opens in any browser.  One section per page with a sortable-looking table of
// rungs; the boolean and function equations are shown in monospace.

using System.Net;
using System.Text;

namespace CircuitReader.Core;

public static class HtmlExporter
{
    public static string ToHtml(string pdfName, string dialect, IReadOnlyList<PageLadder> pages,
                                XrefIndex? xref = null)
    {
        var sb = new StringBuilder();
        int totalRungs = pages.Sum(p => p.Rungs.Count);

        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append($"<title>Circuit Reader — {Enc(pdfName)}</title>");
        sb.Append("<style>");
        sb.Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1b1b1b;background:#fafafa}");
        sb.Append("h1{font-size:20px}h2{font-size:16px;margin-top:28px;border-bottom:2px solid #ddd;padding-bottom:4px}");
        sb.Append(".meta{color:#555;font-size:13px;margin:4px 0}");
        sb.Append("table{border-collapse:collapse;width:100%;margin:10px 0;background:#fff}");
        sb.Append("th,td{border:1px solid #ddd;padding:6px 8px;font-size:13px;vertical-align:top;text-align:left}");
        sb.Append("th{background:#f0f0f0}");
        sb.Append("td.eq{font-family:Consolas,monospace;white-space:pre-wrap}");
        sb.Append("td.num{text-align:right;color:#666;width:48px}");
        sb.Append(".note{color:#8a6d00}.cmt{color:#3a6ea5;font-style:italic}");
        sb.Append(".summary{background:#fff;border:1px solid #ddd;padding:8px 14px;border-radius:6px}");
        sb.Append(".summary li{margin:2px 0}.warn{color:#b00020;font-weight:bold}");
        sb.Append("</style></head><body>");

        sb.Append($"<h1>Circuit Reader Report — {Enc(pdfName)}</h1>");
        sb.Append($"<p class=\"meta\">Dialect: <b>{Enc(DocumentAnalyzer.DialectName(dialect))}</b></p>");
        sb.Append($"<p class=\"meta\">Pages analyzed: <b>{pages.Count}</b> &nbsp;•&nbsp; Total rungs: <b>{totalRungs}</b></p>");

        if (xref is not null)
            sb.Append(SummaryHtml(xref));

        foreach (var page in pages)
        {
            if (page.Rungs.Count == 0) continue;
            sb.Append($"<h2>Page {page.PageNumber} <span class=\"meta\">({page.Rungs.Count} rungs)</span></h2>");
            sb.Append("<table><thead><tr>");
            sb.Append("<th>#</th><th>Output</th><th>Label</th><th>Boolean equation</th><th>Function equation</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var r in page.Rungs)
            {
                var addr = r.Output?.Address ?? "?";
                var label = page.AddressLabels.TryGetValue(addr, out var l) ? l : r.Output?.Label ?? "";
                sb.Append("<tr>");
                sb.Append($"<td class=\"num\">{r.Number}</td>");
                sb.Append($"<td>{KindGlyph(r.Output?.Kind)} <code>{Enc(addr)}</code></td>");
                sb.Append($"<td>{Enc(label)}</td>");
                sb.Append($"<td class=\"eq\">{Enc(r.BooleanEquation)}</td>");
                sb.Append($"<td class=\"eq\">{Enc(r.FunctionEquation)}</td>");
                sb.Append("</tr>");
                if (!string.IsNullOrEmpty(r.Comment))
                    sb.Append($"<tr><td></td><td colspan=\"4\" class=\"cmt\">{Enc(r.Comment)}</td></tr>");
                if (!string.IsNullOrEmpty(r.Note))
                    sb.Append($"<tr><td></td><td colspan=\"4\" class=\"note\">{Enc(r.Note)}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static void Write(string pdfName, string dialect,
        IReadOnlyList<PageLadder> pages, string path, XrefIndex? xref = null) =>
        File.WriteAllText(path, ToHtml(pdfName, dialect, pages, xref), new UTF8Encoding(false));

    private static string SummaryHtml(XrefIndex idx)
    {
        int stuck = idx.StuckLatches.Count();
        int writeOnly = idx.WriteOnlyUnlatches.Count();
        int dangling = idx.DanglingReads.Count();
        int orphans = idx.OrphanedWrites.Count();
        int multi = idx.MultipleWriters.Count();
        int state = idx.StateVariables.Count();
        int coils = idx.AllCoils.Count();
        int fieldIn = idx.FieldInputs.Count();
        int fieldOut = idx.FieldOutputs.Count();

        string Item(string label, int n, bool warn) =>
            $"<li>{(warn && n > 0 ? "<span class=\"warn\">⚠ " : "")}{label}: <b>{n}</b>{(warn && n > 0 ? "</span>" : "")}</li>";

        var sb = new StringBuilder();
        sb.Append("<h2>System Summary</h2><div class=\"summary\"><ul>");
        sb.Append(Item("Distinct addresses referenced", idx.ByAddress.Count, false));
        sb.Append(Item("Coil outputs (any kind)", coils, false));
        if (fieldIn > 0) sb.Append(Item("Field inputs referenced (X/WX)", fieldIn, false));
        if (fieldOut > 0) sb.Append(Item("Field outputs driven (Y/WY)", fieldOut, false));
        sb.Append(Item("State variables (LATCH/UNLATCH)", state, false));
        sb.Append(Item("Stuck latches (set but never reset)", stuck, true));
        sb.Append(Item("Write-only unlatches (cleared but never set)", writeOnly, true));
        sb.Append(Item("Dangling reads (read in-ladder but never written)", dangling, false));
        sb.Append(Item("Orphaned writes (written in-ladder but never read)", orphans, false));
        sb.Append(Item("Addresses with multiple writers", multi, true));
        sb.Append("</ul></div>");
        if (fieldIn > 0 || fieldOut > 0)
            sb.Append("<p class=\"meta\">Physical field I/O (X/WX inputs, Y/WY outputs) is " +
                "one-sided in the ladder by design, so it is counted separately and excluded " +
                "from the dangling-read / orphaned-write anomaly totals.</p>");
        return sb.ToString();
    }

    private static string KindGlyph(ElementKind? k) => k switch
    {
        ElementKind.Coil => "( )",
        ElementKind.Latch => "(L)",
        ElementKind.Unlatch => "(U)",
        ElementKind.Timer => "(T)",
        ElementKind.LetAssign => "LET",
        ElementKind.ClippedCoil => "[clip]",
        _ => "?",
    };

    private static string Enc(string? s) => string.IsNullOrEmpty(s) ? "" : WebUtility.HtmlEncode(s);
}
