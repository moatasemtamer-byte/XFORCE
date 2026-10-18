// Markdown report writer — produces the same column layout as the Python
// version, with `|` characters in equations escaped as `\|` so they don't
// terminate the markdown-table cell.

using System.Text;

namespace CircuitReader.Core;

public static class ReportWriter
{
    public static string ToMarkdown(string pdfName, IReadOnlyList<PageLadder> pages,
                                    string? dialect = null, XrefIndex? xref = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Circuit Reader Report — `{pdfName}`");
        var mode = dialect is null
            ? "OFFLINE — C# port (column-based parser)"
            : $"OFFLINE — C# ({DocumentAnalyzer.DialectName(dialect)})";
        sb.AppendLine($"**Mode:** {mode}");
        sb.AppendLine();
        sb.AppendLine($"- Pages analyzed: **{pages.Count}**");
        sb.AppendLine($"- Total rungs:    **{pages.Sum(p => p.Rungs.Count)}**");
        sb.AppendLine();
        if (xref is not null)
        {
            sb.AppendLine(XrefReport.ToMarkdownSummary(pages, xref));
            sb.AppendLine();
        }
        foreach (var page in pages)
        {
            if (page.Rungs.Count == 0) continue;
            sb.AppendLine($"## Page {page.PageNumber}");
            sb.AppendLine();
            sb.AppendLine($"- rung_count: `{page.Rungs.Count}`");
            sb.AppendLine();
            sb.AppendLine("### Ladder rungs");
            sb.AppendLine();
            sb.AppendLine("| # | Output (addr — label) | Boolean equation | Minimized |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var r in page.Rungs)
            {
                var addr = r.Output?.Address ?? "?";
                var label = page.AddressLabels.TryGetValue(addr, out var l) ? l : r.Output?.Label ?? "";
                var kindStr = r.Output?.Kind switch
                {
                    ElementKind.Coil => "( )",
                    ElementKind.Latch => "(L)",
                    ElementKind.Unlatch => "(U)",
                    ElementKind.Timer => "(T)",
                    ElementKind.Compare => "[CMP]",
                    ElementKind.LetAssign => "LET",
                    ElementKind.ClippedCoil => "[clip]",
                    _ => "?",
                };
                var outCell = $"{kindStr} `{addr}` — {EscapeCell(label)}";
                var eqCell = EscapeCodeCell(r.BooleanEquation);
                var mnCell = EscapeCodeCell(r.Minimized);
                sb.AppendLine($"| {r.Number} | {outCell} | {eqCell} | {mnCell} |");
            }
            sb.AppendLine();

            var notes = page.Rungs.Where(r => !string.IsNullOrEmpty(r.Note)).ToList();
            if (notes.Count > 0)
            {
                foreach (var r in notes)
                    sb.AppendLine($"- *Rung {r.Number}:* {r.Note}");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    public static string EscapeCell(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "—";
        return s.Replace("|", @"\|").Replace("\n", "<br>");
    }

    public static string EscapeCodeCell(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "—";
        return $"`{EscapeCell(s)}`";
    }
}
