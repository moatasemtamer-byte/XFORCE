// Emits a Triconex TRISTATION *functional-notation* listing — the two-column
// (network #, logic) form produced by TRISTATION's own "Logic" export, e.g.
//
//     25   mP0011=OR(AND(mP0011,>/wP0011T,716/,</wP0011T,4150/), ...)
//     89   cY6060A=AND(SYSRdy05,!SYSOvFl05,==/SYSkeysw,3/)
//     125  f6INSUR_LCH=</r6MAR,-1/
//     125  f6INSUR_RST=>/r6MAR,1/
//     125  f6INSUR=SR(f6INSUR_LCH,f6INSUR_RST)
//
// It renders the tool's already-accurate equations in that syntax so a user can
// diff it directly against a TRISTATION spreadsheet.  Only the Triconex dialect
// produces the function-style equations this reads, so it is a no-op elsewhere.
//
// Notation map (tool FunctionEquation -> TRISTATION):
//   AND(a,b)/OR(a,b)         AND(a,b)/OR(a,b)   (spaces stripped)
//   NOT(atom)                !atom              NOT(expr) -> !(expr)
//   GT/LT/GE/LE/EQ/NE(a,b)   >//  <//  >=//  <=//  ==//  <>//   (op/a,b/)
//   RE(x)/FE(x)              ^/x/  /  v/x/
//   TDD/TDE/PLS/BNK(e,p)     TDD_I/TDE_I/TP_I/BNK_I(e,p)
//   $name                    SYSname
//   a-b (in an identifier)   a_b
//   Latch/Unlatch coils      X_LCH=set / X_RST=reset / X=SR(X_LCH,X_RST)

using System.Text;
using System.Text.RegularExpressions;

namespace CircuitReader.Core;

public static class TristationExporter
{
    /// <summary>Render one document to the TRISTATION functional listing and
    /// write it (tab-separated: <c>Networks #\tLogic</c>) to <paramref name="path"/>.</summary>
    public static void Write(IReadOnlyList<PageLadder> pages, string path)
        => File.WriteAllText(path, Render(pages));

    public static string Render(IReadOnlyList<PageLadder> pages)
    {
        var sb = new StringBuilder();
        sb.Append("Networks #\tLogic\n");
        foreach (var (net, logic) in Rows(pages))
            sb.Append(net).Append('\t').Append(logic).Append('\n');
        return sb.ToString();
    }

    /// <summary>The (network#, logic) rows, in document order.</summary>
    public static IEnumerable<(int Net, string Logic)> Rows(IReadOnlyList<PageLadder> pages)
    {
        var rungs = pages.SelectMany(p => p.Rungs).ToList();

        // Pre-scan latch / unlatch coils: a Triconex bit can be set in one network
        // and reset in another, and TRISTATION scatters the X_LCH / X_RST / SR rows
        // into the networks where each coil physically sits, so gather every set and
        // reset condition (and which coil is scanned last) before emitting.
        var latches = ScanLatches(rungs);
        var lchDone = new HashSet<string>();
        var rstDone = new HashSet<string>();
        var srDone = new HashSet<string>();

        var rows = new List<(int Net, string Logic)>();
        foreach (var r in rungs)
        {
            var outAddr = r.Output?.Address;
            var kind = r.Output?.Kind;

            if ((kind == ElementKind.Latch || kind == ElementKind.Unlatch) &&
                outAddr is not null && latches.TryGetValue(outAddr, out var info))
            {
                var name = NormalizeName(outAddr);
                if (kind == ElementKind.Latch && lchDone.Add(outAddr) && info.HasSet)
                    rows.Add((r.Number, $"{name}_LCH={OrJoin(info.Sets)}"));
                if (kind == ElementKind.Unlatch && rstDone.Add(outAddr) && info.HasReset)
                    rows.Add((r.Number, $"{name}_RST={OrJoin(info.Rsts)}"));

                // Emit the SR/RS row right after the later of the two sides appears.
                if (info.HasSet && info.HasReset &&
                    lchDone.Contains(outAddr) && rstDone.Contains(outAddr) && srDone.Add(outAddr))
                {
                    var op = info.ResetDominant ? "RS" : "SR";
                    rows.Add((r.Number, $"{name}={op}({name}_LCH,{name}_RST)"));
                }
                continue;
            }

            // Structured-text block: one row per statement (setpoint tables, analog
            // scaling, PID configuration).  Nested IF/THEN/ENDIF fragments are folded
            // so each assignment carries the AND of the ladder gate plus every
            // enclosing IF condition (see EmitStatements).
            if (r.Statements.Count > 0)
            {
                EmitStatements(r.Number, GateOf(r.FunctionEquation), r.Statements, rows);
                continue;
            }

            var eq = r.FunctionEquation;
            if (string.IsNullOrWhiteSpace(eq)) continue;
            rows.Add((r.Number, ConvertAssignment(eq)));
        }
        return rows;
    }

