using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// Report data extraction: decodes a .pt2/.pt2x container into a <see cref="ReportData"/>
/// holding the shared header row plus one row set per TriStation report template. Reuses
/// the same POU/tag/config decoding as <c>frompt2</c>/<c>topdf</c>.
/// </summary>
public sealed partial class FromPt2
{
    /// <summary>Builds the full report data set for a container.</summary>
    public static ReportData BuildReportData(string input, string? projectName = null)
    {
        var cf = CompoundFile.Open(input);
        var streams = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in cf.ListStreams()) streams[s.Path] = s.Data;

        var tags = LoadTags(streams);
        var self = new FromPt2(streams, tags);
        string projName = projectName ?? Path.GetFileNameWithoutExtension(input);
        self._reportProjectName = projName;

        var blocks = new List<Pt2Block>();
        foreach (var folder in self.PouFolders())
        {
            try
            {
                var block = self.ReadPou(folder);
                if (block != null) blocks.Add(block);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  {folder}: {ex.Message}");
            }
        }
        blocks = self.OrderPous(blocks);
        self.CollectReferencedGlobals(blocks);

        var data = new ReportData { ProjectName = projName };
        self.BuildHeader(data, cf);
        self.BuildPouReport(data, blocks);
        self.BuildInstVarReport(data, blocks);
        self.BuildProgramVariableListing(data, blocks);
        self.BuildTagnameCompleteListing(data, tags);
        self.BuildPouXRefReport(data, blocks);
        self.BuildSharedLibReport(data, streams);
        self.BuildGeneralParameters(data);
        self.BuildProgramExecutionList(data, blocks);
        self.BuildSequenceOfEvents(data, streams, tags);
        return data;
    }

    private string _reportProjectName = "";

    // ---- header (Table2) ----------------------------------------------------

    private void BuildHeader(ReportData data, CompoundFile cf)
    {
        var h = data.Header;
        var ws = LoadConfig("Workspace.Xml");
        var plat = LoadConfig("Current/Platform/Platform.Xml");
        var comm = LoadConfig("Current/Elements/Configuration/TSCommunication.xml");
        var op = LoadConfig("Current/Elements/Configuration/OpParams.xml");
        var projRoot = LoadConfig("Current/Project.xml");

        string platform = (string?)ws?.Attribute("Platform") ?? "TRICON_CX";
        string nodeName = (string?)comm?.Attribute("NetNodeName") ?? "";
        string nodeNumber = (string?)op?.Attribute("NodeNumber") ?? "";

        h["PRJNAME"] = _reportProjectName;
        h["PLATFORM"] = platform;
        h["TS_VER"] = TsVersion();
        h["SAVEDDATE"] = SavedOn(cf);
        h["DWLDDATE"] = "";
        h["DWNLD_VER"] = PreviousTsVersion(h["TS_VER"]);
        h["CURNT_VER"] = h["TS_VER"];
        h["NODE_NAME"] = nodeName;
        h["NODE_NUM"] = nodeNumber;
        h["DESCRIP"] = ProjectDescription();
        h["USER_NAME"] = (string?)ws?.Attribute("LastLoggedUser") ?? "";
        h["OS_VER"] = "";
    }

    private string TsVersion()
    {
        // Workspace.Xml carries no explicit version; ProjectInfo link holds PreviousTSVersion.
        if (_streams.TryGetValue("ProjectInfo/ProjectInfo.Xml", out var data))
        {
            try
            {
                var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
                string cur = (string?)doc.Root?.Attribute("CurrentTSVersion") ?? "";
                if (cur.Length > 0) return cur;
            }
            catch { /* ignore */ }
        }
        return PreviousTsVersion("");
    }

    /// <summary>The container's "saved on" timestamp: the root directory entry's FILETIME.</summary>
    private static string SavedOn(CompoundFile cf)
    {
        var root = cf.Entries.FirstOrDefault(e => e.IsRoot);
        if (root == null || root.ModifiedTime <= 0) return "";
        try
        {
            var dt = DateTime.FromFileTimeUtc(root.ModifiedTime).ToLocalTime();
            return dt.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch { return ""; }
    }

    // ---- POU Variables ------------------------------------------------------

    private void BuildPouReport(ReportData data, List<Pt2Block> blocks)
    {
        foreach (var b in blocks.Where(b => b.Sheets.Count > 0 || b.St != null))
        {
            foreach (var v in b.Vars)
            {
                if (v.Section is "global" or "Global") continue;
                data.PouReport.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VARPATH"] = b.Name + "." + v.Name,
                    ["POUNAME"] = b.Name,
                    ["VARNAME"] = v.Name,
                    ["DESCRIP"] = v.Desc,
                    ["DATATYPE"] = v.DataType,
                    ["DATACLASS"] = v.Section,
                    ["INITVALUE"] = v.Init,
                });
            }
        }
    }

    // ---- Instance Variable Connections --------------------------------------

    private void BuildInstVarReport(ReportData data, List<Pt2Block> blocks)
    {
        var byName = blocks.ToDictionary(b => b.Name, b => b, StringComparer.OrdinalIgnoreCase);
        foreach (var b in blocks.Where(b => b.Sheets.Count > 0 || b.St != null))
        {
            foreach (var v in b.Vars)
            {
                if (!v.IsInstance) continue;
                if (!byName.TryGetValue(v.DataType, out var fb))
                {
                    data.InstVarReport.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["INSTNAME"] = v.Name,
                        ["PROGNAME"] = b.Name,
                        ["TAGNAME"] = v.DataType,
                        ["VARNAME"] = "",
                        ["VAROUTPUT"] = "",
                        ["VARPATH"] = b.Name + "." + v.Name,
                        ["DESCRIP"] = v.Desc,
                    });
                    continue;
                }
                foreach (var fv in fb.Vars)
                {
                    bool isOut = fv.Section is "output" or "Output" or "inout" or "InOut";
                    data.InstVarReport.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["INSTNAME"] = v.Name,
                        ["PROGNAME"] = b.Name,
                        ["TAGNAME"] = v.DataType,
                        ["VARNAME"] = fv.Name,
                        ["VAROUTPUT"] = isOut ? "Y" : "",
                        ["VARPATH"] = b.Name + "." + v.Name + "." + fv.Name,
                        ["DESCRIP"] = fv.Desc,
                    });
                }
            }
        }
    }

    // ---- Program Variable Listing -------------------------------------------

    private void BuildProgramVariableListing(ReportData data, List<Pt2Block> blocks)
    {
        foreach (var b in blocks.Where(b => b.PouType == "program"))
        {
            foreach (var v in b.Vars)
            {
                if (v.Section is "global" or "Global") continue;
                data.ProgramVariableListing.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["POUTYPE"] = b.PouType,
                    ["VARNAME"] = b.Name + "." + v.Name,
                    ["DATATYPE"] = v.DataType,
                    ["INITVALUE"] = v.Init,
                    ["DATACLASS"] = v.Section,
                    ["DESCRIP"] = v.Desc,
                });
            }
        }
    }

    // ---- Tagnames Complete Listing ------------------------------------------

    private void BuildTagnameCompleteListing(ReportData data, List<Pt2Tag> tags)
    {
        foreach (var t in tags)
        {
            data.TagnameCompleteListing.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TAGNAME"] = t.Name,
                ["DATATYPE"] = t.Type,
                ["DATACLASS"] = t.Class,
                ["DESCRIP"] = t.Desc,
                ["LOCATION"] = t.Location,
                ["AREA"] = t.Area,
                ["EQUIPMENT"] = t.Equipment,
                ["GROUP1"] = t.Group1,
                ["GROUP2"] = t.Group2,
                ["MINSPAN"] = t.MinimumValue,
                ["MAXSPAN"] = t.MaximumValue,
                ["DECPL"] = t.Precision,
                ["RETENTIVE"] = t.Retentive,
                ["SIF NAME"] = t.SIFName,
                ["INITVALUE"] = t.InitialValue,
                ["MODBUS"] = t.Address,
                ["MULTIWRIT"] = t.EnableMultipleWrites,
                ["SHARED"] = t.SharedRead,
                ["EXTALIAS"] = t.UserAliasExtended,
                ["ALIASTYPE"] = t.AliasType,
                ["ALIASNOSEL"] = t.AliasNumberSelection,
                ["APPLICATN"] = "",
                ["SHORTDESC"] = t.Desc,
                ["REALSCALING"] = t.DisableScaling,
                ["POINTTYPE"] = t.PI_PointType,
            });
        }
    }

    // ---- POU Cross-Reference ------------------------------------------------

    private void BuildPouXRefReport(ReportData data, List<Pt2Block> blocks)
    {
        foreach (var b in blocks)
        {
            int seq = 0;
            var declared = new HashSet<string>(b.Vars.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            if (b.St != null)
            {
                int line = 0;
                foreach (var raw in b.St.Replace("\r\n", "\n").Split('\n'))
                {
                    line++;
                    foreach (var t in _tags)
                    {
                        if (declared.Contains(t.Name)) continue;
                        if (!ContainsWord(raw, t.Name)) continue;
                        data.PouXRefReport.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["VARPATH"] = t.Name,
                            ["SEQNO"] = (++seq).ToString(CultureInfo.InvariantCulture),
                            ["SHTCOORD"] = "H0 V" + line,
                        });
                    }
                }
            }
            foreach (var sheet in b.Sheets)
            {
                foreach (var v in sheet.Elems.OfType<Pt2VarElem>())
                {
                    if (declared.Contains(v.Name)) continue;
                    if (IsLiteral(v.Name)) continue;
                    data.PouXRefReport.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["VARPATH"] = v.Name,
                        ["SEQNO"] = (++seq).ToString(CultureInfo.InvariantCulture),
                        ["SHTCOORD"] = $"H{sheet.Number} V1",
                    });
                }
            }
        }
    }

    // ---- Shared Libraries ---------------------------------------------------

    private void BuildSharedLibReport(ReportData data, Dictionary<string, byte[]> streams)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in streams)
        {
            var parts = kv.Key.Split('/');
            if (parts.Length < 2) continue;
            if (!parts[^2].Equals("Bin", StringComparison.OrdinalIgnoreCase)) continue;
            if (!kv.Key.EndsWith(".EMA", StringComparison.OrdinalIgnoreCase)) continue;
            string lib = Path.GetFileNameWithoutExtension(parts[^1]);
            if (!seen.Add(lib)) continue;
            data.SharedLibReport.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LIBNAME"] = lib,
                ["VERSION"] = "",
            });
        }
    }

    // ---- General Parameters -------------------------------------------------

    private void BuildGeneralParameters(ReportData data)
    {
        var op = LoadConfig("Current/Elements/Configuration/OpParams.xml");
        var comm = LoadConfig("Current/Elements/Configuration/TSCommunication.xml");
        var ws = LoadConfig("Workspace.Xml");
        void Add(string item, string value) =>
            data.GeneralParameters.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["ITEM"] = item, ["VALUE"] = value, ["PARNAME"] = item, ["PARDESC"] = value });

        if (ws != null) Add("Platform", (string?)ws.Attribute("Platform") ?? "");
        if (comm != null) Add("Node Name", (string?)comm.Attribute("NetNodeName") ?? "");
        if (op != null)
        {
            Add("Node Number", (string?)op.Attribute("NodeNumber") ?? "");
            foreach (var a in op.Attributes())
                if (!a.Name.LocalName.Equals("NodeNumber", StringComparison.OrdinalIgnoreCase) &&
                    !a.Name.LocalName.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase))
                    Add(a.Name.LocalName, a.Value);
        }
    }

    // ---- Program Execution List ---------------------------------------------

    private void BuildProgramExecutionList(ReportData data, List<Pt2Block> blocks)
    {
        int seq = 0;
        foreach (var b in blocks.Where(b => b.PouType == "program"))
        {
            data.ProgramExecutionList.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SEQUENCE"] = (++seq).ToString(CultureInfo.InvariantCulture),
                ["PROGRAM"] = b.Name,
                ["PROGNAME"] = b.Name,
                ["DESCRIPTION"] = b.Title.Length > 0 ? b.Title : b.DrawingTitle,
            });
        }
    }

    // ---- Sequence of Events -------------------------------------------------

    private void BuildSequenceOfEvents(ReportData data, Dictionary<string, byte[]> streams, List<Pt2Tag> tags)
    {
        var soe = LoadConfig("Current/Elements/Configuration/SOEGlobals.xml");
        if (soe != null)
        {
            int blockNum = 0;
            var coll = soe.Element(soe.Name.Namespace + "Collection");
            var globals = coll != null ? coll.Elements() : soe.Elements();
            foreach (var g in globals)
            {
                if (!string.Equals((string?)g.Attribute("Modified"), "True", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty((string?)g.Attribute("SoeTitle"))) { blockNum++; continue; }
                string title = (string?)g.Attribute("SoeTitle") ?? "";
                string type = (string?)g.Attribute("SoeBlockType") ?? "";
                string size = (string?)g.Attribute("SoeBuffSize") ?? "";
                if (title.Length == 0 && (size == "0" || size.Length == 0)) { blockNum++; continue; }
                data.SequenceOfEvents.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SOEBLKNUM"] = blockNum.ToString(CultureInfo.InvariantCulture),
                    ["SOEBLKTTL"] = title,
                    ["SOEBLKTYP"] = SoeTypeName(type),
                    ["SOEBUFSIZ"] = size,
                });
                blockNum++;
            }
        }

        foreach (var t in tags)
        {
            if (string.IsNullOrWhiteSpace(t.SOEFlags)) continue;
            foreach (var flag in t.SOEFlags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string f = flag.Trim('{', '}', ' ');
                if (f.Length == 0) continue;
                if (f.Equals("UNASSIGNED", StringComparison.OrdinalIgnoreCase)) continue;
                data.SoeAssignments.Add((t.Name, f));
            }
        }
    }
}
