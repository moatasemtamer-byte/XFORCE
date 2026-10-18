// Triconex TRISTATION ladder-logic parser.
//
// TRISTATION prints TMR safety-PLC programs as fixed-pitch ASCII ladder art:
//
//     Network #   34:        Size:    99 bytes
//       ESD1      ESD2      ESD3                                      esd-all
//     ---] [-------] [-------] [-----------------------------------------(   )
//
// Symbols (verified against the 255-page North plant printout):
//   contacts:  ] [ NO   ]/[ NC   ]^[ rising-edge   ]v[ falling-edge
//              ]<[ ]>[ ]<=[ ]>=[ ]=[  compare (right operand on the line BELOW)
//   coils:     ( ) energize  (NOT) negate  (TDE)/(TDD) on/off-delay timer
//              (PLS) pulse  (BNK) blink  (LCH) latch/set  (RST) reset
//   structure: parallel branches join with "+ ... | ... +"  =>  OR
//              the variable NAME sits on the line ABOVE each element;
//              compare operands / timer presets sit on the line BELOW.
//   blocks:    +===+ comment box (with "* name - desc" label defs)
//              +---+ structured-text box (var = expr; {comment}), which may
//              be gated by a contact to the left of the box border.
//
// Output: a TriconexDocument whose networks each carry the descriptive
// comment, harvested labels, ST statements, and one Rung per output coil
// (so the existing reporters / exporters / xref all work unchanged).

using System.Text;
using System.Text.RegularExpressions;

namespace CircuitReader.Core;

public sealed class TriconexNetwork
{
    public int Number { get; set; }
    public int SizeBytes { get; set; }
    public int Page { get; set; }
    public string Comment { get; set; } = string.Empty;
    public List<(string Name, string Desc)> Labels { get; } = new();
    public List<string> Statements { get; } = new();
    public List<Rung> Rungs { get; } = new();
    public string Kind { get; set; } = "ladder";   // ladder | st | comment
    public string? Note { get; set; }
}

public sealed class TriconexDocument
{
    public List<TriconexNetwork> Networks { get; } = new();
    public Dictionary<string, string> AddressLabels { get; } = new();
    public int NetworkCount => Networks.Count;
    public int RungCount => Networks.Sum(n => n.Rungs.Count);
}

public static class TriconexParser
{
    private static readonly Regex HeaderRe =
        new(@"^\s*Network #\s*(\d+):\s*Size:\s*(\d+)\s*bytes", RegexOptions.Compiled);
    private static readonly Regex FooterRe =
        new(@"TRISTATION|Ladder Logic\s*:", RegexOptions.Compiled);
    private static readonly Regex CommentBorderRe = new(@"^\s*\+=+\+\s*$", RegexOptions.Compiled);
    private static readonly Regex StBorderRe = new(@"\+-{3,}\+", RegexOptions.Compiled);
    private static readonly Regex StBoxCommentRe = new(@"\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex LabelDefRe = new(@"\*\s+(\S+)\s+-\s+(.+?)\s*$", RegexOptions.Compiled);
    // A contact: ] <op> [ where <op> is empty/space (NO) or one of the operators.
    // Equality appears as ]==[ in TRISTATION listings; keep it ahead of the
    // single-char '=' alternative so the two-character form is matched greedily.
    private static readonly Regex ContactRe =
        new(@"\](==|<=|>=|<>|<|>|=|/|\^|v|\s)\[", RegexOptions.Compiled);
    private static readonly string[] CoilTypes =
        { "NOT", "TDE", "TDD", "LCH", "RST", "PLS", "BNK" };

    // ------------------------------------------------------------------ detect

    public static bool IsTriconex(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains("TRISTATION")) return true;
        bool hasNet = Regex.IsMatch(text, @"Network #\s*\d+:");
        return hasNet && text.Contains("---]");
    }

    public static bool IsTriconex(IReadOnlyList<string> lines) =>
        IsTriconex(string.Join("\n", lines));

    // ------------------------------------------------------------------- parse

    /// <summary>Parse a single block of text (one page or a test snippet).</summary>
    public static TriconexDocument Parse(string text) =>
        Parse(new (IReadOnlyList<string>, int)[] { (SplitLines(text), 1) });

    public static TriconexDocument Parse(IReadOnlyList<string> lines, int page = 1) =>
        Parse(new (IReadOnlyList<string>, int)[] { (lines, page) });

    /// <summary>
    /// Parse the whole document.  Networks frequently span page breaks, so we
    /// stitch every page's lines into one stream (dropping page footers) and
    /// split on the "Network #" headers.
    /// </summary>
    public static TriconexDocument Parse(IEnumerable<(IReadOnlyList<string> Lines, int Page)> pages)
    {
        var doc = new TriconexDocument();

        // Flatten to (line, page), dropping footer lines.
        var stream = new List<(string Line, int Page)>();
        foreach (var (lines, page) in pages)
            foreach (var ln in lines)
            {
                if (FooterRe.IsMatch(ln)) continue;
                stream.Add((ln, page));
            }

        // Split into networks on header lines.
        var current = new List<(string Line, int Page)>();
        int curNum = 0, curSize = 0, curPage = 1;
        bool have = false;
        foreach (var (line, page) in stream)
        {
            var m = HeaderRe.Match(line);
            if (m.Success)
            {
                if (have)
                    EmitNetwork(doc, curNum, curSize, curPage, current);
                curNum = int.Parse(m.Groups[1].Value);
                curSize = int.Parse(m.Groups[2].Value);
                curPage = page;
                current = new List<(string, int)>();
                have = true;
            }
            else if (have)
            {
                current.Add((line, page));
            }
        }
        if (have)
            EmitNetwork(doc, curNum, curSize, curPage, current);

        return doc;
    }

