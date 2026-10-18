// Texas Instruments TI505 / TI565 (SIMATIC 505) ladder-listing parser.
//
// These programs are printed by TISOFT / SoftShop as a "LAD" listing where the
// rung geometry is encoded purely by the X/Y position of each word — there are
// NO "| |" / "( )" ASCII symbols (so the Square D tokenizer sees nothing and
// returns 0 rungs).  The convention is:
//
//   * Networks start with a header row:  "LAD Network <n> Address <m>".
//   * Bold comment text is rendered by over-printing each glyph twice, so a
//     comment word looks like  "TTHHIISS"  ("THIS").  We de-double it.
//   * Every contact / coil is a short address token (C1, X7, Y1006, …) with a
//     1-4 line symbolic nickname stacked directly ABOVE it.
//   * A "/" printed just BELOW a contact address marks a normally-closed
//     contact;  no slash = normally-open.
//   * Contacts live on the left;  output coils live on the far-right rail.
//   * Contacts sharing one X column but different rows are in PARALLEL (OR);
//     contacts across columns on the way to the coil are in SERIES (AND).
//   * Box instructions (COMPARE / TMR / MATH / MOVE / COUNTER …) sit in the
//     middle;  COMPARE boxes drive LT:/EQ:/GT: output bits, which we render as
//     comparison equations.
//
// The parser rebuilds that geometry into the shared Rung / PageLadder model so
// every downstream exporter, the cross-reference index and the GUI work
// unchanged.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CircuitReader.Core;

public static class Ti505Parser
{
    private static readonly Regex HeaderRe =
        new(@"LAD\s+Network\s+\d+\s+Address\s+\d+", RegexOptions.Compiled);

    // Discrete one-bit references: X input, Y output, C control relay, B bit.
    private static readonly Regex DiscreteRe =
        new(@"^[XYCB]\d{1,5}$", RegexOptions.Compiled);

    // Word / analog operands that appear only inside instruction boxes.
    private static readonly Regex AnalogRe =
        new(@"^(WX|WY|WL|V|K|TCP|TCC|DSC|STW|TD|DCP|DCC)\d{1,6}$", RegexOptions.Compiled);

    private static readonly HashSet<string> BoxKeywords = new(StringComparer.Ordinal)
    {
        "COMPARE", "MATH", "TMR", "TIMER", "MOVE", "MOVW", "COUNTER", "CTR",
        "BIT", "MIRW", "SHRW", "WAND", "WOR", "WXOR", "DRUM", "MWFT", "MWIT",
        "MWI", "MWT", "SUB", "MULT", "DIV", "SQRT", "BCD", "CMP", "LCT", "LDC",
    };

    // A real relay rung has at most a handful of contacts.  A pool far larger
    // than this means we swept up a non-ladder block (the cross-reference /
    // address-register / PID-loop / SFPGM documentation that TISOFT prints
    // AFTER the program), not a rung — so we discard it.
    private const int MaxLadderContacts = 60;