    // --------------------------------------------------------------- latches

    private sealed class LatchInfo
    {
        public List<string> Sets { get; } = new();   // set conditions, rendered
        public List<string> Rsts { get; } = new();   // reset conditions, rendered
        public bool ResetDominant { get; set; }       // last coil scanned is an unlatch
        public bool HasSet => Sets.Count > 0;
        public bool HasReset => Rsts.Count > 0;
    }

    // For every Latch/Unlatch coil address collect its set and reset conditions
    // (the tool renders "X = OR(X_prev, set)", "X = AND(X_prev, NOT(reset))",
    // "X = TRUE" for an unconditional latch, "X = FALSE" for an unconditional
    // reset).  The dominance (SR set-dominant vs RS reset-dominant) follows the
    // physical scan order: whichever coil is written last wins, exactly as the
    // PLC evaluates the rungs.
    private static Dictionary<string, LatchInfo> ScanLatches(IReadOnlyList<Rung> rungs)
    {
        var map = new Dictionary<string, LatchInfo>();
        foreach (var r in rungs)
        {
            var kind = r.Output?.Kind;
            if (kind != ElementKind.Latch && kind != ElementKind.Unlatch) continue;
            var addr = r.Output!.Address;
            if (!map.TryGetValue(addr, out var info)) { info = new LatchInfo(); map[addr] = info; }

            var rhs = RhsOf(r.FunctionEquation);
            if (rhs is null) continue;
            var node = PrefixParser.Parse(rhs);

            if (kind == ElementKind.Latch)
            {
                var cond = SealSet(node);                 // second arg of OR(X_prev, set)
                info.Sets.Add(cond is null ? "TRUE" : RenderTri(cond));
                info.ResetDominant = false;               // latch scanned last -> set wins
            }
            else
            {
                var cond = SealReset(node);               // arg of NOT in AND(X_prev, NOT(reset))
                info.Rsts.Add(cond is null ? "TRUE" : RenderTri(cond));
                info.ResetDominant = true;                // unlatch scanned last -> reset wins
            }
        }
        return map;
    }

    private static string OrJoin(List<string> conds)
    {
        var distinct = conds.Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : "OR(" + string.Join(",", distinct) + ")";
    }

    // OR(X_prev, set) -> set ; a bare "TRUE" -> null (unconditional).
    private static FNode? SealSet(FNode node)
    {
        if (node is Atom a) return a.Text == "TRUE" ? null : a;
        if (node is Call c && c.Name == "OR" && c.Args.Count == 2 &&
            c.Args[0] is Atom prev && prev.Text.EndsWith("_prev"))
            return c.Args[1];
        return node;
    }

    // AND(X_prev, NOT(reset)) -> reset ; a bare "FALSE" -> null (unconditional).
    private static FNode? SealReset(FNode node)
    {
        if (node is Atom a) return a.Text == "FALSE" ? null : a;
        if (node is Call c && c.Name == "AND" && c.Args.Count == 2 &&
            c.Args[0] is Atom prev && prev.Text.EndsWith("_prev") &&
            c.Args[1] is Call not && not.Name == "NOT" && not.Args.Count == 1)
            return not.Args[0];
        return node;
    }

    // ------------------------------------------------------------- rendering

    private static string ConvertAssignment(string funcEq)
    {
        int eq = funcEq.IndexOf(" = ", StringComparison.Ordinal);
        if (eq < 0) return funcEq.Trim();
        var lhs = NormalizeName(funcEq.Substring(0, eq).Trim());
        var rhs = funcEq.Substring(eq + 3).Trim();
        return $"{lhs}={RenderTri(PrefixParser.Parse(rhs))}";
    }

