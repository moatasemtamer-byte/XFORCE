// Top-level boolean-equation builder for one rung.
//
// Strategy (mirrors Python ``circuit_reader.ladder._build_rung``):
//   1. Pick the "main row" — the row carrying the output coil.
//   2. Try the COLUMNAR PARALLEL-BLOCK algorithm first (preferred when
//      the rung has 2+ vertical jumpers carving the input area into
//      columns).  Produces ``AND_over_columns(OR_over_rows(...))``.
//   3. Fall back to a row-based builder that walks the main row and
//      OR-s in branch contacts whose X range covers each main contact.
//   4. Wrap the body for sequential coils:
//        LATCH   → next-state = prev | body
//        UNLATCH → next-state = prev & ~body
//        COIL    → just the body
//
// Returns the rung with BooleanEquation and Minimized fields populated.

namespace CircuitReader.Core;

public static class EquationBuilder
{
    public static void Build(Rung rung, IReadOnlyList<VerticalLine> vlines)
    {
        if (rung.Output is null) return;

        // ---- LET assignment: emit the math expression directly. ----
        // The "label" carries the human-readable form built by PdfReader.
        if (rung.Output.Kind == ElementKind.LetAssign)
        {
            // Strip the "LET " prefix from the label so the equation reads
            // "S0810 = S0025 x 1.000000E+03 / 09999 024".
            var lbl = rung.Output.Label ?? "";
            if (lbl.StartsWith("LET ")) lbl = lbl.Substring(4);
            rung.BooleanEquation = lbl;
            rung.Minimized = lbl;
            rung.FunctionEquation = lbl;   // not boolean; show as-is
            rung.Note = "math/scale assignment (LET block); not a boolean equation.";
            return;
        }

        // ---- pick the main row (the one carrying the output coil) ----
        Row? mainRow = null;
        foreach (var r in rung.Rows)
        {
            if (r.Elements.Contains(rung.Output)) { mainRow = r; break; }
        }
        mainRow ??= rung.Rows.OrderByDescending(r => r.Elements.Count).FirstOrDefault();
        if (mainRow is null) return;

        rung.Inputs.Clear();
        rung.Inputs.AddRange(mainRow.Elements.Where(e => e.IsInput));
        rung.Branches.Clear();
        rung.Branches.AddRange(rung.Rows.Where(r => r != mainRow));

        // ---- try columnar first ----
        var columnResult = ColumnAnalyzer.TryBuild(rung, vlines);
        string bodyStr;
        if (columnResult is not null)
        {
            bodyStr = columnResult.BodyString;
        }
        else
        {
            // Fall back to the legacy row-based form.
            bodyStr = BuildLegacyBody(rung);
        }

        // ---- wrap for sequential coils ----
        var coilSym = ColumnAnalyzer.SafeAddress(rung.Output.Address);
        string eq;
        switch (rung.Output.Kind)
        {
            case ElementKind.Latch:
                if (bodyStr == "True")
                {
                    // Coil sits straight on the power rail: prev | True ≡ True.
                    eq = $"{coilSym} = True";
                    rung.Note = "sequential coil (LATCH); unconditional set.";
                }
                else
                {
                    eq = $"{coilSym} = {coilSym}_prev | ({bodyStr})";
                    rung.Note = $"sequential coil (LATCH); equation is NEXT-STATE — use {coilSym}_prev for the previous value.";
                }
                break;
            case ElementKind.Unlatch:
                if (bodyStr == "True")
                {
                    // Coil sits straight on the power rail: prev & ~True ≡ False.
                    eq = $"{coilSym} = False";
                    rung.Note = "sequential coil (UNLATCH); unconditional reset.";
                }
                else
                {
                    eq = $"{coilSym} = {coilSym}_prev & ~({bodyStr})";
                    rung.Note = $"sequential coil (UNLATCH); equation is NEXT-STATE — use {coilSym}_prev for the previous value.";
                }
                break;
            default:
                eq = $"{coilSym} = {bodyStr}";
                break;
        }

        rung.BooleanEquation = eq;
        // Keep the factored form as the canonical "minimized" — DNF-expanding
        // it (the old behaviour) destroys the column structure.
        rung.Minimized = eq;
        rung.FunctionEquation = BoolExprParser.ToFunctionStyle(eq);
    }

    /// <summary>
    /// Legacy row-based body builder, used when the columnar algorithm
    /// doesn't apply.  Walks the main row left→right and OR-s any branch
    /// whose X-range covers the current contact.  Builds a proper
    /// <see cref="BoolExpr"/> AST so the renderer adds parens only where
    /// precedence actually requires them (no over-wrapping).
    /// </summary>
    private static string BuildLegacyBody(Rung rung)
    {
        if (rung.Inputs.Count == 0 && rung.Branches.Count == 0)
            return "True";

        // Build branch expressions with their X-spans.
        var branches = new List<(double Lo, double Hi, BoolExpr Expr)>();
        foreach (var br in rung.Branches)
        {
            if (br.Elements.Count == 0) continue;
            if (br.Elements.Any(e => e.IsOutput)) continue;
            var inputElems = br.Elements
                .Where(e => e.IsInput)
                .OrderBy(e => e.X)
                .ToList();
            if (inputElems.Count == 0) continue;
            var xs = inputElems.Select(e => e.X).ToList();
            var seriesParts = inputElems.Select(ColumnAnalyzer.ElemLiteral).ToList();
            branches.Add((xs.Min(), xs.Max(), BoolExpr.And(seriesParts)));
        }

        var inputs = rung.Inputs.OrderBy(e => e.X).ToList();
        var seriesTerms = new List<BoolExpr>();
        var usedBranches = new HashSet<int>();

        int j = 0;
        while (j < inputs.Count)
        {
            var c = inputs[j];
            int coverIdx = -1;
            for (int bi = 0; bi < branches.Count; bi++)
            {
                if (usedBranches.Contains(bi)) continue;
                var (lo, hi, _) = branches[bi];
                if (lo - 6 <= c.X && c.X <= hi + 6) { coverIdx = bi; break; }
            }
            if (coverIdx < 0)
            {
                seriesTerms.Add(ColumnAnalyzer.ElemLiteral(c));
                j++;
                continue;
            }
            var (xlo, xhi, bexpr) = branches[coverIdx];
            var run = new List<Element> { c };
            j++;
            while (j < inputs.Count && xlo - 6 <= inputs[j].X && inputs[j].X <= xhi + 6)
            {
                run.Add(inputs[j]); j++;
            }
            var mainSeg = BoolExpr.And(run.Select(ColumnAnalyzer.ElemLiteral).ToList());
            seriesTerms.Add(BoolExpr.Or(new BoolExpr[] { mainSeg, bexpr }));
            usedBranches.Add(coverIdx);
        }

        // Any un-used branches → wrap whole chain in an OR with each.
        for (int bi = 0; bi < branches.Count; bi++)
        {
            if (usedBranches.Contains(bi)) continue;
            if (seriesTerms.Count > 0)
            {
                var current = BoolExpr.And(seriesTerms);
                seriesTerms = new List<BoolExpr> { BoolExpr.Or(new[] { current, branches[bi].Expr }) };
            }
            else
            {
                seriesTerms.Add(branches[bi].Expr);
            }
        }

        var body = BoolExpr.And(seriesTerms);
        return body.Render();
    }
}
