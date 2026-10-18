// Renders the cross-reference "System Summary" — a compact, deterministic
// analysis section for the markdown/HTML reports plus a where-used sheet for
// the XLSX workbook.

using System.Text;

namespace CircuitReader.Core;

public static class XrefReport
{
    private const int TableCap = 50;

    public static string ToMarkdownSummary(IReadOnlyList<PageLadder> pages, XrefIndex idx)
    {
        var sb = new StringBuilder();
        int totalRungs = pages.Sum(p => p.Rungs.Count);
        var state = idx.StateVariables.ToList();
        var coils = idx.AllCoils.ToList();
        var dangling = idx.DanglingReads.ToList();
        var orphans = idx.OrphanedWrites.ToList();
        var multi = idx.MultipleWriters.ToList();
        var stuck = idx.StuckLatches.ToList();
        var writeOnly = idx.WriteOnlyUnlatches.ToList();
        int fieldIn = idx.FieldInputs.Count();
        int fieldOut = idx.FieldOutputs.Count();

        sb.AppendLine("## System Summary");
        sb.AppendLine();
        sb.AppendLine($"- Pages analyzed: **{pages.Count}**");
        sb.AppendLine($"- Total rungs: **{totalRungs}**");
        sb.AppendLine($"- Distinct addresses referenced: **{idx.ByAddress.Count}**");
        sb.AppendLine($"- Coil outputs (any kind): **{coils.Count}**");
        if (fieldIn > 0)
            sb.AppendLine($"- Field inputs referenced (X/WX, read in-ladder): **{fieldIn}**");
        if (fieldOut > 0)
            sb.AppendLine($"- Field outputs driven (Y/WY, written in-ladder): **{fieldOut}**");
        sb.AppendLine($"- State variables (LATCH/UNLATCH bits): **{state.Count}**");
        sb.AppendLine($"- ⚠ Stuck latches (set but never reset): **{stuck.Count}**");
        sb.AppendLine($"- ⚠ Write-only unlatches (cleared but never set): **{writeOnly.Count}**");
        sb.AppendLine($"- Dangling reads (read in-ladder but never written): **{dangling.Count}**");
        sb.AppendLine($"- Orphaned writes (written in-ladder but never read): **{orphans.Count}**");
        sb.AppendLine($"- Addresses with suspicious multiple writers: **{multi.Count}**");
        if (fieldIn > 0 || fieldOut > 0)
        {
            sb.AppendLine();
            sb.AppendLine("> Physical field I/O (X/WX inputs, Y/WY outputs) is one-sided in the " +
                "ladder by design, so it is counted separately above and excluded from the " +
                "dangling-read / orphaned-write anomaly totals.");
        }
        sb.AppendLine();

        Section(sb, "⚠ Stuck latches (LATCH with no UNLATCH or COIL reset path)",
            stuck, "set on", e => Locs(e.Writers.Where(w => w.Kind == "LATCH")));
        Section(sb, "⚠ Write-only unlatches (UNLATCH with no LATCH or COIL set path)",
            writeOnly, "reset on", e => Locs(e.Writers.Where(w => w.Kind == "UNLATCH")));
        Section(sb, "⚠ Addresses with multiple writers", multi, "writers",
            e => string.Join(", ", e.Writers
                .Select(w => $"p{w.Page}/r{w.Rung}({w.Kind})")
                .Distinct()));
        Section(sb, "⚠ Dangling reads (read in-ladder but never written — e.g. HMI command / SF-program bits)",
            dangling, "read on", e => Locs(e.Readers));

        if (state.Count > 0)
        {
            sb.AppendLine("### State variables (machine memory)");
            sb.AppendLine();
            sb.AppendLine("| address | label | set on | reset on | read on |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var e in state.OrderBy(e => e.Address))
            {
                sb.AppendLine($"| `{e.Address}` | {Lbl(e.Label)} | " +
                    $"{Locs(e.Writers.Where(w => w.Kind == "LATCH"))} | " +
                    $"{Locs(e.Writers.Where(w => w.Kind == "UNLATCH"))} | " +
                    $"{Locs(e.Readers)} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title,
        List<AddressXref> items, string col, Func<AddressXref, string> cell)
    {
        if (items.Count == 0) return;
        sb.AppendLine($"### {title}");
        sb.AppendLine();
        sb.AppendLine($"| address | label | {col} |");
        sb.AppendLine("|---|---|---|");
        foreach (var e in items.OrderBy(e => e.Address).Take(TableCap))
            sb.AppendLine($"| `{e.Address}` | {Lbl(e.Label)} | {cell(e)} |");
        if (items.Count > TableCap)
            sb.AppendLine($"_…and {items.Count - TableCap} more._");
        sb.AppendLine();
    }

    private static string Locs(IEnumerable<AddressUse> uses, int max = 8)
    {
        var locs = uses.Select(u => (u.Page, u.Rung)).Distinct()
                       .OrderBy(t => t.Page).ThenBy(t => t.Rung).ToList();
        if (locs.Count == 0) return "—";
        var head = string.Join(", ", locs.Take(max).Select(t => $"p{t.Page}/r{t.Rung}"));
        if (locs.Count > max) head += $", …(+{locs.Count - max})";
        return head;
    }

    private static string Lbl(string? s) =>
        string.IsNullOrEmpty(s) ? "—" : ReportWriter.EscapeCell(s);

    // -------- XLSX: a where-used "Cross-Reference" sheet --------------------

    public static XlsxExporter.Sheet CrossRefSheet(XrefIndex idx)
    {
        var headers = new[] { "Address", "Label", "Writers", "Readers", "WrittenOn", "ReadOn", "Flags" };
        var rows = new List<IReadOnlyList<XlsxExporter.Cell>>();
        foreach (var e in idx.ByAddress.Values.OrderBy(e => e.Address))
        {
            var flags = new List<string>();
            if (e.Class == AddressClass.Input) flags.Add("field-input");
            if (e.Class == AddressClass.Output) flags.Add("field-output");
            if (e.IsDanglingRead) flags.Add("dangling-read");
            if (e.IsOrphanedWrite) flags.Add("orphaned-write");
            if (e.HasMultipleWriters) flags.Add("multiple-writers");
            if (e.IsStateVariable) flags.Add("state-var");
            rows.Add(new[]
            {
                XlsxExporter.Cell.S(e.Address),
                XlsxExporter.Cell.S(e.Label),
                XlsxExporter.Cell.N(e.Writers.Count),
                XlsxExporter.Cell.N(e.Readers.Count),
                XlsxExporter.Cell.S(PlainLocs(e.Writers)),
                XlsxExporter.Cell.S(PlainLocs(e.Readers)),
                XlsxExporter.Cell.S(string.Join(", ", flags)),
            });
        }
        return new XlsxExporter.Sheet("Cross-Reference", headers, rows);
    }

    private static string PlainLocs(IEnumerable<AddressUse> uses) =>
        string.Join(", ", uses.Select(u => (u.Page, u.Rung)).Distinct()
            .OrderBy(t => t.Page).ThenBy(t => t.Rung)
            .Select(t => $"p{t.Page}/r{t.Rung}"));
}