    private static void EmitNetwork(TriconexDocument doc, int num, int size, int page,
                                    List<(string Line, int Page)> body)
    {
        var net = new TriconexNetwork { Number = num, SizeBytes = size, Page = page };
        var lines = body.Select(b => b.Line).ToList();

        // 1) Pull out +===+ comment boxes (description + label definitions).
        var ladder = ExtractComments(lines, net, doc);

        // 2) Structured-text box?  (+---+ borders with "var = expr" content)
        if (TryParseStructuredText(ladder, net))
        {
            net.Kind = "st";
            doc.Networks.Add(net);
            return;
        }

        // 3) Relay ladder.
        ParseLadder(ladder, net);
        if (net.Rungs.Count == 0)
            net.Kind = "comment";
        doc.Networks.Add(net);
    }

    // --------------------------------------------------------------- comments

    private static List<string> ExtractComments(List<string> lines, TriconexNetwork net,
                                                TriconexDocument doc)
    {
        var rest = new List<string>();
        var comment = new StringBuilder();
        bool inBox = false;
        foreach (var ln in lines)
        {
            if (CommentBorderRe.IsMatch(ln))
            {
                inBox = !inBox;   // toggle on each +===+ border
                continue;
            }
            if (inBox)
            {
                var inner = ln.Trim();
                if (inner.StartsWith("|")) inner = inner.Substring(1);
                if (inner.EndsWith("|")) inner = inner.Substring(0, inner.Length - 1);
                inner = inner.TrimEnd();
                var ld = LabelDefRe.Match(inner);
                if (ld.Success)
                {
                    var name = ld.Groups[1].Value;
                    var desc = ld.Groups[2].Value.Trim();
                    net.Labels.Add((name, desc));
                    if (!doc.AddressLabels.ContainsKey(name))
                        doc.AddressLabels[name] = desc;
                }
                var t = inner.Trim().TrimStart('*').Trim();
                if (t.Length > 0) comment.Append(comment.Length > 0 ? " | " : "").Append(t);
            }
            else
            {
                rest.Add(ln);
            }
        }
        net.Comment = comment.ToString();
        return rest;
    }

    // -------------------------------------------------------- structured text

    /// <summary>A structured-text box border is a line whose <c>+---+</c> run is a
    /// real box edge — the line carries no ladder contact or coil.  A wide rung can
    /// contain a long horizontal connector wire (<c>+--------+</c>) joining a branch
    /// point to the coil rail; that also matches <see cref="StBorderRe"/> but is NOT
    /// a box border (e.g. net 56/80's TDD self-seal timer).  Excluding element lines
    /// keeps those from being misread as an (empty) structured-text block.</summary>
    private static bool IsStBoxBorderLine(string l) =>
        StBorderRe.IsMatch(l) && !ContactRe.IsMatch(l) && FindCoils(l).Count == 0;

