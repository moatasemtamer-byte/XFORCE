using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pt2Conv;

/// <summary>
/// Generates a TriStation CFBDDocXml (.FBD container stream) from a PLCopen XML POU export.
/// Rules reverse-engineered from reference containers FB05_DCS_CMD and FB04_DI.
/// Accepted deviations: wire ids are contiguous (reference gaps are edit-history artifacts),
/// displayables emitted in id order (reference file order is editor draw order),
/// dateTime is generation time.
/// </summary>
public static class FbdGenerator
{
    /// <summary>Authoritative per-tag info (from the template tag database) used for program global declarations.</summary>
    public readonly struct TagInfo
    {
        public readonly string DataType, Desc;
        public TagInfo(string dataType, string desc) { DataType = dataType; Desc = desc; }
    }

    // ============================================================ library metadata (OBSERVED)

    private sealed class LibPin
    {
        public string Name = "", DataType = "", InitVal = "";
        public int Vt, Flags;
    }

    private sealed class LibMeta
    {
        public int DogTag, Type, ExtOptFlags, CemEI, CemAO, EventId, User = 2;
        public long SubType;
        public string Date = "";
        public List<LibPin> Pins = new();
    }

    // Element names of every library function/function block/data type in the project
    // library. A program variable whose name matches one of these (case-insensitively)
    // is resolved by TriStation as a library reference, not as an unresolved tag, so it
    // is NOT written to the FBD declarations (e.g. "max"/"min" vs. the MAX/MIN functions).
    public static readonly HashSet<string> LibraryNames = new(StringComparer.OrdinalIgnoreCase);

    public static void LoadLibraryNames(string projectXml)
    {
        var doc = System.Xml.Linq.XDocument.Parse(projectXml);
        foreach (var e in doc.Descendants())
        {
            var n = e.Attribute("ElemName");
            if (n != null && !string.IsNullOrEmpty(n.Value)) LibraryNames.Add(n.Value);
        }
        foreach (var k in Lib.Keys) LibraryNames.Add(k);
    }

