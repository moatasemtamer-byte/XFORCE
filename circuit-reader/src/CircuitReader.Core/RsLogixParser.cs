// Allen-Bradley RSLogix mnemonic ladder parser (C# port of rslogix.py).
//
// RSLogix-500 / RSLogix-5000 export programs as line-oriented text where each
// rung is a sequence of instruction mnemonics separated by whitespace, with
// optional BST/NXB/BND markers delimiting parallel branches:
//
//     SOR  XIC I:0/0  XIO I:0/1  XIC B3:0/0  OTE O:0/0  EOR
//     SOR  XIC I:0/3  BST XIC I:0/4 NXB XIC B3:0/0 XIO I:0/5 BND  OTE O:0/1  EOR
//
// Supported mnemonics:
//   XIC <addr>  examine-if-closed  -> NO contact (addr)
//   XIO <addr>  examine-if-open    -> NC contact (~addr)
//   OTE <addr>  output energize    -> COIL
//   OTL <addr>  output latch       -> LATCH
//   OTU <addr>  output unlatch     -> UNLATCH
//   TON/TOF/RTO <timer>            -> TIMER coil
//   SOR / EOR                      start / end of rung
//   BST / NXB / BND                branch start / next leg / branch end

using System.Text.RegularExpressions;

namespace CircuitReader.Core;

public static class RsLogixParser
{
    private static readonly HashSet<string> InputOps = new(StringComparer.OrdinalIgnoreCase) { "XIC", "XIO" };