    // Join box-interior fragments while a "(" is still open, so a wrapped function
    // call argument list becomes one statement.  Parens inside { comments } are
    // ignored; balanced lines (constant/setpoint tables) stay separate.
    private static List<string> MergeStBoxContinuations(List<string> frags)
    {
        var outp = new List<string>();
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        foreach (var f in frags)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(f);
            foreach (var ch in StBoxCommentRe.Replace(f, ""))
            {
                if (ch == '(') depth++;
                else if (ch == ')' && depth > 0) depth--;
            }
            if (depth <= 0)
            {
                outp.Add(sb.ToString().Trim());
                sb.Clear();
                depth = 0;
            }
        }
        if (sb.Length > 0) outp.Add(sb.ToString().Trim());
        return outp;
    }

    private static bool TryParseStructuredText(List<string> lines, TriconexNetwork net)
    {
        int top = lines.FindIndex(IsStBoxBorderLine);
        if (top < 0) return false;
        int bottom = lines.FindIndex(top + 1, IsStBoxBorderLine);
        if (bottom < 0) bottom = lines.Count - 1;

        var topMatch = StBorderRe.Match(lines[top]);
        int leftCol = topMatch.Index;
        int rightCol = topMatch.Index + topMatch.Length - 1;

        // Gate = contacts to the left of the box border on the content lines.
        Node? gate = null;
        var rawFrags = new List<string>();
        for (int i = top + 1; i < bottom; i++)
        {
            var ln = lines[i];
            // statement text between the box borders
            int a = Math.Min(leftCol + 1, ln.Length);
            int b = Math.Min(rightCol, ln.Length);
            if (b > a)
            {
                var stmt = ln.Substring(a, b - a).Trim().TrimEnd('|').Trim();
                if (stmt.Length > 0) rawFrags.Add(stmt);
            }
            // gate contacts in the left margin
            if (gate is null && leftCol > 0 && ln.Length > 0)
            {
                var left = ln.Substring(0, Math.Min(leftCol, ln.Length));
                // Compare operands sit on the line *below* the contact line, in
                // the same left margin (e.g. rMINTIM ]==[ 0 / rS6081S ]>=[ kSPDMIN).
                // That line is often the box's bottom border, so slice to leftCol
                // to keep the margin operand and drop the '+---+' border itself.
                string? below = null;
                if (i + 1 < lines.Count && lines[i + 1].Length > 0)
                    below = lines[i + 1].Substring(0, Math.Min(leftCol, lines[i + 1].Length));
                var contacts = ParseContactsOnLine(left, FindLabelLine(lines, i), below);
                if (contacts.Count > 0)
                    gate = SeriesAnd(contacts.Select(c => c.ToNode()).ToList());
            }
        }

        // Rejoin fragments that continue an unclosed "(" — a function call whose
        // argument list wraps across several source lines (e.g. lini01-03(...)) —
        // so each fully-parenthesized statement lands on its own row.
        var statements = MergeStBoxContinuations(rawFrags);

        if (statements.Count == 0) return false;

        foreach (var s in statements) net.Statements.Add(s);
        // Output = LHS of the last real assignment (skip {comment}-only lines).
        string outVar = "";
        foreach (var s in statements)
        {
            var lhs = AssignLhs(s);
            if (lhs is not null) outVar = lhs;
        }

        var rung = new Rung { Number = net.Number };
        rung.Comment = net.Comment;
        foreach (var s in statements) rung.Statements.Add(s);
        if (outVar.Length > 0)
            rung.Output = new Element(ElementKind.LetAssign, outVar, outVar, 0, 0);

        var joined = string.Join("; ", statements);
        if (gate is not null)
        {
            rung.BooleanEquation = $"IF {gate.Infix} THEN {{ {joined} }}";
            rung.FunctionEquation = $"IF {gate.Func} THEN {{ {joined} }}";
            rung.Note = "gated structured-text block";
        }
        else
        {
            rung.BooleanEquation = joined;
            rung.FunctionEquation = joined;
            rung.Note = "structured-text block";
        }
        rung.Minimized = rung.BooleanEquation;
        net.Rungs.Add(rung);

        // A network can pair an ST box (or several stacked boxes) with relay
        // coils that hang off the same left power rail — e.g. the turbine
        // mode-select boxes that also (LCH) fNEWMOD, or the surge boxes that
        // (LCH)/(RST) f6INSUR.  The ST path returns here without running the
        // ladder solver, so those coils' set/reset paths would be lost (fNEWMOD
        // would otherwise look like a write-only unlatch).  Blank every ST box
        // out of the geometry — leaving only the rail, gate contacts, and coil
        // branches — and run the normal ladder solver over what remains so each
        // coil gets its exact series-parallel condition (a box's own gate
        // contact simply becomes a harmless dead-end for a sibling coil branch).
        var ladderOnly = BlankStBoxes(lines);
        ParseLadder(ladderOnly, net, forceGraph: true);
        return true;
    }

    /// <summary>Blank every <c>+---+</c> structured-text box (its border rows and
    /// everything to their right) so the remaining geometry is pure relay ladder.
    /// The left power rail and coil branches sit to the LEFT of the box borders,
    /// so they survive; a contact that fed a box turns into a harmless dead-end.</summary>
    private static List<string> BlankStBoxes(List<string> lines)
    {
        var outp = new List<string>(lines);
        int i = 0;
        while (i < outp.Count)
        {
            if (!IsStBoxBorderLine(outp[i])) { i++; continue; }
            int leftCol = StBorderRe.Match(outp[i]).Index;
            int bottom = i;
            for (int j = i + 1; j < outp.Count; j++)
            {
                if (!IsStBoxBorderLine(outp[j])) continue;
                var bm = StBorderRe.Match(outp[j]);
                if (Math.Abs(bm.Index - leftCol) <= 1) { bottom = j; break; }
            }
            for (int r = i; r <= bottom; r++)
                if (leftCol < outp[r].Length)
                    outp[r] = outp[r].Substring(0, leftCol);
            i = bottom + 1;
        }
        return outp;
    }

    private static string? AssignLhs(string stmt)
    {
        // strip {comments}
        var s = Regex.Replace(stmt, @"\{[^}]*\}", "").Trim();
        int eq = s.IndexOf('=');
        if (eq <= 0) return null;
        if (eq + 1 < s.Length && (s[eq + 1] == '=')) return null;   // comparison, not assign
        var lhs = s.Substring(0, eq).Trim();
        return Regex.IsMatch(lhs, @"^[A-Za-z_][\w$@%\-]*$") ? lhs : null;
    }

    // ----------------------------------------------------------------- ladder

    private sealed record Contact(string Kind, int Col, int RCol, int Center, string Name,
                                  string? Op = null, string? Rhs = null)
    {
        public Node ToNode()
        {
            switch (Kind)
            {
                case "NC": return Node.Lit("!" + Name, $"NOT({Name})");
                case "RISING": return Node.Lit($"RE({Name})", $"RE({Name})");
                case "FALLING": return Node.Lit($"FE({Name})", $"FE({Name})");
                case "COMPARE":
                    var inf = $"({Name} {Op} {Rhs})";
                    var fn = $"{CmpFunc(Op!)}({Name}, {Rhs})";
                    return Node.Lit(inf, fn);
                default: return Node.Lit(Name, Name);   // NO
            }
        }
    }

    private static string CmpFunc(string op) => op switch
    {
        "<" => "LT", ">" => "GT", "<=" => "LE", ">=" => "GE",
        "=" => "EQ", "==" => "EQ", "<>" => "NE", _ => "CMP",
    };

    private static void ParseLadder(List<string> lines, TriconexNetwork net, bool forceGraph = false)
    {
        // Classify element lines (those bearing contacts and/or a coil).
        var elemIdx = new List<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (ContactRe.IsMatch(lines[i]) || FindCoils(lines[i]).Count > 0)
                elemIdx.Add(i);
        }
        if (elemIdx.Count == 0) return;

        // The coil line(s): element lines that actually carry a coil token.
        var coilLines = elemIdx.Where(i => FindCoils(lines[i]).Count > 0).ToList();
        if (coilLines.Count == 0) return;

        // Parse every element line into contacts (+ its coil, if any).
        var parsed = new Dictionary<int, (List<Contact> Contacts, List<CoilHit> Coils)>();
        foreach (var i in elemIdx)
        {
            var labelLine = FindLabelLine(lines, i);
            var belowLine = (i + 1 < lines.Count) ? lines[i + 1] : "";
            var contacts = ParseContactsOnLine(lines[i], labelLine, belowLine);
            var coils = FindCoils(lines[i])
                .Select(c => ResolveCoil(c, labelLine, belowLine))
                .ToList();
            parsed[i] = (contacts, coils);
        }

        // Multi-coil network → emit one rung per coil, sharing the input chain.
        var allCoils = coilLines.SelectMany(i => parsed[i].Coils).ToList();
        bool multiCoil = allCoils.Count > 1;

        // Every contact anywhere in the network reads its address — collect them
        // so the cross-reference index sees parallel-branch reads too.
        var allContacts = elemIdx.SelectMany(i => parsed[i].Contacts).ToList();

        // A multi-coil network is a shared power rail: several coils tap the
        // trunk at different nodes, so the per-coil series/OR-block heuristic
        // loses any branch that lives on a *different* coil's line (rendering it
        // as "True").  Solve those with a proper series-parallel reduction of
        // the reconstructed wire graph; fall back to the heuristic for any coil
        // whose sub-ladder is not series-parallel.  Trailing coils below an ST
        // box (forceGraph) also need the graph even when there is a single coil:
        // the column-based heuristic would OR-in contacts from a sibling branch
        // that is not actually wired to the coil (e.g. net 126's TDD).
        var graph = LadderGraph.Build(lines, elemIdx, parsed);

        foreach (var ci in coilLines)
        {
            foreach (var coil in parsed[ci].Coils)
            {
                var body = graph?.SolveCoil(ci, coil.Col);
                var rung = body is not null
                    ? MakeRung(net, coil, body, multiCoil)
                    : BuildRung(net, lines, elemIdx, parsed, ci, coil, multiCoil);
                if (rung is null) continue;
                rung.Inputs.Clear();
                foreach (var c in allContacts)
                    rung.Inputs.Add(MakeInputElement(c));
                net.Rungs.Add(rung);
            }
        }
    }

    private static Rung? BuildRung(TriconexNetwork net, List<string> lines, List<int> elemIdx,
        Dictionary<int, (List<Contact> Contacts, List<CoilHit> Coils)> parsed,
        int coilLineIdx, CoilHit coil, bool multiCoil)
    {
        var spineContacts = parsed[coilLineIdx].Contacts;

        // Locate the parallel OR-block on the coil line: branch rows tie into
        // the spine at a DIVERGE column (left rail, where they split off) and a
        // MERGE column (right rail, where they rejoin).  When the branches split
        // at the implicit left rail there is only the single merge '+'.
        var (diverge, merge) = FindOrBlock(lines, coilLineIdx);

        Node body;
        if (merge < 0)
        {
            // No parallel branch on this coil line → pure series.
            var lits = spineContacts.Where(c => c.Col < coil.Col)
                                    .OrderBy(c => c.Col)
                                    .Select(c => c.ToNode()).ToList();
            body = SeriesAnd(lits);
        }
        else
        {
            // Spine contacts partition into series-BEFORE the block (left of the
            // diverge), the spine's own branch INSIDE the block, and series-AFTER
            // the block (right of the merge).  Only in-block contacts join the OR.
            var before = (diverge < 0)
                ? new List<Node>()
                : spineContacts.Where(c => c.Col < diverge)
                               .OrderBy(c => c.Col).Select(c => c.ToNode()).ToList();

            var spineBranch = spineContacts
                .Where(c => c.Col < merge && (diverge < 0 || c.Col > diverge))
                .OrderBy(c => c.Col).Select(c => c.ToNode()).ToList();

            // Branches ordered top-to-bottom so the rendered OR matches the
            // visual ladder layout.
            var branches = new List<(int Line, Node Expr)>();
            if (spineBranch.Count > 0) branches.Add((coilLineIdx, SeriesAnd(spineBranch)));
            foreach (var i in elemIdx)
            {
                if (i == coilLineIdx) continue;
                if (parsed[i].Coils.Count > 0) continue;   // a different coil's line
                var c = parsed[i].Contacts
                    .Where(c => c.Col < merge + 3 && (diverge < 0 || c.Col > diverge))
                    .OrderBy(c => c.Col).Select(c => c.ToNode()).ToList();
                if (c.Count > 0) branches.Add((i, SeriesAnd(c)));
            }

            var ordered = branches.OrderBy(b => b.Line).Select(b => b.Expr).ToList();

            // Anything on the spine to the RIGHT of the merge is in series after.
            var after = spineContacts.Where(c => c.Col > merge && c.Col < coil.Col)
                                     .OrderBy(c => c.Col)
                                     .Select(c => c.ToNode()).ToList();

            // Assemble  series-before · (OR of branches) · series-after.  When the
            // span encloses no branch contacts it is a bare pass-through wire
            // (logically True) and simply drops out of the series AND rather than
            // littering the equation with "& True".
            var series = new List<Node>(before);
            if (ordered.Count == 1) series.Add(ordered[0]);
            else if (ordered.Count > 1) series.Add(Node.Or(ordered));
            series.AddRange(after);
            body = series.Count == 0 ? Node.Lit("True", "TRUE")
                 : series.Count == 1 ? series[0]
                 : Node.And(series);
        }

        return MakeRung(net, coil, body, multiCoil);
    }

    private static Rung MakeRung(TriconexNetwork net, CoilHit coil, Node body, bool multiCoil)
    {
        var rung = new Rung { Number = net.Number, Comment = net.Comment };
        rung.Output = new Element(CoilKind(coil.Type), coil.Name, coil.Name, coil.Col, 0);
        RenderEquation(rung, coil, body, multiCoil);
        return rung;
    }

    private static void RenderEquation(Rung rung, CoilHit coil, Node body, bool multiCoil)
    {
        string bInf = body.Infix, bFunc = body.Func;
        string outName = coil.Name;
        string note = multiCoil ? "multi-output network; " : "";
        bool uncond = body.IsTrue;   // fed straight from the power rail (no conditioning contacts)

        switch (coil.Type)
        {
            case "NOT":
                rung.BooleanEquation = $"{outName} = !({bInf})";
                rung.FunctionEquation = $"{outName} = NOT({bFunc})";
                note += "negated coil";
                break;
            case "TDE":
                rung.BooleanEquation = $"{outName} = TDE({bInf}, {coil.Preset})";
                rung.FunctionEquation = $"{outName} = TDE({bFunc}, {coil.Preset})";
                note += $"on-delay timer (preset {coil.Preset})";
                break;
            case "TDD":
                rung.BooleanEquation = $"{outName} = TDD({bInf}, {coil.Preset})";
                rung.FunctionEquation = $"{outName} = TDD({bFunc}, {coil.Preset})";
                note += $"off-delay timer (preset {coil.Preset})";
                break;
            case "PLS":
                rung.BooleanEquation = $"{outName} = PLS({bInf}, {coil.Preset})";
                rung.FunctionEquation = $"{outName} = PLS({bFunc}, {coil.Preset})";
                note += $"pulse/one-shot (preset {coil.Preset})";
                break;
            case "BNK":
                rung.BooleanEquation = $"{outName} = BNK({bInf}, {coil.Preset})";
                rung.FunctionEquation = $"{outName} = BNK({bFunc}, {coil.Preset})";
                note += $"blink (preset {coil.Preset})";
                break;
            case "LCH":
                if (uncond)
                {
                    rung.BooleanEquation = $"{outName} = True";
                    rung.FunctionEquation = $"{outName} = TRUE";
                    note += "latch/set (unconditional)";
                }
                else
                {
                    rung.BooleanEquation = $"{outName} = {outName}_prev | ({bInf})";
                    rung.FunctionEquation = $"{outName} = OR({outName}_prev, {bFunc})";
                    note += "latch/set (next-state)";
                }
                break;
            case "RST":
                if (uncond)
                {
                    rung.BooleanEquation = $"{outName} = False";
                    rung.FunctionEquation = $"{outName} = FALSE";
                    note += "reset (unconditional)";
                }
                else
                {
                    rung.BooleanEquation = $"{outName} = {outName}_prev & !({bInf})";
                    rung.FunctionEquation = $"{outName} = AND({outName}_prev, NOT({bFunc}))";
                    note += "reset (next-state)";
                }
                break;
            default:   // energize ( )
                rung.BooleanEquation = $"{outName} = {bInf}";
                rung.FunctionEquation = $"{outName} = {bFunc}";
                break;
        }
        rung.Minimized = rung.BooleanEquation;
        rung.Note = string.IsNullOrEmpty(note) ? null : note;
    }

    // ----------------------------------------------------------- line parsing

    private sealed record CoilHit(string Type, int Col, int Center, string Name, string? Preset);

    private sealed record RawCoil(string Type, int Start, int Center);

    private static List<RawCoil> FindCoils(string line)
    {
        var hits = new List<RawCoil>();
        foreach (Match m in Regex.Matches(line, @"\(([A-Za-z]{1,3}|\s*)\)"))
        {
            var inner = m.Groups[1].Value.Trim();
            string type = inner.Length == 0 ? "ENERGIZE" : inner.ToUpperInvariant();
            if (type != "ENERGIZE" && !CoilTypes.Contains(type)) continue;
            hits.Add(new RawCoil(type, m.Index, m.Index + m.Length / 2));
        }
        return hits;
    }

    private static CoilHit ResolveCoil(RawCoil c, string labelLine, string belowLine)
    {
        var name = NearestToken(labelLine, c.Center) ?? "?";
        string? preset = null;
        if (c.Type is "TDE" or "TDD" or "PLS" or "BNK")
            preset = NearestToken(belowLine, c.Center) ?? "?";
        return new CoilHit(c.Type, c.Start, c.Center, name, preset);
    }

    private static List<Contact> ParseContactsOnLine(string line, string labelLine, string? belowLine)
    {
        var list = new List<Contact>();
        foreach (Match m in ContactRe.Matches(line))
        {
            int lb = m.Index;                       // column of ']'
            int rb = m.Index + m.Length - 1;        // column of '['
            int center = m.Index + m.Length / 2;
            var op = m.Groups[1].Value;
            var name = NearestToken(labelLine, center) ?? "?";
            string kind;
            string? cop = null, rhs = null;
            switch (op)
            {
                case "/": kind = "NC"; break;
                case "^": kind = "RISING"; break;
                case "v": kind = "FALLING"; break;
                case "==": case "<": case ">": case "<=": case ">=": case "=": case "<>":
                    kind = "COMPARE"; cop = op;
                    rhs = (belowLine is null) ? "?" : (NearestToken(belowLine, center) ?? "?");
                    break;
                default: kind = "NO"; break;   // space
            }
            list.Add(new Contact(kind, lb, rb, center, name, cop, rhs));
        }
        return list;
    }

    /// <summary>Find the merge column: a '+' on the coil line that is tied to a
    /// branch row by a vertical '|'/'+' on an adjacent line.  Returns -1 when
    /// the coil line has no parallel branch.</summary>
    // Locate the parallel OR-block on the coil line.  A '+' on the spine only
    // counts as a branch tie when some OTHER row carries a '|' or '+' at the
    // same column (a genuine vertical connector).  Returns the leftmost tie as
    // the DIVERGE column (where branches split) and the rightmost tie as the
    // MERGE column (where they rejoin).  A single tie means the branches split
    // at the implicit left rail, so DIVERGE is returned as -1.
    private static (int Diverge, int Merge) FindOrBlock(List<string> lines, int coilLineIdx)
    {
        var spine = lines[coilLineIdx];
        var tied = new List<int>();
        for (int x = 0; x < spine.Length; x++)
        {
            if (spine[x] != '+') continue;
            for (int j = 0; j < lines.Count; j++)
            {
                if (j == coilLineIdx) continue;
                var ln = lines[j];
                if (x < ln.Length && (ln[x] == '|' || ln[x] == '+')) { tied.Add(x); break; }
            }
        }
        if (tied.Count == 0) return (-1, -1);
        int merge = tied[tied.Count - 1];
        int diverge = tied.Count >= 2 ? tied[0] : -1;
        return (diverge, merge);
    }

    private static string FindLabelLine(List<string> lines, int elemLineIdx)
    {
        // The label line is the nearest non-blank text line above that is not
        // itself an element/connector line.
        for (int i = elemLineIdx - 1; i >= 0 && i >= elemLineIdx - 2; i--)
        {
            var ln = lines[i];
            if (ln.Trim().Length == 0) continue;
            if (ContactRe.IsMatch(ln)) return "";
            if (IsConnectorLine(ln)) continue;
            return ln;
        }
        return "";
    }

    private static bool IsConnectorLine(string line)
    {
        bool sawBar = false;
        foreach (var ch in line)
        {
            if (ch == '|' || ch == '+') { sawBar = true; continue; }
            if (ch == ' ' || ch == '-') continue;
            return false;
        }
        return sawBar;
    }

    /// <summary>Token (whitespace-delimited) whose centre column is closest to
    /// <paramref name="center"/>; null if none within tolerance.</summary>
    private static string? NearestToken(string line, int center, int tol = 8)
    {
        if (string.IsNullOrEmpty(line)) return null;
        string? best = null;
        int bestDist = int.MaxValue;
        foreach (Match m in Regex.Matches(line, @"\S+"))
        {
            int c = m.Index + m.Length / 2;
            int d = Math.Abs(c - center);
            if (d < bestDist) { bestDist = d; best = m.Value; }
        }
        return bestDist <= tol ? best : null;
    }

    private static Element MakeInputElement(Contact c)
    {
        var kind = c.Kind switch
        {
            "NC" => ElementKind.NormallyClosed,
            "COMPARE" => ElementKind.Compare,
            _ => ElementKind.NormallyOpen,
        };
        var addr = c.Kind == "COMPARE" ? $"{c.Name}{c.Op}{c.Rhs}" : c.Name;
        return new Element(kind, addr, c.Name, c.Col, 0);
    }

    private static ElementKind CoilKind(string type) => type switch
    {
        "LCH" => ElementKind.Latch,
        "RST" => ElementKind.Unlatch,
        "TDE" or "TDD" or "PLS" or "BNK" => ElementKind.Timer,
        _ => ElementKind.Coil,
    };

    private static Node SeriesAnd(List<Node> parts) =>
        parts.Count == 0 ? Node.Lit("True", "TRUE")
        : parts.Count == 1 ? parts[0]
        : Node.And(parts);

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    // --------------------------------------------------- shared-rail SP solver
    // Reconstructs the wire graph of a network from its fixed-pitch ASCII art
    // and reduces the two-terminal (left rail → coil) sub-network to a boolean
    // expression by series-parallel reduction.  Used only for multi-coil
    // networks (a shared power rail feeding several coils); single-coil rungs
    // keep the lighter series/OR-block heuristic untouched.  Contacts become
    // edges between wire "nets"; a virtual super-source ties every rail net to
    // logical TRUE.  Restricting each solve to the target coil's ancestors lets
    // a fan-out node dissolve in series for one coil at a time, so the shared
    // prefix is naturally factored (EN & (A | B)) instead of distributed.

    private sealed class LadderGraph
    {
        private static readonly char[] Conductors = { '-', '+', '|' };

        private readonly List<string> _lines;
        private readonly int _w;
        private readonly int[] _net;                 // (row*W + col) -> netId, -1 = not a conductor
        private readonly HashSet<int> _source = new();
        private readonly List<(int From, int To, Node Lit, int Row)> _edges = new();

        private LadderGraph(List<string> lines, int w, int[] net)
        {
            _lines = lines; _w = w; _net = net;
        }

        private int NetAt(int r, int c) =>
            (r >= 0 && r < _lines.Count && c >= 0 && c < _w) ? _net[r * _w + c] : -1;

        public static LadderGraph Build(List<string> lines, List<int> elemIdx,
            Dictionary<int, (List<Contact> Contacts, List<CoilHit> Coils)> parsed)
        {
            int h = lines.Count;
            int w = 0;
            foreach (var l in lines) w = Math.Max(w, l.Length);
            var net = new int[Math.Max(1, h * w)];
            for (int i = 0; i < net.Length; i++) net[i] = -1;

            bool IsCond(int r, int c) =>
                r >= 0 && r < h && c >= 0 && c < w && c < lines[r].Length &&
                Array.IndexOf(Conductors, lines[r][c]) >= 0;

            // Flood-fill 4-connected conductor cells into nets.
            int nid = 0;
            var q = new Queue<(int, int)>();
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    if (!IsCond(r, c) || net[r * w + c] != -1) continue;
                    q.Enqueue((r, c)); net[r * w + c] = nid;
                    while (q.Count > 0)
                    {
                        var (rr, cc) = q.Dequeue();
                        foreach (var (dr, dc) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                        {
                            int nr = rr + dr, ncx = cc + dc;
                            if (IsCond(nr, ncx) && net[nr * w + ncx] == -1)
                            { net[nr * w + ncx] = nid; q.Enqueue((nr, ncx)); }
                        }
                    }
                    nid++;
                }

            var g = new LadderGraph(lines, w, net);

            // Left power rail = the leftmost conductor column of the element
            // lines; every net that reaches it is a source (logically TRUE).
            int railCol = int.MaxValue;
            foreach (int r in elemIdx)
            {
                var ln = lines[r];
                for (int c = 0; c < ln.Length; c++)
                    if (Array.IndexOf(Conductors, ln[c]) >= 0) { railCol = Math.Min(railCol, c); break; }
            }
            if (railCol == int.MaxValue) railCol = 0;
            for (int r = 0; r < h; r++)
            {
                int id = g.NetAt(r, railCol);
                if (id >= 0) g._source.Add(id);
            }

            // Each contact is an edge: left-terminal net -> right-terminal net.
            foreach (int r in elemIdx)
                foreach (var ct in parsed[r].Contacts)
                {
                    int ln = g.NetAt(r, ct.Col - 1);
                    int rn = g.NetAt(r, ct.RCol + 1);
                    if (ln >= 0 && rn >= 0 && ln != rn)
                        g._edges.Add((ln, rn, ct.ToNode(), r));
                }

            return g;
        }

        /// <summary>Series-parallel reduce the rail→coil sub-network to a Node,
        /// or null when the sub-ladder is not series-parallel (the caller then
        /// falls back to the series/OR-block heuristic).</summary>
        public Node? SolveCoil(int coilRow, int coilCol)
        {
            int target = NetAt(coilRow, coilCol - 1);
            if (target < 0) return null;
            if (_source.Contains(target)) return Node.True;   // coil sits on the rail

            const int VS = -1;                                 // virtual super-source

            // Restrict the graph to nets that can reach the target coil.  This
            // lets a fan-out trunk node dissolve in series for this coil even
            // though it also feeds other coils' branches.
            var anc = new HashSet<int> { target };
            var stack = new Stack<int>(); stack.Push(target);
            while (stack.Count > 0)
            {
                int t = stack.Pop();
                foreach (var e in _edges)
                    if (e.To == t && anc.Add(e.From)) stack.Push(e.From);
            }

            var work = new List<(int From, int To, Node Lit, int Row)>();
            foreach (var e in _edges)
                if (anc.Contains(e.From) && anc.Contains(e.To)) work.Add(e);
            foreach (var s in _source)
                if (anc.Contains(s)) work.Add((VS, s, Node.True, RailRow(s)));

            int guard = 0;
            bool changed = true;
            while (changed && guard++ < 20000)
            {
                changed = false;

                // Parallel: merge every group of edges sharing both endpoints.
                var groups = work.GroupBy(e => (e.From, e.To))
                                 .Where(gp => gp.Count() > 1).ToList();
                if (groups.Count > 0)
                {
                    foreach (var gp in groups)
                    {
                        var members = gp.OrderBy(e => e.Row).ToList();
                        work.RemoveAll(e => e.From == gp.Key.From && e.To == gp.Key.To);
                        work.Add((gp.Key.From, gp.Key.To,
                                  OrSP(members.Select(m => m.Lit).ToList()),
                                  members.Min(m => m.Row)));
                    }
                    changed = true;
                    continue;
                }

                // Series: dissolve any interior node with exactly one in + one out.
                var nodes = work.Select(e => e.From).Concat(work.Select(e => e.To))
                                .Distinct().Where(n => n != VS && n != target).ToList();
                foreach (var w0 in nodes)
                {
                    var ins = work.Where(e => e.To == w0).ToList();
                    var outs = work.Where(e => e.From == w0).ToList();
                    if (ins.Count == 1 && outs.Count == 1)
                    {
                        var a = ins[0]; var b = outs[0];
                        work.Remove(a); work.Remove(b);
                        work.Add((a.From, b.To, AndSP(a.Lit, b.Lit), Math.Min(a.Row, b.Row)));
                        changed = true;
                        break;
                    }
                }
            }

            var finals = work.Where(e => e.From == VS && e.To == target).ToList();
            if (finals.Count >= 1 && finals.Count == work.Count)
                return OrSP(finals.OrderBy(e => e.Row).Select(e => e.Lit).ToList());
            return null;   // not series-parallel → fall back
        }

        // Topmost row occupied by a source net, used to order OR branches.
        private int RailRow(int netId)
        {
            for (int r = 0; r < _lines.Count; r++)
                for (int c = 0; c < _w; c++)
                    if (NetAt(r, c) == netId) return r;
            return int.MaxValue;
        }

        private static Node AndSP(Node a, Node b)
        {
            if (a.IsTrue) return b;
            if (b.IsTrue) return a;
            return Node.And(new List<Node> { a, b });
        }

        private static Node OrSP(List<Node> parts)
        {
            if (parts.Any(p => p.IsTrue)) return Node.True;
            return parts.Count == 1 ? parts[0] : Node.Or(parts);
        }
    }

    // ------------------------------------------------------ tiny expr tree
    // Renders BOTH infix ("A & !B | C") and function ("OR(AND(A, NOT(B)), C)")
    // forms in one pass.  Compares / edges / timers aren't expressible as a
    // pure boolean AST, so we carry the rendered strings directly.

    internal sealed class Node
    {
        private enum T { Lit, And, Or }
        private readonly T _t;
        private readonly string _inf;
        private readonly string _fn;
        private readonly List<Node> _parts = new();

        private Node(T t, string inf = "", string fn = "") { _t = t; _inf = inf; _fn = fn; }

        public static Node Lit(string infix, string func) => new(T.Lit, infix, func);

        public static readonly Node True = new(T.Lit, "True", "TRUE");
        public bool IsTrue => _t == T.Lit && _inf == "True";

        public static Node And(List<Node> parts) => Flatten(T.And, parts);
        public static Node Or(List<Node> parts) => Flatten(T.Or, parts);

        private static Node Flatten(T t, List<Node> parts)
        {
            var n = new Node(t);
            foreach (var p in parts)
            {
                if (p._t == t) n._parts.AddRange(p._parts);
                else n._parts.Add(p);
            }
            return n;
        }

        public string Infix
        {
            get
            {
                switch (_t)
                {
                    case T.Lit: return _inf;
                    case T.And:
                        return string.Join(" & ", _parts.Select(p =>
                            p._t == T.Or ? $"({p.Infix})" : p.Infix));
                    default: // Or
                        return string.Join(" | ", _parts.Select(p => p.Infix));
                }
            }
        }

        public string Func
        {
            get
            {
                switch (_t)
                {
                    case T.Lit: return _fn;
                    case T.And: return "AND(" + string.Join(", ", _parts.Select(p => p.Func)) + ")";
                    default: return "OR(" + string.Join(", ", _parts.Select(p => p.Func)) + ")";
                }
            }
        }
    }
}
