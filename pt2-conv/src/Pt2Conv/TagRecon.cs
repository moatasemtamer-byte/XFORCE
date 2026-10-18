using System.Text;
using System.Text.RegularExpressions;

namespace Pt2Conv;

/// <summary>
/// Reverse-engineering harness: for each column of a reference Tagnames.csv, rank the XML
/// keys (element.attr / element.#text / @type / @documentation / @initial) that best predict
/// the tool's value, by purity (each distinct XML value maps to one tool value) and coverage.
/// </summary>
public static class TagRecon
{
    public static int RunDump(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: tagrecon-dump <xml> <csv> <out.tsv>"); return 1; }
        var xml = ParseXml(File.ReadAllText(args[1]));
        var (cols, rows) = ParseCsv(File.ReadAllLines(args[2]));

        string[] xmlKeys = { "@name", "@type",
            "POINT_ASSIGNMENT.PhysicalAddress", "POINT_ASSIGNMENT.BinNumber", "POINT_ASSIGNMENT.BinOffset",
            "POINT_ASSIGNMENT.AliasNumber", "POINT_ASSIGNMENT.AliasNumberExt", "POINT_ASSIGNMENT.PointType",
            "POINT_ASSIGNMENT.AliasCategory", "POINT_ASSIGNMENT.AliasType", "POINT_ASSIGNMENT.TagClass",
            "TAG_DECLARATION.Application", "TAG_DECLARATION.Group1", "TAG_DECLARATION.Group2",
            "IO_MODULE_PARAMS.IoModuleType", "SOE_BLOCKS_ASSIGNMENT.#text", "TAG_DISPLAY.TrueStateName" };
        string[] csvCols = { "Tagname", "DataType", "Group1", "Group2", "Description", "IsSafety", "IsControl",
            "IsMemory", "IsInput", "IsOutput", "AliasNumberSelection", "UserAliasLegacy", "UserAliasExtended",
            "AliasType", "Location", "Address", "PhysicalAddress", "Class", "ProgramsList" };

        var sb = new StringBuilder();
        sb.Append("name\t");
        sb.Append(string.Join("\t", xmlKeys.Select(w => w.Replace("POINT_ASSIGNMENT.", "PA.").Replace("TAG_DECLARATION.", "TD.").Replace("IO_MODULE_PARAMS.", "IO.").Replace("SOE_BLOCKS_ASSIGNMENT.", "SOE.").Replace("TAG_DISPLAY.", "DISP."))));
        sb.Append('\t');
        sb.Append(string.Join("\t", csvCols));
        sb.Append('\n');

        int n = 0;
        foreach (var kv in rows)
        {
            string name = kv.Key;
            if (!xml.TryGetValue(name, out var flat)) continue;
            sb.Append(name).Append('\t');
            foreach (var w in xmlKeys) { sb.Append(flat.TryGetValue(w, out var v) ? v : "").Append('\t'); }
            foreach (var c in csvCols) { int ci = Col(cols, c); sb.Append(ci >= 0 && ci < kv.Value.Length ? kv.Value[ci] : "").Append('\t'); }
            sb.Append('\n');
            n++;
        }
        File.WriteAllText(args[3], sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Wrote {args[3]} ({n} rows)");
        return 0;
    }

    private static int Col(List<string> cols, string name)
    {
        for (int i = 0; i < cols.Count; i++) if (cols[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: tagrecon <xml> <tool.csv> [out.txt]"); return 1; }
        string xmlPath = args[1], csvPath = args[2];
        string outPath = args.Length > 3 ? args[3] : csvPath + ".recon.txt";

        var xml = ParseXml(File.ReadAllText(xmlPath));
        var (cols, rows) = ParseCsv(File.ReadAllLines(csvPath));

        var shared = rows.Where(r => xml.ContainsKey(r.Key)).ToList();
        var onlyXml = xml.Keys.Except(rows.Keys).ToList();
        var onlyCsv = rows.Keys.Except(xml.Keys).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"XML ts:TAGNAME tags = {xml.Count}");
        sb.AppendLine($"CSV rows = {rows.Count}  columns = {cols.Count}");
        sb.AppendLine($"shared = {shared.Count}  only-xml = {onlyXml.Count}  only-csv = {onlyCsv.Count}");
        if (onlyCsv.Count > 0) sb.AppendLine("  only-csv sample: " + string.Join(", ", onlyCsv.Take(30)));
        if (onlyXml.Count > 0) sb.AppendLine("  only-xml sample: " + string.Join(", ", onlyXml.Take(30)));

        var keyUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in xml) foreach (var k in kv.Value.Keys) keyUnion.Add(k);

        for (int ci = 0; ci < cols.Count; ci++)
        {
            string column = cols[ci];
            var distinct = shared.Select(r => r.Value[ci]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            sb.AppendLine();
            sb.AppendLine($"=== {column}  (distinct tool values = {distinct.Count})");
            if (distinct.Count == 1)
            {
                sb.AppendLine($"    CONSTANT = {Trunc(distinct[0])}");
                continue;
            }

            var ranked = new List<(string key, double score, int cov, int pure, Dictionary<string, HashSet<string>> map)>();
            foreach (var key in keyUnion) ranked.Add(Score(key, shared, xml, ci));

            foreach (var (key, score, cov, pure, map) in ranked.OrderByDescending(x => x.Item2).Take(6))
            {
                string sample = string.Join(", ", map.Take(4)
                    .Select(kv => $"{Trunc(kv.Key)}=>[{string.Join("|", kv.Value.Take(3).Select(Trunc))}]"));
                sb.AppendLine($"    {key,-40} score={score:F3} cov={cov}/{shared.Count} pureVals={pure}  {sample}");
            }
        }

        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Wrote {outPath}");
        return 0;
    }

    private static (string, double, int, int, Dictionary<string, HashSet<string>>) Score(
        string key, List<KeyValuePair<string, string[]>> shared,
        Dictionary<string, Dictionary<string, string>> xml, int ci)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        int cov = 0;
        foreach (var r in shared)
        {
            if (!xml[r.Key].TryGetValue(key, out var xv)) continue;
            cov++;
            if (!map.TryGetValue(xv, out var set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); map[xv] = set; }
            set.Add(r.Value[ci]);
        }
        int pure = map.Values.Count(set => set.Count == 1);
        double purity = map.Count == 0 ? 0 : (double)pure / map.Count;
        double coverage = (double)cov / Math.Max(1, shared.Count);
        return (key, purity * coverage, cov, pure, map);
    }

    private static string Trunc(string s) => s == null ? "" : (s.Length > 40 ? s.Substring(0, 40) + "..." : s);

    public static Dictionary<string, Dictionary<string, string>> ParseXml(string t)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(t, "<ts:TAGNAME"))
        {
            int vstart = t.LastIndexOf("<variable ", m.Index, StringComparison.Ordinal);
            if (vstart < 0) continue;
            var nm = Regex.Match(t.Substring(vstart, Math.Min(200, t.Length - vstart)), "<variable\\s+name=\"([^\"]+)\"");
            if (!nm.Success) continue;
            string name = nm.Groups[1].Value;

            int vend = t.IndexOf("</variable>", m.Index, StringComparison.Ordinal);
            if (vend < 0) continue;
            string body = t.Substring(vstart, vend - vstart);
            string inner = t.Substring(m.Index, vend - m.Index);

            var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["@name"] = name };

