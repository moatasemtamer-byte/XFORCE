// Siemens STEP 7 STL (Statement List) ladder parser (C# port of step7.py).
//
// STEP 7 exports rungs as a sequence of statement-list mnemonics evaluated
// against an implicit RLO (Result-of-Logic-Operation) accumulator:
//
//     NETWORK 1
//     A     I0.0
//     AN    I0.1
//     O
//     A     I0.2
//     =     Q0.0
//
// Supported mnemonics:
//   A/U <bit>    AND  (U = German "UND")        AN/UN <bit>  AND NOT
//   O <bit>      OR                              ON <bit>     OR NOT
//   A( O( AN( ON( U( UN(   grouped sub-expression start   )  close group
//   =  <bit>     assign body to coil  -> COIL
//   S  <bit>     set/latch            -> LATCH
//   R  <bit>     reset/unlatch        -> UNLATCH
//   NETWORK n[:title]   rung boundary
//   NOP/BLD/labels/declarations   ignored

using System.Text.RegularExpressions;

namespace CircuitReader.Core;

public static class Step7Parser
{
    private static readonly Regex NetworkRe =
        new(@"^NETWORK\s+(\d+)(?:\s*[:.\-]\s*(.*))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SkipRe = new(
        @"^(BLD|NOP|TITLE|NAME|VERSION|FUNCTION|FUNCTION_BLOCK|" +
        @"VAR|VAR_INPUT|VAR_OUTPUT|VAR_TEMP|END_VAR|" +
        @"BEGIN|END_FUNCTION|END_FUNCTION_BLOCK|" +
        @"END_NETWORK|NETWORK_TITLE|COMMENT)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LineComment = new(@"(?://|;).*$", RegexOptions.Compiled);
    private static readonly Regex BlockComment = new(@"\(\*.*?\*\)", RegexOptions.Singleline | RegexOptions.Compiled);

    private enum Join { And, AndNot, Or, OrNot }

    private static readonly Dictionary<string, Join> GroupStart = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A("] = Join.And,
        ["U("] = Join.And,
        ["AN("] = Join.AndNot,
        ["UN("] = Join.AndNot,
        ["O("] = Join.Or,
        ["ON("] = Join.OrNot,
    };

    private sealed class Frame
    {
        public Join JoinMode;
        public BoolExpr? SavedRlo;
        public bool SavedFirst = true;
    }

    private static string StripComment(string line)
    {
        line = BlockComment.Replace(line, "");
        line = LineComment.Replace(line, "");
        return line.Trim();
    }

    public static bool IsStep7(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var up = text.ToUpperInvariant();
        bool hasNetwork = up.Contains("NETWORK ");
        bool hasAssign = Regex.IsMatch(up, @"^\s*=\s+\S", RegexOptions.Multiline);
        // Siemens bit addresses (I0.0 / Q0.0 / M0.0) are the discriminating
        // signature — without one this is almost certainly not STEP 7 STL
        // (and we must not steal Square D / LET-math listings that also use
        // bare "=" assignment lines).
        bool hasSiemensAddr = Regex.IsMatch(up, @"\b[IQM]\d+\.\d+\b");
        bool hasInputOp = Regex.IsMatch(up, @"^\s*(A|AN|O|ON|U|UN)\s+\S", RegexOptions.Multiline);
        return hasInputOp && hasSiemensAddr && (hasNetwork || hasAssign);
    }

    public static PageLadder ParsePage(string text, int pageNumber = 0)
    {
        var page = new PageLadder { PageNumber = pageNumber };
        if (string.IsNullOrEmpty(text)) return page;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var networkStarts = new List<int>();
        var titles = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = StripComment(lines[i]);
            if (line.Length == 0) continue;
            var m = NetworkRe.Match(line);
            if (m.Success)
            {
                networkStarts.Add(i);
                titles.Add(m.Groups[2].Success ? m.Groups[2].Value.Trim() : "");
            }
        }

        if (networkStarts.Count == 0)
        {
            page.Rungs.Add(ParseOneRung(1, lines, ""));
            return page;
        }

        networkStarts.Add(lines.Length);   // sentinel
        for (int k = 0; k < networkStarts.Count - 1; k++)
        {
            var body = lines[(networkStarts[k] + 1)..networkStarts[k + 1]];
            page.Rungs.Add(ParseOneRung(k + 1, body, titles[k]));
        }
        return page;
    }

    private static Rung ParseOneRung(int num, IReadOnlyList<string> lines, string title)
    {
        var rung = new Rung { Number = num };
        if (title.Length > 0) { rung.Note = title; rung.Comment = title; }

        BoolExpr? rlo = null;
        bool isFirst = true;
        var stack = new List<Frame>();
        var inputs = new List<Element>();
        Element? output = null;
        var notes = new List<string>();

        void Apply(string op, BoolExpr expr)
        {
            if (isFirst)
            {
                rlo = (op == "AN" || op == "ON") ? BoolExpr.Not(expr) : expr;
                isFirst = false;
                return;
            }
            rlo = op switch
            {
                "A" => BoolExpr.And(new[] { rlo!, expr }),
                "AN" => BoolExpr.And(new[] { rlo!, BoolExpr.Not(expr) }),
                "O" => BoolExpr.Or(new[] { rlo!, expr }),
                "ON" => BoolExpr.Or(new[] { rlo!, BoolExpr.Not(expr) }),
                _ => rlo!,
            };
        }

        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            if (line.Length == 0 || SkipRe.IsMatch(line)) continue;
            var toks = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length == 0) continue;
            var head = toks[0].ToUpperInvariant();

            if (GroupStart.TryGetValue(head, out var gmode))
            {
                stack.Add(new Frame { JoinMode = gmode, SavedRlo = rlo, SavedFirst = isFirst });
                rlo = null;
                isFirst = true;
                continue;
            }

            if (head == ")")
            {
                if (stack.Count == 0) { notes.Add("unbalanced-paren"); continue; }
                var frame = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                var inner = rlo ?? new BTrue();
                if (frame.SavedFirst)
                {
                    rlo = frame.JoinMode is Join.AndNot or Join.OrNot ? BoolExpr.Not(inner) : inner;
                }
                else
                {
                    rlo = frame.JoinMode switch
                    {
                        Join.And => BoolExpr.And(new[] { frame.SavedRlo!, inner }),
                        Join.AndNot => BoolExpr.And(new[] { frame.SavedRlo!, BoolExpr.Not(inner) }),
                        Join.Or => BoolExpr.Or(new[] { frame.SavedRlo!, inner }),
                        Join.OrNot => BoolExpr.Or(new[] { frame.SavedRlo!, BoolExpr.Not(inner) }),
                        _ => inner,
                    };
                }
                isFirst = false;
                continue;
            }

            if (NetworkRe.IsMatch(line)) continue;

            // Output mnemonics: =, S, R
            if (head == "=" || head == "S" || head == "R")
            {
                if (toks.Length < 2) { notes.Add($"missing-arg-after-{head}"); continue; }
                var addr = toks[1].TrimEnd(',', ';');
                var kind = head == "=" ? ElementKind.Coil : head == "S" ? ElementKind.Latch : ElementKind.Unlatch;
                output = new Element(kind, addr, "", 1000, 0);
                continue;
            }

            // Input mnemonics
            bool isAnd = head is "A" or "U";
            bool isAndNot = head is "AN" or "UN";
            bool isOr = head == "O";
            bool isOrNot = head == "ON";
            if (isAnd || isAndNot || isOr || isOrNot)
            {
                if (toks.Length < 2)
                {
                    // Bare 'O' = start a new OR product term.
                    if (isOr && rlo is not null)
                    {
                        stack.Add(new Frame { JoinMode = Join.Or, SavedRlo = rlo, SavedFirst = false });
                        rlo = null;
                        isFirst = true;
                        continue;
                    }
                    if (!isOr) notes.Add($"missing-arg-after-{head}");
                    continue;
                }
                var addr = toks[1].TrimEnd(',', ';');
                var sym = BoolExpr.Var(addr);
                var elemKind = (isAndNot || isOrNot) ? ElementKind.NormallyClosed : ElementKind.NormallyOpen;
                inputs.Add(new Element(elemKind, addr, "", inputs.Count, 0));
                if (isAnd) Apply("A", sym);
                else if (isAndNot) Apply("AN", sym);
                else if (isOr) Apply("O", sym);
                else Apply("ON", sym);
                continue;
            }

            notes.Add($"unknown-op:{head}");
        }

        // Auto-close pending OR-product frames left by bare 'O'.
        while (stack.Count > 0)
        {
            var frame = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            var inner = rlo ?? new BTrue();
            rlo = frame.SavedFirst ? inner : frame.JoinMode switch
            {
                Join.Or => BoolExpr.Or(new[] { frame.SavedRlo!, inner }),
                Join.And => BoolExpr.And(new[] { frame.SavedRlo!, inner }),
                Join.OrNot => BoolExpr.Or(new[] { frame.SavedRlo!, BoolExpr.Not(inner) }),
                Join.AndNot => BoolExpr.And(new[] { frame.SavedRlo!, BoolExpr.Not(inner) }),
                _ => inner,
            };
            isFirst = false;
        }

        var body = rlo ?? new BTrue();
        foreach (var e in inputs) rung.Inputs.Add(e);
        rung.Output = output;
        if (notes.Count > 0) rung.Note = ((rung.Note ?? "") + " " + string.Join(" ", notes)).Trim();

        if (output is null)
        {
            rung.Note = ((rung.Note ?? "") + " no output detected").Trim();
            return rung;
        }

        LadderEquation.Apply(rung, body);
        return rung;
    }
}
