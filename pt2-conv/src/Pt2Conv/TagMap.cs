using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// PHASE-2: reverse-engineers the mapping from a PLCopen XML &lt;ts:TAGNAME&gt; block to a
/// Tagnames.csv row. For tags present in both the XML and a reference .pt2x, reports
/// per-column match rates. Read-only; the mapping is data, not assumed.
/// </summary>
public static class TagMap
{
    // CSV column -> flattened XML key ("Elem.Attr" or "Elem.#text")
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tagname"] = "@name",
        ["DataType"] = "@type",
        ["Group1"] = "TAG_DECLARATION.Group1",
        ["Group2"] = "TAG_DECLARATION.Group2",
        ["Description"] = "@documentation",
        ["InitialValue"] = "@initial",
        ["AliasType"] = "POINT_ASSIGNMENT.AliasType",
        ["Location"] = "POINT_ASSIGNMENT.AliasNumber",
        ["Address"] = "POINT_ASSIGNMENT.PhysicalAddress",
        ["PhysicalAddress"] = "POINT_ASSIGNMENT.PhysicalAddress",
        ["MinimumValue"] = "TAG_SCALING.MinSpan",
        ["MaximumValue"] = "TAG_SCALING.MaxSpan",
        ["Precision"] = "TAG_SCALING.DisplayPrecision",
        ["DisableScaling"] = "TAG_SCALING.DisableScaling",
        ["StateNameTRUE"] = "TAG_DISPLAY.TrueStateName",
        ["ColorTRUE"] = "TAG_DISPLAY.TrueStateColor",
        ["StateNameFALSE"] = "TAG_DISPLAY.FalseStateName",
        ["ColorFALSE"] = "TAG_DISPLAY.FalseStateColor",
        ["Area"] = "TAG_LOCATION.AreaName",
        ["Equipment"] = "TAG_LOCATION.Equipment",
        ["SIFName"] = "TAG_LOCATION.SIFName",
        ["IOModuleType"] = "IO_MODULE_PARAMS.IoModuleType",
        ["MinimumTriggerTime"] = "SDI_PARAMS.MinimumTriggerTime",
        ["SOEDeadTime"] = "SDI_PARAMS.SoeDeadTime",
        ["R_Adjust"] = "SDI_PARAMS.RAdjust",
        ["ShortedThreshold"] = "SDO_PARAMS.ShortedThreshold",
        ["OpenThreshold"] = "SDO_PARAMS.OpenThreshold",
        ["AI_FilterType"] = "AI_PARAMS.FilterType",
        ["AI_ClampEngineeringVal"] = "AI_PARAMS.ClampEngValue",
        ["AI_FieldFaultMonitor"] = "AI_PARAMS.FldFaultMon",
        ["AI_NamurHighLimit"] = "AI_PARAMS.FldFaultHiLimit",
        ["AI_NamurLowLimit"] = "AI_PARAMS.FldFaultLoLimit",
        ["AI_MinimumRawValue"] = "AI_PARAMS.MinRawValue",
        ["AI_MaximumRawValue"] = "AI_PARAMS.MaxRawValue",
        ["AI_MinimumEngValue"] = "AI_PARAMS.MinEngValue",
        ["AI_MaximumEngValue"] = "AI_PARAMS.MaxEngValue",
        ["HART_Enable"] = "HART_PARAMS.Enable",
        ["HART_MonitorDeviceHealth"] = "HART_PARAMS.MonDeviceHealth",
        ["HART_MonitorConfigChanges"] = "HART_PARAMS.MonConfigChanges",
        ["HART_LockConfiguration"] = "HART_PARAMS.LockConfig",
        ["Class"] = "POINT_ASSIGNMENT.TagClass",
        ["SOEFlags"] = "SOE_BLOCKS_ASSIGNMENT.#text",
        ["PI_PointType"] = "PI_PARAMS.PIPointType",
        ["PI_PointGearTeeth"] = "PI_PARAMS.GearTeeth",
        ["PI_PointGearRatio"] = "PI_PARAMS.GearRatio",
    };

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: tagmap <xml> <file.pt2x>"); return 1; }
        var xmlTags = ParseXmlTags(args[1]);
        Console.WriteLine($"XML tags with <ts:TAGNAME>: {xmlTags.Count}");

        var cf = CompoundFile.Open(args[2]);
        var s = cf.ListStreams().First(x => x.Path.Equals("Current/Tagnames/Tagnames.csv", StringComparison.OrdinalIgnoreCase));
        var lines = Encoding.UTF8.GetString(s.Data).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var cols = SplitCsv(lines[1]);

        var csv = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 2; i < lines.Count; i++)
            if (lines[i].StartsWith("\"")) { var f = SplitCsv(lines[i]); csv[f[0]] = f; }

        var shared = csv.Keys.Where(xmlTags.ContainsKey).ToList();
        Console.WriteLine($"CSV tags: {csv.Count}   shared: {shared.Count}");
        Console.WriteLine();

        Console.WriteLine($"{"CSV column",-28} {"match",6}  {"total",6}  source");
        foreach (var col in cols)
        {
            if (!Map.TryGetValue(col, out var key)) { Console.WriteLine($"{col,-28} {"-",6}  {"-",6}  (no mapping)"); continue; }
            int ok = 0, tot = 0;
            foreach (var name in shared)
            {
                if (!xmlTags[name].TryGetValue(key, out var xv)) continue;
                tot++;
                if (Eq(csv[name][Index(cols, col)], xv)) ok++;
            }
            Console.WriteLine($"{col,-28} {ok,6}  {tot,6}  {key}");
        }
        return 0;
    }

    public static int RunPairs(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: tagpairs <xml> <file.pt2x> <csvColumn> [xmlKey]"); return 1; }
        var xmlTags = ParseXmlTags(args[1]);
        var cf = CompoundFile.Open(args[2]);
        var s = cf.ListStreams().First(x => x.Path.Equals("Current/Tagnames/Tagnames.csv", StringComparison.OrdinalIgnoreCase));
        var lines = Encoding.UTF8.GetString(s.Data).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var cols = SplitCsv(lines[1]);
        string col = args[3];
        string key = args.Length >= 5 ? args[4] : (Map.TryGetValue(col, out var k) ? k : col);
        int ci = Index(cols, col);

        var pairs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(2))
        {
            if (!line.StartsWith("\"")) continue;
            var f = SplitCsv(line);
            string name = f[0];
            if (!xmlTags.TryGetValue(name, out var flat)) continue;
            string xv = flat.TryGetValue(key, out var v) ? v : "(absent)";
            string cv = ci >= 0 && ci < f.Count ? f[ci] : "";
            string pk = "csv='" + cv + "'  <-  xml='" + xv + "'";
            pairs[pk] = pairs.TryGetValue(pk, out var n) ? n + 1 : 1;
        }
        Console.WriteLine($"{col}  (key={key})  distinct pairs={pairs.Count}");
        foreach (var kv in pairs.OrderByDescending(p => p.Value).Take(25))
            Console.WriteLine($"{kv.Value,6}  {kv.Key}");
        return 0;
    }

    private static bool Eq(string csv, string xml)
    {
        csv ??= ""; xml ??= "";
        if (csv.Equals(xml, StringComparison.OrdinalIgnoreCase)) return true;
        if (decimal.TryParse(csv, NumberStyles.Any, CultureInfo.InvariantCulture, out var a) &&
            decimal.TryParse(xml, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
            return a == b;
        return csv.Trim().Equals(xml.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Index(List<string> cols, string name)
    {
        for (int i = 0; i < cols.Count; i++) if (cols[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseXmlTags(string xmlPath)
    {
        string t = File.ReadAllText(xmlPath);
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(t, "(?s)<variable\\s+name=\"([^\"]+)\"\\s*>(.*?)</variable>"))
        {
            string name = m.Groups[1].Value;
            string body = m.Groups[2].Value;
            var tn = Regex.Match(body, "(?s)<ts:TAGNAME>(.*?)</ts:TAGNAME>");
            if (!tn.Success) continue;

            var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            flat["@name"] = name;
            var tm = Regex.Match(body, "<type>\\s*<([A-Za-z_]\\w*)");
            flat["@type"] = tm.Success ? tm.Groups[1].Value : "";
            var doc = Regex.Match(body, "(?s)<documentation><!\\[CDATA\\[(.*?)\\]\\]></documentation>");
            flat["@documentation"] = doc.Success ? doc.Groups[1].Value : "";
            var iv = Regex.Match(body, "<simpleValue\\s+value=\"([^\"]*)\"");
            flat["@initial"] = iv.Success ? iv.Groups[1].Value : "";

            foreach (Match e in Regex.Matches(tn.Groups[1].Value, "<ts:([A-Za-z_]\\w*)([^>]*?)(/?)>"))
            {
                string elem = e.Groups[1].Value, attrs = e.Groups[2].Value;
                foreach (Match a in Regex.Matches(attrs, "([A-Za-z_]\\w*)=\"([^\"]*)\""))
                    flat[elem + "." + a.Groups[1].Value] = a.Groups[2].Value;
                if (e.Groups[3].Value != "/")
                {
                    var txt = Regex.Match(tn.Groups[1].Value, $"<ts:{elem}[^>]*>([^<]*)</ts:{elem}>");
                    if (txt.Success) flat[elem + ".#text"] = txt.Groups[1].Value;
                }
            }
            result[name] = flat;
        }
        return result;
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