    private static readonly Dictionary<string, LibMeta> Lib = new()
    {
        ["R_TRIG"] = new()
        {
            DogTag = 184, Type = 2, SubType = 6, ExtOptFlags = 159, CemEI = 1, CemAO = 1,
            EventId = 297, Date = "1995-07-12 17:31:07",
            Pins =
            {
                new LibPin { Name = "CLK", DataType = "BOOL", Vt = 0 },
                new LibPin { Name = "Q", DataType = "BOOL", Vt = 1 },
                new LibPin { Name = "M", DataType = "BOOL", Vt = 3 },
            },
        },
        ["OR"] = new()
        {
            DogTag = 434, Type = 8, SubType = 2147483652L, ExtOptFlags = 7, CemEI = 1, CemAO = 0,
            EventId = 292, Date = "1995-07-12 17:30:51",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_BIT", Vt = 0, Flags = 1 },
                new LibPin { Name = "OR", DataType = "ANY_BIT", Vt = 1 },
            },
        },
        ["AND"] = new()
        {
            DogTag = 358, Type = 8, SubType = 2147483652L, ExtOptFlags = 7, CemEI = 2, CemAO = 0,
            EventId = 20, Date = "1995-07-12 17:24:00",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_BIT", Vt = 0, Flags = 1 },
                new LibPin { Name = "AND", DataType = "ANY_BIT", Vt = 1 },
            },
        },
        ["MOVE"] = new()
        {
            DogTag = 428, Type = 8, SubType = 1, ExtOptFlags = 31, CemEI = 1, CemAO = 1,
            EventId = 276, Date = "1995-07-12 17:30:27",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY", Vt = 1 },
            },
        },
        ["ADD"] = new()
        {
            DogTag = 357, Type = 8, SubType = 2147483652L, ExtOptFlags = 3, CemEI = 0, CemAO = 0,
            EventId = 16, Date = "1995-07-12 17:23:55",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NUM", Vt = 0, Flags = 1 },
                new LibPin { Name = "+", DataType = "ANY_NUM", Vt = 1 },
            },
        },
        ["SUB"] = new()
        {
            DogTag = 461, Type = 8, SubType = 2, ExtOptFlags = 3, CemEI = 0, CemAO = 0,
            EventId = 403, Date = "1995-07-12 17:34:26",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NUM", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY_NUM", Vt = 0 },
                new LibPin { Name = "-", DataType = "ANY_NUM", Vt = 1 },
            },
        },
        ["MUL"] = new()
        {
            DogTag = 429, Type = 8, SubType = 2147483652L, ExtOptFlags = 3, CemEI = 0, CemAO = 0,
            EventId = 280, Date = "1995-07-12 17:30:33",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NUM", Vt = 0, Flags = 1 },
                new LibPin { Name = "*", DataType = "ANY_NUM", Vt = 1 },
            },
        },
        ["DIV"] = new()
        {
            DogTag = 384, Type = 8, SubType = 2, ExtOptFlags = 3, CemEI = 0, CemAO = 0,
            EventId = 120, Date = "1995-07-12 17:26:35",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NUM", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY_NUM", Vt = 0 },
                new LibPin { Name = "/", DataType = "ANY_NUM", Vt = 1 },
            },
        },
        ["NOT"] = new()
        {
            DogTag = 432, Type = 8, SubType = 1, ExtOptFlags = 31, CemEI = 1, CemAO = 1,
            EventId = 288, Date = "1995-07-12 17:30:45",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_BIT", Vt = 0 },
                new LibPin { Name = "NOT", DataType = "ANY_BIT", Vt = 1 },
            },
        },
        ["R_TRIG"] = new()
        {
            DogTag = 435, Type = 2, SubType = 6, ExtOptFlags = 159, CemEI = 1, CemAO = 1,
            EventId = 297, Date = "1995-07-12 17:31:07",
            Pins =
            {
                new LibPin { Name = "CLK", DataType = "BOOL", Vt = 0 },
                new LibPin { Name = "Q", DataType = "BOOL", Vt = 1 },
                new LibPin { Name = "M", DataType = "BOOL", Vt = 3 },
            },
        },
        ["F_TRIG"] = new()
        {
            DogTag = 392, Type = 2, SubType = 6, ExtOptFlags = 159, CemEI = 1, CemAO = 1,
            EventId = 157, Date = "1995-07-12 17:27:34",
            Pins =
            {
                new LibPin { Name = "CLK", DataType = "BOOL", Vt = 0 },
                new LibPin { Name = "Q", DataType = "BOOL", Vt = 1 },
                new LibPin { Name = "M", DataType = "BOOL", InitVal = "1", Vt = 3 },
            },
        },
        ["RS"] = new()
        {
            DogTag = 445, Type = 2, SubType = 6, ExtOptFlags = 143, CemEI = 1, CemAO = 1,
            EventId = 349, Date = "1995-07-12 17:32:38",
            Pins =
            {
                new LibPin { Name = "S", DataType = "BOOL", Vt = 0 },
                new LibPin { Name = "R1", DataType = "BOOL", Vt = 0 },
                new LibPin { Name = "Q1", DataType = "BOOL", Vt = 1 },
            },
        },
        ["SR"] = new()
        {
            DogTag = 460, Type = 2, SubType = 6, ExtOptFlags = 143, CemEI = 1, CemAO = 1,
            EventId = 400, Date = "1995-07-12 17:34:20",
            Pins =
            {
                new LibPin { Name = "S1", DataType = "BOOL", Vt = 0 },
                new LibPin { Name = "R", DataType = "BOOL", Vt = 0 },
                new LibPin { Name = "Q1", DataType = "BOOL", Vt = 1 },
            },
        },
        ["TON"] = new()
        {
            DogTag = 216, Type = 2, SubType = 6, ExtOptFlags = 159, CemEI = 1, CemAO = 1,
            EventId = 603, Date = "1995-07-12 19:31:52",
            Pins =
            {
                new LibPin { Name = "IN", DataType = "BOOL", Vt = 0 },
                new LibPin { Name = "PT", DataType = "TIME", Vt = 0 },
                new LibPin { Name = "Q", DataType = "BOOL", Vt = 1 },
                new LibPin { Name = "ET", DataType = "TIME", Vt = 1 },
                new LibPin { Name = "M", DataType = "BOOL", Vt = 3 },
                new LibPin { Name = "T0", DataType = "DT", Vt = 3 },
            },
        },
        ["EQ"] = new()
        {
            DogTag = 389, Type = 8, SubType = 2, ExtOptFlags = 7, CemEI = 2, CemAO = 0,
            EventId = 678, Date = "1995-07-20 21:21:32",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "=", DataType = "BOOL", Vt = 1 },
            },
        },
        ["NE"] = new()
        {
            DogTag = 0, Type = 8, SubType = 2, ExtOptFlags = 7, CemEI = 2, CemAO = 0,
            EventId = 0, Date = "",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "<>", DataType = "BOOL", Vt = 1 },
            },
        },
        ["GT"] = new()
        {
            DogTag = 397, Type = 8, SubType = 2, ExtOptFlags = 7, CemEI = 2, CemAO = 0,
            EventId = 682, Date = "1995-07-20 21:21:33",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = ">", DataType = "BOOL", Vt = 1 },
            },
        },
        ["GE"] = new()
        {
            DogTag = 395, Type = 8, SubType = 2, ExtOptFlags = 7, CemEI = 2, CemAO = 0,
            EventId = 680, Date = "1995-07-20 21:21:33",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = ">=", DataType = "BOOL", Vt = 1 },
            },
        },
        ["LT"] = new()
        {
            DogTag = 421, Type = 8, SubType = 2, ExtOptFlags = 7, CemEI = 2, CemAO = 0,
            EventId = 686, Date = "1995-07-20 21:21:34",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "<", DataType = "BOOL", Vt = 1 },
            },
        },
        ["LE"] = new()
        {
            DogTag = 408, Type = 8, SubType = 2, ExtOptFlags = 7, CemEI = 2, CemAO = 0,
            EventId = 684, Date = "1995-07-20 21:21:34",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY_NOTE1", Vt = 0 },
                new LibPin { Name = "<=", DataType = "BOOL", Vt = 1 },
            },
        },
        ["SEL"] = new()
        {
            DogTag = 447, Type = 8, SubType = 8, ExtOptFlags = 3, CemEI = 0, CemAO = 0,
            EventId = 625, Date = "1995-07-12 19:47:27",
            Pins =
            {
                new LibPin { Name = "G", DataType = "BOOL", Vt = 0 },
                new LibPin { Name = "IN0", DataType = "ANY", Vt = 0 },
                new LibPin { Name = "IN1", DataType = "ANY", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY", Vt = 1 },
            },
        },
        ["SQRT"] = new()
        {
            DogTag = 459, Type = 8, SubType = 1, ExtOptFlags = 3, CemEI = 0, CemAO = 0,
            EventId = 395, Date = "1995-07-12 17:34:03",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_REAL", Vt = 0 },
                new LibPin { Name = "BIF", DataType = "ANY_REAL", Vt = 1 },
            },
        },
        ["REAL_TO_DINT"] = new()
        {
            DogTag = 81, Type = 8, SubType = 1, ExtOptFlags = 3, CemEI = 0, CemAO = 0,
            EventId = 300, Date = "1995-07-12 17:31:12",
            Pins =
            {
                new LibPin { Name = "", DataType = "REAL", Vt = 0 },
                new LibPin { Name = "()", DataType = "DINT", Vt = 1 },
            },
        },
        ["DINT_TO_DWORD"] = new()
        {
            DogTag = 125, Type = 8, SubType = 1, ExtOptFlags = 3, CemEI = 0, CemAO = 0,
            EventId = 1276, Date = "1996-07-08 19:32:30",
            Pins =
            {
                new LibPin { Name = "BIF", DataType = "DWORD", Vt = 1 },
                new LibPin { Name = "", DataType = "DINT", Vt = 0 },
            },
        },
        ["DWORD_TO_DINT"] = new()
        {
            DogTag = 137, Type = 8, SubType = 1, ExtOptFlags = 3, CemEI = 0, CemAO = 0,
            EventId = 1281, Date = "1996-07-08 19:32:32",
            Pins =
            {
                new LibPin { Name = "BIF", DataType = "DINT", Vt = 1 },
                new LibPin { Name = "", DataType = "DWORD", Vt = 0 },
            },
        },
    };

    /// <summary>
    /// Learns library block metadata (dogTag/type/subType/flags/eventId/pins) from a reference
    /// FBD stream's &lt;snapshots&gt; collection. Called with the template's own POUs so
    /// regenerated snapshots reproduce the project's library identities exactly.
    /// </summary>
    public static void LoadLibraryFromFbd(string fbdXml)
    {
        int si = fbdXml.IndexOf("<Collection Name=\"snapshots\"", StringComparison.Ordinal);
        if (si < 0) return;
        string block = fbdXml.Substring(si);

        var head = System.Text.RegularExpressions.Regex.Matches(block,
            "<Class name=\"([^\"]*)\" dogTag=\"(\\d+)\" type=\"(\\d+)\" subType=\"(-?\\d+)\" " +
            "instanceCount=\"\\d+\" commentCount=\"\\d+\" extOptFlags=\"(-?\\d+)\" " +
            "cemEventInputs=\"(\\d+)\" cemActionOutputs=\"(\\d+)\">");
        for (int i = 0; i < head.Count; i++)
        {
            string name = System.Net.WebUtility.HtmlDecode(head[i].Groups[1].Value);
            if (name.Length == 0) continue;
            var meta = new LibMeta
            {
                DogTag = int.Parse(head[i].Groups[2].Value),
                Type = int.Parse(head[i].Groups[3].Value),
                SubType = long.Parse(head[i].Groups[4].Value),
                ExtOptFlags = int.Parse(head[i].Groups[5].Value),
                CemEI = int.Parse(head[i].Groups[6].Value),
                CemAO = int.Parse(head[i].Groups[7].Value),
            };
            int start = head[i].Index;
            int end = (i + 1 < head.Count) ? head[i + 1].Index : block.Length;
            string chunk = block.Substring(start, end - start);
            var ev = System.Text.RegularExpressions.Regex.Match(chunk,
                "<Class Embed=\"eventConnectionsChanged\" eventType=\"\\d+\" eventId=\"(\\d+)\" date=\"([^\"]*)\" user=\"(\\d+)\"");
            if (ev.Success)
            {
                meta.EventId = int.Parse(ev.Groups[1].Value);
                meta.Date = ev.Groups[2].Value;
                meta.User = int.Parse(ev.Groups[3].Value);
            }

            int di = chunk.IndexOf("<Collection Name=\"declarations\"", StringComparison.Ordinal);
            if (di >= 0)
            {
                int de = chunk.IndexOf("</Collection>", di, StringComparison.Ordinal);
                if (de < 0) de = chunk.Length;
                string pins = chunk.Substring(di, de - di);
                foreach (System.Text.RegularExpressions.Match pm in System.Text.RegularExpressions.Regex.Matches(pins,
                    "<Class name=\"([^\"]*)\" dataType=\"([^\"]*)\" initVal=\"([^\"]*)\" desc=\"[^\"]*\" " +
                    "varVTType=\"(\\d+)\" refCount=\"\\d+\" fbRefCount=\"\\d+\" flags=\"(\\d+)\" writes=\"0\""))
                {
                    meta.Pins.Add(new LibPin
                    {
                        Name = System.Net.WebUtility.HtmlDecode(pm.Groups[1].Value),
                        DataType = System.Net.WebUtility.HtmlDecode(pm.Groups[2].Value),
                        InitVal = System.Net.WebUtility.HtmlDecode(pm.Groups[3].Value),
                        Vt = int.Parse(pm.Groups[4].Value),
                        Flags = int.Parse(pm.Groups[5].Value),
                    });
                }
            }
            // The built-in table holds the standard TriStation library identities and is
            // authoritative; template-project streams only fill in types we do not know
            // (a stale base project must not override the platform library).
            if (!Lib.ContainsKey(name)) Lib[name] = meta;
        }
    }

    private static int LineOffset(string type, string pin, int inputIndex, int inputCount)
    {
        if (type == "R_TRIG" && pin == "CLK") return 4;
        if (type == "MOVE" && pin == "IN1") return 4;
        if (pin == "IN1") return 3;
        if (pin == "IN2") return 7;
        return inputCount <= 1 ? 4 : (inputIndex == 0 ? 3 : 7);
    }

    // ============================================================ export model

    private abstract class ExElem
    {
        public int DocIndex;
        public int ExportId = -1;
        public int CopyId;
        public int Sheet = 1;
        public int X, Y;
        public bool Clipped;
    }

    // Elements whose bottom edge lies below the drawing frame are dropped by the
    // TriStation editor. The export coordinates start at the top-left corner of the
    // drawing area, so the usable height of template D (3300 x 2100 with a 30 unit
    // margin all round) is 2100 - 2 * 30 = 2040.
    private const int SheetHeightExport = 2040;
    private static bool IsClipped(int y, int h) => y + h > SheetHeightExport;

    private sealed class ExVar : ExElem
    {
        public string Name = "", Doc = "", Section = "", DataType = "", Derived = "", InitVal = "";
        public bool IsInstance => Derived.Length > 0;
    }

    private sealed class ExComment : ExElem
    {
        public int W, H;
        public string Text = "", Align = "Center", Border = "Single", Size = "10";
        public string Expand = "FALSE", Macro = "FALSE", Behind = "FALSE";
    }

    private sealed class ExDivider : ExElem
    {
        public string Type = "HORIZONTAL";
    }

    private sealed class ExConn
    {
        public int SrcExportId;
        public string SrcPin = "";
        public ExElem? Owner;
        public string OwnerPin = "";
        public List<(int X, int Y)> Path = new();
    }

    private sealed class ExVarPlace : ExElem
    {
        public string Kind = "in", Expression = "";
        public int W, H;
        public ExConn? InConn;
        public bool IsConstant;
        public string DataType = "";
        public string Annotation = "%DESCRIPTION", Annotate = "TRUE", MonitorValue = "FALSE";
    }

    private sealed class ExPinOut
    {
        public string Name = "";
    }

    private sealed class ExPinIn
    {
        public string Name = "";
        public ExConn? Conn;
    }

    private sealed class ExBlock : ExElem
    {
        public string TypeName = "", InstanceName = "";
        public int W, H;
        public bool DoubleSpace;
        public List<ExPinIn> Inputs = new();
        public List<ExPinOut> Outputs = new();
        public bool IsFunction => InstanceName.Length == 0;
    }

    private sealed class ExPou
    {
        public string Name = "", PouType = "functionblock", Version = "1.0";
        public bool IsBuilt = true;
        public List<ExVar> Vars = new();
        public List<ExComment> Comments = new();
        public List<ExVarPlace> Placements = new();
        public List<ExBlock> Blocks = new();
        public List<ExDivider> Dividers = new();
        public List<ExElem> ElemOrder = new();
        public List<ExConn> ConnOrder = new();
        public Dictionary<int, ExElem> ByExportId = new();
        public List<(int Number, string Title)> Sheets = new();
        public string DrawingNumber = "", DrawingTitle = "", Rev = "";
        public string SheetTemplate = "";
        public int SheetWidth, SheetHeight;
        public IReadOnlyDictionary<string, TagInfo>? TagDesc;
    }

    // ============================================================ generated wire model

    private sealed class Wire
    {
        public int Id;
        public ExElem Source = null!;
        public ExVarPlace? SourceVar;
        public ExBlock? SourceBlock;
        public string SourcePin = "";
        public ExElem Target = null!;
        public ExVarPlace? TargetVar;
        public ExBlock? TargetBlock;
        public string TargetPin = "";
        public bool TargetIsBlockInput;
        public bool IsBranch;
        public bool Suppress;
        public Wire? Parent;
        public int Vn;
        public List<(int X, int Y)> PathExport = new();

        // Set by the net decomposer for a wire merged through a junction: the two endpoints
        // may be arbitrary element pins (not the connection's original source/target).
        public bool Decomposed;
        public ExElem? EndA;
        public string EndAPin = "";
        public ExElem? EndB;
        public string EndBPin = "";
    }

    private sealed class BlockPlan
    {
        public ExBlock B = null!;
        public int Id, Sequence;
        public string Temp = "";
        public int FirstLine, LineCount;
        public Dictionary<string, Wire> OutWires = new();
        public List<Wire> SinkAssigns = new();
        public List<Wire?> InWires = new();
    }

    private sealed class PlacePlan
    {
        public ExVarPlace P = null!;
        public int Id, LineNumber;
        public Wire? OutWire, InWire;
    }

    private static string A(XElement e, string name, string def = "")
        => (string?)e.Attribute(name) ?? def;

    /// <summary>Attribute lookup by local name (export uses the ts: namespace for some attrs).</summary>
    private static string AL(XElement e, string local, string def = "")
    {
        foreach (var at in e.Attributes())
            if (at.Name.LocalName == local) return at.Value;
        return def;
    }

    private static XElement? Pos(XElement el)
        => el.Elements().FirstOrDefault(e => e.Name.LocalName == "position");

    private static string TextTrim(XElement? e)
    {
        if (e == null) return "";
        var t = e.Nodes().OfType<XText>().Select(t => t.Value).FirstOrDefault() ?? "";
        return t.Trim();
    }

    private static ExPou Parse(string xmlPath, string pouName, IReadOnlyDictionary<string, TagInfo>? tagDesc = null, IReadOnlySet<string>? referencedTypes = null)
    {
        var doc = XmlRepair.Load(xmlPath);
        var pou = doc.Descendants().FirstOrDefault(
            e => e.Name.LocalName == "pou" && A(e, "name") == pouName)
            ?? throw new InvalidOperationException($"POU '{pouName}' not found in {xmlPath}");

        var p = new ExPou { Name = pouName, PouType = A(pou, "pouType", "functionBlock").ToLowerInvariant(), TagDesc = tagDesc };
        p.IsBuilt = referencedTypes == null || referencedTypes.Contains(pouName);
        // TriStation writes function blocks at the current FBD schema version (1.1),
        // regardless of the XML's declared version; programs keep the exported version.
        p.Version = p.PouType == "functionblock" ? "1.1" : A(pou, "version", "1.0");

        var iface = pou.Elements().FirstOrDefault(e => e.Name.LocalName == "interface");
        if (iface != null)
        {
            foreach (var (secName, secKind) in new[] { ("inputVars", "input"), ("inOutVars", "inOut"),
                                                       ("outputVars", "output"), ("localVars", "local"),
                                                       ("globalVars", "global") })
            {
                var secEls = iface.Elements().Where(e => e.Name.LocalName == secName).ToList();
                foreach (var v in secEls.SelectMany(s => s.Elements()).Where(e => e.Name.LocalName == "variable"))
                {
                    var typeEl = v.Elements().FirstOrDefault(e => e.Name.LocalName == "type");
                    var derived = typeEl?.Elements().FirstOrDefault(e => e.Name.LocalName == "derived");
                    string dtype;
                    if (derived != null) dtype = A(derived, "name");
                    else
                    {
                        var t = typeEl?.Elements().FirstOrDefault(e => e.Name.LocalName != "array");
                        dtype = t?.Name.LocalName ?? "";
                    }
                    var initEl = v.Elements().FirstOrDefault(e => e.Name.LocalName == "initialValue")
                        ?.Elements().FirstOrDefault(e => e.Name.LocalName == "simpleValue");
                    p.Vars.Add(new ExVar
                    {
                        Name = A(v, "name"),
                        Section = secKind,
                        DataType = dtype,
                        Derived = derived != null ? A(derived, "name") : "",
                        Doc = TextTrim(v.Elements().FirstOrDefault(e => e.Name.LocalName == "documentation")),
                        InitVal = initEl != null ? A(initEl, "value") : "",
                    });
                }
            }
            // The export repeats every referenced tag inside each program's <globalVars>;
            // the FBD's declarations collection lists each name once, and omits literals.
            // Names that are not identifiers (empty, "dir/…", "L%4102"), or that shadow a
            // library element (max/min), are not variables in TriStation's declarations.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            p.Vars = p.Vars.Where(x => seen.Add(x.Name) && IsDeclarableName(x.Name)).ToList();
        }

        var fbd = pou.Descendants().FirstOrDefault(e => e.Name.LocalName == "FBD")
            ?? throw new InvalidOperationException($"POU '{pouName}' has no FBD body");

        var sheetsEl = fbd.Elements().FirstOrDefault(e => e.Name.LocalName == "SHEETS");
        if (sheetsEl != null)
        {
            p.SheetTemplate = A(sheetsEl, "Template");
            p.SheetWidth = int.Parse(A(sheetsEl, "Width", "0"));
            p.SheetHeight = int.Parse(A(sheetsEl, "Height", "0"));
            foreach (var sh in sheetsEl.Elements().Where(e => e.Name.LocalName == "SHEET"))
            {
                string title = "";
                foreach (var mac in sh.Elements().Where(e => e.Name.LocalName == "Macros"))
                    if (A(mac, "Name") == "SHEET_TITLE")
                        title = mac.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value ?? "";
                int sheetNo = int.Parse(A(sh, "Number", "1"));
                p.Sheets.Add((sheetNo, title));
                int divDoc = 0;
                foreach (var d in sh.Elements().Where(e => e.Name.LocalName == "NETWORKDIVIDER"))
                {
                    var dv = new ExDivider
                    {
                        DocIndex = divDoc++,
                        Sheet = sheetNo,
                        X = int.Parse(A(d, "X", "0")),
                        Y = int.Parse(A(d, "Y", "0")),
                        Type = A(d, "Type", "HORIZONTAL"),
                    };
                    dv.Clipped = IsClipped(dv.Y, 0);
                    p.Dividers.Add(dv);
                }
            }
        }
        if (p.Sheets.Count == 0) p.Sheets.Add((1, ""));

        var ext = pou.Elements().FirstOrDefault(e => e.Name.LocalName == "POU_EXT");
        if (ext != null)
        {
            foreach (var mac in ext.Elements().Where(e => e.Name.LocalName == "Macros"))
            {
                string n = A(mac, "Name");
                if (n != "DRAWING_NUMBER" && n != "DRAWING_TITLE" && n != "REVISION") continue;
                string val = mac.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value ?? "";
                if (n == "DRAWING_NUMBER") p.DrawingNumber = val;
                else if (n == "DRAWING_TITLE") p.DrawingTitle = val;
                else p.Rev = val;
            }
        }

        int docIndex = 0;
        foreach (var el in fbd.Elements())
        {
            string ln = el.Name.LocalName;
            if (ln == "comment")
            {
                var c = new ExComment
                {
                    DocIndex = docIndex++,
                    X = int.Parse(A(Pos(el)!, "x", "0")),
                    Y = int.Parse(A(Pos(el)!, "y", "0")),
                    W = int.Parse(A(el, "width", "0")),
                    H = int.Parse(A(el, "height", "0")),
                    Sheet = int.Parse(AL(Pos(el)!, "SheetNumber", "1")),
                };
                var cx = el.Elements().FirstOrDefault(e => e.Name.LocalName == "COMMENT_EXT");
                if (cx != null)
                {
                    c.Align = TextTrim(cx.Elements().FirstOrDefault(e => e.Name.LocalName == "TEXTALIGNMENT"));
                    c.Border = TextTrim(cx.Elements().FirstOrDefault(e => e.Name.LocalName == "BORDER"));
                    c.Size = TextTrim(cx.Elements().FirstOrDefault(e => e.Name.LocalName == "TEXTSIZE"));
                    c.Expand = TextTrim(cx.Elements().FirstOrDefault(e => e.Name.LocalName == "EXPANDMACROS"));
                    c.Macro = TextTrim(cx.Elements().FirstOrDefault(e => e.Name.LocalName == "MACROEDITING"));
                    c.Behind = TextTrim(cx.Elements().FirstOrDefault(e => e.Name.LocalName == "BEHINDLOGIC"));
                }
                c.Text = el.Elements().FirstOrDefault(e => e.Name.LocalName == "content")?.Value ?? "";
                c.Clipped = IsClipped(c.Y, c.H);
                p.Comments.Add(c);
                p.ElemOrder.Add(c);
                c.ExportId = int.Parse(A(el, "localId", "-1"));
                p.ByExportId[c.ExportId] = c;
            }
            else if (ln is "inVariable" or "outVariable" or "inOutVariable")
            {
                string kind = ln == "inVariable" ? "in" : ln == "outVariable" ? "out" : "inOut";
                var v = new ExVarPlace
                {
                    DocIndex = docIndex++,
                    Kind = kind,
                    X = int.Parse(A(Pos(el)!, "x", "0")),
                    Y = int.Parse(A(Pos(el)!, "y", "0")),
                    W = int.Parse(A(el, "width", "0")),
                    H = int.Parse(A(el, "height", "0")),
                    Sheet = int.Parse(AL(Pos(el)!, "SheetNumber", "1")),
                    Expression = TextTrim(el.Elements().FirstOrDefault(e => e.Name.LocalName == "expression")),
                };
                var cpi = el.Elements().FirstOrDefault(e => e.Name.LocalName == "connectionPointIn");
                v.InConn = ParseConn(cpi?.Elements().FirstOrDefault(e => e.Name.LocalName == "connection"));
                var itemExt = el.Elements().FirstOrDefault(e => e.Name.LocalName == "FBDITEM_EXT");
                var dtEl = itemExt?.Elements().FirstOrDefault(e => e.Name.LocalName == "DATATYPE");
                if (dtEl != null) v.DataType = TextTrim(dtEl);
                var annText = itemExt?.Elements().FirstOrDefault(e => e.Name.LocalName == "TEXT");
                if (annText != null) v.Annotation = TextTrim(annText);
                var annFlag = itemExt?.Elements().FirstOrDefault(e => e.Name.LocalName == "ANNOTATE");
                if (annFlag != null) v.Annotate = TextTrim(annFlag);
                var annMon = itemExt?.Elements().FirstOrDefault(e => e.Name.LocalName == "ANNOTATEMONITORVALUE");
                if (annMon != null) v.MonitorValue = TextTrim(annMon);
                v.IsConstant = IsConstantName(v.Expression);
                if (v.InConn != null) { v.InConn.Owner = v; p.ConnOrder.Add(v.InConn); }
                p.Placements.Add(v);
                p.ElemOrder.Add(v);
                v.ExportId = int.Parse(A(el, "localId", "-1"));
                p.ByExportId[v.ExportId] = v;
            }
            else if (ln == "block")
            {
                var b = new ExBlock
                {
                    DocIndex = docIndex++,
                    TypeName = A(el, "typeName"),
                    InstanceName = A(el, "instanceName"),
                    X = int.Parse(A(Pos(el)!, "x", "0")),
                    Y = int.Parse(A(Pos(el)!, "y", "0")),
                    W = int.Parse(A(el, "width", "0")),
                    H = int.Parse(A(el, "height", "0")),
                    Sheet = int.Parse(AL(Pos(el)!, "SheetNumber", "1")),
                };
                var iv = el.Elements().FirstOrDefault(e => e.Name.LocalName == "inputVariables");
                if (iv != null)
                    foreach (var pinEl in iv.Elements().Where(e => e.Name.LocalName == "variable"))
                    {
                        var pin = new ExPinIn { Name = A(pinEl, "formalParameter") };
                        var cpi = pinEl.Elements().FirstOrDefault(e => e.Name.LocalName == "connectionPointIn");
                        pin.Conn = ParseConn(cpi?.Elements().FirstOrDefault(e => e.Name.LocalName == "connection"));
                        if (pin.Conn != null) { pin.Conn.Owner = b; pin.Conn.OwnerPin = pin.Name; p.ConnOrder.Add(pin.Conn); }
                        b.Inputs.Add(pin);
                    }
                var ov = el.Elements().FirstOrDefault(e => e.Name.LocalName == "outputVariables");
                if (ov != null)
                    foreach (var pinEl in ov.Elements().Where(e => e.Name.LocalName == "variable"))
                        b.Outputs.Add(new ExPinOut { Name = A(pinEl, "formalParameter") });
                b.Clipped = IsClipped(b.Y, b.H);
                var dsEl = el.Elements().FirstOrDefault(e => e.Name.LocalName == "DOUBLE_SPACE_TERMINAL");
                if (dsEl != null) b.DoubleSpace = TextTrim(dsEl).Equals("true", StringComparison.OrdinalIgnoreCase);
                p.Blocks.Add(b);
                p.ElemOrder.Add(b);
                b.ExportId = int.Parse(A(el, "localId", "-1"));
                p.ByExportId[b.ExportId] = b;
            }
        }

        // Function-block instances used in the body are declared in the FBD's
        // declarations collection even when they are not named in <globalVars>.
        var declared = new HashSet<string>(p.Vars.Select(v => v.Name), StringComparer.Ordinal);
        foreach (var b in p.Blocks)
            if (b.InstanceName.Length > 0 && declared.Add(b.InstanceName))
                p.Vars.Add(new ExVar { Name = b.InstanceName, Section = "", DataType = "", Derived = b.TypeName, Doc = "" });

        return p;
    }

    private static bool IsConstantName(string name)
    {
        if (name.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return true;
        return double.TryParse(name, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
    }

    // A name is written to the FBD declarations only when it is a plain identifier, is not
    // a literal, and does not shadow a library element. Anything else (empty, "dir/…",
    // "L%4102", "max"/"min") is a drawn placeholder that TriStation never declares.
    private static bool IsDeclarableName(string name)
    {
        if (string.IsNullOrEmpty(name) || IsConstantName(name)) return false;
        if (name.IndexOfAny(new[] { '/', '%', ' ' }) >= 0) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return !LibraryNames.Contains(name);
    }

    private static bool IsBoolConstant(ExVarPlace v) =>
        v.DataType.Equals("BOOL", StringComparison.OrdinalIgnoreCase) ||
        v.Expression.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
        v.Expression.Equals("FALSE", StringComparison.OrdinalIgnoreCase);

    private static ExConn? ParseConn(XElement? connEl)
    {
        if (connEl == null) return null;
        var c = new ExConn
        {
            SrcExportId = int.Parse(A(connEl, "refLocalId", "-1")),
            SrcPin = A(connEl, "formalParameter"),
        };
        foreach (var pos in connEl.Elements().Where(e => e.Name.LocalName == "position"))
            c.Path.Add((int.Parse(A(pos, "x", "0")), int.Parse(A(pos, "y", "0"))));
        return c;
    }

    // ============================================================ wire building

    private sealed class Plan
    {
        public ExPou P = null!;
        public List<Wire> Wires = new();
        public Dictionary<ExElem, PlacePlan> Places = new();
        public Dictionary<ExDivider, int> DividerIds = new();
        public List<BlockPlan> Blocks = new();
        public Dictionary<string, ExVar> VarByName = new();
        public List<BlockPlan> BlockSeq = new();
        public int TempCount;
        public int LastId;
        public int PrimarySpan;
    }

    private static int TargetPriority(ExPou p, Wire w)
    {
        if (w.TargetIsBlockInput) return 2;
        var name = w.TargetPin;
        var v = p.Vars.FirstOrDefault(x => x.Name == name);
        return v?.Section switch
        {
            "output" => 0,
            "inOut" => 1,
            "local" => 1,
            _ => 3,
        };
    }

    private static int Cross((int X, int Y) o, (int X, int Y) a, (int X, int Y) b)
        => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    private static bool OnSegment((int X, int Y) q, (int X, int Y) a, (int X, int Y) b)
    {
        if (Cross(q, a, b) != 0) return false;
        return Math.Min(a.X, b.X) <= q.X && q.X <= Math.Max(a.X, b.X)
            && Math.Min(a.Y, b.Y) <= q.Y && q.Y <= Math.Max(a.Y, b.Y);
    }

    // ============================================================ net decomposition
    //
    // TriStation stores one authoring net as arms split at junctions: through each junction
    // the two collinear arms are merged into one wire and every other arm is emitted as its
    // own wire. The export lists one connection per sink, so a merge net shows up as several
    // connections sharing geometry. This rebuilds the editor's decomposition.
    private static void DecomposeNets(Plan plan)
    {
        var live = plan.Wires.Where(w => !w.Suppress).ToList();
        if (live.Count < 2) return;

        int n = live.Count;
        var par = Enumerable.Range(0, n).ToArray();
        int Find(int a) { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; }
        void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) par[rb] = ra; }

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (ShareGeometry(live[i].PathExport, live[j].PathExport)) Union(i, j);

        var nets = new Dictionary<int, List<Wire>>();
        for (int i = 0; i < n; i++)
        {
            int r = Find(i);
            if (!nets.TryGetValue(r, out var lst)) { lst = new List<Wire>(); nets[r] = lst; }
            lst.Add(live[i]);
        }

        var replaced = new HashSet<Wire>();
        var added = new List<Wire>();
        foreach (var net in nets.Values)
        {
            if (net.Count < 2) continue;

            var allPts = new HashSet<(int X, int Y)>();
            foreach (var w in net) foreach (var p in w.PathExport) allPts.Add(p);
            var pts = allPts.ToList();

            var adj = new Dictionary<(int X, int Y), HashSet<(int X, int Y)>>();
            void AddEdge((int X, int Y) a, (int X, int Y) b)
            {
                if (a == b) return;
                if (!adj.TryGetValue(a, out var la)) { la = new HashSet<(int X, int Y)>(); adj[a] = la; }
                if (!adj.TryGetValue(b, out var lb)) { lb = new HashSet<(int X, int Y)>(); adj[b] = lb; }
                la.Add(b); lb.Add(a);
            }
            foreach (var w in net)
                for (int k = 0; k + 1 < w.PathExport.Count; k++)
                {
                    var a = w.PathExport[k]; var b = w.PathExport[k + 1];
                    var between = pts.Where(s => s != a && s != b && OnSegment(s, a, b))
                        .OrderBy(s => Math.Abs(s.X - a.X) + Math.Abs(s.Y - a.Y)).ToList();
                    var chain = new List<(int X, int Y)> { a };
                    chain.AddRange(between); chain.Add(b);
                    for (int t = 0; t + 1 < chain.Count; t++) AddEdge(chain[t], chain[t + 1]);
                }

            int Deg((int X, int Y) p) => adj.TryGetValue(p, out var s) ? s.Count : 0;
            var junctions = adj.Keys.Where(p => Deg(p) >= 3).ToHashSet();
            if (junctions.Count == 0) continue;
            // Only rebuild simple taps (one junction, three arms). Larger fan-out nets keep the
            // export's per-connection wires, which is what the editor stores for them.
            if (junctions.Count != 1) continue;

            var usedEdge = new HashSet<string>();
            var arms = new List<List<(int X, int Y)>>();
            void WalkFrom((int X, int Y) start, (int X, int Y) first)
            {
                var arm = new List<(int X, int Y)> { start, first };
                var prev = start; var cur = first;
                while (!junctions.Contains(cur) && Deg(cur) == 2)
                {
                    var nxt = adj[cur].First(x => x != prev);
                    arm.Add(nxt); prev = cur; cur = nxt;
                }
                for (int t = 0; t + 1 < arm.Count; t++) usedEdge.Add(EdgeKey(arm[t], arm[t + 1]));
                arms.Add(arm);
            }
            foreach (var j in junctions)
                foreach (var nb in adj[j])
                    if (!usedEdge.Contains(EdgeKey(j, nb))) WalkFrom(j, nb);

            var used = new bool[arms.Count];
            foreach (var w in net) replaced.Add(w);

            if (Environment.GetEnvironmentVariable("PT2_NETDBG") == "1")
            {
                Console.Error.WriteLine($"[net] junctions={junctions.Count} arms={arms.Count} juncts={string.Join(" ", junctions.Select(p => $"{p.X},{p.Y}"))}");
                for (int a = 0; a < arms.Count; a++)
                    Console.Error.WriteLine($"   arm{a}: {string.Join(" ; ", arms[a].Select(p => $"{p.X},{p.Y}"))}");
            }

            for (int i = 0; i < arms.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                var cur = arms[i];
                for (int pass = 0; pass < 2; pass++)
                {
                    int end = pass == 0 ? cur.Count - 1 : 0;
                    var jpt = cur[end];
                    if (!junctions.Contains(jpt)) continue;
                    var inDir = end == cur.Count - 1
                        ? Dir(cur[end], cur[end - 1]) : Dir(cur[0], cur[1]);
                    for (int k = 0; k < arms.Count; k++)
                    {
                        if (used[k]) continue;
                        int other = arms[k][0] == jpt ? 0 : arms[k][^1] == jpt ? arms[k].Count - 1 : -1;
                        if (other < 0) continue;
                        var outDir = other == 0
                            ? Dir(arms[k][0], arms[k][1]) : Dir(arms[k][^1], arms[k][^2]);
                        bool parallel = inDir.X * outDir.Y == inDir.Y * outDir.X;
                        bool opposite = inDir.X * outDir.X + inDir.Y * outDir.Y < 0;
                        if (parallel && opposite)
                        {
                            used[k] = true;
                            // orient arm i so it starts at the junction, arm k so it ends there
                            var iOriented = end == cur.Count - 1
                                ? Enumerable.Reverse(cur).ToList() : new List<(int X, int Y)>(cur);
                            var kOriented = other == 0
                                ? Enumerable.Reverse(arms[k]).ToList() : new List<(int X, int Y)>(arms[k]);
                            var merged = new List<(int X, int Y)>(kOriented);
                            for (int q = 1; q < iOriented.Count; q++) merged.Add(iOriented[q]);
                            cur = merged;
                            break;
                        }
                    }
                }
                added.Add(MakeDecomposedWire(net[0], cur));
                if (Environment.GetEnvironmentVariable("PT2_NETDBG") == "1")
                    Console.Error.WriteLine($"   ->wire: {string.Join(" ; ", cur.Select(p => $"{p.X},{p.Y}"))}");
            }
        }

        if (replaced.Count == 0) return;
        var final = plan.Wires.Where(w => !replaced.Contains(w)).ToList();
        final.AddRange(added);
        plan.Wires = final;
    }

    private static bool ShareGeometry(List<(int X, int Y)> A, List<(int X, int Y)> B)
    {
        foreach (var p in A) if (B.Contains(p)) return true;
        for (int k = 0; k + 1 < B.Count; k++)
            foreach (var p in A) if (OnSegment(p, B[k], B[k + 1])) return true;
        for (int k = 0; k + 1 < A.Count; k++)
            foreach (var p in B) if (OnSegment(p, A[k], A[k + 1])) return true;
        return false;
    }

    private static string EdgeKey((int X, int Y) a, (int X, int Y) b)
        => a.X < b.X || (a.X == b.X && a.Y <= b.Y)
            ? $"{a.X},{a.Y}|{b.X},{b.Y}" : $"{b.X},{b.Y}|{a.X},{a.Y}";

    private static (int X, int Y) Dir((int X, int Y) a, (int X, int Y) b) => (b.X - a.X, b.Y - a.Y);

    private static Wire MakeDecomposedWire(Wire template, List<(int X, int Y)> path)
    {
        var rev = new List<(int X, int Y)>(path);
        rev.Reverse();
        return new Wire
        {
            Source = template.Source,
            Target = template.Target,
            SourcePin = template.SourcePin,
            TargetPin = template.TargetPin,
            SourceVar = template.SourceVar,
            SourceBlock = template.SourceBlock,
            TargetVar = template.TargetVar,
            TargetBlock = template.TargetBlock,
            TargetIsBlockInput = template.TargetIsBlockInput,
            PathExport = rev,
            Decomposed = true,
        };
    }

    private static Plan BuildPlan(ExPou p)
    {
        var plan = new Plan { P = p };
        foreach (var v in p.Vars) plan.VarByName[v.Name] = v;

        // 1. raw wires in connection document order
        var connWire = new Dictionary<ExConn, Wire>();

        // A POU may reuse localIds: SYSTEMSOE exports four identical SOE sub-networks that
        // share block localIds (1238/1239) and variable localIds. The sub-networks repeat at a
        // fixed document period (each begins with the same block id), so group the elements
        // into copies and resolve each refLocalId only against elements in the owner's own
        // copy. A connection whose source id has no element in the same copy is dropped (this
        // matches SYSTEMSOE's lossy 4th copy, which only carries 5 of its 32 input vars).
        var byId = new Dictionary<int, List<ExElem>>();
        foreach (var e in p.ElemOrder)
        {
            if (e.ExportId < 0) continue;
            if (!byId.TryGetValue(e.ExportId, out var lst)) { lst = new List<ExElem>(); byId[e.ExportId] = lst; }
            lst.Add(e);
        }

        int period = 0, anchorDoc = -1;
        {
            foreach (var e in p.ElemOrder) if (e is ExBlock) { anchorDoc = e.DocIndex; break; }
            if (anchorDoc >= 0)
            {
                int anchorId = p.ElemOrder.First(e => e is ExBlock).ExportId;
                foreach (var e in p.ElemOrder)
                    if (e.DocIndex > anchorDoc && e.ExportId == anchorId) { period = e.DocIndex - anchorDoc; break; }
            }
        }
        foreach (var e in p.ElemOrder)
            e.CopyId = (period > 0 && e.DocIndex >= anchorDoc) ? (e.DocIndex - anchorDoc) / period : 0;

        ExElem? ResolveSrc(int srcId, ExElem owner)
        {
            if (!byId.TryGetValue(srcId, out var lst) || lst.Count == 0) return null;
            if (lst.Count == 1) return lst[0];
            ExElem? best = null;
            foreach (var e in lst)
                if (e.CopyId == owner.CopyId && (best == null || Math.Abs(e.DocIndex - owner.DocIndex) < Math.Abs(best.DocIndex - owner.DocIndex))) best = e;
            return best;
        }

        foreach (var c in p.ConnOrder)
        {
            var src = ResolveSrc(c.SrcExportId, c.Owner!);
            if (src == null) continue;
            var tgt = c.Owner!;
            if (src.Clipped || tgt.Clipped) continue;
            var w = new Wire { Source = src, Target = tgt, PathExport = new List<(int, int)>(c.Path) };
            if (src is ExVarPlace sv) { w.SourceVar = sv; w.SourcePin = sv.Expression; }
            else if (src is ExBlock sb)
            {
                w.SourceBlock = sb;
                w.SourcePin = c.SrcPin.Length > 0 ? c.SrcPin
                    : (sb.Outputs.Count > 0 ? sb.Outputs[0].Name : "");
            }
            if (tgt is ExVarPlace tv) { w.TargetVar = tv; w.TargetPin = tv.Expression; }
            else if (tgt is ExBlock tb) { w.TargetBlock = tb; w.TargetPin = c.OwnerPin; w.TargetIsBlockInput = true; }
            bool degenerate = c.Path.Count >= 1 && c.Path.All(pp => pp == c.Path[0]);
            bool srcDrop = w.SourceVar != null && w.SourceVar.IsConstant
                && (w.SourceVar.Kind != "in" || degenerate || IsBoolConstant(w.SourceVar));
            bool tgtDrop = w.TargetVar != null && w.TargetVar.IsConstant && w.TargetVar.Kind != "in";
            if (srcDrop || tgtDrop) w.Suppress = true;
            plan.Wires.Add(w);
            connWire[c] = w;
        }

        // 1b. rebuild merge nets the way the editor stores them (junctions + collinear arms).
        // Disabled: the single-junction/collinear-pair rule fixed CHANGEOVER geometry but
        // over-merged ~10 AI_COND nets (good keeps those arms separate), so it is not yet
        // reliable. Keep the export's per-connection wires until the rule is exact.
        // DecomposeNets(plan);

        // 2. group by source -> choose main wire, mark branches, compute junctions
        foreach (var g in plan.Wires.Where(w => !w.Suppress && !w.Decomposed).GroupBy(w => (w.Source, w.SourcePin)))
        {
            var list = g.ToList();
            if (list.Count == 1) continue;
            var main = list.OrderBy(w => TargetPriority(p, w)).ThenByDescending(w => w.Target.X).First();
            foreach (var b in list)
            {
                if (ReferenceEquals(b, main)) continue;
                b.IsBranch = true;
                b.Parent = main;

                // common suffix of branch path and main path (both target -> source)
                var pathB = b.PathExport;
                var pathM = main.PathExport;
                int k = 0;
                while (k < pathB.Count && k < pathM.Count
                       && pathB[pathB.Count - 1 - k] == pathM[pathM.Count - 1 - k]) k++;
                int keep = pathB.Count - k;
                if (keep < 1) keep = 1;
                b.PathExport = pathB.Take(keep).ToList();
            }
        }

        // 3. main paths: reverse, insert junctions in path order, number Vn
        foreach (var g in plan.Wires.Where(w => !w.Suppress && !w.Decomposed).GroupBy(w => (w.Source, w.SourcePin)))
        {
            var main = g.FirstOrDefault(w => !w.IsBranch);
            if (main == null) continue;
            var branches = g.Where(w => w.IsBranch).ToList();
            if (branches.Count == 0) { main.PathExport.Reverse(); continue; }

            var rev = Enumerable.Reverse(main.PathExport).ToList();
            var junctions = branches.Select(b => (Point: b.PathExport[^1], Branch: b)).ToList();

            // order junctions by (segment index, distance from segment start)
            var ordered = new List<((int X, int Y) Point, Wire Branch, int Seg, long Dist)>();
            foreach (var j in junctions)
            {
                for (int i = 0; i + 1 < rev.Count; i++)
                {
                    if (OnSegment(j.Point, rev[i], rev[i + 1]))
                    {
                        var d = (long)(j.Point.X - rev[i].X) * (j.Point.X - rev[i].X)
                              + (long)(j.Point.Y - rev[i].Y) * (j.Point.Y - rev[i].Y);
                        ordered.Add((j.Point, j.Branch, i, d));
                        break;
                    }
                }
            }
            ordered = ordered.OrderBy(o => o.Seg).ThenBy(o => o.Dist).ToList();
            for (int n = 0; n < ordered.Count; n++) ordered[n].Branch.Vn = n + 1;

            var final = new List<(int, int)>();
            var bySeg = ordered.GroupBy(o => o.Seg).ToDictionary(g2 => g2.Key, g2 => g2.ToList());
            for (int i = 0; i + 1 < rev.Count; i++)
            {
                final.Add(rev[i]);
                if (bySeg.TryGetValue(i, out var js))
                    foreach (var j in js) final.Add(j.Point);
            }
            final.Add(rev[^1]);
            main.PathExport = final;
        }

        // 4. element ids: the export already carries authoritative localIds (the XML
        //    localId attribute). TriStation preserves them verbatim, so we reuse them
        //    instead of renumbering. The "primary span" is the maximum element localId;
        //    the duplicated (second) copy of the diagram is offset by this span, and the
        //    generated wires continue after it.
        _commentIds.Clear();
        int maxElemId = 0;
        foreach (var e in p.ElemOrder)
            if (e.ExportId > maxElemId) maxElemId = e.ExportId;
        plan.PrimarySpan = maxElemId;

        foreach (var dv in p.Dividers.Where(d => !d.Clipped).OrderBy(d => d.DocIndex))
            plan.DividerIds[dv] = dv.ExportId >= 0 ? dv.ExportId : ++maxElemId;

        foreach (var b in p.Blocks.Where(b => !b.Clipped))
            plan.Blocks.Add(new BlockPlan { B = b, Id = b.ExportId >= 0 ? b.ExportId : ++maxElemId });

        foreach (var e in p.ElemOrder.Where(e => !e.Clipped))
        {
            if (e is ExVarPlace vp) plan.Places[vp] = new PlacePlan { P = vp, Id = vp.ExportId >= 0 ? vp.ExportId : ++maxElemId };
            else if (e is ExComment cm) _commentIds[cm] = cm.ExportId >= 0 ? cm.ExportId : ++maxElemId;
        }

        // wire ids continue after the elements (reference keeps edit-history gaps in the wire
        // range that cannot be reproduced generically; contiguous ids are an accepted
        // deviation). The primary span is the largest id used by the first (primary) copy;
        // the second copy is offset by exactly this span and lastUniqueID is twice it.
        int wireId = maxElemId + 1;
        foreach (var w in plan.Wires.Where(w => !w.Suppress)) w.Id = wireId++;
        plan.PrimarySpan = wireId - 1;
        // Programs are stored once, so their id space is not doubled.
        plan.LastId = p.PouType == "program" ? plan.PrimarySpan : 2 * plan.PrimarySpan;
        return plan;
    }

    private static readonly Dictionary<ExComment, int> _commentIds = new();

    // ============================================================ ST layout emulation

    private static void ComputeLayout(Plan plan)
    {
        var p = plan.P;
        // topological order (Kahn), ready ordered by (Y, X)
        var blocks = plan.Blocks;
        var remaining = new List<BlockPlan>(blocks);
        var seq = new List<BlockPlan>();
        var deps = new Dictionary<BlockPlan, HashSet<BlockPlan>>();
        foreach (var b in blocks)
        {
            var d = new HashSet<BlockPlan>();
            foreach (var w in plan.Wires)
                if (w.TargetBlock == b.B && w.SourceBlock != null)
                {
                    var sp = blocks.FirstOrDefault(x => x.B == w.SourceBlock);
                    if (sp != null && !ReferenceEquals(sp, b)) d.Add(sp);
                }
            deps[b] = d;
        }
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(b => deps[b].All(d => !remaining.Contains(d))).ToList();
            if (ready.Count == 0) ready = new List<BlockPlan>(remaining);
            var pick = ready.OrderBy(b => b.B.Y).ThenBy(b => b.B.X).First();
            seq.Add(pick);
            remaining.Remove(pick);
        }
        for (int i = 0; i < seq.Count; i++) seq[i].Sequence = i + 1;

        // wire wiring per block plan
        foreach (var bp in blocks)
        {
            bp.OutWires.Clear();
            foreach (var pin in bp.B.Outputs)
            {
                var w = plan.Wires.FirstOrDefault(x => x.SourceBlock == bp.B && x.SourcePin == pin.Name && !x.IsBranch && !x.Suppress);
                if (w != null) bp.OutWires[pin.Name] = w;
            }
            bp.InWires = bp.B.Inputs
                .Select(pin => plan.Wires.FirstOrDefault(x => x.TargetBlock == bp.B && x.TargetPin == pin.Name))
                .ToList();
            bp.SinkAssigns = new List<Wire>();
            foreach (var pin in bp.B.Outputs)
            {
                if (!bp.OutWires.TryGetValue(pin.Name, out var main)) continue;
                if (main.TargetVar != null) bp.SinkAssigns.Add(main);
            }
            // branch var sinks (branch wires off any of this block's main wires)
            var branchSinks = plan.Wires
                .Where(x => x.IsBranch && x.TargetVar != null && !x.Suppress && x.Parent != null
                            && x.Parent.SourceBlock == bp.B)
                .OrderBy(x => x.Vn);
            bp.SinkAssigns.AddRange(branchSinks);
        }

        // temp allocation
        var tempOf = new Dictionary<BlockPlan, int>();
        var live = new Dictionary<int, BlockPlan>();
        var emitted = new HashSet<BlockPlan>();
        foreach (var b in seq)
        {
            if (b.B.IsFunction)
            {
                int idx = 1;
                while (live.ContainsKey(idx)) idx++;
                tempOf[b] = idx;
                b.Temp = "_TEMP_BOOL_" + idx;
                live[idx] = b;
            }
            emitted.Add(b);
            foreach (var kv in live.ToList())
            {
                var consumers = blocks.Where(x => x != kv.Value && deps[x].Contains(kv.Value));
                if (consumers.All(c => emitted.Contains(c))) live.Remove(kv.Key);
            }
        }
        int tempCount = blocks.Count(b => b.B.IsFunction) == 0 ? 0 : tempOf.Values.DefaultIfEmpty(0).Max();

        // header line computation
        var lines = new List<string>();
        void Add(string s) => lines.Add(s);
        int Line() => lines.Count + 1;

        Add("(* Line 1: Extern Declaration *)");
        var fbTypes = new List<string>();
        foreach (var b in seq)
            if (!b.B.IsFunction && !fbTypes.Contains(b.B.TypeName)) fbTypes.Add(b.B.TypeName);
        foreach (var t in fbTypes)
        {
            Add($"FUNCTION_BLOCK EXTERN {t}");
            if (Lib.TryGetValue(t, out var meta))
            {
                var ins = meta.Pins.Where(x => x.Vt == 0).ToList();
                var outs = meta.Pins.Where(x => x.Vt == 1).ToList();
                var ios = meta.Pins.Where(x => x.Vt == 3).ToList();
                if (ins.Count > 0) { Add("VAR_INPUT"); foreach (var x in ins) Add($"\t{x.Name} : {x.DataType};"); Add("END_VAR"); }
                if (outs.Count > 0) { Add("VAR_OUTPUT"); foreach (var x in outs) Add($"\t{x.Name} : {x.DataType};"); Add("END_VAR"); }
                if (ios.Count > 0) { Add("VAR"); foreach (var x in ios) Add($"\t{x.Name} : {x.DataType};"); Add("END_VAR"); }
            }
            Add("END_FUNCTION_BLOCK");
        }
        Add("");
        Add($"(* Line {Line()}: POU Header *)");
        Add($"FUNCTION_BLOCK {p.Name}");
        var declLine = new Dictionary<string, int>();
        var inputs = p.Vars.Where(v => v.Section == "input").ToList();
        var inouts = p.Vars.Where(v => v.Section == "inOut").ToList();
        var outputs = p.Vars.Where(v => v.Section == "output").ToList();
        var locals = p.Vars.Where(v => v.Section == "local").ToList();
        void VarSection(string kw, List<ExVar> vs)
        {
            if (vs.Count == 0) return;
            Add(kw);
            foreach (var v in vs) { declLine[v.Name] = Line(); Add($"\t{v.Name} : {v.DataType}{(v.Derived.Length > 0 ? v.Derived : "")};"); }
            Add("END_VAR");
        }
        VarSection("VAR_INPUT", inputs);
        VarSection("VAR_OUTPUT", outputs);
        VarSection("VAR", locals);
        if (tempCount > 0)
        {
            Add("VAR_TEMP");
            for (int i = 1; i <= tempCount; i++) Add($"\t_TEMP_BOOL_{i} : BOOL;");
            Add("END_VAR");
        }
        Add("");
        Add($"(* Line {Line()}: POU Body *)");
        foreach (var s in p.Sheets.OrderBy(s => s.Number))
            Add($"(* Line {Line()}: Network{s.Number}, Begin *)");

        foreach (var b in seq)
        {
            b.FirstLine = Line();
            string t = b.B.TypeName;
            if (b.B.IsFunction)
                Add($"(* Line {b.FirstLine}: Network{b.B.Sheet}, Block {b.Sequence}, FUNCTION \"{t}\"  *)");
            else
                Add($"(* Line {b.FirstLine}: Network{b.B.Sheet}, Block {b.Sequence}, FUNCTION_BLOCK \"{b.B.InstanceName}\", Type \"{t}\"  *)");
            foreach (var l in ExprLines(b)) Add(l);
        }
        Add("END_FUNCTION_BLOCK");
        int endLine = Line() - 1;
        for (int i = 0; i < seq.Count; i++)
        {
            int nextLine = i + 1 < seq.Count ? seq[i + 1].FirstLine : endLine;
            seq[i].LineCount = nextLine - seq[i].FirstLine;
        }

        // store temp on block plan and per-placement line numbers
        foreach (var bp in blocks)
            if (tempOf.TryGetValue(bp, out var idx)) bp.Temp = "_TEMP_BOOL_" + idx;

        // attach wires to placements
        foreach (var w in plan.Wires)
        {
            if (!w.IsBranch && w.SourceVar != null && plan.Places.TryGetValue(w.SourceVar, out var ps))
                ps.OutWire = w;
            if (w.TargetVar != null && plan.Places.TryGetValue(w.TargetVar, out var pt))
                pt.InWire = w;
        }

        foreach (var pp in plan.Places.Values)
        {
            if (pp.OutWire != null)
            {
                var tgt = pp.OutWire.TargetBlock;
                var bp = tgt == null ? null : blocks.FirstOrDefault(x => x.B == tgt);
                pp.LineNumber = bp != null ? bp.FirstLine - 1 : 0;
            }
            else if (pp.InWire != null)
                pp.LineNumber = declLine.TryGetValue(pp.P.Expression, out var ln) ? ln : 0;
            else
                pp.LineNumber = declLine.TryGetValue(pp.P.Expression, out var ln2) ? ln2 : 0;
        }

        // stash for emission
        plan.BlockSeq = seq;
        plan.TempCount = tempCount;

        IEnumerable<string> ExprLines(BlockPlan b)
        {
            string Resolve(Wire w)
            {
                if (w.IsBranch && w.Parent != null) return Resolve(w.Parent);
                if (w.SourceVar != null) return w.SourceVar.Expression;
                if (w.SourceBlock != null)
                {
                    var sp = blocks.FirstOrDefault(x => x.B == w.SourceBlock);
                    return w.SourceBlock.IsFunction ? (sp?.Temp ?? "") : $"{w.SourceBlock.InstanceName}.{w.SourcePin}";
                }
                return "";
            }
            if (!b.B.IsFunction)
            {
                yield return $"\t{b.B.InstanceName}(";
                for (int i = 0; i < b.B.Inputs.Count; i++)
                {
                    var w = b.InWires[i];
                    if (w == null) continue;
                    yield return $"\t{b.B.Inputs[i].Name} :=";
                    yield return "\t(";
                    yield return $"\t\t{Resolve(w)}";
                    yield return "\t)";
                }
                yield return "\t);";
            }
            else
            {
                var ins = b.InWires.Where(w => w != null).Select(w => Resolve(w!)).ToList();
                string op = b.B.Outputs.Count > 0 ? b.B.Outputs[0].Name : b.B.TypeName;
                yield return $"\t{b.Temp} :=(";
                if (ins.Count <= 1)
                {
                    yield return "\t";
                    if (ins.Count == 1)
                    {
                        yield return "\t(";
                        yield return $"\t\t{ins[0]}";
                        yield return "\t)";
                    }
                }
                else
                {
                    for (int i = 0; i < ins.Count; i++)
                    {
                        if (i > 0) yield return $"\t{op}";
                        yield return "\t(";
                        yield return $"\t\t{ins[i]}";
                        yield return "\t)";
                    }
                }
                yield return "\t);";
            }
            foreach (var s in b.SinkAssigns)
            {
                yield return $"\t{s.TargetVar!.Expression} :=(";
                yield return $"\t\t{Resolve(s)}";
                yield return "\t);";
            }
        }
    }

    // ============================================================ emission

    private static string Esc(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string CData(string s)
        => "<![CDATA[" + s.Replace("]]>", "]]]]><![CDATA[>") + "]]>";

    private static int Tx(int v) => 10 * v + 40;

    public static byte[] GenerateBytes(string xmlPath, string pouName, IReadOnlyDictionary<string, TagInfo>? tagDesc = null, IReadOnlySet<string>? referencedTypes = null, int templateSheetCount = -1, bool forceDuplicated = false)
        => Encoding.UTF8.GetBytes(Generate(xmlPath, pouName, tagDesc, referencedTypes, templateSheetCount, forceDuplicated));

    private static string Generate(string xmlPath, string pouName, IReadOnlyDictionary<string, TagInfo>? tagDesc = null, IReadOnlySet<string>? referencedTypes = null, int templateSheetCount = -1, bool forceDuplicated = false)
    {
        var p = Parse(xmlPath, pouName, tagDesc, referencedTypes);
        // TriStation stores a duplicated (primary + secondary) diagram for function blocks
        // and for any POU that appears more than once in the export (one <pou> element per
        // stored copy); plain programs appear once and are stored once.
        bool duplicated = p.PouType != "program" || forceDuplicated;
        var plan = BuildPlan(p);
        _ = _commentIds;
        ComputeLayout(plan);

        string sheetTemplate = p.SheetTemplate.Length > 0 ? p.SheetTemplate : "SheetTemplateD";
        string drawingSize = sheetTemplate.StartsWith("SheetTemplate", StringComparison.Ordinal)
            ? sheetTemplate.Substring("SheetTemplate".Length) : "D";
        // Zone counts are fixed per sheet template (not derived from the XML page size):
        // SheetTemplateC = 12x6, SheetTemplateD = 16x8.
        bool cSize = sheetTemplate.EndsWith("C", StringComparison.Ordinal);
        int hZoneCount = cSize ? 12 : 16;
        int vZoneCount = cSize ? 6 : 8;

        var sb = new StringBuilder(20000);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n");
        sb.Append("<SerializableClass Type=\"Serializable\" Version=\"1.0\" ClassType=\"CFBDDocXml\"")
              .Append($" pouName=\"{Esc(p.Name)}\" pouType=\"{Esc(p.PouType)}\" pouVersion=\"{Esc(p.Version)}\"")
          .Append($" dateTime=\"{DateTime.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture)}\"")
          .Append($" sheetTemplate=\"{Esc(sheetTemplate)}\" drawingSize=\"{Esc(drawingSize)}\" hZoneCount=\"{hZoneCount}\" vZoneCount=\"{vZoneCount}\"")
          .Append($" attributes=\"1204\" lastZoom=\"0\" lastMonVar=\"0\" lastUniqueID=\"{plan.LastId}\"")
          .Append(" D0=\"\" D1=\"\" D2=\"\" D3=\"\" D4=\"\" D5=\"\" D6=\"\" D7=\"\" D8=\"\" D9=\"\"")
          .Append($" drawingNumber=\"{Esc(p.DrawingNumber)}\" drawingTitle=\"{Esc(p.DrawingTitle)}\"")
          .Append($" approvedName=\"\" approvedTime=\"\" rev=\"{Esc(p.Rev)}\"");
        sb.Append(p.PouType == "program"
            ? " extOptFlags=\"513\">"
            : " extOptFlags=\"3\" cemEventInputs=\"0\" cemActionOutputs=\"0\">");

        // declarations
        sb.Append("<Collection Name=\"declarations\" ClassType=\"CTS2Vars\" CollectionType=\"1\">");
        // Programs list every referenced tag (globals) first, sorted case-sensitively,
        // then the function-block instances used by the body. FBs keep interface order.
        IEnumerable<ExVar> declOrder = p.Vars;
        if (p.PouType == "program")
            declOrder = p.Vars.Where(v => !v.IsInstance).OrderBy(v => v.Name, StringComparer.Ordinal)
                .Concat(p.Vars.Where(v => v.IsInstance).OrderBy(v => v.Name, StringComparer.Ordinal));
        foreach (var v in declOrder)
        {
            var places = p.Placements.Where(x => x.Expression == v.Name).ToList();
            int srcCount = places.Count(x => plan.Places[x].OutWire != null);
            int sinkCount = places.Count(x => plan.Places[x].InWire != null);
            string vt;
            if (v.Section == "global") vt = "5";
            else if (v.IsInstance) vt = "3";
            else if (v.Section == "output") vt = "1";
            else if (srcCount > 0 && sinkCount > 0) vt = "3";
            else if (sinkCount > 0) vt = "1";
            else if (srcCount > 0) vt = "0";
            else vt = v.Section switch { "inOut" => "3", _ => "0" };
            string flags = v.IsInstance ? "33554434" : "0";
            // The diagram is stored twice (primary + secondary copy), so each placement and
            // each FB instance occurrence is counted twice in the reference's reference
            // counts. A POU with no logic is not duplicated.
            int copies = duplicated ? 2 : 1;
            int refCount = copies * (v.IsInstance
                ? p.Blocks.Count(b => b.InstanceName == v.Name)
                : places.Count);
            // TriStation stores fbRefCount equal to refCount for instances (verified across
            // programs and function blocks); non-instance declarations carry 0.
            string fbrc = v.IsInstance ? refCount.ToString() : "0";
            string desc = v.Doc;
            string dataType = v.IsInstance ? v.Derived : v.DataType;
            if (v.Section == "global")
            {
                if (p.TagDesc != null && p.TagDesc.TryGetValue(v.Name, out var ti))
                {
                    desc = ti.Desc;
                    if (ti.DataType.Length > 0) dataType = ti.DataType;
                }
                else
                {
                    // A declared global not present in the tag database is a program-local,
                    // flagged 65536 only inside programs (function blocks keep flags 0).
                    if (p.PouType == "program") flags = "65536";
                    if ((v.Name.StartsWith("w", StringComparison.Ordinal) || v.Name.StartsWith("v", StringComparison.Ordinal))
                        && (dataType == "REAL" || dataType == "DINT"))
                        dataType = "BOOL";
                }
            }
            sb.Append($"<Class name=\"{Esc(v.Name)}\" dataType=\"{Esc(dataType)}\"")
              .Append($" initVal=\"{Esc(v.InitVal)}\" desc=\"{Esc(desc)}\" varVTType=\"{vt}\" refCount=\"{refCount}\"")
              .Append($" fbRefCount=\"{fbrc}\" flags=\"{flags}\" writes=\"0\"/>");
        }
        sb.Append("</Collection>");

        // hiddenVarTypes
        sb.Append("<Collection Name=\"hiddenVarTypes\" ClassType=\"CTS2HiddenVarTypeXML\" CollectionType=\"1\"/>");

        // snapshots (type first-use by doc order)
        if (p.Blocks.Count == 0)
        {
            sb.Append("<Collection Name=\"snapshots\" ClassType=\"CPRJLogElemSnapshot\" CollectionType=\"1\"/>");
        }
        else
        {
            sb.Append("<Collection Name=\"snapshots\" ClassType=\"CPRJLogElemSnapshot\" CollectionType=\"1\">");
            int snapCopies = duplicated ? 2 : 1;
            // Snapshot records follow first use in the stored sheet order: sheets are written
            // in ascending number, elements within a sheet in document order.
            foreach (var type in p.Blocks.OrderBy(b => b.Sheet).ThenBy(b => b.DocIndex).Select(b => b.TypeName).Distinct())
            {
                int count = snapCopies * p.Blocks.Count(b => b.TypeName == type);
                if (!Lib.TryGetValue(type, out var m))
                {
                    sb.Append($"<Class name=\"{Esc(type)}\" dogTag=\"0\" type=\"8\" subType=\"1\" instanceCount=\"{count}\"")
                      .Append(" commentCount=\"0\" extOptFlags=\"0\" cemEventInputs=\"0\" cemActionOutputs=\"0\">")
                      .Append("<Class Embed=\"eventConnectionsChanged\" eventType=\"0\" eventId=\"0\" date=\"\" user=\"2\"/>")
                      .Append("<Collection Name=\"declarations\" ClassType=\"CTS2Vars\" CollectionType=\"1\"/></Class>");
                    continue;
                }
                sb.Append($"<Class name=\"{Esc(type)}\" dogTag=\"{m.DogTag}\" type=\"{m.Type}\" subType=\"{m.SubType}\"")
                  .Append($" instanceCount=\"{count}\" commentCount=\"0\" extOptFlags=\"{m.ExtOptFlags}\"")
                  .Append($" cemEventInputs=\"{m.CemEI}\" cemActionOutputs=\"{m.CemAO}\">")
                  .Append($"<Class Embed=\"eventConnectionsChanged\" eventType=\"0\" eventId=\"{m.EventId}\" date=\"{m.Date}\" user=\"{m.User}\"/>")
                  .Append("<Collection Name=\"declarations\" ClassType=\"CTS2Vars\" CollectionType=\"1\">");
                foreach (var pin in m.Pins)
                    sb.Append($"<Class name=\"{Esc(pin.Name)}\" dataType=\"{Esc(pin.DataType)}\" initVal=\"{Esc(pin.InitVal)}\" desc=\"\"")
                      .Append($" varVTType=\"{pin.Vt}\" refCount=\"0\" fbRefCount=\"0\" flags=\"{pin.Flags}\" writes=\"0\"/>");
                sb.Append("</Collection></Class>");
            }
            sb.Append("</Collection>");
        }

        // sheets. TriStation stores every logic sheet twice: a "primary" copy using the
        // authored localIds and a "secondary" copy offset by the primary span. A POU with a
        // FACESHEET additionally relocates the secondary copy of its first logic comment on
        // to the FACESHEET (that is the faceplate title), so the FACESHEET displayables hold
        // exactly that one comment.
        int span = plan.PrimarySpan;
        var sheets = p.Sheets.OrderBy(s => s.Number).ToList();

        sb.Append("<Collection Name=\"sheets\" ClassType=\"CFBDSheetXml\" CollectionType=\"1\">");
        foreach (var sheet in sheets)
        {
            bool isFace = sheet.Title.Equals("FACESHEET", StringComparison.OrdinalIgnoreCase);
            sb.Append(SheetHeader(p.IsBuilt && p.PouType != "program" ? sheet.Number : 0, sheet.Title));
            var only = new HashSet<int> { sheet.Number };
            // The FACESHEET holds each of its comments twice, the copy (id+span) first and
            // then the authored object; a logic sheet holds its objects twice with the
            // authored (primary) copy first and the shifted (secondary) copy after.
            if (duplicated && isFace)
            {
                foreach (var item in SheetItems(plan, only, span, null)) sb.Append(item);
                foreach (var item in SheetItems(plan, only, 0, null)) sb.Append(item);
            }
            else
            {
                foreach (var item in SheetItems(plan, only, 0, null)) sb.Append(item);
                if (duplicated)
                    foreach (var item in SheetItems(plan, only, span, null)) sb.Append(item);
            }
            sb.Append("</Collection></Class>");
        }
        // Blank trailing sheets the editor appends/preserves. The count is not derivable
        // from the PLCopen export (empty sheets carry no content), so when a template
        // stream is available we keep however many blanks it had beyond the XML's sheets.
        int trailingEmpty = templateSheetCount >= 0 ? Math.Max(0, templateSheetCount - sheets.Count) : 1;
        for (int i = 0; i < trailingEmpty; i++)
        {
            sb.Append("<Class sheetTitle=\"\" firstBuildNetwork=\"0\" lastBuildNetwork=\"0\"")
              .Append(" S0=\"\" S1=\"\" S2=\"\" S3=\"\" S4=\"\" S5=\"\" S6=\"\" S7=\"\" S8=\"\" S9=\"\">")
              .Append("<Collection Name=\"displayables\" CollectionType=\"1\"/></Class>");
        }
        sb.Append("</Collection>");

        sb.Append("</SerializableClass>\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Emits a sheet's displayables in TriStation's serialization order: each element in
    /// document (authoring) order, immediately followed by the wire(s) anchored at it.
    /// A wire is anchored at its target when the target is a variable placement, otherwise
    /// at its source (so a block-to-block wire follows the feeding block).
    /// <paramref name="idOffset"/> shifts every localId/connectId for the second copy.
    /// </summary>
    private static IEnumerable<string> SheetItems(Plan plan, IReadOnlySet<int> sheets, int idOffset, IReadOnlySet<ExElem>? skip)
    {
        var p = plan.P;

        int AnchorId(Wire w)
        {
            var anchor = w.Target is ExVarPlace ? w.Target : w.Source;
            return ElemIdRaw(plan, anchor);
        }

        var wires = plan.Wires.Where(w => !w.Suppress && sheets.Contains(w.Target.Sheet)).ToList();
        var byAnchor = new Dictionary<int, List<Wire>>();
        foreach (var w in wires)
        {
            int a = AnchorId(w);
            if (!byAnchor.TryGetValue(a, out var lst)) { lst = new List<Wire>(); byAnchor[a] = lst; }
            lst.Add(w);
        }

        var results = new List<string>();
        // document order over element kinds, interleaving dividers and wires
        var elems = p.ElemOrder
            .Where(e => sheets.Contains(e.Sheet) && !e.Clipped && (skip == null || !skip.Contains(e)))
            .OrderBy(e => e.DocIndex).ToList();
        foreach (var elem in elems)
        {
            int id = ElemIdRaw(plan, elem);
            string? xml = elem switch
            {
                ExComment c => EmitComment(c, id + idOffset),
                ExVarPlace v => EmitVar(plan, v, id + idOffset, idOffset),
                ExBlock b => EmitBlock(plan, b, id + idOffset, idOffset),
                _ => null,
            };
            if (xml != null) results.Add(xml);
            if (byAnchor.TryGetValue(id, out var aw))
                foreach (var w in aw.OrderBy(x => x.Id))
                    results.Add(EmitWire(plan, w, idOffset));
        }
        foreach (var dv in plan.DividerIds.Where(kv => sheets.Contains(kv.Key.Sheet)).OrderBy(kv => kv.Key.DocIndex))
            results.Add(EmitDivider(dv.Key, dv.Value + idOffset));
        return results;
    }

    /// <summary>Element id (before any copy offset); matches the plan maps.</summary>
    private static int ElemIdRaw(Plan plan, ExElem e) => elementIdOf(plan, e);

    private static string SheetHeader(int buildNetwork, string title)
        => $"<Class sheetTitle=\"{Esc(title)}\" firstBuildNetwork=\"{buildNetwork}\""
         + $" lastBuildNetwork=\"{buildNetwork}\" S0=\"\" S1=\"\" S2=\"\" S3=\"\" S4=\"\""
         + " S5=\"\" S6=\"\" S7=\"\" S8=\"\" S9=\"\"><Collection Name=\"displayables\" CollectionType=\"1\">";

    private static string EmitDivider(ExDivider d, int id)
    {
        bool horiz = d.Type.Equals("HORIZONTAL", StringComparison.OrdinalIgnoreCase);
        int posX = d.Type.Equals("HORIZONTAL", StringComparison.OrdinalIgnoreCase) ? 31 : Tx(d.X);
        int posY = d.Type.Equals("HORIZONTAL", StringComparison.OrdinalIgnoreCase) ? Tx(d.Y) : 31;
        int width = d.Type.Equals("HORIZONTAL", StringComparison.OrdinalIgnoreCase) ? 3238 : 1;
        int height = d.Type.Equals("HORIZONTAL", StringComparison.OrdinalIgnoreCase) ? 1 : 3238;
        return $"<Class ClassType=\"CDGMnetworkDividerXml\" orientation=\"{Esc(d.Type)}\" localId=\"{id}\""
             + $" posX=\"{posX}\" posY=\"{Tx(d.Y)}\" width=\"{(d.Type == "HORIZONTAL" ? 3238 : 1)}\" height=\"{(d.Type == "HORIZONTAL" ? 1 : 3238)}\"/>";
    }

    private static string EmitComment(ExComment c, int id)
    {
        string text = c.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        return $"<Class ClassType=\"CDGMCommentXml\" localId=\"{id}\" posX=\"{Tx(c.X)}\" posY=\"{Tx(c.Y)}\""
             + $" width=\"{10 * c.W}\" height=\"{10 * c.H}\" textAlignment=\"{Esc(c.Align)}\" border=\"{Esc(c.Border)}\""
             + $" textSize=\"{Esc(c.Size)}\" expandMacros=\"{Title(c.Expand)}\" macroEditing=\"{Title(c.Macro)}\""
             + $" behindLogic=\"{Title(c.Behind)}\"><CData Name=\"text\">{CData(text)}</CData></Class>";
    }

    private static string Title(string s)
        => s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? "True"
         : s.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ? "False" : s;

    private static string? EmitVar(Plan plan, ExVarPlace v, int id, int idOffset = 0)
    {
        var pp = plan.Places[v];
        // Only programs tag variable placements with the build network; TriStation never
        // writes a lineNumber on a placement.
        string netAttr = plan.P.PouType == "program" ? $" network=\"{v.Sheet}\"" : "";
        bool wired = pp.OutWire != null && !pp.OutWire.Suppress;
        if (v.IsConstant && v.Kind != "in" && !wired) return null;
        if (v.IsConstant && v.Kind == "in")
        {
            var ow = pp.OutWire;
            string outConn; int outId;
            if (ow != null && ow.Suppress) { outConn = ow.TargetPin; outId = elementIdOf(plan, ow.Target) + idOffset; }
            else if (ow != null) { outConn = "IN1"; outId = ow.Id + idOffset; }
            else { outConn = ""; outId = 0; }
            string dt = v.DataType.Length > 0 ? v.DataType : (v.Expression.Contains('.') ? "REAL" : "DINT");
            return $"<Class ClassType=\"CFBDOutVarXml\" varName=\"{Esc(v.Expression)}\" varType=\"CONSTANT\""
                 + $" varDataType=\"{Esc(dt)}\" localId=\"{id}\" posX=\"{Tx(v.X)}\" posY=\"{Tx(v.Y)}\""
                 + $" width=\"{10 * v.W}\" height=\"{10 * v.H}\"{netAttr} outConnection=\"{Esc(outConn)}\""
                 + $" outConnectId=\"{outId}\" annotate=\"False\" annotationHeight=\"4\" annotateMonitorValue=\"False\"/>";
        }
        string cls = v.Kind == "in" ? "CFBDOutVarXml" : "CFBDInOutVarXml";
        var sb = new StringBuilder();
        sb.Append($"<Class ClassType=\"{cls}\" varName=\"{Esc(v.Expression)}\" localId=\"{id}\"")
          .Append($" posX=\"{Tx(v.X)}\" posY=\"{Tx(v.Y)}\" width=\"{10 * v.W}\" height=\"{10 * v.H}\"{netAttr}");
        if (pp.OutWire != null)
            sb.Append($" outConnection=\"IN1\" outConnectId=\"{pp.OutWire.Id + idOffset}\"");
        sb.Append($" annotation=\"{Esc(v.Annotation)}\" annotate=\"{Title(v.Annotate)}\"")
          .Append($" annotationHeight=\"{v.H - 2}\" annotateMonitorValue=\"{Title(v.MonitorValue)}\"");
        if (pp.InWire != null)
            sb.Append($" inConnection=\"{(pp.InWire.IsBranch ? "IN1" : "OUT1")}\" inConnectId=\"{pp.InWire.Id + idOffset}\"");
        sb.Append("/>");
        return sb.ToString();
    }

    private static string EmitBlock(Plan plan, ExBlock b, int id, int idOffset = 0)
    {
        var bp = plan.Blocks.First(x => x.B == b);
        bool isFn = b.IsFunction;
        var sb = new StringBuilder();
        sb.Append("<Class ClassType=\"").Append(isFn ? "CFBDFunctionXml" : "CFBDFBlockXml").Append('"');
        if (!isFn) sb.Append($" instanceName=\"{Esc(b.InstanceName)}\"");
        sb.Append($" typeName=\"{Esc(b.TypeName)}\" localId=\"{id}\"")
          .Append($" posX=\"{Tx(b.X)}\" posY=\"{Tx(b.Y)}\" width=\"{10 * b.W}\" height=\"{10 * b.H}\"");
        // Build network ids on blocks are editor edit-history state (present on some blocks
        // and absent on others), so they are not emitted; the execute sequence is always 0.
        sb.Append($" snapEventId=\"{(Lib.TryGetValue(b.TypeName, out var m) ? m.EventId : 0)}\"")
          .Append($" enEnO=\"False\" doubleSpace=\"{(b.DoubleSpace ? "True" : "False")}\"")
          .Append(" sequence=\"0\">");
        sb.Append("<Collection Name=\"inputs\" ClassType=\"CDGMInputXml\" CollectionType=\"1\">");
        for (int i = 0; i < b.Inputs.Count; i++)
        {
            var w = bp.InWires[i];
            if (w == null) continue;
            if (w.Suppress)
            {
                var sv = (ExVarPlace)w.Source;
                if (sv.Kind == "in")
                    sb.Append($"<Class formalParam=\"{Esc(b.Inputs[i].Name)}\" effectiveParam=\"{Esc(sv.Expression)}\"")
                      .Append($" connectId=\"{elementIdOf(plan, w.Source) + idOffset}\"/>");
                else
                    sb.Append($"<Class formalParam=\"{Esc(b.Inputs[i].Name)}\"/>");
                continue;
            }
            sb.Append($"<Class formalParam=\"{Esc(b.Inputs[i].Name)}\" effectiveParam=\"{(w.IsBranch ? "IN1" : "OUT1")}\"")
              .Append($" connectId=\"{w.Id + idOffset}\"/>");
        }
        sb.Append("</Collection>");
        sb.Append("<Collection Name=\"outputs\" ClassType=\"CDGMOutputXml\" CollectionType=\"1\">");
        foreach (var pin in b.Outputs)
        {
            bp.OutWires.TryGetValue(pin.Name, out var w);
            sb.Append($"<Class formalParam=\"{Esc(pin.Name)}\" effectiveParam=\"IN1\" connectId=\"{(w?.Id ?? 0) + (w != null ? idOffset : 0)}\"/>");
        }
        sb.Append("</Collection></Class>");
        return sb.ToString();
    }

    private static string EmitWire(Plan plan, Wire w, int idOffset = 0)
    {
        string inputEffective = w.IsBranch ? w.TargetPin : w.SourcePin;
        int inputConnect = elementIdOf(plan, w.IsBranch ? w.Target : w.Source) + idOffset;
        string outputEffective = w.IsBranch ? "V" + w.Vn : w.TargetPin;
        int outputConnect = (w.IsBranch ? w.Parent!.Id : elementIdOf(plan, w.Target)) + idOffset;
        var sb = new StringBuilder();
        sb.Append($"<Class ClassType=\"CDGMWireXml\" localId=\"{w.Id + idOffset}\" feedBack=\"False\" network=\"0\">");
        sb.Append($"<Class Embed=\"input\" formalParam=\"IN1\" effectiveParam=\"{Esc(inputEffective)}\" connectId=\"{inputConnect}\"/>");
        sb.Append($"<Class Embed=\"output\" formalParam=\"OUT1\" effectiveParam=\"{Esc(outputEffective)}\" connectId=\"{outputConnect}\"/>");
        sb.Append("<Collection Name=\"positions\" ClassType=\"CDGMPositionXml\" CollectionType=\"1\">");
        foreach (var (x, y) in w.PathExport)
            sb.Append($"<Class posX=\"{Tx(x)}\" posY=\"{Tx(y)}\"/>");
        sb.Append("</Collection></Class>");
        return sb.ToString();
    }

    private static int ElemId(Plan plan, ExElem e) => elementIdOf(plan, e);

    private static int elementIdOf(Plan plan, ExElem e) => e switch
    {
        ExVarPlace v when plan.Places.TryGetValue(v, out var pp) => pp.Id,
        ExBlock b => plan.Blocks.First(x => x.B == b).Id,
        ExComment c => _commentIds[c],
        _ => 0,
    };

    // ============================================================ CLI

    public static int Run(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: genfbd <xml> <pouName> <out.FBD>");
            return 1;
        }
        string xml = args[1], pouName = args[2], outPath = args[3];
        string text = Generate(xml, pouName);
        File.WriteAllText(outPath, text, new UTF8Encoding(false));
        Console.WriteLine($"Generated {outPath} ({text.Length} chars) from '{pouName}'");
        return 0;
    }
}
