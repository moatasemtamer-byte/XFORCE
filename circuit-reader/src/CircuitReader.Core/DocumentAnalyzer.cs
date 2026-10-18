// Unified document analyzer — opens a PDF once, detects which PLC dialect it
// holds, and returns a normalized result (a list of PageLadder plus the
// detected dialect name).  Both the CLI and the WPF GUI go through here so new
// dialects light up everywhere at once.

namespace CircuitReader.Core;

public sealed class AnalysisResult
{
    public string Dialect { get; init; } = "unknown";
    public IReadOnlyList<PageLadder> Pages { get; init; } = new List<PageLadder>();
    public int PageCount => Pages.Count;
    public int RungCount => Pages.Sum(p => p.Rungs.Count);
}

public static class DocumentAnalyzer
{
    /// <summary>Human-readable label for each dialect id (for reports / UI).</summary>
    public static string DialectName(string id) => id switch
    {
        "triconex" => "Triconex TRISTATION (ASCII ladder)",
        "ti505" => "TI505 / TI565 SIMATIC (LAD listing)",
        "rslogix" => "Allen-Bradley RSLogix (mnemonic ladder)",
        "step7" => "Siemens STEP 7 STL",
        "iec61131" => "IEC 61131-3 (ST / IL)",
        "squared" => "Square D / geometry ladder",
        _ => id,
    };

    public static AnalysisResult Analyze(string path)
    {
        return Analyze(ReadSource(path));
    }

    /// <summary>
    /// Open a source document as normalized pages.  A real PDF (identified by
    /// the "%PDF-" file signature, not just the extension) is read with
    /// PdfReader; anything else is treated as a raw ASCII program listing
    /// (e.g. a Triconex TRISTATION ".LPT" printout) and read with
    /// TextListingReader.  Both yield the same PdfPageData shape, so every
    /// dialect and exporter works from either kind of source.
    /// </summary>
    public static IReadOnlyList<PdfPageData> ReadSource(string path) =>
        LooksLikePdf(path)
            ? PdfReader.ReadPages(path).ToList()
            : TextListingReader.ReadPages(path);

    private static bool LooksLikePdf(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> sig = stackalloc byte[5];
            int read = fs.Read(sig);
            return read == 5 &&
                   sig[0] == (byte)'%' && sig[1] == (byte)'P' &&
                   sig[2] == (byte)'D' && sig[3] == (byte)'F' && sig[4] == (byte)'-';
        }
        catch
        {
            return false;
        }
    }

    public static AnalysisResult Analyze(IReadOnlyList<PdfPageData> pages)
    {
        var fullText = string.Join("\n", pages.Select(p => p.Text));

        // Triconex TRISTATION — text/ASCII ladder (spans page breaks).
        if (TriconexParser.IsTriconex(fullText))
        {
            var doc = TriconexParser.Parse(
                pages.Select(p => ((IReadOnlyList<string>)p.TextLines, p.PageNumber)));
            return new AnalysisResult
            {
                Dialect = "triconex",
                Pages = TriconexConverter.ToPageLadders(doc),
            };
        }

        // TI505 / TI565 SIMATIC — geometry LAD listing (word-position based).
        // Detected before the text-mnemonic dialects so its "LAD Network"
        // listings aren't mis-claimed.
        if (Ti505Parser.IsTi505(fullText))
        {
            return new AnalysisResult
            {
                Dialect = "ti505",
                Pages = Ti505Parser.Parse(pages),
            };
        }

        // Text-mnemonic dialects — detected from the embedded page text and
        // parsed page-by-page.  Order mirrors the reference pipeline:
        // RSLogix, then STEP 7, then IEC 61131-3.
        if (RsLogixParser.IsRsLogix(fullText))
            return TextDialect(pages, "rslogix", RsLogixParser.ParsePage);
        if (Step7Parser.IsStep7(fullText))
            return TextDialect(pages, "step7", Step7Parser.ParsePage);
        if (Iec61131Parser.IsIec(fullText))
            return TextDialect(pages, "iec61131", Iec61131Parser.ParsePage);

        // Default: Square D geometry-based ladder.
        var ladders = new LadderAnalyzer().Analyze(pages);
        return new AnalysisResult { Dialect = "squared", Pages = ladders };
    }

    private static AnalysisResult TextDialect(
        IReadOnlyList<PdfPageData> pages, string dialect, Func<string, int, PageLadder> parse)
    {
        var result = new List<PageLadder>();
        int rungNo = 0;
        foreach (var p in pages)
        {
            var pl = parse(p.Text, p.PageNumber);
            // Number rungs globally across the document so cross-reference
            // "p/r" coordinates and report numbering stay monotonic.
            foreach (var r in pl.Rungs) r.Number = ++rungNo;
            result.Add(pl);
        }
        return new AnalysisResult { Dialect = dialect, Pages = result };
    }
}
