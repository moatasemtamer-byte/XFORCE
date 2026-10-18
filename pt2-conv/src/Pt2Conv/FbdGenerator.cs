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
            DogTag = 183, Type = 8, SubType = 2147483652L, ExtOptFlags = 7, CemEI = 1, CemAO = 0,
            EventId = 292, Date = "1995-07-12 17:30:51",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_BIT", Vt = 0, Flags = 1 },
                new LibPin { Name = "OR", DataType = "ANY_BIT", Vt = 1 },
            },
        },
        ["AND"] = new()
        {
            DogTag = 105, Type = 8, SubType = 2147483652L, ExtOptFlags = 7, CemEI = 2, CemAO = 0,
            EventId = 20, Date = "1995-07-12 17:24:00",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY_BIT", Vt = 0, Flags = 1 },
                new LibPin { Name = "AND", DataType = "ANY_BIT", Vt = 1 },
            },
        },
        ["MOVE"] = new()
        {
            DogTag = 177, Type = 8, SubType = 1, ExtOptFlags = 31, CemEI = 1, CemAO = 1,
            EventId = 276, Date = "1995-07-12 17:30:27",
            Pins =
            {
                new LibPin { Name = "", DataType = "ANY", Vt = 0 },
                new LibPin { Name = "", DataType = "ANY", Vt = 1 },
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
            Lib[name] = meta;
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
    // TriStation editor; the sheet height for template D (export coordinates) is 2070.
    private const int SheetHeightExport = 2070;
    private static bool IsClipped(int y, int h) => 10 * y + 40 + 10 * h > SheetHeightExport;

    private sealed class ExVar : ExElem
    {
        public string Name = "", Doc = "", Section = "", DataType = "", Derived = "";
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
        public string DrawingNumber = "", DrawingTitle = "";
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
                    p.Vars.Add(new ExVar
                    {
                        Name = A(v, "name"),
                        Section = secKind,
                        DataType = dtype,
                        Derived = derived != null ? A(derived, "name") : "",
                        Doc = TextTrim(v.Elements().FirstOrDefault(e => e.Name.LocalName == "documentation")),
                    });
                }
            }
            // The export repeats every referenced tag inside each program's <globalVars>;
            // the FBD's declarations collection lists each name once, and omits literals.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            p.Vars = p.Vars.Where(x => seen.Add(x.Name) && !IsConstantName(x.Name)).ToList();
        }

        var fbd = pou.Descendants().FirstOrDefault(e => e.Name.LocalName == "FBD")
            ?? throw new InvalidOperationException($"POU '{pouName}' has no FBD body");

        var sheetsEl = fbd.Elements().FirstOrDefault(e => e.Name.LocalName == "SHEETS");
        if (sheetsEl != null)
        {
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
                if (n != "DRAWING_NUMBER" && n != "DRAWING_TITLE") continue;
                string val = mac.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value ?? "";
                if (n == "DRAWING_NUMBER") p.DrawingNumber = val; else p.DrawingTitle = val;
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

        // 2. group by source -> choose main wire, mark branches, compute junctions
        foreach (var g in plan.Wires.Where(w => !w.Suppress).GroupBy(w => (w.Source, w.SourcePin)))
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
        foreach (var g in plan.Wires.Where(w => !w.Suppress).GroupBy(w => (w.Source, w.SourcePin)))
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

        // 4. element ids: per sheet, blocks band first then comments+vars band
        _commentIds.Clear();
        int next = 1;
        foreach (var sheet in p.Sheets.OrderBy(s => s.Number))
        {
            foreach (var dv in p.Dividers.Where(d => d.Sheet == sheet.Number && !d.Clipped).OrderBy(d => d.DocIndex))
                plan.DividerIds[dv] = next++;
            foreach (var b in p.Blocks.Where(b => b.Sheet == sheet.Number && !b.Clipped).OrderBy(b => b.DocIndex))
                plan.Blocks.Add(new BlockPlan { B = b, Id = next++ });
            foreach (var e in p.ElemOrder
                         .Where(e => e is not ExBlock && e.Sheet == sheet.Number && !e.Clipped)
                         .OrderBy(e => e.DocIndex))
            {
                if (e is ExVarPlace vp) plan.Places[vp] = new PlacePlan { P = vp, Id = next++ };
                else if (e is ExComment cm) _commentIds[cm] = next++;
            }
        }
        foreach (var w in plan.Wires.Where(w => !w.Suppress)) w.Id = next++;
        plan.LastId = next - 1;
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

    public static byte[] GenerateBytes(string xmlPath, string pouName, IReadOnlyDictionary<string, TagInfo>? tagDesc = null, IReadOnlySet<string>? referencedTypes = null)
        => Encoding.UTF8.GetBytes(Generate(xmlPath, pouName, tagDesc, referencedTypes));

    private static string Generate(string xmlPath, string pouName, IReadOnlyDictionary<string, TagInfo>? tagDesc = null, IReadOnlySet<string>? referencedTypes = null)
    {
        var p = Parse(xmlPath, pouName, tagDesc, referencedTypes);
        var plan = BuildPlan(p);
        _ = _commentIds;
        ComputeLayout(plan);

        var sb = new StringBuilder(20000);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n");
        sb.Append("<SerializableClass Type=\"Serializable\" Version=\"1.0\" ClassType=\"CFBDDocXml\"")
              .Append($" pouName=\"{Esc(p.Name)}\" pouType=\"{Esc(p.PouType)}\" pouVersion=\"{Esc(p.Version)}\"")
          .Append($" dateTime=\"{DateTime.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture)}\"")
          .Append(" sheetTemplate=\"SheetTemplateD\" drawingSize=\"D\" hZoneCount=\"16\" vZoneCount=\"8\"")
          .Append($" attributes=\"1204\" lastZoom=\"0\" lastMonVar=\"0\" lastUniqueID=\"{plan.LastId}\"")
          .Append(" D0=\"\" D1=\"\" D2=\"\" D3=\"\" D4=\"\" D5=\"\" D6=\"\" D7=\"\" D8=\"\" D9=\"\"")
          .Append($" drawingNumber=\"{Esc(p.DrawingNumber)}\" drawingTitle=\"{Esc(p.DrawingTitle)}\"")
          .Append(" approvedName=\"\" approvedTime=\"\" rev=\"\"");
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
            string fbrc = v.IsInstance ? "1" : "0";
            int refCount = v.IsInstance
                ? p.Blocks.Count(b => b.InstanceName == v.Name)
                : places.Count;
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
                    // A declared global that is not a tag-database entry is a program-local.
                    flags = "65536";
                    if ((v.Name.StartsWith("w", StringComparison.Ordinal) || v.Name.StartsWith("v", StringComparison.Ordinal))
                        && (dataType == "REAL" || dataType == "DINT"))
                        dataType = "BOOL";
                }
            }
            sb.Append($"<Class name=\"{Esc(v.Name)}\" dataType=\"{Esc(dataType)}\"")
              .Append($" initVal=\"\" desc=\"{Esc(desc)}\" varVTType=\"{vt}\" refCount=\"{refCount}\"")
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
            foreach (var type in p.Blocks.OrderBy(b => b.DocIndex).Select(b => b.TypeName).Distinct())
            {
                int count = p.Blocks.Count(b => b.TypeName == type);
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

        // sheets
        sb.Append("<Collection Name=\"sheets\" ClassType=\"CFBDSheetXml\" CollectionType=\"1\">");
        foreach (var sheet in p.Sheets.OrderBy(s => s.Number))
        {
            int bn = p.IsBuilt && p.PouType != "program" ? sheet.Number : 0;
            sb.Append($"<Class sheetTitle=\"{Esc(sheet.Title)}\" firstBuildNetwork=\"{bn}\"")
              .Append($" lastBuildNetwork=\"{bn}\" S0=\"\" S1=\"\" S2=\"\" S3=\"\" S4=\"\"")
              .Append(" S5=\"\" S6=\"\" S7=\"\" S8=\"\" S9=\"\"><Collection Name=\"displayables\" CollectionType=\"1\">");
            foreach (var item in SheetItems(plan, sheet.Number))
                sb.Append(item);
            sb.Append("</Collection></Class>");
        }
        sb.Append("</Collection>");

        sb.Append("</SerializableClass>\r\n");
        return sb.ToString();
    }

    private static IEnumerable<string> SheetItems(Plan plan, int sheet)
    {
        var p = plan.P;
        var all = new List<(int Id, ExElem Elem)>();
        foreach (var c in p.Comments.Where(c => c.Sheet == sheet && !c.Clipped))
            all.Add((_commentIds[c], c));
        foreach (var pp in plan.Places.Values.Where(x => x.P.Sheet == sheet))
            all.Add((pp.Id, pp.P));
        foreach (var bp in plan.Blocks.Where(x => x.B.Sheet == sheet))
            all.Add((bp.Id, bp.B));

        var results = new List<(int Id, string Xml)>();
        foreach (var (id, elem) in all)
        {
            string? xml = elem switch
            {
                ExComment c => EmitComment(c, id),
                ExVarPlace v => EmitVar(plan, v, id),
                ExBlock b => EmitBlock(plan, b, id),
                _ => null,
            };
            if (xml != null) results.Add((id, xml));
        }
        foreach (var dv in plan.DividerIds.Where(kv => kv.Key.Sheet == sheet))
            results.Add((dv.Value, EmitDivider(dv.Key, dv.Value)));
        foreach (var w in plan.Wires.Where(w => !w.Suppress && w.Target.Sheet == sheet))
            results.Add((w.Id, EmitWire(plan, w)));
        return results.OrderBy(r => r.Id).Select(r => r.Xml);
    }

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

    private static string? EmitVar(Plan plan, ExVarPlace v, int id)
    {
        var pp = plan.Places[v];
        bool wired = pp.OutWire != null && !pp.OutWire.Suppress;
        if (v.IsConstant && v.Kind != "in" && !wired) return null;
        if (v.IsConstant && v.Kind == "in")
        {
            var ow = pp.OutWire;
            string outConn; int outId;
            if (ow != null && ow.Suppress) { outConn = ow.TargetPin; outId = elementIdOf(plan, ow.Target); }
            else if (ow != null) { outConn = "IN1"; outId = ow.Id; }
            else { outConn = ""; outId = 0; }
            string dt = v.DataType.Length > 0 ? v.DataType : (v.Expression.Contains('.') ? "REAL" : "DINT");
            return $"<Class ClassType=\"CFBDOutVarXml\" varName=\"{Esc(v.Expression)}\" varType=\"CONSTANT\""
                 + $" varDataType=\"{Esc(dt)}\" localId=\"{id}\" posX=\"{Tx(v.X)}\" posY=\"{Tx(v.Y)}\""
                 + $" width=\"{10 * v.W}\" height=\"{10 * v.H}\" network=\"{v.Sheet}\" outConnection=\"{Esc(outConn)}\""
                 + $" outConnectId=\"{outId}\" annotate=\"False\" annotationHeight=\"4\" annotateMonitorValue=\"False\"/>";
        }
        string cls = v.Kind == "in" ? "CFBDOutVarXml" : "CFBDInOutVarXml";
        var sb = new StringBuilder();
        sb.Append($"<Class ClassType=\"{cls}\" varName=\"{Esc(v.Expression)}\" localId=\"{id}\"")
          .Append($" posX=\"{Tx(v.X)}\" posY=\"{Tx(v.Y)}\" width=\"{10 * v.W}\" height=\"{10 * v.H}\" network=\"{v.Sheet}\"")
          .Append($" lineNumber=\"{pp.LineNumber}\"");
        if (pp.OutWire != null)
            sb.Append($" outConnection=\"IN1\" outConnectId=\"{pp.OutWire.Id}\"");
        sb.Append(" annotation=\"%DESCRIPTION\" annotate=\"True\"")
          .Append($" annotationHeight=\"{v.H - 2}\" annotateMonitorValue=\"False\"");
        if (pp.InWire != null)
            sb.Append($" inConnection=\"{(pp.InWire.IsBranch ? "IN1" : "OUT1")}\" inConnectId=\"{pp.InWire.Id}\"");
        sb.Append("/>");
        return sb.ToString();
    }

    private static string EmitBlock(Plan plan, ExBlock b, int id)
    {
        var bp = plan.Blocks.First(x => x.B == b);
        bool isFn = b.IsFunction;
        var sb = new StringBuilder();
        sb.Append("<Class ClassType=\"").Append(isFn ? "CFBDFunctionXml" : "CFBDFBlockXml").Append('"');
        if (!isFn) sb.Append($" instanceName=\"{Esc(b.InstanceName)}\"");
        sb.Append($" typeName=\"{Esc(b.TypeName)}\" localId=\"{id}\"")
          .Append($" posX=\"{Tx(b.X)}\" posY=\"{Tx(b.Y)}\" width=\"{10 * b.W}\" height=\"{10 * b.H}\" network=\"{b.Sheet}\"")
          .Append($" firstLineNumber=\"{bp.FirstLine}\" lineCount=\"{bp.LineCount}\"")
          .Append($" snapEventId=\"{(Lib.TryGetValue(b.TypeName, out var m) ? m.EventId : 0)}\"")
          .Append($" enEnO=\"False\" doubleSpace=\"{(b.DoubleSpace ? "True" : "False")}\"")
          .Append($" sequence=\"{bp.Sequence}\">");
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
                      .Append($" connectId=\"{elementIdOf(plan, w.Source)}\" lineOffset=\"{LineOffset(b.TypeName, b.Inputs[i].Name, i, b.Inputs.Count)}\"/>");
                else
                    sb.Append($"<Class formalParam=\"{Esc(b.Inputs[i].Name)}\"/>");
                continue;
            }
            sb.Append($"<Class formalParam=\"{Esc(b.Inputs[i].Name)}\" effectiveParam=\"{(w.IsBranch ? "IN1" : "OUT1")}\"")
              .Append($" connectId=\"{w.Id}\" lineOffset=\"{LineOffset(b.TypeName, b.Inputs[i].Name, i, b.Inputs.Count)}\"/>");
        }
        sb.Append("</Collection>");
        sb.Append("<Collection Name=\"outputs\" ClassType=\"CDGMOutputXml\" CollectionType=\"1\">");
        foreach (var pin in b.Outputs)
        {
            bp.OutWires.TryGetValue(pin.Name, out var w);
            sb.Append($"<Class formalParam=\"{Esc(pin.Name)}\" effectiveParam=\"IN1\" connectId=\"{w?.Id ?? 0}\"/>");
        }
        sb.Append("</Collection></Class>");
        return sb.ToString();
    }

    private static string EmitWire(Plan plan, Wire w)
    {
        string inputEffective = w.IsBranch ? w.TargetPin : w.SourcePin;
        int inputConnect = elementIdOf(plan, w.IsBranch ? w.Target : w.Source);
        string outputEffective = w.IsBranch ? "V" + w.Vn : w.TargetPin;
        int outputConnect = w.IsBranch ? w.Parent!.Id : elementIdOf(plan, w.Target);
        var sb = new StringBuilder();
        sb.Append($"<Class ClassType=\"CDGMWireXml\" localId=\"{w.Id}\" feedBack=\"False\" network=\"{w.Target.Sheet}\">");
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
