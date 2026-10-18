// Group recognized elements into rungs (one rung per horizontal stripe
// bounded by consecutive "Rung N" headers).

namespace CircuitReader.Core;

public static class RungBuilder
{
    public static List<Rung> BuildRungs(
        IReadOnlyList<Element> elements,
        IReadOnlyList<VerticalLine> vlines,
        IReadOnlyList<(int Number, double Y)> rungHeaders)
    {
        if (elements.Count == 0) return new List<Rung>();
        if (rungHeaders.Count == 0) return new List<Rung>();

        var rungs = new List<Rung>();
        for (int i = 0; i < rungHeaders.Count; i++)
        {
            var (number, yTop) = rungHeaders[i];
            var yBot = (i + 1 < rungHeaders.Count) ? rungHeaders[i + 1].Y : double.MaxValue;

            var inRung = elements.Where(e => e.Y > yTop && e.Y < yBot).ToList();
            if (inRung.Count == 0) continue;

            var rung = new Rung { Number = number };

            // Group elements by Y-band (rows).
            var sorted = inRung.OrderBy(e => e.Y).ThenBy(e => e.X).ToList();
            foreach (var e in sorted)
            {
                Row? row = null;
                foreach (var r in rung.Rows)
                {
                    if (Math.Abs(r.Y - e.Y) < 6) { row = r; break; }
                }
                if (row is null) { row = new Row(e.Y); rung.Rows.Add(row); }
                row.Elements.Add(e);
                row.Y = (row.Y + e.Y) / 2.0;
            }
            foreach (var r in rung.Rows) r.Elements.Sort((a, b) => a.X.CompareTo(b.X));
            rung.Rows.Sort((a, b) => a.Y.CompareTo(b.Y));

            // Output coil = rightmost output element.
            rung.Output = inRung.Where(e => e.IsOutput)
                                .OrderByDescending(e => e.X)
                                .FirstOrDefault();

            if (rung.Output is null) continue;
            EquationBuilder.Build(rung, vlines);
            rungs.Add(rung);
        }
        return rungs;
    }
}
