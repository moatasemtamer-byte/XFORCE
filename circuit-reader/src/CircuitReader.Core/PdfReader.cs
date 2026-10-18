// PDF reader — uses PdfPig to extract words and line primitives, then
// recognizes Square D ladder elements.
//
// Recognition rules (derived from probing the actual San Martin PDF):
//
//   PLC bit address — split across two words stacked vertically:
//       "0200"   ← column part, ~12pt ABOVE the symbol
//       "| |"    ← the contact (or coil)
//       "01"     ← bit part, ~12pt BELOW the symbol
//     We re-assemble as "0200:01".
//
//   NO contact   = TWO bare "|" words at the same Y, ~5pt apart in X.
//   NC contact   = a single "|/|" word.
//   Coil  ( )    = the literal "( )" word (rendered as "()" sometimes).
//   Latch (L)    = the literal "(L)" word.
//   Unlatch (U)  = the literal "(U)" word.
//   Timer (T)    = the literal "(T)" word.
//
//   Vertical jumpers = line primitives where |x0-x1| < 1 AND height > 5.
//
//   Rung header  = the word "Rung" followed by a digit at similar Y;
//                  marks the top of a rung's drawing band.
//
//   Multi-line label above an element = words in 1-4 stacked rows
//   immediately above (within ~50 pt) and within ~30 pt X of the symbol,
//   joined as "WORD WORD\nWORD WORD\n…".
//
// PDF Y is flipped so top-of-page is smaller Y (matches Python convention).

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CircuitReader.Core;

public sealed record PdfPageData(
    int PageNumber,
    double Width,
    double Height,
    IReadOnlyList<Element> Elements,
    IReadOnlyList<VerticalLine> VerticalLines,
    IReadOnlyList<(string Text, double X, double Y)> Words,
    IReadOnlyList<(int Number, double Y)> RungHeaders,
    IReadOnlyList<string> TextLines,
    string Text);

public static class PdfReader
{
    public static IEnumerable<PdfPageData> ReadPages(string pdfPath)
    {
        using var doc = PdfDocument.Open(pdfPath);
        int idx = 0;
        foreach (var page in doc.GetPages())
        {
            idx++;
            yield return ExtractPage(page, idx);
        }
    }

    private static PdfPageData ExtractPage(Page page, int pageNumber)
    {
        var height = page.Height;
        var width = page.Width;

        // Collect words with flipped Y.
        var words = new List<(string Text, double X, double Y)>();
        var rawWords = new List<(string Text, double Left, double Right, double Mid)>();
        foreach (var w in page.GetWords())
        {
            var bbox = w.BoundingBox;
            var cx = (bbox.Left + bbox.Right) / 2.0;
            var cy = height - (bbox.Top + bbox.Bottom) / 2.0;
            words.Add((w.Text, cx, cy));
            // Keep native PdfPig coordinates (Y increases upward) for the
            // monospace ASCII-grid reconstruction used by text dialects.
            // Group lines by the glyph vertical CENTRE — stable across mixed
            // glyph heights (a '-' or '=' has a different top edge than a
            // letter but nearly the same centre), unlike the top edge.
            rawWords.Add((w.Text, bbox.Left, bbox.Right, (bbox.Top + bbox.Bottom) / 2.0));
        }

        var textLines = BuildTextLines(rawWords);
        var rawText = string.Join("\n", textLines);

        // Collect vertical lines.
        var vlines = new List<VerticalLine>();
        try
        {
            foreach (var path in page.ExperimentalAccess.Paths)
            {
                foreach (PdfSubpath sub in path)
                {
                    foreach (var cmd in sub.Commands)
                    {
                        if (cmd is PdfSubpath.Line line)
                        {
                            var x0 = line.From.X;
                            var y0 = height - line.From.Y;
                            var x1 = line.To.X;
                            var y1 = height - line.To.Y;
                            if (Math.Abs(x0 - x1) < 1.0 && Math.Abs(y1 - y0) > 5.0)
                            {
                                vlines.Add(new VerticalLine
                                {
                                    X = x0,
                                    YTop = Math.Min(y0, y1),
                                    YBottom = Math.Max(y0, y1),
                                });
                            }
                        }
                    }
                }
            }
        }
        catch { /* malformed path stream — degrade to row-based parser */ }
        vlines = MergeCollinearVerticals(vlines);

        // Recognize ladder elements + rung headers.
        var elements = TokenizeElements(words);
        var rungHeaders = FindRungHeaders(words);

        return new PdfPageData(pageNumber, width, height, elements, vlines, words, rungHeaders, textLines, rawText);
    }

