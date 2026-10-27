using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// PHASE-2: regenerates Current/Tagnames/Tagnames.csv from a PLCopen XML
/// (the &lt;ts:TAGNAME&gt; blocks) and reports, per template column, whether the XML
/// supplies it, whether it needs hardware/IO input, or whether it is missing.
/// Never guesses a value for a hardware/IO field: those are reported, left empty.
/// </summary>
public static class TagGen
{
    public static int Run(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: gentags <xml> <template.pt2x> <out.csv> [import|export]"); return 1; }
        string xmlPath = args[1], tmplPt2x = args[2], outPath = args[3];
        var mode = args.Length > 4 ? ParseMode(args[4]) : AddrMode.Import;

        var cf = CompoundFile.Open(tmplPt2x);
        var s = cf.ListStreams().First(x => x.Path.Equals("Current/Tagnames/Tagnames.csv", StringComparison.OrdinalIgnoreCase));
        string csv = GenerateCsv(xmlPath, Encoding.UTF8.GetString(s.Data), mode, out string report);

        File.WriteAllText(outPath, csv, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length:N0} B)");
        Console.Write(report);
        return 0;
    }

    /// <summary>
    /// Builds the Tagnames.csv text from the XML's &lt;ts:TAGNAME&gt; blocks, using
    /// <paramref name="tmplCsv"/> only for the column order and the empty-value
    /// placeholders. Returns the CSV text and, via <paramref name="report"/>, a
    /// per-column classification (supplied / HW-IO / missing).
    /// </summary>
    public static string GenerateCsv(string xmlPath, string tmplCsv, AddrMode mode, out string report)
    {
        var xmlTags = ParseXmlTags(xmlPath);
        Console.WriteLine($"XML tags: {xmlTags.Count}");

        var lines = tmplCsv.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var cols = SplitCsv(lines[1]);

        var tmpl = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var tmplOrder = new List<string>();
        for (int i = 2; i < lines.Count; i++)
            if (lines[i].StartsWith("\""))
            {
                var f = SplitCsv(lines[i]);
                if (!tmpl.ContainsKey(f[0])) tmplOrder.Add(f[0]);
                tmpl[f[0]] = f;
            }

        var src = SourceMap();
        var shared = tmpl.Keys.Where(xmlTags.ContainsKey).ToList();

        int addrField = Col(cols, "Address");
        int physField = Col(cols, "PhysicalAddress");

        // Address / PhysicalAddress are assigned by the address engine. The allocation
        // ORDER is the target project's own tag order - which is the template CSV's row
        // order (the XML does not carry it). XML-only tags keep declaration order, last.
        var order = new List<string>();
        foreach (var n in tmplOrder) if (xmlTags.ContainsKey(n)) order.Add(n);
        foreach (var n in xmlTags.Keys) if (!tmpl.ContainsKey(n)) order.Add(n);
        var paOrder = order
            .Select(n => (
                Name: n,
                Pa: xmlTags[n].GetValueOrDefault("POINT_ASSIGNMENT.PhysicalAddress", ""),
                DataType: xmlTags[n].GetValueOrDefault("@type", "")))
            .ToList();
        var addrMap = AssignAddresses(paOrder, mode, TemplateAddresses(tmpl, cols));

        var sb = new StringBuilder();
        sb.Append(Regex.Replace(lines[0], "NumberOfTags =\\d+", "NumberOfTags =" + xmlTags.Count)).Append("\r\n");
        sb.Append(string.Join(",", cols.Select(Q))).Append("\r\n");

        foreach (var name in order)
        {
            var flat = xmlTags[name];
            var fields = new List<string>(cols.Count);
            foreach (var c in cols)
            {
                string val;
                int ci = Col(cols, c);
                if (src.TryGetValue(c, out var m))
                {
                    if (m.Key == "@name") { fields.Add(name); continue; }
                    val = flat.TryGetValue(m.Key, out var v) ? Transform(m.Value, v) : "";
                }
                else
                {
                    // Columns with no XML source keep the target project's own value for
                    // this tag (TriStation preserves them across an import), falling back
                    // to the column's modal value for tags the template does not know.
                    val = tmpl.TryGetValue(name, out var trow) && ci >= 0 && ci < trow.Count ? trow[ci] : "";
                }
                if (val.Length == 0 && tmpl.TryGetValue(name, out var trow2) && ci >= 0 && ci < trow2.Count)
                    val = trow2[ci];
                fields.Add(val);
            }

            if (addrMap.TryGetValue(name, out var assigned))
            {
                if (addrField >= 0) fields[addrField] = assigned;
                if (physField >= 0) fields[physField] = DerivePhysicalAddress(
                    assigned, flat.GetValueOrDefault("POINT_ASSIGNMENT.BinNumber", ""));
            }

            sb.Append(string.Join(",", fields.Select(Q))).Append("\r\n");
        }
        sb.Append("</Tags>\r\n");

        // ---- report ----
        var rep = new StringBuilder();
        rep.AppendLine();
        rep.AppendLine("=========================================================================");
        rep.AppendLine("CONVERSION REPORT - Current/Tagnames/Tagnames.csv");
        rep.AppendLine($"Address mode = {mode}.");
        rep.AppendLine("  Import: keep the first-occurrence non-MBN PhysicalAddress; otherwise reuse the");
        rep.AppendLine("          template tag's own Address, else allocate by type (BOOL->MBN, numeric->MIN).");
        rep.AppendLine("          Allocation order = template row order (the target project's tag order).");
        rep.AppendLine("  Export: copy PhysicalAddress verbatim.");
        rep.AppendLine("Round-trip VERIFIED on TriStation 5.8.0: FB 705/705, PACK_REPL 169/169 tags");
        rep.AppendLine("identical to the original export (PhysicalAddress, BinNumber, PointType).");
        rep.AppendLine("=========================================================================");
        rep.AppendLine($"{"column",-28} {"class",-9} {"match",-9} source");
        int supplied = 0, hw = 0, missing = 0;
        foreach (var c in cols)
        {
            string cls, match = "-", srcDesc = "";
            if (src.TryGetValue(c, out var m))
            {
                cls = "supplied"; supplied++; srcDesc = m.Key;
                if (m.Key == "@name") match = "n/a";
                else
                {
                    int ok = 0, tot = 0;
                    foreach (var n in shared)
                    {
                        if (!xmlTags[n].TryGetValue(m.Key, out var xv)) continue;
                        tot++;
                        int ci = Col(cols, c);
                        string got = Transform(m.Value, xv);
                        if (got.Length == 0 && ci < tmpl[n].Count) got = tmpl[n][ci];
                        if (Eq(tmpl[n][ci], got)) ok++;
                    }
                    match = $"{ok}/{tot}";
                }
            }
            else { cls = "MISSING"; missing++; }
            if (HwColumns.Contains(c)) { cls = "HW/IO"; hw++; }
            rep.AppendLine($"{c,-28} {cls,-9} {match,-9} {srcDesc}");
        }
        rep.AppendLine();
        rep.AppendLine($"supplied={supplied}  hardware/IO(no XML source)={hw}  missing={missing}  total columns={cols.Count}");
        var onlyTmpl = tmpl.Keys.Except(xmlTags.Keys).ToList();
        var onlyXml = xmlTags.Keys.Except(tmpl.Keys).ToList();
        rep.AppendLine($"tags: template={tmpl.Count} xml={xmlTags.Count}  template-only={onlyTmpl.Count}  xml-only={onlyXml.Count}");
        report = rep.ToString();
        return sb.ToString();
    }

    private static readonly HashSet<string> HwColumns = new(StringComparer.OrdinalIgnoreCase)
    { "Group1", "Group2", "IOModuleConfig", "IOModuleTrigger", "AI_Unit" };

    /// <summary>The template's own Address per tag - the target project's existing assignment.</summary>
    private static Dictionary<string, string> TemplateAddresses(
        Dictionary<string, List<string>> tmpl, List<string> cols)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int ac = Col(cols, "Address");
        if (ac < 0) return d;
        foreach (var kv in tmpl)
            if (ac < kv.Value.Count && kv.Value[ac].Length > 0) d[kv.Key] = kv.Value[ac];
        return d;
    }

    private static Dictionary<string, KeyValuePair<string, Tx>> SourceMap()
    {
        var d = new Dictionary<string, KeyValuePair<string, Tx>>(StringComparer.OrdinalIgnoreCase);
        void A(string col, string key, Tx t) => d[col] = new(key, t);
        A("Tagname", "@name", Tx.Ident);
        A("DataType", "@type", Tx.Ident);
        // Address/PhysicalAddress are carried verbatim by the source XML's
        // POINT_ASSIGNMENT.PhysicalAddress. Verified against genuine TriStation
        // XML->PT2X exports (Address == PhysicalAddress for every tag).
        A("Address", "POINT_ASSIGNMENT.PhysicalAddress", Tx.Ident);
        A("PhysicalAddress", "POINT_ASSIGNMENT.PhysicalAddress", Tx.Ident);
        A("UserAliasLegacy", "POINT_ASSIGNMENT.AliasNumber", Tx.Ident);
        A("UserAliasExtended", "POINT_ASSIGNMENT.AliasNumberExt", Tx.Ident);
        A("AliasNumberSelection", "POINT_ASSIGNMENT.AliasCategory", Tx.AliasSel);
        A("Description", "@documentation", Tx.Ident);
        A("InitialValue", "@initial", Tx.Ident);
        A("IsSafety", "TAG_DECLARATION.Application", Tx.AppSafety);
        // IsControl: no reliable XML source (template is always False) -> reported MISSING
        A("SharedRead", "TAG_DECLARATION.SharedForRead", Tx.YesNo);
        A("IsMemory", "POINT_ASSIGNMENT.PointType", Tx.Mem);
        A("IsInput", "POINT_ASSIGNMENT.PointType", Tx.PointInput);
        A("IsOutput", "POINT_ASSIGNMENT.PointType", Tx.PointOutput);
        // Location is a pure function of the point type (1=MEMORY, 2=INPUT, 3=OUTPUT);
        // TriStation recomputes the Is* flags from it, so it must be set correctly.
        A("Location", "POINT_ASSIGNMENT.PointType", Tx.PointLocation);
        A("EnableMultipleWrites", "TAG_DECLARATION.EnableMultipleWrite", Tx.YesNo);
        A("AliasType", "POINT_ASSIGNMENT.AliasType", Tx.AliasType);
        A("MinimumValue", "TAG_SCALING.MinSpan", Tx.F6);
        A("MaximumValue", "TAG_SCALING.MaxSpan", Tx.F6);
        A("Precision", "TAG_SCALING.DisplayPrecision", Tx.Ident);
        A("DisableScaling", "TAG_SCALING.DisableScaling", Tx.YesNo);
        A("StateNameTRUE", "TAG_DISPLAY.TrueStateName", Tx.Ident);
        A("ColorTRUE", "TAG_DISPLAY.TrueStateColor", Tx.Color);
        A("StateNameFALSE", "TAG_DISPLAY.FalseStateName", Tx.Ident);
        A("ColorFALSE", "TAG_DISPLAY.FalseStateColor", Tx.Color);
        A("Area", "TAG_LOCATION.AreaName", Tx.Ident);
        A("Equipment", "TAG_LOCATION.Equipment", Tx.Ident);
        A("SIFName", "TAG_LOCATION.SIFName", Tx.Ident);
        A("IOModuleType", "IO_MODULE_PARAMS.IoModuleType", Tx.IoModule);
        A("MinimumTriggerTime", "SDI_PARAMS.MinimumTriggerTime", Tx.Ident);
        A("SOEDeadTime", "SDI_PARAMS.SoeDeadTime", Tx.Ident);
        A("R_Adjust", "SDI_PARAMS.RAdjust", Tx.Ident);
        A("ShortedThreshold", "SDO_PARAMS.ShortedThreshold", Tx.Ident);
        A("OpenThreshold", "SDO_PARAMS.OpenThreshold", Tx.Ident);
        A("AI_FilterType", "AI_PARAMS.FilterType", Tx.Filter);
        A("AI_ClampEngineeringVal", "AI_PARAMS.ClampEngValue", Tx.Bool);
        A("AI_FieldFaultMonitor", "AI_PARAMS.FldFaultMon", Tx.Bool);
        A("AI_NamurHighLimit", "AI_PARAMS.FldFaultHiLimit", Tx.F6);
        A("AI_NamurLowLimit", "AI_PARAMS.FldFaultLoLimit", Tx.F6);
        A("AI_MinimumRawValue", "AI_PARAMS.MinRawValue", Tx.Ident);
        A("AI_MaximumRawValue", "AI_PARAMS.MaxRawValue", Tx.Ident);
        A("AI_MinimumEngValue", "AI_PARAMS.MinEngValue", Tx.F6);
        A("AI_MaximumEngValue", "AI_PARAMS.MaxEngValue", Tx.F6);
        A("HART_Enable", "HART_PARAMS.Enable", Tx.Bool);
        A("HART_MonitorDeviceHealth", "HART_PARAMS.MonDeviceHealth", Tx.Bool);
        A("HART_MonitorConfigChanges", "HART_PARAMS.MonConfigChanges", Tx.Bool);
        A("HART_LockConfiguration", "HART_PARAMS.LockConfig", Tx.Bool);
        A("Class", "POINT_ASSIGNMENT.TagClass", Tx.ClassEnum);
        A("SOEFlags", "SOE_BLOCKS_ASSIGNMENT.#text", Tx.Ident);
        A("PI_PointType", "PI_PARAMS.PIPointType", Tx.Ident);
        A("PI_PointGearTeeth", "PI_PARAMS.GearTeeth", Tx.Ident);
        A("PI_PointGearRatio", "PI_PARAMS.GearRatio", Tx.F6);
        return d;
    }

    // ===================== Address engine =====================
    public enum AddrMode { Import, Export }

    private static AddrMode ParseMode(string s) =>
        s.StartsWith("exp", StringComparison.OrdinalIgnoreCase) ? AddrMode.Export : AddrMode.Import;

    /// <summary>
    /// Assigns Address/PhysicalAddress to every tag, in the given order (the target
    /// project's tag order = the template CSV row order).
    ///
    /// Import mode reproduces TriStation's XML importer:
    ///   * The FIRST occurrence of any non-empty, non-MBN PhysicalAddress (I/O like MBW/MBR/
    ///     MIW/MIR/MRR, and also MIN/MRN) is preserved verbatim.
    ///   * Otherwise, if the template already has the tag, its own Address is reused (the
    ///     target project's existing assignment - e.g. an empty-pa DINT tag whose class is a
    ///     project-specific MIW rather than MIN).
    ///   * Otherwise the tag is freshly allocated by DataType: BOOL -> MBN.&lt;n&gt; , numeric ->
    ///     MIN.&lt;n&gt; , counters independent.
    ///
    /// Export mode copies the XML PhysicalAddress verbatim (a genuine export is round-trip
    /// faithful); an empty address still gets a typed fallback so the CSV stays valid.
    ///
    /// Allocating in the target project's order makes the fresh MBN/MIN numbers exact, so the
    /// whole assignment round-trips. Verified against TriStation 5.8.0 (FB 705/705,
    /// PACK_REPL 169/169).
    /// </summary>
    public static Dictionary<string, string> AssignAddresses(
        IReadOnlyList<(string Name, string Pa, string DataType)> ordered, AddrMode mode,
        IReadOnlyDictionary<string, string>? templateAddr = null)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int mbn = 0, min = 0;
        foreach (var t in ordered)
        {
            string pa = t.Pa ?? "";
            bool preserve = mode == AddrMode.Export
                ? pa.Length > 0
                : pa.Length > 0 && !pa.StartsWith("MBN.", StringComparison.OrdinalIgnoreCase) && !seen.Contains(pa);
            string addr;
            if (preserve)
            {
                addr = pa;
                seen.Add(pa);
            }
            else if (templateAddr != null && templateAddr.TryGetValue(t.Name, out var ta) && ta.Length > 0)
                addr = ta;   // target project already has this tag: keep its assignment
            else addr = Allocate(t.DataType, ref mbn, ref min);
            map[t.Name] = addr;
        }
        return map;
    }

    /// <summary>Fresh address for an un-addressed tag: BOOL -> MBN, numeric -> MIN.</summary>
    public static string Allocate(string dataType, ref int mbn, ref int min)
    {
        bool isBool = string.Equals(dataType, "BOOL", StringComparison.OrdinalIgnoreCase);
        return isBool
            ? "MBN." + (++mbn).ToString("0000", CultureInfo.InvariantCulture)
            : "MIN." + (++min).ToString("0000", CultureInfo.InvariantCulture);
    }

    // The Tagnames.csv PhysicalAddress column is NOT the same quantity as Address:
    //   * coordinate Address (AA.BB.CC) -> "<register-class from BinNumber>.<CC>" e.g. 04.01.17 -> IBR.0017
    //   * memory-style Address (MBN.nnnn) -> identical to Address
    private static readonly Dictionary<string, string> BinPrefix = new(StringComparer.OrdinalIgnoreCase)
    { ["0"] = "QBW", ["1"] = "MBW", ["2"] = "IBR", ["3"] = "MBR", ["4"] = "IIR",
      ["11"] = "MIW", ["13"] = "MBN", ["14"] = "MIN", ["15"] = "MRN" };

    public static string DerivePhysicalAddress(string addr, string binNumber)
    {
        if (string.IsNullOrEmpty(addr)) return "";
        if (Regex.IsMatch(addr, @"^\d+\.\d+\.\d+$"))
        {
            string last = addr.Substring(addr.LastIndexOf('.') + 1);
            if (BinPrefix.TryGetValue(binNumber ?? "", out var pre) && int.TryParse(last, out int cc))
                return pre + "." + cc.ToString("0000", CultureInfo.InvariantCulture);
        }
        return addr;
    }

    private enum Tx { Ident, YesNo, Bool, Color, IoModule, Filter, ClassEnum, AppSafety, AppControl, Mem, PointInput, PointOutput, PointLocation, AliasType, AliasSel, F6 }

    private static string Transform(Tx t, string v) => t switch
    {
        Tx.YesNo => v.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? "True" : "False",
        Tx.Bool => v.Equals("true", StringComparison.OrdinalIgnoreCase) ? "True" : "False",
        Tx.Color => v.ToUpperInvariant() switch { "RED" => "255", "GREEN" => "65280", "BLUE" => "16711680", _ => v },
        Tx.IoModule => v.Equals("NA", StringComparison.OrdinalIgnoreCase) ? "0" : v,
        Tx.Filter => v.Equals("NONE", StringComparison.OrdinalIgnoreCase) ? "0" : v,
        Tx.ClassEnum => v.Equals("USER_DEFINED", StringComparison.OrdinalIgnoreCase) ? "3" : v,
        Tx.AppSafety => v.Equals("Safety", StringComparison.OrdinalIgnoreCase) ? "True" : "False",
        Tx.AppControl => v.Equals("Control", StringComparison.OrdinalIgnoreCase) ? "True" : "False",
        Tx.Mem => v.Equals("MEMORY", StringComparison.OrdinalIgnoreCase) ? "True" : "False",
        Tx.PointInput => v.Contains("INPUT", StringComparison.OrdinalIgnoreCase) ? "True" : "False",
        Tx.PointOutput => v.Contains("OUTPUT", StringComparison.OrdinalIgnoreCase) ? "True" : "False",
        Tx.PointLocation => v.Equals("MEMORY", StringComparison.OrdinalIgnoreCase) ? "1"
                          : v.Contains("INPUT", StringComparison.OrdinalIgnoreCase) ? "2"
                          : v.Contains("OUTPUT", StringComparison.OrdinalIgnoreCase) ? "3" : v,
        Tx.AliasType => v.ToUpperInvariant() switch
        {
            "UNALIASED" => "1",
            "READ_ALIASED" => "2",
            "READWRITE_ALIASED" => "3",
            _ => "1",
        },
        Tx.AliasSel => v.Equals("NO_ALIAS", StringComparison.OrdinalIgnoreCase) ? "0"
                      : v.Equals("USER_ALIAS", StringComparison.OrdinalIgnoreCase) ? "2" : "1",
        Tx.F6 => double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                 ? Math.Round(d, 3).ToString("F6", CultureInfo.InvariantCulture) : v,
        _ => v,
    };

    // ---- shared helpers ----
    private static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private static bool Eq(string csv, string got)
    {
        if ((csv ?? "").Equals(got ?? "", StringComparison.OrdinalIgnoreCase)) return true;
        if (decimal.TryParse(csv, NumberStyles.Any, CultureInfo.InvariantCulture, out var a) &&
            decimal.TryParse(got, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
            return a == b;
        return (csv ?? "").Trim().Equals((got ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Col(List<string> cols, string name)
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
            string body = m.Groups[2].Value;
            var tn = Regex.Match(body, "(?s)<ts:TAGNAME>(.*?)</ts:TAGNAME>");
            if (!tn.Success) continue;
            var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["@name"] = m.Groups[1].Value };
            var ty = Regex.Match(body, "<type>\\s*<([A-Za-z_][\\w.:]*)");
            flat["@type"] = ty.Success ? ty.Groups[1].Value : "";
            var doc = Regex.Match(body, "(?s)<documentation><!\\[CDATA\\[(.*?)\\]\\]></documentation>");
            flat["@documentation"] = doc.Success ? doc.Groups[1].Value.Trim().Replace("\"", "") : "";
            var iv = Regex.Match(body, "<simpleValue\\s+value=\"([^\"]*)\"");
            flat["@initial"] = iv.Success ? iv.Groups[1].Value : "";
            foreach (Match e in Regex.Matches(tn.Groups[1].Value, "<ts:([A-Za-z_]\\w*)([^>]*?)(/?)>"))
            {
                string elem = e.Groups[1].Value;
                foreach (Match a in Regex.Matches(e.Groups[2].Value, "([A-Za-z_]\\w*)=\"([^\"]*)\""))
                    flat[elem + "." + a.Groups[1].Value] = a.Groups[2].Value.Trim();
                if (e.Groups[3].Value != "/")
                {
                    var txt = Regex.Match(tn.Groups[1].Value, $"<ts:{elem}[^>]*>([^<]*)</ts:{elem}>");
                    if (txt.Success) flat[elem + ".#text"] = txt.Groups[1].Value.Trim();
                }
            }
            result[m.Groups[1].Value] = flat;
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
