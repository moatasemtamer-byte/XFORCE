// Parser for the canonical infix boolean strings the EquationBuilder
// emits.  The grammar is small and unambiguous:
//
//   expr   := or-list
//   or-list := and-list ("|" and-list)*
//   and-list := factor ("&" factor)*
//   factor := "~" factor | "!" factor | "(" expr ")" | identifier | "True" | "False"
//   identifier := [A-Za-z_][A-Za-z_0-9]*
//
// We use this to round-trip equations through the BoolExpr AST so we can
// re-emit them in the function-style form (AND/OR/!).

using System.Text;

namespace CircuitReader.Core;

public static class BoolExprParser
{
    /// <summary>
    /// Parse <paramref name="expr"/> back into a BoolExpr AST.  Returns
    /// null if parsing fails (caller should fall back to the raw string).
    /// </summary>
    public static BoolExpr? TryParse(string expr)
    {
        try
        {
            var p = new Parser(expr);
            var e = p.ParseExpr();
            p.SkipWs();
            if (!p.AtEnd) return null;
            return e;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Convert a full equation string of the form "LHS = RHS" to its
    /// function-style representation.  If parsing fails, returns the raw
    /// equation unchanged so the caller never silently drops content.
    /// </summary>
    public static string ToFunctionStyle(string equation)
    {
        if (string.IsNullOrEmpty(equation)) return string.Empty;
        var idx = equation.IndexOf('=');
        if (idx < 0)
        {
            // No assignment — just an expression.  Try to convert it; on
            // failure, leave as-is.
            var e = TryParse(equation);
            return e is null ? equation : e.RenderFunction();
        }
        var lhs = equation.Substring(0, idx).Trim();
        var rhs = equation.Substring(idx + 1).Trim();

        // Sequential coil RHS may be split across newlines (multi-coil
        // rungs).  Convert each branch independently.
        if (rhs.Contains('\n'))
        {
            var sb = new StringBuilder();
            sb.Append(lhs).Append(" = ");
            bool first = true;
            foreach (var branch in rhs.Split('\n'))
            {
                if (!first) sb.Append('\n');
                first = false;
                var parsed = TryParse(branch);
                sb.Append(parsed is null ? branch : parsed.RenderFunction());
            }
            return sb.ToString();
        }

        var rhsExpr = TryParse(rhs);
        if (rhsExpr is null) return equation;
        return $"{lhs} = {rhsExpr.RenderFunction()}";
    }

    // -------- recursive-descent parser ----------------------------------

    private sealed class Parser
    {
        private readonly string _src;
        private int _pos;

        public Parser(string src) { _src = src; }
        public bool AtEnd => _pos >= _src.Length;

        public BoolExpr ParseExpr() => ParseOr();

        // OR has the lowest precedence.
        private BoolExpr ParseOr()
        {
            var first = ParseAnd();
            var parts = new List<BoolExpr> { first };
            while (true)
            {
                SkipWs();
                if (Peek() != '|') break;
                _pos++;
                if (Peek() == '|') _pos++;   // tolerate "||"
                parts.Add(ParseAnd());
            }
            return parts.Count == 1 ? parts[0] : BoolExpr.Or(parts);
        }

        private BoolExpr ParseAnd()
        {
            var first = ParseFactor();
            var parts = new List<BoolExpr> { first };
            while (true)
            {
                SkipWs();
                if (Peek() != '&') break;
                _pos++;
                if (Peek() == '&') _pos++;   // tolerate "&&"
                parts.Add(ParseFactor());
            }
            return parts.Count == 1 ? parts[0] : BoolExpr.And(parts);
        }

        private BoolExpr ParseFactor()
        {
            SkipWs();
            char c = Peek();
            if (c == '~' || c == '!')
            {
                _pos++;
                return BoolExpr.Not(ParseFactor());
            }
            if (c == '(')
            {
                _pos++;
                var inner = ParseExpr();
                SkipWs();
                if (Peek() != ')') throw new FormatException("expected ')'");
                _pos++;
                return inner;
            }
            // Identifier / True / False
            int start = _pos;
            if (!IsIdentStart(c)) throw new FormatException(
                $"unexpected '{c}' at position {_pos}");
            _pos++;
            while (_pos < _src.Length && IsIdentPart(_src[_pos])) _pos++;
            var name = _src.Substring(start, _pos - start);
            if (name == "True" || name == "TRUE") return new BTrue();
            if (name == "False" || name == "FALSE") return new BFalse();
            return BoolExpr.Var(name);
        }

        public void SkipWs()
        {
            while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos])) _pos++;
        }

        private char Peek()
        {
            SkipWs();
            return _pos < _src.Length ? _src[_pos] : '\0';
        }

        private static bool IsIdentStart(char c) =>
            char.IsLetter(c) || c == '_';
        private static bool IsIdentPart(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == ':' || c == '.';
    }
}
