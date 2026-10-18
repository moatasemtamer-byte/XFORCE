// Text-listing reader — opens a raw ASCII / monospace PLC program printout
// (e.g. a Triconex TRISTATION ".LPT" listing, or any plain-text mnemonic dump)
// as the same PdfPageData shape the PDF reader produces, so the text-dialect
// parsers (Triconex, RSLogix, STEP 7, IEC 61131-3) can consume a listing file
// directly — no PDF required.
//
// Only the text fields (PageNumber / TextLines / Text) are populated; the
// geometry fields (Words / Elements / VerticalLines / RungHeaders) are empty
// because a flat listing carries no glyph coordinates.  The geometry dialects
// (TI505, Square D) are inherently coordinate-based and simply yield no rungs
// from a flat listing, which is correct.
//
// Column alignment inside each line is significant to the ladder parsers, so
// line content is preserved verbatim (only the line/page delimiters are
// interpreted).

namespace CircuitReader.Core;

public static class TextListingReader
{
    /// <summary>Read a text listing file into normalized pages.</summary>
    public static IReadOnlyList<PdfPageData> ReadPages(string path) =>
        BuildPages(File.ReadAllText(path));

    /// <summary>
    /// Split a raw listing into pages.  Printer form-feed characters (\f) are
    /// the primary page delimiter; when absent the whole listing is treated as
    /// one page (the Triconex parser stitches networks across page breaks
    /// anyway, so page numbers only affect cross-reference display).
    /// </summary>
    public static IReadOnlyList<PdfPageData> BuildPages(string raw)
    {
        var pages = new List<PdfPageData>();
        int n = 0;
        foreach (var block in SplitIntoPageBlocks(raw))
        {
            var lines = SplitLines(block);
            pages.Add(NewPage(++n, lines));
        }
        if (pages.Count == 0)
            pages.Add(NewPage(1, new List<string>()));
        return pages;
    }

    private static PdfPageData NewPage(int number, IReadOnlyList<string> lines) =>
        new(
            PageNumber: number,
            Width: 0,
            Height: 0,
            Elements: Array.Empty<Element>(),
            VerticalLines: Array.Empty<VerticalLine>(),
            Words: Array.Empty<(string, double, double)>(),
            RungHeaders: Array.Empty<(int, double)>(),
            TextLines: lines,
            Text: string.Join("\n", lines));

    private static List<string> SplitIntoPageBlocks(string raw)
    {
        if (raw.IndexOf('\f') >= 0)
        {
            var blocks = raw.Split('\f')
                            .Where(b => b.Trim().Length > 0)
                            .ToList();
            if (blocks.Count > 0) return blocks;
        }
        return new List<string> { raw };
    }

    private static List<string> SplitLines(string block) =>
        block.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
}
