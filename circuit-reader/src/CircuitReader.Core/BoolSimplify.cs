// Conservative, structure-preserving Boolean simplifier for the "Minimized"
// column.
//
// It applies ONLY sound, well-known identities and never expands to DNF (which
// would destroy the readable factored AND-of-OR column form the column-block
// algorithm produces — see BoolExpr.cs):
//
//   * constant folding       ~True→False, X&True→X, X|False→X, X&False→False, X|True→True
//   * double negation        ~~X → X
//   * idempotence            X & X → X,   X | X → X
//   * complementary literals X & ~X → False,  X | ~X → True   (the whole group folds)
//   * absorption             X & (X | Y) → X,  X | (X & Y) → X
//
// Every rule preserves logical equivalence, so the Minimized text always means
// exactly the same function as the raw BooleanEquation — it is only smaller.
// In particular the common self-seal form `(SET | OUT) & ~RESET` is left intact
// because none of its factors is repeated as a standalone sibling.

namespace CircuitReader.Core;

public static class BoolSimplify
{
    public static BoolExpr Run(BoolExpr e) => Simplify(e);

    private static BoolExpr Simplify(BoolExpr e) => e switch
    {
        BNot n => SimplifyNot(Simplify(n.Inner)),
        BAnd a => SimplifyAnd(a.Parts.Select(Simplify).ToList()),
        BOr o => SimplifyOr(o.Parts.Select(Simplify).ToList()),
        BXor x => new BXor(x.Parts.Select(Simplify).ToList()),
        _ => e,   // BVar, BTrue, BFalse
    };

    private static BoolExpr SimplifyNot(BoolExpr x) => x switch
    {
        BTrue => new BFalse(),
        BFalse => new BTrue(),
        BNot n => n.Inner,        // double negation
        _ => new BNot(x),
    };

    private static BoolExpr SimplifyAnd(List<BoolExpr> parts)
    {
        var flat = Flatten(parts, andLevel: true);
        if (flat.Any(p => p is BFalse)) return new BFalse();      // annihilator
        flat = flat.Where(p => p is not BTrue).ToList();          // identity
        flat = Distinct(flat);                                    // idempotence
        if (HasComplementPair(flat)) return new BFalse();         // X & ~X
        flat = Absorb(flat, containerIsOr: true);                 // X & (X | Y) → X
        return BoolExpr.And(flat);                                // 0→True, 1→part
    }

    private static BoolExpr SimplifyOr(List<BoolExpr> parts)
    {
        var flat = Flatten(parts, andLevel: false);
        if (flat.Any(p => p is BTrue)) return new BTrue();        // annihilator
        flat = flat.Where(p => p is not BFalse).ToList();         // identity
        flat = Distinct(flat);                                    // idempotence
        if (HasComplementPair(flat)) return new BTrue();          // X | ~X
        flat = Absorb(flat, containerIsOr: false);                // X | (X & Y) → X
        return BoolExpr.Or(flat);                                 // 0→False, 1→part
    }

    private static List<BoolExpr> Flatten(List<BoolExpr> parts, bool andLevel)
    {
        var outp = new List<BoolExpr>();
        foreach (var p in parts)
        {
            if (andLevel && p is BAnd a) outp.AddRange(a.Parts);
            else if (!andLevel && p is BOr o) outp.AddRange(o.Parts);
            else outp.Add(p);
        }
        return outp;
    }

    /// <summary>Drop later sub-expressions that render identically (keep first).</summary>
    private static List<BoolExpr> Distinct(List<BoolExpr> parts)
    {
        var seen = new HashSet<string>();
        var outp = new List<BoolExpr>();
        foreach (var p in parts)
            if (seen.Add(p.Render())) outp.Add(p);
        return outp;
    }

    /// <summary>True when the group holds both some literal and its negation.</summary>
    private static bool HasComplementPair(List<BoolExpr> parts)
    {
        var positive = new HashSet<string>();
        var negated = new HashSet<string>();
        foreach (var p in parts)
        {
            if (p is BNot n) negated.Add(n.Inner.Render());
            else positive.Add(p.Render());
        }
        return positive.Overlaps(negated);
    }

    /// <summary>
    /// Absorption.  In an AND, drop any OR sub-group that already contains a
    /// sibling factor (<c>A &amp; (A | B) = A</c>).  In an OR, drop any AND
    /// sub-group that already contains a sibling term (<c>A | (A &amp; B) = A</c>).
    /// </summary>
    private static List<BoolExpr> Absorb(List<BoolExpr> parts, bool containerIsOr)
    {
        var renders = parts.Select(p => p.Render()).ToList();
        var keep = new List<BoolExpr>();
        for (int i = 0; i < parts.Count; i++)
        {
            IReadOnlyList<BoolExpr>? inner = containerIsOr
                ? (parts[i] as BOr)?.Parts
                : (parts[i] as BAnd)?.Parts;
            if (inner is not null)
            {
                var innerRenders = inner.Select(c => c.Render()).ToHashSet();
                bool absorbed = false;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (j == i) continue;
                    if (innerRenders.Contains(renders[j])) { absorbed = true; break; }
                }
                if (absorbed) continue;   // a sibling makes this whole group redundant
            }
            keep.Add(parts[i]);
        }
        return keep;
    }
}