    private static readonly Dictionary<string, ElementKind> OutputOps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OTE"] = ElementKind.Coil,
        ["OTL"] = ElementKind.Latch,
        ["OTU"] = ElementKind.Unlatch,
        ["TON"] = ElementKind.Timer,
        ["TOF"] = ElementKind.Timer,
        ["RTO"] = ElementKind.Timer,
    };

    private static readonly HashSet<string> BranchOps = new(StringComparer.OrdinalIgnoreCase) { "BST", "NXB", "BND" };
    private static readonly HashSet<string> RungDelims = new(StringComparer.OrdinalIgnoreCase) { "SOR", "EOR" };

    private sealed record Tok(string Op, string Arg = "");

    private sealed class Branch
    {
        public List<List<Element>> Legs { get; } = new();
    }

    /// <summary>Heuristic: does this text look like an RSLogix mnemonic export?</summary>
    public static bool IsRsLogix(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool hasInput = false, hasOutput = false;
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = raw.Trim(',', ';', '(', ')');
            if (InputOps.Contains(t)) hasInput = true;
            else if (OutputOps.ContainsKey(t)) hasOutput = true;
            if (hasInput && hasOutput) return true;
        }
        return false;
    }

    public static PageLadder ParsePage(string text, int pageNumber = 0)
    {
        var page = new PageLadder { PageNumber = pageNumber };
        int n = 1;
        foreach (var toks in Tokenize(text))
            page.Rungs.Add(ParseOneRung(n++, toks));
        return page;
    }

    private static List<List<Tok>> Tokenize(string text)
    {
        var rungs = new List<List<Tok>>();
        var current = new List<Tok>();
        bool insideRung = false;
        bool hasAnySor = text.IndexOf("SOR", StringComparison.OrdinalIgnoreCase) >= 0;

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("//")) continue;

            foreach (var raw in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var up = raw.ToUpperInvariant();
                if (up == "SOR") { insideRung = true; current = new List<Tok>(); continue; }
                if (up == "EOR")
                {
                    if (current.Count > 0) rungs.Add(current);
                    current = new List<Tok>();
                    insideRung = false;
                    continue;
                }
                if (!hasAnySor) insideRung = true;   // implicit single-rung mode
                if (!insideRung) continue;

                if (InputOps.Contains(up) || OutputOps.ContainsKey(up) ||
                    BranchOps.Contains(up) || RungDelims.Contains(up))
                {
                    current.Add(new Tok(up));
                }
                else
                {
                    var cleaned = raw.Trim(',', ';', '(', ')');
                    if (cleaned.Length > 0) current.Add(new Tok("ADDR", cleaned));
                }
            }

            // Line-per-rung mode: when there are no SOR markers, a line that
            // produced an output op ends the rung here.
            if (!hasAnySor && current.Count > 0 && current.Any(t => OutputOps.ContainsKey(t.Op)))
            {
                rungs.Add(current);
                current = new List<Tok>();
            }
        }

        if (current.Count > 0) rungs.Add(current);
        return rungs;
    }

    private static Rung ParseOneRung(int num, List<Tok> toks)
    {
        var rung = new Rung { Number = num };
        var inputsSoFar = new List<Element>();
        var branches = new List<Branch>();
        Element? output = null;
        var branchStack = new List<Branch>();
        var branchLegInputs = new List<Element>();
        var notes = new List<string>();

        int i = 0, n = toks.Count;
        while (i < n)
        {
            var t = toks[i];

            if (InputOps.Contains(t.Op))
            {
                if (i + 1 >= n || toks[i + 1].Op != "ADDR") { notes.Add($"missing-arg-after-{t.Op}"); i++; continue; }
                var addr = toks[i + 1].Arg;
                var kind = t.Op.Equals("XIC", StringComparison.OrdinalIgnoreCase)
                    ? ElementKind.NormallyOpen : ElementKind.NormallyClosed;
                var elem = new Element(kind, addr, "", i, 0);
                if (branchStack.Count > 0) branchLegInputs.Add(elem);
                else inputsSoFar.Add(elem);
                i += 2;
                continue;
            }

            if (OutputOps.TryGetValue(t.Op, out var outKind))
            {
                if (i + 1 >= n || toks[i + 1].Op != "ADDR") { notes.Add($"missing-arg-after-{t.Op}"); i++; continue; }
                var addr = toks[i + 1].Arg;
                output = new Element(outKind, addr, "", i + 1000, 0);
                i += 2;
                continue;
            }

            if (t.Op == "BST")
            {
                var br = new Branch();
                branches.Add(br);
                branchStack.Add(br);
                branchLegInputs = new List<Element>();
                i++;
                continue;
            }
            if (t.Op == "NXB")
            {
                if (branchStack.Count > 0) { branchStack[^1].Legs.Add(branchLegInputs); branchLegInputs = new List<Element>(); }
                i++;
                continue;
            }
            if (t.Op == "BND")
            {
                if (branchStack.Count > 0)
                {
                    branchStack[^1].Legs.Add(branchLegInputs);
                    branchLegInputs = new List<Element>();
                    branchStack.RemoveAt(branchStack.Count - 1);
                }
                i++;
                continue;
            }

            if (t.Op == "ADDR") notes.Add($"stray-token:{t.Arg}");
            i++;
        }

        // Any branch left open by a missing BND: close it so its legs count.
        while (branchStack.Count > 0)
        {
            branchStack[^1].Legs.Add(branchLegInputs);
            branchLegInputs = new List<Element>();
            branchStack.RemoveAt(branchStack.Count - 1);
        }

        // Record all contacts as rung inputs (main row first, then branches)
        // so the cross-reference index sees every read.
        foreach (var e in inputsSoFar) rung.Inputs.Add(e);
        foreach (var br in branches)
            foreach (var leg in br.Legs)
                foreach (var e in leg)
                    rung.Inputs.Add(e);

        var seriesTerms = new List<BoolExpr>();
        foreach (var e in inputsSoFar) seriesTerms.Add(ToExpr(e));
        foreach (var br in branches)
        {
            var legExprs = new List<BoolExpr>();
            foreach (var leg in br.Legs)
                legExprs.Add(leg.Count == 0 ? new BTrue() : BoolExpr.And(leg.Select(ToExpr).ToList()));
            if (legExprs.Count > 0)
                seriesTerms.Add(legExprs.Count > 1 ? BoolExpr.Or(legExprs) : legExprs[0]);
        }

        var body = seriesTerms.Count == 0 ? (BoolExpr)new BTrue()
                 : seriesTerms.Count == 1 ? seriesTerms[0]
                 : BoolExpr.And(seriesTerms);

        rung.Output = output;
        if (notes.Count > 0) rung.Note = string.Join(" ", notes);

        if (output is null)
        {
            rung.Note = ((rung.Note ?? "") + " no output detected").Trim();
            return rung;
        }

        LadderEquation.Apply(rung, body);
        return rung;
    }

    private static BoolExpr ToExpr(Element e) =>
        e.Kind == ElementKind.NormallyClosed ? BoolExpr.Not(BoolExpr.Var(e.Address)) : BoolExpr.Var(e.Address);
}
