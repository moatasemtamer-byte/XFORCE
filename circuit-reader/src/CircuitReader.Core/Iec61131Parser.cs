// IEC 61131-3 ladder / structured-text parser (C# port of iec61131.py).
//
// Parses the two text-based IEC languages into the shared PageLadder shape:
//
// Structured Text (ST):
//     Q0 := A AND NOT B;
//     Q1 := (Start OR Latch1) AND NOT Stop;
//   Each "LHS := RHS;" statement is one rung.  Operators: AND/& OR/| NOT/!
//   XOR, parentheses.
//
// Instruction List (IL):
//     LD  A
//     ANDN B
//     ST  Q0
//   LD load, ST store, S set/latch, R reset/unlatch, STN store-negated.

using System.Text.RegularExpressions;

namespace CircuitReader.Core;

public static class Iec61131Parser
{
    private static readonly Regex LineComment = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BlockComment = new(@"\(\*.*?\*\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BraceComment = new(@"\{[^}]*\}", RegexOptions.Compiled);

    private static readonly Regex StAssignRe =
        new(@"^\s*[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\s*:=", RegexOptions.Multiline | RegexOptions.Compiled);
    // A real Instruction List rung starts by LOADing the accumulator (LD/LDN)
    // and ends with a STore (ST/STN/S/R).  Requiring both — with an optional
    // "label:" prefix — is the unique IL signature and prevents stealing
    // Square D / Siemens listings that merely contain "S"/"R" line heads or an
    // "AND" substring in their labels.
    private static readonly Regex IlLoadHeadRe =
        new(@"^\s*(?:[A-Za-z_]\w*\s*:\s*)?LDN?\s+\S",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex IlStoreHeadRe =
        new(@"^\s*(?:[A-Za-z_]\w*\s*:\s*)?(ST|STN|S|R)\s+\S",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex IlHeadRe =
        new(@"^\s*(LD|LDN|AND|ANDN|OR|ORN|ST|STN|S|R)\s+\S",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex StStatementRe =
        new(@"([^;]*?:=[^;]*?);", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LhsRe =
        new(@"^([A-Za-z_][A-Za-z_0-9.]*)\s*$", RegexOptions.Compiled);
    private static readonly Regex IlLabelRe =
        new(@"^[A-Za-z_]\w*\s*:\s*", RegexOptions.Compiled);

    private static string StripComments(string text)
    {
        text = BlockComment.Replace(text, "");
        text = BraceComment.Replace(text, "");
        text = LineComment.Replace(text, "");
        return text;
    }

    public static bool IsIec(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var cleaned = StripComments(text);
        bool hasSt = StAssignRe.IsMatch(cleaned);
        // IL must have a genuine LD...ST shape, not merely an "AND" substring.
        bool hasIl = IlLoadHeadRe.IsMatch(cleaned) && IlStoreHeadRe.IsMatch(cleaned);
        if (hasSt) return true;
        if (hasIl)
        {
            bool siemensOnly = Regex.IsMatch(cleaned, @"^\s*(AN|ON|U|UN)\s+\S",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            bool iecOnly = Regex.IsMatch(cleaned, @"^\s*(LDN|ANDN|ORN|STN)\s+\S",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (siemensOnly && !iecOnly) return false;
            return true;
        }
        return false;
    }

    public static PageLadder ParsePage(string text, int pageNumber = 0)
    {
        var page = new PageLadder { PageNumber = pageNumber };
        if (string.IsNullOrEmpty(text)) return page;
        var cleaned = StripComments(text);

        var stStatements = new List<string>();
        var spans = new List<(int start, int len)>();
        foreach (Match m in StStatementRe.Matches(cleaned))
        {
            stStatements.Add(m.Groups[1].Value.Trim());
            spans.Add((m.Index, m.Length));
        }

        var masked = cleaned.ToCharArray();
        foreach (var (start, len) in spans)
            for (int i = start; i < start + len && i < masked.Length; i++) masked[i] = ' ';
        var leftover = new string(masked);

        int rungNo = 0;
        foreach (var body in stStatements)
        {
            var r = ParseStStatement(rungNo + 1, body);
            if (r is not null) { rungNo++; r.Number = rungNo; page.Rungs.Add(r); }
        }

        if (IlHeadRe.IsMatch(leftover))
        {
            foreach (var r in ParseIlStatements(leftover))
            {
                rungNo++;
                r.Number = rungNo;
                page.Rungs.Add(r);
            }
        }
        return page;
    }

    // ----------------------------------------------------------------- ST
    private enum Tk { LParen, RParen, And, Or, Xor, Not, True, False, Ident }

    private sealed record StTok(Tk Kind, string Text);

    private static List<StTok> TokenizeSt(string src)
    {
        var outp = new List<StTok>();
        int pos = 0, n = src.Length;
        while (pos < n)
        {
            char c = src[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            switch (c)
            {
                case '(': outp.Add(new StTok(Tk.LParen, "(")); pos++; continue;
                case ')': outp.Add(new StTok(Tk.RParen, ")")); pos++; continue;
                case '&': outp.Add(new StTok(Tk.And, "&")); pos++; continue;
                case '|': outp.Add(new StTok(Tk.Or, "|")); pos++; continue;
                case '!': outp.Add(new StTok(Tk.Not, "!")); pos++; continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = pos;
                while (pos < n && (char.IsLetterOrDigit(src[pos]) || src[pos] == '_' || src[pos] == '.')) pos++;
                var word = src.Substring(start, pos - start);
                var up = word.ToUpperInvariant();
                outp.Add(up switch
                {
                    "AND" => new StTok(Tk.And, word),
                    "OR" => new StTok(Tk.Or, word),
                    "XOR" => new StTok(Tk.Xor, word),
                    "NOT" => new StTok(Tk.Not, word),
                    "TRUE" => new StTok(Tk.True, word),
                    "FALSE" => new StTok(Tk.False, word),
                    _ => new StTok(Tk.Ident, word),
                });
                continue;
            }
            pos++; // skip unrecognized char
        }
        return outp;
    }

    private sealed class StParser
    {
        private readonly List<StTok> _toks;
        private int _i;
        public List<string> IdentsUsed { get; } = new();

        public StParser(List<StTok> toks) { _toks = toks; }

        private StTok? Peek => _i < _toks.Count ? _toks[_i] : null;
        private StTok Consume() => _toks[_i++];

        public BoolExpr ParseOr()
        {
            var left = ParseXor();
            while (Peek is { Kind: Tk.Or }) { Consume(); left = BoolExpr.Or(new[] { left, ParseXor() }); }
            return left;
        }

        private BoolExpr ParseXor()
        {
            var left = ParseAnd();
            while (Peek is { Kind: Tk.Xor }) { Consume(); left = BoolExpr.Xor(left, ParseAnd()); }
            return left;
        }

        private BoolExpr ParseAnd()
        {
            var left = ParseNot();
            while (Peek is { Kind: Tk.And }) { Consume(); left = BoolExpr.And(new[] { left, ParseNot() }); }
            return left;
        }

        private BoolExpr ParseNot()
        {
            if (Peek is { Kind: Tk.Not }) { Consume(); return BoolExpr.Not(ParseNot()); }
            return ParseAtom();
        }

        private BoolExpr ParseAtom()
        {
            var t = Peek;
            if (t is null) return new BTrue();
            switch (t.Kind)
            {
                case Tk.LParen:
                    Consume();
                    var inner = ParseOr();
                    if (Peek is { Kind: Tk.RParen }) Consume();
                    return inner;
                case Tk.True: Consume(); return new BTrue();
                case Tk.False: Consume(); return new BFalse();
                case Tk.Ident:
                    Consume();
                    IdentsUsed.Add(t.Text);
                    return BoolExpr.Var(t.Text);
                default:
                    Consume();
                    return new BTrue();
            }
        }
    }

    private static Rung? ParseStStatement(int num, string body)
    {
        int idx = body.IndexOf(":=", StringComparison.Ordinal);
        if (idx < 0) return null;
        var lhs = body.Substring(0, idx).Trim();
        var rhs = body.Substring(idx + 2).Trim();
        if (lhs.Length == 0 || rhs.Length == 0) return null;

        var m = LhsRe.Match(lhs);
        if (!m.Success) return null;
        var outputAddr = m.Groups[1].Value;

        var parser = new StParser(TokenizeSt(rhs));
        var expr = parser.ParseOr();

        var rung = new Rung { Number = num };
        foreach (var name in parser.IdentsUsed)
            rung.Inputs.Add(new Element(ElementKind.NormallyOpen, name, "", rung.Inputs.Count, 0));
        rung.Output = new Element(ElementKind.Coil, outputAddr, "", 1000, 0);
        LadderEquation.Apply(rung, expr);
        return rung;
    }

    // ----------------------------------------------------------------- IL
    private static readonly Dictionary<string, (string baseOp, bool negate)> IlInputOps =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["LD"] = ("LD", false),
            ["LDN"] = ("LD", true),
            ["AND"] = ("AND", false),
            ["&"] = ("AND", false),
            ["ANDN"] = ("AND", true),
            ["&N"] = ("AND", true),
            ["OR"] = ("OR", false),
            ["ORN"] = ("OR", true),
            ["XOR"] = ("XOR", false),
            ["XORN"] = ("XOR", true),
        };

    private static readonly Dictionary<string, string> IlOutputOps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ST"] = "COIL",
        ["STN"] = "COIL_NEG",
        ["S"] = "LATCH",
        ["R"] = "UNLATCH",
    };

    private static List<Rung> ParseIlStatements(string text)
    {
        var rungs = new List<Rung>();
        BoolExpr? rlo = null;
        var inputs = new List<Element>();
        int rungNum = 0;

        void Commit(string outputOp, string addr)
        {
            rungNum++;
            var rung = new Rung { Number = rungNum };
            foreach (var e in inputs) rung.Inputs.Add(e);
            var body = rlo ?? new BTrue();

            BoolExpr applyBody;
            ElementKind kind;
            switch (outputOp)
            {
                case "ST": kind = ElementKind.Coil; applyBody = body; break;
                case "STN": kind = ElementKind.Coil; applyBody = BoolExpr.Not(body); break;
                case "S": kind = ElementKind.Latch; applyBody = body; break;
                default: kind = ElementKind.Unlatch; applyBody = body; break; // R
            }
            rung.Output = new Element(kind, addr, "", 1000, 0);
            LadderEquation.Apply(rung, applyBody);
            rungs.Add(rung);

            rlo = null;
            inputs = new List<Element>();
        }

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            line = IlLabelRe.Replace(line, "");
            var toks = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length == 0) continue;
            var head = toks[0].ToUpperInvariant();

            if (IlInputOps.TryGetValue(head, out var op) && toks.Length >= 2)
            {
                var arg = toks[1].TrimEnd(';', ',');
                var sym = BoolExpr.Var(arg);
                var term = op.negate ? BoolExpr.Not(sym) : sym;
                inputs.Add(new Element(op.negate ? ElementKind.NormallyClosed : ElementKind.NormallyOpen,
                    arg, "", inputs.Count, 0));
                if (op.baseOp == "LD" || rlo is null) rlo = term;
                else if (op.baseOp == "AND") rlo = BoolExpr.And(new[] { rlo, term });
                else if (op.baseOp == "OR") rlo = BoolExpr.Or(new[] { rlo, term });
                else if (op.baseOp == "XOR") rlo = BoolExpr.Xor(rlo, term);
            }
            else if (IlOutputOps.TryGetValue(head, out var _) && toks.Length >= 2)
            {
                Commit(head, toks[1].TrimEnd(';', ','));
            }
            // else: unknown opcode, skip silently
        }
        return rungs;
    }
}