    /// <summary>True when the page text looks like a TI505 LAD listing.</summary>
    public static bool IsTi505(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains("TRISTATION")) return false;          // Triconex owns that
        if (!HeaderRe.IsMatch(text)) return false;
        // Require at least one TI discrete address so a stray "LAD Network"
        // phrase in prose can't trigger us.
        return Regex.IsMatch(text, @"\b[XYC]\d{1,5}\b");
    }

    // --------------------------------------------------------------------- model

    private sealed class W
    {
        public string Text = "";
        public double X;
        public double Y;
    }

    private sealed class Net
    {
        public int Number;
        public int Address;
        public int Page;
        public double Width = 792;
        public readonly List<W> Words = new();
    }

    private sealed class Elem
    {
        public string Addr = "";
        public double X;
        public double Y;
        public bool Nc;
        public string Label = "";
    }

    // --------------------------------------------------------------------- parse

    public static IReadOnlyList<PageLadder> Parse(IReadOnlyList<PdfPageData> pages)
    {
        var nets = Segment(pages);

        var byPage = new SortedDictionary<int, PageLadder>();
        int rungNo = 0;
        foreach (var net in nets)
        {
            if (!byPage.TryGetValue(net.Page, out var pl))
            {
                pl = new PageLadder { PageNumber = net.Page };
                byPage[net.Page] = pl;
            }
            BuildNetwork(net, pl, ref rungNo);
        }
        return byPage.Values.ToList();
    }

    /// <summary>Split the whole document into networks, stitching the
    /// continuation of a network that flows across a page break.</summary>
    private static List<Net> Segment(IReadOnlyList<PdfPageData> pages)
    {
        var nets = new List<Net>();
        Net? current = null;

        foreach (var page in pages)
        {
            // Drop the running page header line (Program:/Date/Page at top).
            var ws = page.Words
                .Where(w => w.Y > 18 && !string.IsNullOrWhiteSpace(w.Text))
                .Select(w => new W { Text = w.Text, X = w.X, Y = w.Y })
                .OrderBy(w => w.Y).ThenBy(w => w.X)
                .ToList();

            var headers = FindHeaders(ws);
            var headerWordSet = new HashSet<W>(headers.SelectMany(h => h.Words));

            // Words above the first header continue the previous network.
            double firstHeaderY = headers.Count > 0 ? headers[0].Y : double.MaxValue;
            foreach (var w in ws)
            {
                if (headerWordSet.Contains(w)) continue;
                if (w.Y < firstHeaderY)
                {
                    current?.Words.Add(w);
                }
            }

            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i];
                var net = new Net
                {
                    Number = h.Number,
                    Address = h.Address,
                    Page = page.PageNumber,
                    Width = page.Width,
                };
                double yLo = h.Y;
                double yHi = i + 1 < headers.Count ? headers[i + 1].Y : double.MaxValue;
                foreach (var w in ws)
                {
                    if (headerWordSet.Contains(w)) continue;
                    if (w.Y >= yLo && w.Y < yHi) net.Words.Add(w);
                }
                nets.Add(net);
                current = net;
            }

            // TISOFT prints the program body first, then appends documentation
            // (cross-reference, address registers, PID-loop / SFPGM config) on
            // header-less pages.  Those pages would otherwise be vacuumed into
            // the last open network (firstHeaderY = +inf), producing a single
            // monster "rung" with thousands of OR'd addresses.  Stop at the
            // "END OF THE PROGRAM" sentinel so the appendix is never parsed.
            if (IsProgramEndPage(ws)) break;
        }
        return nets;
    }

    /// <summary>True when a page carries the TISOFT end-of-program sentinel
    /// ("END OF THE PROGRAM" / "PROGRAM END"); everything after it is
    /// documentation, not ladder logic.</summary>
    private static bool IsProgramEndPage(List<W> ws)
    {
        var toks = new List<string>(ws.Count);
        foreach (var w in ws)
        {
            var t = w.Text;
            if (LooksDoubled(t)) t = Undouble(t);
            t = t.Trim().Trim('"', '.', ',', ':', ';', '*', '(', ')').ToUpperInvariant();
            if (t.Length > 0) toks.Add(t);
        }
        for (int i = 0; i + 1 < toks.Count; i++)
            if (toks[i] == "PROGRAM" && toks[i + 1] == "END") return true;

        // "END OF THE PROGRAM" as an ordered subsequence (robust to the
        // glyph-doubled word repeats and interleaved right-rail tokens).
        string[] want = { "END", "OF", "THE", "PROGRAM" };
        int wi = 0;
        foreach (var t in toks)
            if (t == want[wi] && ++wi == want.Length) return true;
        return false;
    }

    private sealed class Header
    {
        public int Number;
        public int Address;
        public double Y;
        public readonly List<W> Words = new();
    }

    private static List<Header> FindHeaders(List<W> ws)
    {
        var result = new List<Header>();
        for (int i = 0; i < ws.Count; i++)
        {
            if (ws[i].Text != "Network") continue;
            // "LAD" must sit just to the left on the same row.
            var lad = ws.FirstOrDefault(w =>
                w.Text == "LAD" && Math.Abs(w.Y - ws[i].Y) < 4 && w.X < ws[i].X && ws[i].X - w.X < 40);
            if (lad is null) continue;

            var row = ws.Where(w => Math.Abs(w.Y - ws[i].Y) < 4).OrderBy(w => w.X).ToList();
            int netNo = 0, addr = 0;
            int ni = row.IndexOf(ws[i]);
            // network number = first integer after "Network"
            for (int k = ni + 1; k < row.Count; k++)
                if (int.TryParse(row[k].Text, out netNo)) break;
            int ai = row.FindIndex(w => w.Text == "Address");
            if (ai >= 0)
                for (int k = ai + 1; k < row.Count; k++)
                    if (int.TryParse(row[k].Text, out addr)) break;

            var h = new Header { Number = netNo, Address = addr, Y = ws[i].Y };
            // Mark the whole header row (LAD Network n Address m) for exclusion.
            foreach (var w in row)
            {
                if (w.Text is "LAD" or "Network" or "Address") h.Words.Add(w);
                else if (int.TryParse(w.Text, out _) && w.X < lad.X + 130) h.Words.Add(w);
            }
            result.Add(h);
        }
        return result.OrderBy(h => h.Y).ToList();
    }

    // --------------------------------------------------------------- per network

    private static void BuildNetwork(Net net, PageLadder pl, ref int rungNo)
    {
        double coilMinX = net.Width * 0.58;

        // 1. Split words into rows by Y.
        var rows = ClusterRows(net.Words, 4.0);

        // 2. Pull out bold comment rows.  TI prints bold text by over-printing,
        //    which the extractor returns either glyph-doubled ("TTHHIISS") or
        //    whole-word-doubled ("THIS THIS").  Either way it is prose, not part
        //    of a rung, so route it to the network comment and de-duplicate it.
        //    A bold comment can share a Y-band with a real contact (or its own
        //    NC "/" markers); keep any ladder-content tokens in the content so
        //    we never lose a contact or its negation to the comment.
        var comment = new StringBuilder();
        var contentRows = new List<List<W>>();
        foreach (var row in rows)
        {
            int glyph = row.Count(w => LooksDoubled(w.Text));
            bool isComment = (row.Count > 0 && glyph * 2 >= row.Count && glyph >= 1)
                             || RowLooksWordDoubled(row);
            if (isComment)
            {
                var keep = new List<W>();
                string? prev = null;
                foreach (var w in row.OrderBy(w => w.X))
                {
                    var raw = w.Text.Trim();
                    if (DiscreteRe.IsMatch(raw) || AnalogRe.IsMatch(raw)
                        || raw == "/" || IsFieldLabel(raw))
                    {
                        keep.Add(w);                    // ladder content stays on the rung
                        continue;
                    }
                    var t = (LooksDoubled(raw) ? Undouble(raw) : raw).Trim('"');
                    if (t.Length == 0 || t is "+" or "-" or "++" or "--") continue;
                    if (prev != null && string.Equals(prev, t, StringComparison.OrdinalIgnoreCase))
                        continue;                       // collapse the doubled word/glyph
                    if (comment.Length > 0) comment.Append(' ');
                    comment.Append(t);
                    prev = t;
                }
                if (keep.Count > 0) contentRows.Add(keep);
            }
            else
            {
                contentRows.Add(row);
            }
        }
        var flat = contentRows.SelectMany(r => r).ToList();

        // 3. Identify box-field VALUE tokens (e.g. "LT: C40", "A: WX337") so the
        //    relay pool doesn't mistake a compare output for a contact.
        var boxValue = new HashSet<W>();
        foreach (var w in flat)
        {
            if (!IsFieldLabel(w.Text)) continue;
            var val = flat
                .Where(v => v != w && Math.Abs(v.Y - w.Y) < 4 && v.X > w.X && v.X - w.X < 150)
                .OrderBy(v => v.X)
                .FirstOrDefault();
            if (val is not null) boxValue.Add(val);
        }

        // 4. Discrete contacts / coils.
        var slashes = flat.Where(w => w.Text == "/").ToList();
        var contacts = new List<Elem>();
        var coils = new List<Elem>();
        foreach (var w in flat)
        {
            var addr = w.Text.Trim();
            if (!DiscreteRe.IsMatch(addr)) continue;
            if (boxValue.Contains(w)) continue;

            bool nc = slashes.Any(s => s.Y > w.Y + 2 && s.Y < w.Y + 12 && Math.Abs(s.X - w.X) < 9);
            var e = new Elem { Addr = addr, X = w.X, Y = w.Y, Nc = nc };
            e.Label = LabelAbove(flat, w, coilMinX);

            bool outputCapable = addr[0] is 'Y' or 'C' or 'B';
            if (w.X >= coilMinX && outputCapable) coils.Add(e);
            else contacts.Add(e);
        }

        // 5. Emit relay rungs.
        // Safety net: if we still ended up with an implausible number of
        // discrete tokens, this network is a documentation / cross-reference
        // dump (not a rung) — discard it rather than emit a monster equation.
        if (contacts.Count > MaxLadderContacts || coils.Count > MaxLadderContacts)
        {
            Console.Error.WriteLine(
                $"[ti505] skipped network {net.Number} (page {net.Page}): " +
                $"{contacts.Count} contacts / {coils.Count} coils — looks like a " +
                "cross-reference/documentation dump, not a ladder rung.");
            return;
        }

        string netComment = comment.ToString().Trim();
        bool hasBox = flat.Any(w => BoxKeywords.Contains(w.Text));
        bool first = true;
        if (coils.Count > 0)
        {
            EmitCoilRungs(contacts, coils, net, pl, ref rungNo, netComment, hasBox, ref first);
        }

        // 6. COMPARE boxes -> comparison output equations.
        EmitCompareRungs(flat, net, pl, ref rungNo, ref first, netComment);

        // 7. Record labels for the cross-reference index.
        foreach (var e in contacts.Concat(coils))
            if (e.Label.Length > 0 && !pl.AddressLabels.ContainsKey(e.Addr))
                pl.AddressLabels[e.Addr] = e.Label;
    }

    private static void EmitCoilRungs(
        List<Elem> contacts, List<Elem> coils, Net net, PageLadder pl,
        ref int rungNo, string netComment, bool hasBox, ref bool first)
    {
        // Decide the topology: independent per-row rungs vs. one shared mesh.
        var rows = ClusterElemRows(contacts, 7.0);

        bool perRow = false;
        Dictionary<Elem, List<Elem>>? coilToRow = null;
        if (coils.Count >= 2 && rows.Count == coils.Count)
        {
            coilToRow = new Dictionary<Elem, List<Elem>>();
            var usedRows = new HashSet<int>();
            bool ok = true;
            foreach (var coil in coils.OrderBy(c => c.Y))
            {
                int bi = -1; double best = 14;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (usedRows.Contains(i)) continue;
                    double d = Math.Abs(rows[i][0].Y - coil.Y);
                    if (d < best) { best = d; bi = i; }
                }
                if (bi < 0) { ok = false; break; }
                usedRows.Add(bi);
                coilToRow[coil] = rows[bi];
            }
            perRow = ok && usedRows.Count == coils.Count;
        }

        double anchorY = coils.Count > 0 ? coils.Min(c => c.Y) : 0;
        BoolExpr sharedBody = perRow ? BoolExpr.Var("?") : MeshExpr(contacts, anchorY);

        foreach (var coil in coils.OrderBy(c => c.Y))
        {
            var body = perRow ? SeriesExpr(coilToRow![coil]) : sharedBody;
            var rung = new Rung
            {
                Number = ++rungNo,
                Output = new Element(ElementKind.Coil, coil.Addr, coil.Label, coil.X, coil.Y),
            };
            LadderEquation.Apply(rung, body);
            foreach (var c in (perRow ? coilToRow![coil] : contacts))
                rung.Inputs.Add(new Element(
                    c.Nc ? ElementKind.NormallyClosed : ElementKind.NormallyOpen,
                    c.Addr, c.Label, c.X, c.Y));
            if (first && netComment.Length > 0) rung.Comment = netComment;
            if (hasBox)
                rung.Note = Append(rung.Note,
                    "network contains an instruction box (TMR/MATH/MOVE/COMPARE); coil enable shown, box data-flow omitted.");
            if (contacts.Count == 0 && !hasBox)
                rung.Note = Append(rung.Note, "unconditional coil (no contacts on the rung).");
            pl.Rungs.Add(rung);
            first = false;
        }
    }

    private static void EmitCompareRungs(
        List<W> flat, Net net, PageLadder pl, ref int rungNo, ref bool first, string netComment)
    {
        // TI listings echo the same COMPARE box (its keyword and operand columns
        // are over-printed for bold), so the same comparison can be discovered
        // several times.  A network never needs the identical "bit = (A op B)"
        // twice, so collapse duplicates instead of emitting a stack of clones.
        var seenEq = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kw in flat.Where(w => w.Text is "COMPARE" or "CMP").OrderBy(w => w.Y).ThenBy(w => w.X))
        {
            double xLo = kw.X - 25, xHi = kw.X + 150, yLo = kw.Y - 2, yHi = kw.Y + 210;
            // Clip the right edge at the next box on the same band so a stacked
            // pair of COMPARE boxes don't read each other's operand columns.
            var nextBox = flat
                .Where(w => w != kw && BoxKeywords.Contains(w.Text) && w.X > kw.X && Math.Abs(w.Y - kw.Y) < 6)
                .OrderBy(w => w.X).FirstOrDefault();
            if (nextBox is not null) xHi = Math.Min(xHi, nextBox.X - 12);

            string? a = Field(flat, "A:", xLo, xHi, yLo, yHi);
            string? b = Field(flat, "B:", xLo, xHi, yLo, yHi);
            if (a is null || b is null) continue;

            foreach (var (label, op) in new[] { ("LT:", "<"), ("EQ:", "=="), ("GT:", ">") })
            {
                var bit = Field(flat, label, xLo, xHi, yLo, yHi);
                if (bit is null || !DiscreteRe.IsMatch(bit)) continue;
                var eq = $"{bit} = ({a} {op} {b})";
                if (!seenEq.Add(eq)) continue;   // duplicate comparison echoed by bold text
                var rung = new Rung
                {
                    Number = ++rungNo,
                    Output = new Element(ElementKind.Compare, bit, "", kw.X, kw.Y),
                    BooleanEquation = eq,
                    Minimized = eq,
                    FunctionEquation = eq,
                    Note = "numeric COMPARE output (not a boolean equation).",
                };
                if (first && netComment.Length > 0) rung.Comment = netComment;
                pl.Rungs.Add(rung);
                first = false;
            }
        }
    }

    // ----------------------------------------------------------------- geometry

    // Reduce a contact mesh to a boolean expression.
    //
    // The rung is modelled as a TRUNK (the contact branch on the coil's main
    // line) plus parallel branches that drop above/below it.  A branch is OR-ed
    // into ONLY the run of trunk contacts whose columns it actually spans;
    // trunk contacts outside that span stay in series.  That is what makes a
    // reset contact drawn *inside* a seal branch bind to the seal alone —
    //   ~X3 | (C102 & ~C7)        (reset inside the seal branch)
    // instead of the wrong
    //   (~X3 | C102) & ~C7        (reset gating the whole rung).
    // A single row collapses to a plain series AND and a single column to a
    // plain parallel OR, so ordinary series / parallel rungs are unchanged.
    private static BoolExpr MeshExpr(List<Elem> contacts, double coilY)
    {
        if (contacts.Count == 0) return new BTrue();

        var rows = ClusterElemRows(contacts, 7.0);
        if (rows.Count == 1) return SeriesExpr(rows[0]);

        // Shared left-to-right column index for every contact so a parallel
        // branch can be mapped onto the trunk contacts it spans.
        var cols = ClusterCols(contacts, 35.0);
        var colOf = new Dictionary<Elem, int>();
        for (int ci = 0; ci < cols.Count; ci++)
            foreach (var e in cols[ci]) colOf[e] = ci;

        // Trunk = the branch sharing the coil's main line (nearest in Y).
        var trunk = rows.OrderBy(r => Math.Abs(RowY(r) - coilY)).ThenBy(r => RowY(r)).First();
        var branches = rows.Where(r => !ReferenceEquals(r, trunk)).ToList();

        // One segment per trunk contact, each carrying its column span, the
        // trunk sub-series, and any parallel branch alternatives.
        var segments = trunk
            .OrderBy(e => colOf[e])
            .Select(e => new Seg { Lo = colOf[e], Hi = colOf[e], Trunk = { e } })
            .ToList();

        var globalBranches = new List<(double Y, BoolExpr Expr)>();
        foreach (var br in branches)
        {
            int lo = br.Min(e => colOf[e]);
            int hi = br.Max(e => colOf[e]);
            var hit = segments.Where(s => s.Lo <= hi && s.Hi >= lo).ToList();
            var expr = SeriesExpr(br);
            double y = RowY(br);
            if (hit.Count == 0) { globalBranches.Add((y, expr)); continue; }
            MergeSegments(segments, hit).Alts.Add((y, expr));
        }

        var andParts = new List<BoolExpr>();
        foreach (var s in segments.OrderBy(s => s.Lo))
        {
            var trunkExpr = SeriesExpr(s.Trunk);
            if (s.Alts.Count == 0) { andParts.Add(trunkExpr); continue; }
            var alts = new List<(double Y, BoolExpr Expr)> { (RowY(s.Trunk), trunkExpr) };
            alts.AddRange(s.Alts);
            andParts.Add(BoolExpr.Or(Dedup(alts.OrderBy(a => a.Y).Select(a => a.Expr))));
        }

        BoolExpr body = BoolExpr.And(andParts);

        // A branch spanning no trunk contact (a full-height alternative) just
        // parallels the whole trunk.
        if (globalBranches.Count > 0)
        {
            var alts = new List<(double Y, BoolExpr Expr)> { (RowY(trunk), body) };
            alts.AddRange(globalBranches);
            body = BoolExpr.Or(Dedup(alts.OrderBy(a => a.Y).Select(a => a.Expr)));
        }
        return body;
    }

    private sealed class Seg
    {
        public int Lo;
        public int Hi;
        public List<Elem> Trunk = new();
        public List<(double Y, BoolExpr Expr)> Alts = new();
    }

    /// <summary>Merge the segments a branch overlaps into one contiguous run
    /// (trunk contacts kept in column order) and return the merged segment.</summary>
    private static Seg MergeSegments(List<Seg> segments, List<Seg> hit)
    {
        if (hit.Count == 1) return hit[0];
        var merged = new Seg
        {
            Lo = hit.Min(s => s.Lo),
            Hi = hit.Max(s => s.Hi),
            Trunk = hit.OrderBy(s => s.Lo).SelectMany(s => s.Trunk).ToList(),
            Alts = hit.SelectMany(s => s.Alts).ToList(),
        };
        int idx = segments.IndexOf(hit[0]);
        foreach (var s in hit) segments.Remove(s);
        segments.Insert(Math.Min(idx, segments.Count), merged);
        return merged;
    }

    private static double RowY(List<Elem> row) => row.Average(e => e.Y);

    private static BoolExpr SeriesExpr(List<Elem> row) =>
        row.Count == 0 ? new BTrue()
                       : BoolExpr.And(Dedup(row.OrderBy(e => e.X).Select(Literal)));

    /// <summary>Drop literals that render to the same string (preserving order).</summary>
    private static List<BoolExpr> Dedup(IEnumerable<BoolExpr> parts)
    {
        var seen = new HashSet<string>();
        var outp = new List<BoolExpr>();
        foreach (var p in parts)
            if (seen.Add(p.Render())) outp.Add(p);
        return outp;
    }

    private static BoolExpr Literal(Elem e) =>
        e.Nc ? BoolExpr.Not(BoolExpr.Var(e.Addr)) : BoolExpr.Var(e.Addr);

    private static List<List<Elem>> ClusterCols(List<Elem> es, double gap)
    {
        var sorted = es.OrderBy(e => e.X).ToList();
        var cols = new List<List<Elem>>();
        foreach (var e in sorted)
        {
            if (cols.Count == 0 || e.X - cols[^1].Max(c => c.X) > gap)
                cols.Add(new List<Elem>());
            cols[^1].Add(e);
        }
        return cols;
    }

    private static List<List<Elem>> ClusterElemRows(List<Elem> es, double tol)
    {
        var sorted = es.OrderBy(e => e.Y).ToList();
        var rows = new List<List<Elem>>();
        foreach (var e in sorted)
        {
            if (rows.Count == 0 || e.Y - rows[^1].Max(c => c.Y) > tol)
                rows.Add(new List<Elem>());
            rows[^1].Add(e);
        }
        return rows;
    }

    private static List<List<W>> ClusterRows(List<W> ws, double tol)
    {
        var sorted = ws.Where(w => w is not null).OrderBy(w => w.Y).ToList();
        var rows = new List<List<W>>();
        foreach (var w in sorted)
        {
            if (rows.Count == 0 || w.Y - rows[^1].Max(c => c.Y) > tol)
                rows.Add(new List<W>());
            rows[^1].Add(w);
        }
        return rows;
    }

    private static string LabelAbove(List<W> flat, W addr, double coilMinX)
    {
        var near = flat
            .Where(w => w != addr
                        && Math.Abs(w.X - addr.X) < 40
                        && w.Y < addr.Y - 2 && w.Y > addr.Y - 58
                        && IsLabelWord(w.Text))
            .ToList();
        if (near.Count == 0) return "";
        var rows = ClusterRows(near, 4.0);
        var parts = new List<string>();
        foreach (var row in rows.OrderBy(r => r[0].Y))
            parts.Add(string.Join(" ", row.OrderBy(w => w.X).Select(w => w.Text)));
        var label = string.Join(" ", parts).Trim();
        // Collapse adjacent duplicate words left by bold over-printing
        // ("SHUTDOWN. SHUTDOWN." -> "SHUTDOWN.", "UNIT UNIT" -> "UNIT"), but keep
        // genuinely-doubled short alarm codes intact ("HI HI", "LO LO").
        var toks = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lw = new List<string>();
        foreach (var t in toks)
        {
            if (lw.Count > 0 && t.Length > 2 &&
                string.Equals(lw[^1], t, StringComparison.OrdinalIgnoreCase))
                continue;
            lw.Add(t);
        }
        // Defensive cap: a symbolic nickname is a few short rows.  Anything far
        // longer is harvested noise, not a label.
        if (lw.Count > 24) lw = lw.Take(24).ToList();
        label = string.Join(" ", lw);
        if (label.Length > 160) label = label.Substring(0, 160).TrimEnd();
        return label;
    }

    private static string? Field(List<W> flat, string label, double xLo, double xHi, double yLo, double yHi)
    {
        var lab = flat
            .Where(w => w.Text == label && w.X >= xLo && w.X <= xHi && w.Y >= yLo && w.Y <= yHi)
            .OrderBy(w => w.Y).FirstOrDefault();
        if (lab is null) return null;
        var val = flat
            .Where(w => w != lab && Math.Abs(w.Y - lab.Y) < 4 && w.X > lab.X && w.X - lab.X < 150)
            .OrderBy(w => w.X).FirstOrDefault();
        return val?.Text.Trim();
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsFieldLabel(string s) =>
        s.Length is >= 2 and <= 8 && s.EndsWith(":") && s.Take(s.Length - 1).All(char.IsLetter);

    private static bool IsLabelWord(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return false;
        if (DiscreteRe.IsMatch(s) || AnalogRe.IsMatch(s)) return false;
        if (s == "/" || IsFieldLabel(s)) return false;
        if (BoxKeywords.Contains(s)) return false;
        if (LooksDoubled(s)) return false;
        if (s.All(c => !char.IsLetter(c))) return false;   // pure numbers / punctuation
        return true;
    }

    internal static bool LooksDoubled(string s)
    {
        s = s.Trim();
        if (s.Length < 4) return false;
        int pairs = 0, matches = 0;
        for (int i = 0; i + 1 < s.Length; i += 2)
        {
            pairs++;
            if (s[i] == s[i + 1]) matches++;
        }
        return pairs >= 2 && matches >= (int)Math.Ceiling(pairs * 0.6);
    }

    internal static string Undouble(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i += 2) sb.Append(s[i]);
        return sb.ToString();
    }

    /// <summary>True when a row is bold prose rendered as whole-word doubling
    /// ("THE THE SPEED SPEED ...") — i.e. about half its words repeat the word
    /// immediately to their left.  A genuine HiHi/LoLo nickname (e.g.
    /// "... HI HI SD") has at most one such pair and is not flagged.
    /// This is deliberately aggressive: any ladder content on a flagged row
    /// (addresses, NC "/" markers, box fields) is preserved by the caller, so a
    /// false positive only moves prose to the comment, never drops a contact.</summary>
    private static bool RowLooksWordDoubled(List<W> row)
    {
        if (row.Count < 4) return false;
        var words = row.OrderBy(w => w.X)
            .Select(w => w.Text.Trim().Trim('"'))
            .Where(s => s.Length > 0)
            .ToList();
        if (words.Count < 4) return false;
        int repeats = 0;
        for (int i = 1; i < words.Count; i++)
            if (string.Equals(words[i], words[i - 1], StringComparison.OrdinalIgnoreCase))
                repeats++;
        return repeats * 2 >= words.Count - 1;
    }

    private static string Append(string? existing, string add) =>
        string.IsNullOrEmpty(existing) ? add : existing + " " + add;
}
