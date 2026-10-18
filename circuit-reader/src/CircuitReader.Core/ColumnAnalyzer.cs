// Columnar parallel-block algorithm — port of
// ``circuit_reader.ladder._build_body_via_columns``.
//
// Square D ladders use two- (or more-) column parallel-block layouts:
//
//     L1 ──[A]──┬──[C]──── coil
//               │
//        ──[B]─┼─[~D]──[E]── (branch row)
//               │
//     two verticals (┬┼) carve the input area into 2 columns
//
// Within each column, the row-segments run in PARALLEL (OR).
// Across columns, the column-expressions run in SERIES (AND).
//
// Correct body: ((A) OR (B & ~D)) AND ((C) OR (E))
//
// The legacy (row-based) algorithm would mis-model this as
//     (A & C) OR (B & ~D & E)
// which is a genuinely different boolean function.

namespace CircuitReader.Core;

public static class ColumnAnalyzer
{
    public sealed record Result(BoolExpr Expr, string BodyString);

    /// <summary>
    /// Try to build the body of <paramref name="rung"/> using the columnar
    /// parallel-block algorithm.  Returns null when the rung doesn't match
    /// the columnar pattern (in which case the caller should fall back to
    /// the legacy row-based builder).
    /// </summary>
    public static Result? TryBuild(Rung rung, IReadOnlyList<VerticalLine> vlines)
    {
        if (rung.Rows.Count < 2) return null;

        var inputRows = rung.Rows
            .Where(r => r.Elements.Any(e => e.IsInput))
            .ToList();
        if (inputRows.Count < 2) return null;

        var inputXs = inputRows
            .SelectMany(r => r.Elements.Where(e => e.IsInput).Select(e => e.X))
            .ToList();
        if (inputXs.Count == 0) return null;

        var yLo = inputRows.Min(r => r.Y) - 6.0;
        var yHi = inputRows.Max(r => r.Y) + 6.0;
        var xLo = inputXs.Min() - 30.0;
        var xHi = inputXs.Max() + 30.0;

        // Vertical jumpers that span at least two input rows AND fall
        // within the input area.
        var spanningX = new List<double>();
        foreach (var v in vlines)
        {
            if (v.X < xLo || v.X > xHi) continue;
            if (v.YTop > yLo + 4 || v.YBottom < yHi - 4)
            {
                // Doesn't cover full Y range — still accept if it bridges
                // at least two input rows.
                int covered = inputRows.Count(r => v.YTop - 4 <= r.Y && r.Y <= v.YBottom + 4);
                if (covered < 2) continue;
            }
            spanningX.Add(Math.Round(v.X, 1));
        }

        // De-duplicate close-together verticals (rendering can split a
        // single jumper into 2-3 segments at slightly different X).
        spanningX = spanningX.Distinct().OrderBy(x => x).ToList();
        var clustered = new List<double>();
        foreach (var vx in spanningX)
        {
            if (clustered.Count > 0 && Math.Abs(vx - clustered[^1]) < 4.0) continue;
            clustered.Add(vx);
        }
        if (clustered.Count < 2) return null;
        spanningX = clustered;

        // Column boundaries: [left_edge, vert1, vert2, ..., right_edge].
        var edges = new List<double> { xLo };
        edges.AddRange(spanningX);
        edges.Add(xHi);
        var columns = new List<(double Lo, double Hi)>();
        for (int i = 0; i < edges.Count - 1; i++)
        {
            if (edges[i + 1] - edges[i] > 8.0)
                columns.Add((edges[i], edges[i + 1]));
        }
        if (columns.Count < 2) return null;

        // For each column, collect input contacts that fall inside it,
        // grouped by row.  A column "qualifies" if it has contacts from
        // 2+ rows (proves it's a real parallel block, not a single-row
        // branch fork point).
        int qualifying = 0;
        var columnExprs = new List<BoolExpr>();
        var columnStrs = new List<string>();

        foreach (var (cxLo, cxHi) in columns)
        {
            var perRow = new List<(double Y, BoolExpr Expr, string Str)>();
            int rowsWithContact = 0;
            foreach (var r in inputRows)
            {
                var rowElems = r.Elements
                    .Where(e => e.IsInput && cxLo - 4 <= e.X && e.X <= cxHi + 4)
                    .OrderBy(e => e.X)
                    .ToList();
                if (rowElems.Count == 0) continue;
                rowsWithContact++;

                var literals = rowElems.Select(ElemLiteral).ToList();
                var term = BoolExpr.And(literals);
                var rowStr = string.Join(" & ", literals.Select(lit => lit.Render()));
                if (literals.Count > 1) rowStr = $"({rowStr})";
                perRow.Add((r.Y, term, rowStr));
            }

            if (rowsWithContact >= 2) qualifying++;
            if (perRow.Count > 0)
            {
                perRow.Sort((a, b) => a.Y.CompareTo(b.Y));   // top → bottom
                columnExprs.Add(BoolExpr.Or(perRow.Select(t => t.Expr).ToList()));
                var colStr = string.Join(" | ", perRow.Select(t => t.Str));
                if (perRow.Count > 1) colStr = $"({colStr})";
                columnStrs.Add(colStr);
            }
        }

        if (qualifying < 1 || columnExprs.Count < 2) return null;

        var bodyExpr = BoolExpr.And(columnExprs);
        var bodyStr = string.Join(" & ", columnStrs);
        return new Result(bodyExpr, bodyStr);
    }

    /// <summary>Convert an element to its boolean literal (handles negation).</summary>
    public static BoolExpr ElemLiteral(Element e)
    {
        var v = BoolExpr.Var(SafeAddress(e.Address));
        return e.Kind switch
        {
            ElementKind.NormallyClosed => BoolExpr.Not(v),
            _ => v,
        };
    }

    /// <summary>
    /// Convert an address like ``0008:01`` to a code-safe identifier
    /// ``A_0008_01`` (matches the Python parser's convention).
    /// Also handles synthesized symbols from compare/LET blocks.
    /// </summary>
    public static string SafeAddress(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return "Q";
        var chars = addr.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var s = new string(chars).Trim('_');
        if (s.Length == 0) return "Q";
        if (char.IsDigit(s[0])) s = "A_" + s;
        return s;
    }
}