            var ty = Regex.Match(body, "<type>\\s*<([A-Za-z_][\\w.:]*)");
            if (ty.Success) flat["@type"] = ty.Groups[1].Value;

            var doc = Regex.Match(body, "(?s)<documentation><!\\[CDATA\\[(.*?)\\]\\]></documentation>");
            flat["@documentation"] = doc.Success ? doc.Groups[1].Value : "";

            var iv = Regex.Match(body, "<simpleValue\\s+value=\"([^\"]*)\"");
            flat["@initial"] = iv.Success ? iv.Groups[1].Value : "";

            foreach (Match e in Regex.Matches(inner, "<ts:([A-Za-z_]\\w*)((?:[^>\"']|\"[^\"]*\")*?)(/?)>"))
            {
                string elem = e.Groups[1].Value;
                foreach (Match a in Regex.Matches(e.Groups[2].Value, "([A-Za-z_]\\w*)=\"([^\"]*)\""))
                    flat[elem + "." + a.Groups[1].Value] = a.Groups[2].Value;
                if (e.Groups[3].Value != "/")
                {
                    string close = $"</ts:{elem}>";
                    int ce = inner.IndexOf(close, e.Index + e.Length, StringComparison.Ordinal);
                    if (ce >= 0) flat[elem + ".#text"] = inner.Substring(e.Index + e.Length, ce - (e.Index + e.Length)).Trim();
                }
            }
            result[name] = flat;
        }
        return result;
    }

    public static (List<string> cols, Dictionary<string, string[]> rows) ParseCsv(string[] lines)
    {
        var cols = SplitCsv(lines[1]);
        var rows = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 2; i < lines.Length; i++)
        {
            if (lines[i].Length == 0 || lines[i][0] != '"') continue;
            var f = SplitCsv(lines[i]);
            if (f.Count > 0) rows[f[0]] = f.ToArray();
        }
        return (cols, rows);
    }

    private static List<string> SplitCsv(string line)
    {
        var outp = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
            else { if (c == '"') q = true; else if (c == ',') { outp.Add(sb.ToString()); sb.Clear(); } else sb.Append(c); }
        }
        outp.Add(sb.ToString());
        return outp;
    }
}