    private static string RenderTri(FNode node)
    {
        switch (node)
        {
            case Atom a: return NormalizeName(a.Text);
            case Call c:
                var n = c.Name;
                switch (n)
                {
                    case "AND" or "OR":
                        return $"{n}({string.Join(",", c.Args.Select(RenderTri))})";
                    case "NOT":
                        var inner = c.Args[0];
                        var r = RenderTri(inner);
                        return inner is Atom ? "!" + r : "!(" + r + ")";
                    case "GT": return Cmp(">", c);
                    case "LT": return Cmp("<", c);
                    case "GE": return Cmp(">=", c);
                    case "LE": return Cmp("<=", c);
                    case "EQ": return Cmp("==", c);
                    case "NE": return Cmp("<>", c);
                    case "RE": return $"^/{RenderTri(c.Args[0])}/";
                    case "FE": return $"v/{RenderTri(c.Args[0])}/";
                    case "TDD": return Timer("TDD_I", c);
                    case "TDE": return Timer("TDE_I", c);
                    case "PLS": return Timer("TP_I", c);
                    case "BNK": return Timer("BNK_I", c);
                    default:
                        return $"{n}({string.Join(",", c.Args.Select(RenderTri))})";
                }
            default: return string.Empty;
        }
    }

    private static string Cmp(string op, Call c)
        => $"{op}/{RenderTri(c.Args[0])},{RenderTri(c.Args[1])}/";

    private static string Timer(string name, Call c)
        => $"{name}({RenderTri(c.Args[0])},{RenderTri(c.Args[1])})";

    // ----------------------------------------------------------- statements

