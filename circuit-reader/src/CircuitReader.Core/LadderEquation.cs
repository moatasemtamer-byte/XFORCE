// Shared "LHS = body" equation emitter for the text-mnemonic dialects.
//
// Given a rung whose Output element is already set and a boolean body
// expression, it fills in BooleanEquation (infix) and FunctionEquation
// (AND/OR/NOT form) applying the standard coil semantics:
//
//   COIL / TIMER : OUT = body
//   LATCH (OTL/S): OUT = body | OUT_prev          (set-dominant, holds)
//   UNLATCH(OTU/R): OUT = OUT_prev & ~body         (reset-dominant, holds)
//
// A negated coil (IL "STN") is handled by the caller passing Not(body) with
// a COIL output kind.

namespace CircuitReader.Core;

internal static class LadderEquation
{
    public static void Apply(Rung rung, BoolExpr body)
    {
        var o = rung.Output;
        if (o is null)
        {
            rung.BooleanEquation = string.Empty;
            rung.FunctionEquation = string.Empty;
            return;
        }

        var addr = o.Address;
        BoolExpr expr = o.Kind switch
        {
            ElementKind.Latch => BoolExpr.Or(new[] { body, BoolExpr.Var(addr + "_prev") }),
            ElementKind.Unlatch => BoolExpr.And(new[] { BoolExpr.Var(addr + "_prev"), BoolExpr.Not(body) }),
            _ => body, // Coil, Timer, others
        };

        rung.BooleanEquation = $"{addr} = {expr.Render()}";
        rung.FunctionEquation = $"{addr} = {expr.RenderFunction()}";
        // The raw form keeps the readable factored AND-of-OR topology; the
        // Minimized column applies sound Boolean identities (constant folding,
        // complementary literals, absorption) so tautological box-enable groups
        // like `(~X | X | A) & ~A` collapse to `~A`.
        rung.Minimized = $"{addr} = {BoolSimplify.Run(expr).Render()}";
    }
}
