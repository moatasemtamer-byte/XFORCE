using System.Text;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// Corpus harness. Walks a directory tree of TriStation .pt2x projects and PLCopen .xml
/// exports, pairs each .pt2x with its most-overlapping XML, labels the .pt2x as
/// export-made (XML is a faithful export of that PT2X: Address == PhysicalAddress) or
/// import-made (addresses were reallocated by the importer), then scores the address
/// engine (TagGen.AssignAddresses) against the PT2X's embedded Tagnames.csv.
///
/// Matching the embedded CSV is NOT proof the project loads; every result is UNVERIFIED
/// until the generated .pt2x is opened in the owner's licensed TriStation.
/// </summary>
public static class TagMatrix
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: tagmatrix <corpusRoot> [outDir]"); return 1; }
        string root = args[1];
        string outDir = args.Length > 2 ? args[2] : Path.Combine(root, "_matrix");
        Directory.CreateDirectory(outDir);

        var pt2xFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".pt2x", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".pt2", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var xmlFiles = Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).EndsWith("_log.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        Console.WriteLine($"corpus: {pt2xFiles.Count} pt2x, {xmlFiles.Count} xml under {root}");

        var xmlCache = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Dictionary<string, string>> GetXml(string p)
        {
            if (!xmlCache.TryGetValue(p, out var v)) { v = TagRecon.ParseXml(File.ReadAllText(p)); xmlCache[p] = v; }
            return v;
        }

        var summary = new StringBuilder();
        summary.AppendLine("pt2x\tpaired_xml\tcsv_tags\txml_tags\toverlap\toverlap_frac\texport_frac\tfull_xmlorder\tfull_csvorder\tdecision_xml\tdecision_csv\tempty_n\tempty_type_ok\tphys_xml\tLABEL");
        var details = new StringBuilder();
        details.AppendLine("pt2x\tkind\ttagname\txml_PhysicalAddress\tpred\tactual");

        foreach (var p2 in pt2xFiles)
        {
            Dictionary<string, string[]> rows; List<string> cols;
            try
            {
                var cf = CompoundFile.Open(p2);
                var st = cf.ListStreams().FirstOrDefault(x => x.Path.EndsWith("Tagnames/Tagnames.csv", StringComparison.OrdinalIgnoreCase));
                if (st == null) { summary.AppendLine(string.Join("\t", Rel(root, p2), "NO-TAGSTREAM", "", "", "", "", "", "", "", "", "", "", "", "", "NO-PAIR")); continue; }
                var lines = Encoding.UTF8.GetString(st.Data).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
                (cols, rows) = TagRecon.ParseCsv(lines);
            }
            catch (Exception ex) { summary.AppendLine(string.Join("\t", Rel(root, p2), "ERR:" + ex.GetType().Name, "", "", "", "", "", "", "", "", "", "NO-PAIR")); continue; }

            int aCol = Col(cols, "Address");
            int pCol = Col(cols, "PhysicalAddress");
            var csvOrder = rows.Keys.ToList();
            var csvNames = new HashSet<string>(csvOrder, StringComparer.OrdinalIgnoreCase);

            // best-pair by name overlap
            string bestXml = null; int bestOv = 0;
            foreach (var xf in xmlFiles)
            {
                var x = GetXml(xf);
                int ov = 0; foreach (var k in csvNames) if (x.ContainsKey(k)) ov++;
                if (ov > bestOv) { bestOv = ov; bestXml = xf; }
            }
            int ovFrac = csvNames.Count == 0 ? 0 : bestOv * 100 / csvNames.Count;

            var xmap = bestXml != null ? GetXml(bestXml) : new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            // export test: PT2X Address vs paired XML PhysicalAddress
            int exportMatch = 0, overlap = 0;
            foreach (var name in csvOrder)
            {
                if (!xmap.TryGetValue(name, out var flat)) continue;
                overlap++;
                if (Eq(rows[name][aCol], flat.GetValueOrDefault("POINT_ASSIGNMENT.PhysicalAddress", ""))) exportMatch++;
            }

            // import rule, XML declaration order and CSV row order
            var xmlOrderList = xmap.Select(kv => (Name: kv.Key,
                Pa: kv.Value.GetValueOrDefault("POINT_ASSIGNMENT.PhysicalAddress", ""),
                DataType: kv.Value.GetValueOrDefault("@type", ""))).ToList();
            var csvOrderList = csvOrder.Select(n => (Name: n,
                Pa: xmap.TryGetValue(n, out var f) ? f.GetValueOrDefault("POINT_ASSIGNMENT.PhysicalAddress", "") : "",
                DataType: xmap.TryGetValue(n, out var f0) ? f0.GetValueOrDefault("@type", "") : "")).ToList();
            var xpred = TagGen.AssignAddresses(xmlOrderList, TagGen.AddrMode.Import);
            var cpred = TagGen.AssignAddresses(csvOrderList, TagGen.AddrMode.Import);

            int impX = 0, impC = 0, emptyN = 0, emptyOk = 0, decX = 0, decC = 0, physOk = 0;
            foreach (var name in csvOrder)
            {
                string pa = xmap.TryGetValue(name, out var f2) ? f2.GetValueOrDefault("POINT_ASSIGNMENT.PhysicalAddress", "") : "";
                string dt = xmap.TryGetValue(name, out var f2b) ? f2b.GetValueOrDefault("@type", "") : "";
                string act = rows[name][aCol];
                if (Eq(xpred.GetValueOrDefault(name, act), act)) impX++;
                if (Eq(cpred.GetValueOrDefault(name, act), act)) impC++;

                // Decision accuracy = address CLASS (physical / MBN / MIN), independent of numbering.
                string actualClass = Cls(act);
                if (Cls(cpred.GetValueOrDefault(name, "")) == actualClass) decC++;
                if (Cls(xpred.GetValueOrDefault(name, "")) == actualClass) decX++;

                if (pCol >= 0)
                {
                    string want = TagGen.DerivePhysicalAddress(
                        xpred.GetValueOrDefault(name, ""), xmap.TryGetValue(name, out var fq) ? fq.GetValueOrDefault("POINT_ASSIGNMENT.BinNumber", "") : "");
                    if (Eq(want, rows[name][pCol])) physOk++;
                }

                // empty-address case: XML gives none; allocated by DataType (BOOL->MBN else MIN)
                if (pa.Length == 0)
                {
                    emptyN++;
                    string want = dt.Equals("BOOL", StringComparison.OrdinalIgnoreCase) ? "MBN." : "MIN.";
                    if (act.StartsWith(want, StringComparison.OrdinalIgnoreCase)) emptyOk++;
                }
            }

            string label = bestXml == null || bestOv * 100 / Math.Max(1, csvNames.Count) < 50 ? "NO-PAIR"
                : (overlap > 0 && exportMatch == overlap && bestOv * 100 / Math.Max(1, csvNames.Count) >= 90 ? "export-made" : "import-made");

            summary.AppendLine(string.Join("\t", Rel(root, p2), bestXml == null ? "" : Rel(root, bestXml),
                csvNames.Count, xmap.Count, overlap,
                bestOv * 100 / Math.Max(1, csvNames.Count) + "%",
                overlap == 0 ? "-" : (exportMatch * 100 / overlap) + "%",
                $"{impX}/{csvOrder.Count}", $"{impC}/{csvOrder.Count}", $"{decX}/{csvOrder.Count}", $"{decC}/{csvOrder.Count}", emptyN, emptyOk,
                pCol >= 0 ? $"{physOk}/{csvOrder.Count}" : "-", label));

            // detail rows: every row where the deployed mode disagrees with the PT2X's CSV.
            //   export-made -> export mode (Address = PhysicalAddress)
            //   import-made -> import mode (default), assigned in XML declaration order
            foreach (var name in csvOrder)
            {
                string pa = xmap.TryGetValue(name, out var f3) ? f3.GetValueOrDefault("POINT_ASSIGNMENT.PhysicalAddress", "") : "";
                string act = rows[name][aCol];
                if (label == "export-made")
                {
                    if (!Eq(act, pa)) details.AppendLine($"{Rel(root, p2)}\tEXPORT\t{name}\t{pa}\t{pa}\t{act}");
                }
                else
                {
                    string px = xpred.GetValueOrDefault(name, act);
                    if (!Eq(px, act)) details.AppendLine($"{Rel(root, p2)}\tIMPORT\t{name}\t{pa}\t{px}\t{act}");
                }
            }
        }

        File.WriteAllText(Path.Combine(outDir, "summary.tsv"), summary.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "mismatches.tsv"), details.ToString(), new UTF8Encoding(false));
        Console.WriteLine(summary.ToString());
        Console.WriteLine($"Wrote {Path.Combine(outDir, "summary.tsv")}");
        Console.WriteLine($"Wrote {Path.Combine(outDir, "mismatches.tsv")}");
        return 0;
    }

    private static string Cls(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return "?";
        if (addr.StartsWith("MBN.", StringComparison.OrdinalIgnoreCase)) return "MBN";
        if (addr.StartsWith("MIN.", StringComparison.OrdinalIgnoreCase)) return "MIN";
        return "P";
    }

    private static string Rel(string root, string p) => p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p.Substring(root.Length).TrimStart('\\', '/') : p;
    private static int Col(List<string> cols, string name) { for (int i = 0; i < cols.Count; i++) if (cols[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
    private static bool Eq(string a, string b) => (a ?? "").Equals(b ?? "", StringComparison.OrdinalIgnoreCase);
}