    private static readonly Regex CommentRe = new(@"\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex AinRe =
        new(@"\bAIN\(\s*([^,()]+?)\s*,\s*([^,()]+?)\s*,", RegexOptions.Compiled);

    // A structured-text statement is arithmetic/config text, not a boolean tree.
    // Strip {comments}, drop the trailing ';', normalize $->SYS and swap AIN's
    // (signal, scale) into the reference's (scale, signal) order.
    private static string CleanStatement(string stmt)
    {
        var s = CommentRe.Replace(stmt, "").Trim();
        s = s.TrimEnd(';', ' ');
        s = AinRe.Replace(s, m => $"AIN({m.Groups[2].Value.Trim()},{m.Groups[1].Value.Trim()},");
        s = s.Replace("$", "SYS");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        // TRISTATION writes the assignment operator tight (x=y).  Safe: the compound
        // compares >= <= == <> never contain a space-equals-space run.
        return s.Replace(" = ", "=");
    }

    // "IF <gate> THEN { ... }" -> the ladder gate rendered in TRISTATION notation.
    // The trailing "{" is required so this only matches the parser's real gate
    // wrapper, never an ungated box whose first *interior* statement happens to be
    // "IF cond THEN;" (those interior IFs are folded by EmitStatements instead).
    private static string? GateOf(string funcEq)
    {
        var m = Regex.Match(funcEq ?? "", @"^IF\s+(.*?)\s+THEN\s*\{");
        if (!m.Success) return null;
        try { return RenderTri(PrefixParser.Parse(m.Groups[1].Value.Trim())); }
        catch { return m.Groups[1].Value.Trim(); }
    }

    // Render a structured-text box's statements, folding nested IF/THEN/ENDIF
    // fragments into per-assignment guards.  The parser stores the box interior as
    // one fragment per physical source line, but structured-text statements do not
    // align to source lines (a single IF/THEN/ELSE/ENDIF may span several lines, or
    // a whole IF..ENDIF may sit on one line).  For a box that contains control flow
    // we rejoin the fragments into one statement stream and fold IF/THEN/ELSE/ENDIF
    // so every assignment carries the AND of the ladder gate plus its enclosing
    // conditions.  Mutually-exclusive "IF a>=5" / "IF a<5" branches therefore get
    // their correct opposite guards instead of the first IF stamped onto every line,
    // and the bare IF/ENDIF fragments no longer surface as meaningless "{ ENDIF }"
    // rows.  Boxes with no control flow keep the simple one-row-per-fragment path.
    private static readonly Regex StControlRe =
        new(@"\b(IF|THEN|ELSE|ENDIF)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StSplitRe =
        new(@"(\bIF\b|\bTHEN\b|\bELSE\b|\bENDIF\b|;)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void EmitStatements(int net, string? ladderGate,
                                       IReadOnlyList<string> statements,
                                       List<(int Net, string Logic)> rows)
    {
        if (!statements.Any(s => StControlRe.IsMatch(s)))
        {
            foreach (var stmt in statements)
            {
                var clean = CleanStatement(stmt);
                if (clean.Length == 0) continue;
                rows.Add((net, ladderGate is null ? clean : $"IF {ladderGate} THEN {{ {clean} }}"));
            }
            return;
        }

        var joined = CommentRe.Replace(string.Join(" ", statements), " ");
        var conds = new List<string>();
        if (ladderGate is not null) conds.Add(ladderGate);   // never popped by ENDIF
        int protectedDepth = conds.Count;

        var buf = new StringBuilder();
        var condBuf = new StringBuilder();
        bool readingCond = false;

        void Flush()
        {
            var s = CleanStatement(buf.ToString());
            buf.Clear();
            if (s.Length == 0) return;
            var guard = conds.Count == 0 ? null
                      : conds.Count == 1 ? conds[0]
                      : "AND(" + string.Join(",", conds) + ")";
            rows.Add((net, guard is null ? s : $"IF {guard} THEN {{ {s} }}"));
        }

        foreach (var piece in StSplitRe.Split(joined))
        {
            if (string.IsNullOrWhiteSpace(piece)) continue;
            switch (piece.Trim().ToUpperInvariant())
            {
                case "IF":
                    Flush(); readingCond = true; condBuf.Clear(); break;
                case "THEN":
                    conds.Add(RenderCondition(condBuf.ToString().Trim()));
                    readingCond = false; buf.Clear(); break;
                case "ELSE":
                    Flush();
                    if (conds.Count > protectedDepth)
                        conds[conds.Count - 1] = "!(" + conds[conds.Count - 1] + ")";
                    break;
                case "ENDIF":
                    Flush();
                    if (conds.Count > protectedDepth) conds.RemoveAt(conds.Count - 1);
                    break;
                case ";":
                    if (!readingCond) Flush();
                    break;
                default:
                    if (readingCond) condBuf.Append(' ').Append(piece);
                    else buf.Append(piece);
                    break;
            }
        }
        Flush();
    }

    // A single structured-text IF condition (usually one comparison such as
    // "r6MAR >= 5" or "rP029CO < rV018C1") -> TRISTATION "op/lhs,rhs/".  A compound
    // boolean condition is handed to the prefix parser as a best effort.
    private static readonly string[] CmpOps = { ">=", "<=", "<>", "==", ">", "<" };
    private static string RenderCondition(string cond)
    {
        cond = cond.Trim();
        if (!cond.Contains('&') && !cond.Contains('|'))
            foreach (var op in CmpOps)
            {
                int idx = cond.IndexOf(op, StringComparison.Ordinal);
                if (idx > 0)
                {
                    var lhs = cond.Substring(0, idx).Trim();
                    var rhs = cond.Substring(idx + op.Length).Trim();
                    if (lhs.Length > 0 && rhs.Length > 0)
                        return $"{op}/{NormalizeName(lhs)},{NormalizeName(rhs)}/";
                }
            }
        try { return RenderTri(PrefixParser.Parse(cond)); }
        catch { return cond; }
    }

    // ------------------------------------------------------------- helpers

    private static string? RhsOf(string funcEq)
    {
        int eq = funcEq.IndexOf(" = ", StringComparison.Ordinal);
        return eq < 0 ? null : funcEq.Substring(eq + 3).Trim();
    }

    // Identifiers: $ -> SYS, '-' -> '_'.  Numbers (incl. -1, 1.02, 4.4E+05) are
    // left untouched so subtraction / signs / exponents survive.
    private static string NormalizeName(string tok)
    {
        tok = tok.Trim();
        if (tok.Length == 0) return tok;
        char c0 = tok[0];
        bool isIdent = char.IsLetter(c0) || c0 == '$' || c0 == '_';
        if (!isIdent) return tok;              // numeric / signed literal
        return tok.Replace("$", "SYS").Replace('-', '_');
    }
}

// ---- tiny prefix-notation parser for the tool's FunctionEquation strings ----

internal abstract record FNode;
internal sealed record Atom(string Text) : FNode;
internal sealed record Call(string Name, List<FNode> Args) : FNode;

internal static class PrefixParser
{
    public static FNode Parse(string s)
    {
        int pos = 0;
        var node = ParseExpr(s, ref pos);
        return node;
    }

    private static FNode ParseExpr(string s, ref int pos)
    {
        SkipWs(s, ref pos);
        int start = pos;
        // read a token up to one of  , ( )
        while (pos < s.Length && s[pos] != ',' && s[pos] != '(' && s[pos] != ')')
            pos++;
        string tok = s.Substring(start, pos - start).Trim();

        SkipWs(s, ref pos);
        if (pos < s.Length && s[pos] == '(')
        {
            pos++;   // consume '('
            var args = new List<FNode>();
            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] != ')')
            {
                while (true)
                {
                    args.Add(ParseExpr(s, ref pos));
                    SkipWs(s, ref pos);
                    if (pos < s.Length && s[pos] == ',') { pos++; continue; }
                    break;
                }
            }
            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] == ')') pos++;   // consume ')'
            return new Call(tok, args);
        }
        return new Atom(tok);
    }

    private static void SkipWs(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
    }
}