    /// <summary>
    /// Reconstruct monospace ASCII text lines from PdfPig words.  Triconex
    /// TRISTATION (and similar) printouts are fixed-pitch ASCII art where
    /// horizontal column position carries meaning (which contact a label
    /// sits over, where a branch jumper lands).  PdfPig's raw text loses
    /// that, so we rebuild a character grid: words are grouped into lines by
    /// their top-edge Y, then each word is placed at
    /// <c>round((Left - pageLeft) / charWidth)</c> with space padding.
    /// </summary>
    private static List<string> BuildTextLines(
        IReadOnlyList<(string Text, double Left, double Right, double Mid)> rawWords)
    {
        var lines = new List<string>();
        if (rawWords.Count == 0) return lines;

        // Estimate the fixed character width from multi-char words.
        var widths = new List<double>();
        foreach (var w in rawWords)
        {
            if (w.Text.Length >= 2)
                widths.Add((w.Right - w.Left) / w.Text.Length);
        }
        double charWidth = widths.Count > 0 ? Median(widths) : 6.0;
        if (charWidth <= 0.1) charWidth = 6.0;

        double pageLeft = rawWords.Min(w => w.Left);

        // Group words into lines by their vertical centre (PdfPig Y increases
        // upward, so larger Mid = higher on the page).
        double lineTol = Math.Max(2.0, charWidth * 0.9);
        var byMid = rawWords.OrderByDescending(w => w.Mid).ToList();
        var groups = new List<List<(string Text, double Left, double Right, double Mid)>>();
        double curMid = double.NaN;
        foreach (var w in byMid)
        {
            if (groups.Count == 0 || Math.Abs(curMid - w.Mid) > lineTol)
            {
                groups.Add(new List<(string, double, double, double)> { w });
                curMid = w.Mid;
            }
            else
            {
                groups[^1].Add(w);
                // Track a running average so a line that drifts slightly in Y
                // doesn't spuriously split partway across the page.
                curMid = (curMid + w.Mid) / 2.0;
            }
        }

        foreach (var g in groups)
        {
            var ordered = g.OrderBy(w => w.Left).ToList();
            var sb = new System.Text.StringBuilder();
            foreach (var w in ordered)
            {
                int col = (int)Math.Round((w.Left - pageLeft) / charWidth);
                if (col < 0) col = 0;
                if (sb.Length < col) sb.Append(' ', col - sb.Length);
                if (sb.Length > col)
                {
                    // Overlap (kerning rounding) — append with a single space
                    // so tokens never glue together and corrupt the grid.
                    if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                }
                sb.Append(w.Text);
            }
            lines.Add(sb.ToString().TrimEnd());
        }
        return lines;
    }

    private static double Median(List<double> xs)
    {
        var s = xs.OrderBy(v => v).ToList();
        int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0;
    }

    private static List<VerticalLine> MergeCollinearVerticals(List<VerticalLine> raw)
    {
        if (raw.Count == 0) return raw;
        var groups = raw.GroupBy(v => Math.Round(v.X * 2) / 2.0);
        var merged = new List<VerticalLine>();
        foreach (var g in groups)
        {
            var sorted = g.OrderBy(v => v.YTop).ToList();
            var curTop = sorted[0].YTop;
            var curBot = sorted[0].YBottom;
            foreach (var v in sorted.Skip(1))
            {
                if (v.YTop <= curBot + 1.0)
                    curBot = Math.Max(curBot, v.YBottom);
                else
                {
                    merged.Add(new VerticalLine { X = g.Key, YTop = curTop, YBottom = curBot });
                    curTop = v.YTop; curBot = v.YBottom;
                }
            }
            merged.Add(new VerticalLine { X = g.Key, YTop = curTop, YBottom = curBot });
        }
        return merged;
    }

    // -- Element recognition ----------------------------------------------

