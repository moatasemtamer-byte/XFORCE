// Top-level analyzer — open a PDF, return PageLadders for every page.

namespace CircuitReader.Core;

public sealed class LadderAnalyzer
{
    public IReadOnlyList<PageLadder> Analyze(string pdfPath) =>
        Analyze(PdfReader.ReadPages(pdfPath));

    public IReadOnlyList<PageLadder> Analyze(IEnumerable<PdfPageData> source)
    {
        var pages = new List<PageLadder>();
        foreach (var p in source)
        {
            var pl = new PageLadder { PageNumber = p.PageNumber };
            var rungs = RungBuilder.BuildRungs(p.Elements, p.VerticalLines, p.RungHeaders);
            foreach (var r in rungs)
            {
                pl.Rungs.Add(r);
                if (r.Output is not null && !string.IsNullOrEmpty(r.Output.Label)
                        && !pl.AddressLabels.ContainsKey(r.Output.Address))
                {
                    pl.AddressLabels[r.Output.Address] = r.Output.Label;
                }
                // Also record labels seen on input contacts so the
                // cross-reference index can use the most descriptive text.
                foreach (var e in r.Inputs)
                {
                    if (e.Address is { Length: > 0 } addr
                        && e.Label is { Length: > 0 } lbl
                        && !pl.AddressLabels.ContainsKey(addr))
                    {
                        pl.AddressLabels[addr] = lbl;
                    }
                }
            }
            pages.Add(pl);
        }
        return pages;
    }
}
