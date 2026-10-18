// Boolean-expression AST + pretty-printer.
//
// We deliberately do NOT use a generic CAS (sympy equivalent) — the
// column-block algorithm produces a factored AND-of-OR form that IS the
// canonical, most-readable representation of the rung topology.  Expanding
// it to DNF (the Python tool used to do this with sympy.simplify_logic)
// destroys the column structure and yields a row-based OR-of-products
// that engineers read as "wrong".  Keeping the factored form is the fix.

using System.Text;

namespace CircuitReader.Core;

public abstract record BoolExpr
{
    /// <summary>Render to the canonical infix form ("A & ~B | C").</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        WriteTo(sb, parentPrecedence: 0);
        return sb.ToString();
    }

    /// <summary>
    /// Render to a function-style form: <c>AND(A, NOT(B), C)</c> /
    /// <c>OR(A, AND(B, C))</c> / <c>!A</c>.  Designed for users who want
    /// to paste the equation into Excel, LISP-style tooling, or a script.
    /// </summary>
    public string RenderFunction()
    {
        var sb = new StringBuilder();
        WriteFunction(sb);
        return sb.ToString();
    }

    public sealed override string ToString() => Render();

    internal abstract int Precedence { get; }
    internal abstract void WriteTo(StringBuilder sb, int parentPrecedence);
    internal abstract void WriteFunction(StringBuilder sb);

    public static BoolExpr Var(string name) => new BVar(name);
    public static BoolExpr Not(BoolExpr e) => new BNot(e);

    public static BoolExpr Xor(BoolExpr a, BoolExpr b) => new BXor(new[] { a, b });

    public static BoolExpr And(IReadOnlyList<BoolExpr> parts)
    {
        if (parts.Count == 0) return new BTrue();
        if (parts.Count == 1) return parts[0];
        // Flatten: And(And(a,b), c) → And(a,b,c) so deep build trees print
        // as clean associative chains instead of (((a & b) & c) & d).
        var flat = new List<BoolExpr>(parts.Count);
        foreach (var p in parts)
        {
            if (p is BAnd inner) flat.AddRange(inner.Parts);
            else flat.Add(p);
        }
        if (flat.Count == 1) return flat[0];
        return new BAnd(flat);
    }

    public static BoolExpr Or(IReadOnlyList<BoolExpr> parts)
    {
        if (parts.Count == 0) return new BFalse();
        if (parts.Count == 1) return parts[0];
        // Flatten nested ORs the same way.
        var flat = new List<BoolExpr>(parts.Count);
        foreach (var p in parts)
        {
            if (p is BOr inner) flat.AddRange(inner.Parts);
            else flat.Add(p);
        }
        if (flat.Count == 1) return flat[0];
        return new BOr(flat);
    }
}

public sealed record BTrue : BoolExpr
{
    internal override int Precedence => 6;
    internal override void WriteTo(StringBuilder sb, int p) => sb.Append("True");
    internal override void WriteFunction(StringBuilder sb) => sb.Append("TRUE");
}

public sealed record BFalse : BoolExpr
{
    internal override int Precedence => 6;
    internal override void WriteTo(StringBuilder sb, int p) => sb.Append("False");
    internal override void WriteFunction(StringBuilder sb) => sb.Append("FALSE");
}

public sealed record BVar(string Name) : BoolExpr
{
    internal override int Precedence => 6;
    internal override void WriteTo(StringBuilder sb, int p) => sb.Append(Name);
    internal override void WriteFunction(StringBuilder sb) => sb.Append(Name);
}

public sealed record BNot(BoolExpr Inner) : BoolExpr
{
    internal override int Precedence => 5;
    internal override void WriteTo(StringBuilder sb, int p)
    {
        sb.Append('~');
        // Delegate parenthesization to the inner expression — pass a
        // parentPrecedence > Precedence so it'll add parens around an
        // AND/OR but not around a bare Var/Not.
        Inner.WriteTo(sb, parentPrecedence: Precedence);
    }
    internal override void WriteFunction(StringBuilder sb)
    {
        // Use the C-style "!" prefix — concise and matches what the user
        // asked for ("!A_0006_14").  For compound operands, wrap in parens.
        if (Inner is BVar)
        {
            sb.Append('!');
            Inner.WriteFunction(sb);
        }
        else
        {
            sb.Append("!(");
            Inner.WriteFunction(sb);
            sb.Append(')');
        }
    }
}

public sealed record BAnd(IReadOnlyList<BoolExpr> Parts) : BoolExpr
{
    internal override int Precedence => 4;
    internal override void WriteTo(StringBuilder sb, int p)
    {
        var needsParens = p > Precedence;
        if (needsParens) sb.Append('(');
        for (int i = 0; i < Parts.Count; i++)
        {
            if (i > 0) sb.Append(" & ");
            Parts[i].WriteTo(sb, Precedence);
        }
        if (needsParens) sb.Append(')');
    }
    internal override void WriteFunction(StringBuilder sb)
    {
        sb.Append("AND(");
        for (int i = 0; i < Parts.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            Parts[i].WriteFunction(sb);
        }
        sb.Append(')');
    }
}

public sealed record BOr(IReadOnlyList<BoolExpr> Parts) : BoolExpr
{
    internal override int Precedence => 2;
    internal override void WriteTo(StringBuilder sb, int p)
    {
        var needsParens = p > Precedence;
        if (needsParens) sb.Append('(');
        for (int i = 0; i < Parts.Count; i++)
        {
            if (i > 0) sb.Append(" | ");
            // Although "&" binds tighter than "|", control engineers expect a
            // nested AND term shown explicitly grouped — "~X3 | (C102 & ~C7)"
            // rather than the bare "~X3 | C102 & ~C7" — so an AND operand is
            // parenthesized even though precedence does not strictly require it.
            // (The Square D column renderer already does this manually; this
            // keeps the BoolExpr renderer consistent with it.)
            if (Parts[i] is BAnd)
            {
                sb.Append('(');
                Parts[i].WriteTo(sb, 0);
                sb.Append(')');
            }
            else
            {
                Parts[i].WriteTo(sb, Precedence);
            }
        }
        if (needsParens) sb.Append(')');
    }
    internal override void WriteFunction(StringBuilder sb)
    {
        sb.Append("OR(");
        for (int i = 0; i < Parts.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            Parts[i].WriteFunction(sb);
        }
        sb.Append(')');
    }
}

public sealed record BXor(IReadOnlyList<BoolExpr> Parts) : BoolExpr
{
    internal override int Precedence => 3;
    internal override void WriteTo(StringBuilder sb, int p)
    {
        var needsParens = p > Precedence;
        if (needsParens) sb.Append('(');
        for (int i = 0; i < Parts.Count; i++)
        {
            if (i > 0) sb.Append(" ^ ");
            Parts[i].WriteTo(sb, Precedence);
        }
        if (needsParens) sb.Append(')');
    }
    internal override void WriteFunction(StringBuilder sb)
    {
        sb.Append("XOR(");
        for (int i = 0; i < Parts.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            Parts[i].WriteFunction(sb);
        }
        sb.Append(')');
    }
}