    private static readonly System.Text.RegularExpressions.Regex ColumnAddrPattern =
        new(@"^[A-Z]?\d{3,4}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex BitAddrPattern =
        new(@"^\d{1,2}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static List<Element> TokenizeElements(IReadOnlyList<(string Text, double X, double Y)> words)
    {
        var elements = new List<Element>();
        var consumed = new HashSet<int>();

        // ---- pass 1: NO contacts (two adjacent "|" tokens, ~5pt apart) ----
        for (int i = 0; i < words.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            if (words[i].Text != "|") continue;
            for (int j = i + 1; j < words.Count; j++)
            {
                if (consumed.Contains(j)) continue;
                if (words[j].Text != "|") continue;
                if (Math.Abs(words[i].Y - words[j].Y) > 1.5) continue;
                var dx = Math.Abs(words[i].X - words[j].X);
                if (dx < 3.0 || dx > 9.0) continue;
                var cx = (words[i].X + words[j].X) / 2.0;
                var cy = (words[i].Y + words[j].Y) / 2.0;
                var (addr, label) = ResolveAddressAndLabel(words, cx, cy);
                elements.Add(new Element(ElementKind.NormallyOpen, addr, label, cx, cy));
                consumed.Add(i); consumed.Add(j);
                break;
            }
        }

        // ---- pass 2: split-coil "(" + ")" pair (Square D draws coils
        // as TWO separate words ~6pt apart at the same Y).
        for (int i = 0; i < words.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            if (words[i].Text != "(") continue;
            for (int j = i + 1; j < words.Count; j++)
            {
                if (consumed.Contains(j)) continue;
                if (words[j].Text != ")") continue;
                if (Math.Abs(words[i].Y - words[j].Y) > 1.5) continue;
                var dx = Math.Abs(words[i].X - words[j].X);
                if (dx < 3.0 || dx > 12.0) continue;
                var cx = (words[i].X + words[j].X) / 2.0;
                var cy = (words[i].Y + words[j].Y) / 2.0;
                var (addr, label) = ResolveAddressAndLabel(words, cx, cy);
                elements.Add(new Element(ElementKind.Coil, addr, label, cx, cy));
                consumed.Add(i); consumed.Add(j);
                break;
            }
        }

        // ---- pass 3: LET assignment box ("LET <dst> = <a> x <b> / <c> <round>")
        // Output-class element so the rung treats it as the coil. The address
        // is the destination register, the label captures the full expression
        // so the boolean equation reads as a readable assignment.
        for (int i = 0; i < words.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            if (!string.Equals(words[i].Text, "LET", StringComparison.Ordinal)) continue;
            var letY = words[i].Y;
            // Collect all tokens at the same Y to the right of LET.
            var rowToks = words
                .Select((w, idx) => (Idx: idx, w.Text, w.X, w.Y))
                .Where(t => !consumed.Contains(t.Idx)
                         && Math.Abs(t.Y - letY) < 2.0
                         && t.X >= words[i].X - 1)
                .OrderBy(t => t.X)
                .ToList();
            // Split on the "=" separator: destination register on the left,
            // source expression on the right.  Register operands print as a
            // class-letter + digits pair ("F 0513", "S 0053") that the grid
            // splits into two tokens; re-join them.  Register-class-agnostic,
            // unlike the old "first S-pair" heuristic which dropped F-register
            // destinations (rendering "? = ") and mis-read a source S-operand
            // as the destination on scaling rungs.
            string dstAddr = "?";
            string srcExpr = "";
            var payload = rowToks.Where(t => t.Idx != i).ToList();   // drop "LET"
            int eqPos = payload.FindIndex(t => t.Text == "=");
            if (eqPos > 0)
            {
                var dstJoined = JoinRegisterTokens(payload.Take(eqPos).Select(t => t.Text));
                dstAddr = dstJoined.Length > 0 ? dstJoined.Split(' ')[0] : "?";
                srcExpr = JoinRegisterTokens(payload.Skip(eqPos + 1).Select(t => t.Text));
                foreach (var t in rowToks) consumed.Add(t.Idx);
            }
            // Place the LET element at the box's right edge so it acts as the
            // output coil at the rightmost X like a real coil.
            var rightX = rowToks.Count > 0 ? rowToks[^1].X : words[i].X;
            var label = "LET " + dstAddr + " = " + srcExpr;
            elements.Add(new Element(ElementKind.LetAssign, dstAddr, label, rightX, letY));
        }

        // ---- pass 4: IF comparison box ("IF S<a> ≥ S<b> - <const>")
        // Acts as a compare INPUT element on the rung.
        for (int i = 0; i < words.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            if (!string.Equals(words[i].Text, "IF", StringComparison.Ordinal)) continue;
            var ifY = words[i].Y;
            var rowToks = words
                .Select((w, idx) => (Idx: idx, w.Text, w.X, w.Y))
                .Where(t => !consumed.Contains(t.Idx)
                         && Math.Abs(t.Y - ifY) < 2.5
                         && t.X >= words[i].X - 1
                         && t.X < words[i].X + 200)
                .OrderBy(t => t.X)
                .ToList();
            // Try to assemble:  S <a> <op> S <b> [- <c>]
            string lhsAddr = "?", rhsAddr = "?", op = "ge", konst = "";
            int kEnd = 0;
            for (int k = 0; k < rowToks.Count - 3; k++)
            {
                if (rowToks[k].Text == "S"
                    && System.Text.RegularExpressions.Regex.IsMatch(rowToks[k + 1].Text, @"^\d{3,5}$"))
                {
                    lhsAddr = "S" + rowToks[k + 1].Text;
                    op = ParseCompareOp(rowToks[k + 2].Text);
                    if (k + 4 < rowToks.Count
                        && rowToks[k + 3].Text == "S"
                        && System.Text.RegularExpressions.Regex.IsMatch(rowToks[k + 4].Text, @"^\d{3,5}$"))
                    {
                        rhsAddr = "S" + rowToks[k + 4].Text;
                        kEnd = k + 5;
                        // Optional " - <const>" tail
                        if (kEnd + 1 < rowToks.Count
                            && rowToks[kEnd].Text == "-"
                            && System.Text.RegularExpressions.Regex.IsMatch(rowToks[kEnd + 1].Text, @"^\d+$"))
                        {
                            konst = "_minus_" + rowToks[kEnd + 1].Text;
                            kEnd += 2;
                        }
                        for (int m = k; m < kEnd; m++) consumed.Add(rowToks[m].Idx);
                        break;
                    }
                }
            }
            if (lhsAddr != "?" && rhsAddr != "?")
            {
                consumed.Add(i);   // consume "IF" too
                var addr = $"{lhsAddr}_{op}_{rhsAddr}{konst}";
                var cx = words[i].X;
                elements.Add(new Element(ElementKind.Compare, addr, addr, cx, ifY));
            }
        }

        // ---- pass 5: NC contacts ("|/|"), pre-formed coils, etc. ----
        for (int i = 0; i < words.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            var w = words[i];
            ElementKind? kind = ClassifyToken(w.Text);
            if (kind is null) continue;
            var (addr, label) = ResolveAddressAndLabel(words, w.X, w.Y);
            elements.Add(new Element(kind.Value, addr, label, w.X, w.Y));
            consumed.Add(i);
        }

        return elements;
    }

    /// <summary>
    /// Re-join a register printed as a class-letter token followed by a numeric
    /// token ("F" "0513" → "F0513"), leaving every other token untouched.  Used
    /// to normalise LET destinations/operands that the word-grid splits apart.
    /// </summary>
    private static string JoinRegisterTokens(IEnumerable<string> tokens)
    {
        var toks = tokens.ToList();
        var outp = new List<string>();
        for (int j = 0; j < toks.Count; j++)
        {
            if (j + 1 < toks.Count
                && System.Text.RegularExpressions.Regex.IsMatch(toks[j], @"^[A-Za-z]{1,2}$")
                && System.Text.RegularExpressions.Regex.IsMatch(toks[j + 1], @"^\d{3,5}$"))
            {
                outp.Add(toks[j] + toks[j + 1]);
                j++;
            }
            else
            {
                outp.Add(toks[j]);
            }
        }
        return string.Join(" ", outp);
    }

    /// <summary>Map the comparator glyph to a code-safe identifier suffix.</summary>
    private static string ParseCompareOp(string s) => s switch
    {
        "≥" or ">=" or "GE" => "ge",
        "≤" or "<=" or "LE" => "le",
        ">" or "GT" => "gt",
        "<" or "LT" => "lt",
        "=" or "==" or "EQ" => "eq",
        "≠" or "!=" or "NE" => "ne",
        _ => "ge",
    };

    private static ElementKind? ClassifyToken(string text)
    {
        var t = text.Replace(" ", string.Empty);
        return t switch
        {
            "|/|" => ElementKind.NormallyClosed,
            "()" or "( )" => ElementKind.Coil,
            "(L)" => ElementKind.Latch,
            "(U)" => ElementKind.Unlatch,
            "(T)" => ElementKind.Timer,
            _ => null,
        };
    }

    /// <summary>
    /// Find the two-part PLC address straddling an element AND the
    /// multi-line text label sitting above it.
    /// </summary>
    private static (string Address, string Label) ResolveAddressAndLabel(
        IReadOnlyList<(string Text, double X, double Y)> words,
        double cx, double cy)
    {
        // Address column part: word matching \d{3,4} sitting 5–18pt ABOVE
        // the symbol, within ~6pt X.
        string colPart = string.Empty;
        double colY = double.NaN;
        foreach (var w in words)
        {
            var dy = cy - w.Y;
            if (dy < 5 || dy > 18) continue;
            if (Math.Abs(w.X - cx) > 6) continue;
            if (!ColumnAddrPattern.IsMatch(w.Text)) continue;
            if (colPart == string.Empty
                || Math.Abs(w.X - cx) < Math.Abs(colY - cx))
            {
                colPart = w.Text;
                colY = w.Y;
            }
        }

        // Bit part: word matching \d{1,2} sitting 5–18pt BELOW.
        string bitPart = string.Empty;
        foreach (var w in words)
        {
            var dy = w.Y - cy;
            if (dy < 5 || dy > 18) continue;
            if (Math.Abs(w.X - cx) > 6) continue;
            if (!BitAddrPattern.IsMatch(w.Text)) continue;
            if (bitPart == string.Empty
                || Math.Abs(w.X - cx) < 6)
            {
                bitPart = w.Text;
            }
        }

        var address = (colPart != "" && bitPart != "")
            ? $"{colPart}:{bitPart}"
            : colPart;

        // Multi-line label: walk upward from colY through stacked rows
        // (~12pt apart), collecting words within ~30pt X of the symbol.
        var label = CollectLabelAbove(words, cx, double.IsNaN(colY) ? cy : colY);

        return (address, label);
    }

    private static string CollectLabelAbove(
        IReadOnlyList<(string Text, double X, double Y)> words,
        double cx, double startY)
    {
        const double rowGap = 12.5;
        const double rowTol = 4.0;
        const double xTol = 30.0;
        const int maxRows = 4;

        var lines = new List<string>();
        for (int row = 1; row <= maxRows; row++)
        {
            var targetY = startY - row * rowGap;
            var inRow = words
                .Where(w => Math.Abs(w.Y - targetY) <= rowTol
                         && Math.Abs(w.X - cx) <= xTol
                         && !IsSymbolToken(w.Text))
                .OrderBy(w => w.X)
                .Select(w => w.Text)
                .ToList();
            if (inRow.Count == 0)
            {
                if (lines.Count == 0) continue;   // skip blank gap, keep trying
                break;                            // hit the top of the cluster
            }
            lines.Add(string.Join(" ", inRow));
        }
        // Lines are bottom-up — reverse so the label reads top-down.
        lines.Reverse();
        return string.Join(" / ", lines);
    }

    private static bool IsSymbolToken(string t)
    {
        var s = t.Replace(" ", string.Empty);
        return s == "|" || s == "|/|" || s == "()" || s == "( )"
            || s == "(L)" || s == "(U)" || s == "(T)"
            || s == "(" || s == ")";
    }

    // -- Rung headers ------------------------------------------------------

    private static readonly System.Text.RegularExpressions.Regex IntPattern =
        new(@"^\d{1,4}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static List<(int Number, double Y)> FindRungHeaders(
        IReadOnlyList<(string Text, double X, double Y)> words)
    {
        var headers = new List<(int Number, double Y)>();
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (!w.Text.Equals("Rung", StringComparison.OrdinalIgnoreCase)) continue;
            // Find a digit token ~adjacent in X, same Y band.
            for (int j = 0; j < words.Count; j++)
            {
                if (j == i) continue;
                var c = words[j];
                if (Math.Abs(c.Y - w.Y) > 3.0) continue;
                if (c.X <= w.X || c.X - w.X > 80) continue;
                if (!IntPattern.IsMatch(c.Text)) continue;
                if (int.TryParse(c.Text, out var n))
                {
                    headers.Add((n, w.Y));
                    break;
                }
            }
        }
        // De-dupe + sort top-to-bottom.
        return headers.GroupBy(h => h.Number)
                      .Select(g => g.First())
                      .OrderBy(h => h.Y)
                      .ToList();
    }
}

