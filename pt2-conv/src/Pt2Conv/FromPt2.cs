using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// Decompiles a TriStation .pt2/.pt2x container into the TriStation PLCopen XML dialect that
/// <see cref="BuildXml"/>, <see cref="FbdGenerator"/> and the engine's PlcOpenXmlParser consume.
///
/// Every element folder under <c>Current/Elements/&lt;POU&gt;</c> holds either a <c>.FBD</c> stream
/// (a <c>CFBDDocXml</c> XML graph: declarations + sheets of blocks, terminals, wires and comments)
/// or a <c>.ST</c> stream (IEC Structured Text source). Global tags come from
/// <c>Current/Tagnames/Tagnames.csv</c>.
/// </summary>
public sealed class FromPt2
{
    private readonly Dictionary<string, byte[]> _streams;
    private readonly List<Pt2Tag> _tags;
    private readonly Dictionary<string, string> _tagType = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _projectElems;

    private FromPt2(Dictionary<string, byte[]> streams, List<Pt2Tag> tags)
    {
        _streams = streams;
        _tags = tags;
        foreach (var t in tags) _tagType[t.Name] = t.Type;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: frompt2 <project.pt2x> <out.xml> [--name <projectName>] [--referenced-only]");
            Console.Error.WriteLine("  <project.pt2x>: a TriStation .pt2/.pt2x project container");
            Console.Error.WriteLine("  --name: contentHeader / configuration name (default from Project.xml)");
            Console.Error.WriteLine("  --referenced-only: emit only tags referenced by POU logic (default: full tag table)");
            return 1;
        }

        string input = args[1], outPath = args[2];
        string? nameArg = null;
        bool referencedOnly = false;
        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name": if (i + 1 < args.Length) nameArg = args[++i]; break;
                case "--referenced-only": referencedOnly = true; break;
            }
        }

        var cf = CompoundFile.Open(input);
        var streams = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in cf.ListStreams()) streams[s.Path] = s.Data;

        var tags = LoadTags(streams);
        var self = new FromPt2(streams, tags);
        string projName = nameArg ?? self.ProjectName() ?? Path.GetFileNameWithoutExtension(input);

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

        self.CollectReferencedGlobals(blocks);
        var xml = self.Translate(blocks, projName, referencedOnly);
        File.WriteAllText(outPath, xml, new UTF8Encoding(false));
        int pouCount = blocks.Count(b => b.Sheets.Count > 0 || b.St != null);
        Console.WriteLine($"Wrote {outPath} ({xml.Length:N0} chars): {pouCount} POU(s), {tags.Count} tag(s)");
        return 0;
    }

    /// <summary>Serializes the decompiled project (POUs, interface variables, FBD sheets with
    /// geometry and wires, global tags) to a JSON model consumed by the web PT2X viewer.</summary>
    public static int ToJson(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: tojson <project.pt2x> <out.json>");
            Console.Error.WriteLine("  <project.pt2x>: a TriStation .pt2/.pt2x project container");
            return 1;
        }

        string input = args[1], outPath = args[2];

        var cf = CompoundFile.Open(input);
        var streams = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in cf.ListStreams()) streams[s.Path] = s.Data;

        var tags = LoadTags(streams);
        var self = new FromPt2(streams, tags);
        string projName = self.ProjectName() ?? Path.GetFileNameWithoutExtension(input);

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

        self.CollectReferencedGlobals(blocks);

        var pous = blocks
            .Where(b => b.Sheets.Count > 0 || b.St != null)
            .Select(b => new
            {
                name = b.Name,
                pouType = b.PouType,
                returnType = b.ReturnType,
                title = b.Title,
                drawing = new
                {
                    number = b.DrawingNumber,
                    title = b.DrawingTitle,
                    size = b.DrawingSize,
                    sheetTemplate = b.SheetTemplate,
                    rev = b.Revision,
                    hZones = b.HZoneCount,
                    vZones = b.VZoneCount,
                },
                vars = b.Vars.Select(v => new
                {
                    name = v.Name,
                    dataType = v.DataType,
                    init = v.Init,
                    desc = v.Desc,
                    section = v.Section,
                    isInstance = v.IsInstance,
                }).ToArray(),
                referencedGlobals = b.ReferencedGlobals.ToArray(),
                st = b.St,
                sheets = b.Sheets.Select(s => new
                {
                    number = s.Number,
                    title = s.Title,
                    elems = s.Elems.Select(e => ElemJson(e, b, s, projName, b.Sheets.Count)).ToArray(),
                    wires = s.Wires.Select(w => new
                    {
                        id = w.OldId,
                        aId = w.AId,
                        aPin = w.APin,
                        bId = w.BId,
                        bPin = w.BPin,
                        positions = w.Positions.Select(p => new[] { p.X, p.Y }).ToArray(),
                    }).ToArray(),
                }).ToArray(),
            })
            .ToArray();

        var payload = new
        {
            projectName = projName,
            pouCount = pous.Length,
            tagCount = tags.Count,
            pous,
            tags = tags.Select(t => new { name = t.Name, type = t.Type, desc = t.Desc, address = t.Address }).ToArray(),
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(outPath, json, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {outPath}: {pous.Length} POU(s), {tags.Count} tag(s)");
        return 0;
    }

    private static object ElemJson(Pt2Elem e, Pt2Block b, Pt2Sheet s, string projName, int sheetCount)
    {
        switch (e)
        {
            case Pt2BlockElem blk:
                return new
                {
                    kind = "block",
                    id = blk.OldId,
                    x = blk.RawX,
                    y = blk.RawY,
                    w = blk.RawW,
                    h = blk.RawH,
                    typeName = blk.TypeName,
                    instance = blk.Instance,
                    isFunction = blk.IsFunction,
                    inputs = blk.Inputs.Select(p => new { name = p.Name, wire = p.Wire }).ToArray(),
                    outputs = blk.Outputs.Select(o => o.Name).ToArray(),
                };

            case Pt2VarElem v:
            {
                string annotation = ExpandMacros(v.Annotation, b, s, projName, sheetCount);
                if (v.Annotation.Contains("DESCRIPTION"))
                {
                    string desc = b.Vars.FirstOrDefault(x => string.Equals(x.Name, v.Name, StringComparison.OrdinalIgnoreCase))?.Desc ?? "";
                    annotation = annotation.Replace("%DESCRIPTION", desc).Trim();
                }
                return new
                {
                    kind = "var",
                    id = v.OldId,
                    x = v.RawX,
                    y = v.RawY,
                    w = v.RawW,
                    h = v.RawH,
                    name = v.Name,
                    varType = v.VarType,
                    annotation,
                    annotate = v.Annotate,
                    inWire = v.InWire,
                    outWire = v.OutWire,
                };
            }

            case Pt2CommentElem c:
                return new
                {
                    kind = "comment",
                    id = c.OldId,
                    x = c.RawX,
                    y = c.RawY,
                    w = c.RawW,
                    h = c.RawH,
                    text = c.ExpandMacros ? ExpandMacros(c.Text, b, s, projName, sheetCount) : c.Text,
                    border = c.Border,
                    align = c.Align,
                    textSize = c.TextSize,
                    behindLogic = c.BehindLogic,
                };

            default:
                return new { kind = "unknown", id = e.OldId, x = e.RawX, y = e.RawY, w = e.RawW, h = e.RawH };
        }
    }

    private static string ExpandMacros(string text, Pt2Block b, Pt2Sheet s, string projName, int sheetCount)
    {
        if (text.IndexOf('%') < 0) return text;
        string sheetTitle = string.IsNullOrWhiteSpace(s.Title)
            ? (s.Number == 1 ? b.Title : "")
            : s.Title;
        return text
            .Replace("%PROJECT_NAME", projName)
            .Replace("%ELEMENT_NAME", b.Name)
            .Replace("%DRAWING_TITLE", b.DrawingTitle)
            .Replace("%DRAWING_NUMBER", b.DrawingNumber)
            .Replace("%DRAWING_SIZE", b.DrawingSize)
            .Replace("%REVISION", b.Revision)
            .Replace("%SHEET_TITLE", sheetTitle)
            .Replace("%SHEET_NUMBER", s.Number.ToString())
            .Replace("%SHEET_COUNT", sheetCount.ToString());
    }

    // ---------------------------------------------------------------- container

    private IEnumerable<string> PouFolders()
    {
        // Only project-local elements (LibID == 1 in Current/Project.xml) are exported as <pou>.
        // The official XMLImportExport output contains exactly these; library elements (STDLIB,
        // TCXLIB, TX1LIB, sheet templates) live in the .EMA libraries and every element folder we
        // used to emit (AIN, TON, EQ, ...) is not part of the project.
        var project = ProjectElementNames();
        var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _streams.Keys)
        {
            var parts = path.Split('/');
            if (parts.Length != 4) continue;
            if (!parts[0].Equals("Current", StringComparison.OrdinalIgnoreCase)) continue;
            if (!parts[1].Equals("Elements", StringComparison.OrdinalIgnoreCase)) continue;
            string folder = parts[2];
            if (folder.Equals("Configuration", StringComparison.OrdinalIgnoreCase)) continue;
            if (folder.StartsWith("SheetTemplate", StringComparison.OrdinalIgnoreCase)) continue;
            if (project != null && !project.Contains(folder)) continue;
            folders.Add(folder);
        }
        return folders;
    }

    /// <summary>Names of project-local elements (LibID="1") from Current/Project.xml, or null
    /// when the catalog is unavailable (in which case every element folder is treated as a POU).</summary>
    private HashSet<string>? ProjectElementNames()
    {
        if (_projectElems != null) return _projectElems.Count == 0 ? null : _projectElems;
        _projectElems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!_streams.TryGetValue("Current/Project.xml", out var data)) return null;
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
            foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "Class" && e.Attribute("ElemName") != null))
            {
                if ((string?)e.Attribute("LibID") == "1")
                    _projectElems.Add((string)e.Attribute("ElemName")!);
            }
        }
        catch { /* ignore - fall back to all folders */ }
        return _projectElems.Count == 0 ? null : _projectElems;
    }

    private string? ProjectName()
    {
        if (!_streams.TryGetValue("Current/Project.xml", out var data)) return null;
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
            var desc = (string?)doc.Root?.Attribute("Description");
            if (!string.IsNullOrWhiteSpace(desc))
                return desc.Replace("\r", " ").Replace("\n", " ").Trim();
        }
        catch { /* ignore */ }
        return null;
    }

    private static List<Pt2Tag> LoadTags(Dictionary<string, byte[]> streams)
    {
        var tags = new List<Pt2Tag>();
        if (!streams.TryGetValue("Current/Tagnames/Tagnames.csv", out var data)) return tags;
        string[] lines = Encoding.UTF8.GetString(data).Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2) return tags;

        var header = CsvFields(lines[1]);
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int h = 0; h < header.Length; h++) col[header[h]] = h;
        if (!col.ContainsKey("Tagname")) return tags;

        // Every column the TriStation export reads back into <ts:TAGNAME>. The CSV is the
        // only place the point configuration survives; reading just Name/Type/Desc/Address
        // (as this used to) silently discarded ~56 of the 60 columns.
        string Get(string[] f, string name) => col.TryGetValue(name, out var c) && c < f.Length ? f[c] : "";

        for (int i = 2; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            // The Tagnames.csv is XML-ish: it opens with "<Tags ...>" and closes with
            // "</Tags>". Every real tag row is a quoted record, so skip any other line
            // (notably the "</Tags>" footer) instead of reading it as a tag name.
            if (!lines[i].StartsWith("\"")) continue;
            var f = CsvFields(lines[i]);
            string name = Get(f, "Tagname");
            if (string.IsNullOrWhiteSpace(name)) continue;
            tags.Add(new Pt2Tag
            {
                Name = name,
                Type = Get(f, "DataType"),
                Desc = Get(f, "Description"),
                Group1 = Get(f, "Group1"),
                Group2 = Get(f, "Group2"),
                InitialValue = Get(f, "InitialValue"),
                IsSafety = Get(f, "IsSafety"),
                SharedRead = Get(f, "SharedRead"),
                EnableMultipleWrites = Get(f, "EnableMultipleWrites"),
                AliasNumberSelection = Get(f, "AliasNumberSelection"),
                UserAliasLegacy = Get(f, "UserAliasLegacy"),
                UserAliasExtended = Get(f, "UserAliasExtended"),
                AliasType = Get(f, "AliasType"),
                Location = Get(f, "Location"),
                Address = Get(f, "Address"),
                PhysicalAddress = Get(f, "PhysicalAddress"),
                MinimumValue = Get(f, "MinimumValue"),
                MaximumValue = Get(f, "MaximumValue"),
                Precision = Get(f, "Precision"),
                DisableScaling = Get(f, "DisableScaling"),
                StateNameTRUE = Get(f, "StateNameTRUE"),
                ColorTRUE = Get(f, "ColorTRUE"),
                StateNameFALSE = Get(f, "StateNameFALSE"),
                ColorFALSE = Get(f, "ColorFALSE"),
                Area = Get(f, "Area"),
                Equipment = Get(f, "Equipment"),
                SIFName = Get(f, "SIFName"),
                IOModuleType = Get(f, "IOModuleType"),
                MinimumTriggerTime = Get(f, "MinimumTriggerTime"),
                SOEDeadTime = Get(f, "SOEDeadTime"),
                R_Adjust = Get(f, "R_Adjust"),
                ShortedThreshold = Get(f, "ShortedThreshold"),
                OpenThreshold = Get(f, "OpenThreshold"),
                AI_FilterType = Get(f, "AI_FilterType"),
                AI_ClampEngineeringVal = Get(f, "AI_ClampEngineeringVal"),
                AI_FieldFaultMonitor = Get(f, "AI_FieldFaultMonitor"),
                AI_NamurHighLimit = Get(f, "AI_NamurHighLimit"),
                AI_NamurLowLimit = Get(f, "AI_NamurLowLimit"),
                AI_MinimumRawValue = Get(f, "AI_MinimumRawValue"),
                AI_MaximumRawValue = Get(f, "AI_MaximumRawValue"),
                AI_MinimumEngValue = Get(f, "AI_MinimumEngValue"),
                AI_MaximumEngValue = Get(f, "AI_MaximumEngValue"),
                HART_Enable = Get(f, "HART_Enable"),
                HART_MonitorDeviceHealth = Get(f, "HART_MonitorDeviceHealth"),
                HART_MonitorConfigChanges = Get(f, "HART_MonitorConfigChanges"),
                HART_LockConfiguration = Get(f, "HART_LockConfiguration"),
                Class = Get(f, "Class"),
                SOEFlags = Get(f, "SOEFlags"),
                PI_PointType = Get(f, "PI_PointType"),
                PI_PointGearTeeth = Get(f, "PI_PointGearTeeth"),
                PI_PointGearRatio = Get(f, "PI_PointGearRatio"),
            });
        }
        return tags;
    }

    /// <summary>Minimal RFC-4180 CSV splitter (quoted fields, doubled quotes).</summary>
    private static string[] CsvFields(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    // Keep the doubled quote verbatim ("" stays ""). The official XMLImportExport
                    // export reproduces the raw CSV field text in the tag CDATA, e.g. a CSV field
                    // """NO FLAME"" LAMP" becomes CDATA ""NO FLAME"" LAMP - not "NO FLAME" LAMP.
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append("\"\""); i++; }
                    else quoted = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    // ---------------------------------------------------------------- POU read

    private Pt2Block? ReadPou(string folder)
    {
        string? fbd = null, st = null;
        foreach (var path in _streams.Keys)
        {
            var parts = path.Split('/');
            if (parts.Length != 4) continue;
            if (!parts[2].Equals(folder, StringComparison.OrdinalIgnoreCase)) continue;
            if (parts[3].EndsWith(".FBD", StringComparison.OrdinalIgnoreCase)) fbd = path;
            else if (parts[3].EndsWith(".ST", StringComparison.OrdinalIgnoreCase)) st = path;
        }

        if (fbd != null) return ParseFbd(folder, _streams[fbd]);
        if (st != null) return ParseSt(folder, _streams[st]);
        return null;
    }

    private static Pt2Block ParseSt(string folder, byte[] data)
    {
        string text = DecodeText(data);
        var (name, pouType, returnType) = ParseStHeader(text, folder);
        return new Pt2Block
        {
            Name = name,
            PouType = pouType,
            ReturnType = returnType,
            St = text,
        };
    }

    private static (string Name, string PouType, string? ReturnType) ParseStHeader(string text, string folder)
    {
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("(*") || line.StartsWith("//")) continue;

            string upper = line.ToUpperInvariant();
            if (upper.StartsWith("FUNCTION_BLOCK"))
                return (Token(line, "FUNCTION_BLOCK"), "functionBlock", null);
            if (upper.StartsWith("FUNCTION"))
            {
                string name = Token(line, "FUNCTION");
                int colon = line.IndexOf(':');
                string? rt = colon >= 0 ? line.Substring(colon + 1).Trim().TrimEnd(';').Trim() : null;
                return (name, "function", string.IsNullOrWhiteSpace(rt) ? null : rt);
            }
            if (upper.StartsWith("PROGRAM"))
                return (Token(line, "PROGRAM"), "program", null);
        }
        return (folder, "functionBlock", null);
    }

    private static string Token(string line, string keyword)
    {
        string rest = line.Substring(keyword.Length).TrimStart();
        int end = 0;
        while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] == '_')) end++;
        return end > 0 ? rest.Substring(0, end) : "";
    }

    private static string DecodeText(byte[] data)
    {
        string s = Encoding.UTF8.GetString(data);
        if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);
        return s.Replace("\0", "");
    }

    private Pt2Block ParseFbd(string folder, byte[] data)
    {
        var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
        var root = doc.Root ?? throw new InvalidDataException("empty FBD stream");

        var block = new Pt2Block
        {
            Name = (string?)root.Attribute("pouName") ?? folder,
            PouType = MapPouType((string?)root.Attribute("pouType")),
            Title = (string?)root.Attribute("drawingTitle") ?? "",
            DrawingNumber = (string?)root.Attribute("drawingNumber") ?? "",
            DrawingTitle = (string?)root.Attribute("drawingTitle") ?? "",
            DrawingSize = (string?)root.Attribute("drawingSize") ?? "D",
            SheetTemplate = (string?)root.Attribute("sheetTemplate") ?? "",
            Revision = (string?)root.Attribute("rev") ?? "",
            HZoneCount = Int(root, "hZoneCount"),
            VZoneCount = Int(root, "vZoneCount"),
        };
        if (block.HZoneCount <= 0) block.HZoneCount = 16;
        if (block.VZoneCount <= 0) block.VZoneCount = 8;

        var decl = Child(root, "Collection", "declarations");
        if (decl != null)
        {
            foreach (var c in decl.Elements().Where(e => e.Name.LocalName == "Class"))
            {
                string name = (string?)c.Attribute("name") ?? "";
                if (name.Length == 0) continue;
                string dt = (string?)c.Attribute("dataType") ?? "BOOL";
                string vt = (string?)c.Attribute("varVTType") ?? "0";
                bool isInstance = vt == "3" && (Int(c, "fbRefCount") > 0 || Int(c, "flags") != 0);
                string section = vt switch
                {
                    "0" => "input",
                    "1" => "output",
                    "2" => "local",
                    "3" => isInstance ? "local" : "inOut",
                    "5" => "global",
                    _ => "local",
                };
                if (section == "global") continue;
                block.Vars.Add(new Pt2Var
                {
                    Name = name,
                    DataType = dt,
                    Init = (string?)c.Attribute("initVal") ?? "",
                    Desc = (string?)c.Attribute("desc") ?? "",
                    Section = section,
                    IsInstance = isInstance,
                });
            }
        }

        var sheets = Child(root, "Collection", "sheets");
        if (sheets != null)
        {
            int number = 0;
            foreach (var sheetClass in sheets.Elements().Where(e => e.Name.LocalName == "Class"))
            {
                number++;
                var sheet = new Pt2Sheet { Number = number, Title = (string?)sheetClass.Attribute("sheetTitle") ?? "" };
                for (int i = 0; i < 10; i++)
                {
                    string val = (string?)sheetClass.Attribute("S" + i) ?? "";
                    sheet.Macros[i] = val;
                }
                var disp = Child(sheetClass, "Collection", "displayables");
                if (disp != null) ReadSheet(sheet, disp);
                if (sheet.Elems.Count > 0 || sheet.Dividers.Count > 0) block.Sheets.Add(sheet);
            }
        }

        return block;
    }

    private static void Place(Pt2Elem e, XElement c)
    {
        int rx = Int(c, "posX"), ry = Int(c, "posY");
        int rw = Int(c, "width"), rh = Int(c, "height");
        e.RawX = rx; e.RawY = ry; e.RawW = rw; e.RawH = rh;
        e.X = Pos(rx); e.Y = Pos(ry);
        e.W = Math.Max(1, Size(rw)); e.H = Math.Max(1, Size(rh));
    }

    private static void ReadSheet(Pt2Sheet sheet, XElement displayables)
    {
        foreach (var c in displayables.Elements().Where(e => e.Name.LocalName == "Class"))
        {
            string ct = (string?)c.Attribute("ClassType") ?? "";
            switch (ct)
            {
                 case "CFBDFBlockXml":
                 case "CFBDFunctionXml":
                 {
                     var b = new Pt2BlockElem
                     {
                         OldId = Int(c, "localId"),
                         TypeName = (string?)c.Attribute("typeName") ?? "",
                         Instance = (string?)c.Attribute("instanceName") ?? "",
                         IsFunction = ct == "CFBDFunctionXml",
                         DoubleSpace = string.Equals((string?)c.Attribute("doubleSpace"), "True", StringComparison.OrdinalIgnoreCase),
                         Collapsed = string.Equals((string?)c.Attribute("snapEventId"), "0", StringComparison.Ordinal),
                     };
                    Place(b, c);
                    var inputs = Child(c, "Collection", "inputs");
                    if (inputs != null)
                        foreach (var pin in inputs.Elements().Where(e => e.Name.LocalName == "Class"))
                            b.Inputs.Add(new Pt2Pin
                            {
                                Name = (string?)pin.Attribute("formalParam") ?? "",
                                Wire = AttrInt(pin, "connectId"),
                            });
                    var outputs = Child(c, "Collection", "outputs");
                    if (outputs != null)
                        foreach (var pin in outputs.Elements().Where(e => e.Name.LocalName == "Class"))
                            b.Outputs.Add(new Pt2Pin
                            {
                                Name = (string?)pin.Attribute("formalParam") ?? "",
                                Wire = AttrInt(pin, "connectId"),
                            });
                    sheet.Elems.Add(b);
                    break;
                }
                case "CFBDInVarXml":
                case "CFBDOutVarXml":
                case "CFBDInOutVarXml":
                {
                    var v = new Pt2VarElem
                    {
                        OldId = Int(c, "localId"),
                        Name = (string?)c.Attribute("varName") ?? "",
                        VarType = (string?)c.Attribute("varType") ?? "",
                        Kind = ct switch
                        {
                            "CFBDInVarXml" => "outVariable",
                            "CFBDInOutVarXml" => "inOutVariable",
                            _ => "inVariable",
                        },
                        DataType = (string?)c.Attribute("varDataType") ?? "",
                        IsConstant = (string?)c.Attribute("varDataType") != null
                            || string.Equals((string?)c.Attribute("varType"), "CONSTANT", StringComparison.OrdinalIgnoreCase),
                        AnnotateMonitorValue = string.Equals((string?)c.Attribute("annotateMonitorValue"), "True", StringComparison.OrdinalIgnoreCase),
                        Annotation = (string?)c.Attribute("annotation") ?? "",
                        Annotate = string.Equals((string?)c.Attribute("annotate"), "True", StringComparison.OrdinalIgnoreCase),
                        InWire = AttrInt(c, "inConnectId"),
                        OutWire = AttrInt(c, "outConnectId"),
                    };
                    Place(v, c);
                    sheet.Elems.Add(v);
                    break;
                }
                case "CDGMWireXml":
                {
                    var w = new Pt2Wire
                    {
                        OldId = Int(c, "localId"),
                        FeedBack = string.Equals((string?)c.Attribute("feedBack"), "True", StringComparison.OrdinalIgnoreCase),
                    };
                    foreach (var ep in c.Elements().Where(e => e.Name.LocalName == "Class"))
                    {
                        string embed = (string?)ep.Attribute("Embed") ?? "";
                        int id = AttrInt(ep, "connectId");
                        string pin = (string?)ep.Attribute("effectiveParam") ?? "";
                        if (embed == "input") { w.AId = id; w.APin = pin; }
                        else if (embed == "output") { w.BId = id; w.BPin = pin; }
                    }
                    var positions = Child(c, "Collection", "positions");
                    if (positions != null)
                    {
                        foreach (var p in positions.Elements().Where(e => e.Name.LocalName == "Class"))
                            w.Positions.Add((Int(p, "posX"), Int(p, "posY")));
                    }
                    sheet.Wires.Add(w);
                    break;
                }
                case "CDGMnetworkDividerXml":
                {
                    var d = new Pt2Divider
                    {
                        OldId = Int(c, "localId"),
                        Orientation = string.Equals((string?)c.Attribute("orientation"), "VERTICAL", StringComparison.OrdinalIgnoreCase)
                            ? "VERTICAL" : "HORIZONTAL",
                        RawX = Int(c, "posX"),
                        RawY = Int(c, "posY"),
                        RawW = Int(c, "width"),
                        RawH = Int(c, "height"),
                    };
                    d.X = Pos(d.RawX);
                    d.Y = Pos(d.RawY);
                    d.W = Size(d.RawW);
                    d.H = Size(d.RawH);
                    sheet.Dividers.Add(d);
                    break;
                }
                 case "CDGMCommentXml":
                 {
                     var comment = new Pt2CommentElem
                     {
                         OldId = Int(c, "localId"),
                         Text = c.Descendants().FirstOrDefault(e => e.Name.LocalName == "CData")?.Value ?? "",
                         Border = (string?)c.Attribute("border") ?? "Single",
                         Align = (string?)c.Attribute("textAlignment") ?? "Left",
                         TextSize = Int(c, "textSize"),
                         BehindLogic = string.Equals((string?)c.Attribute("behindLogic"), "True", StringComparison.OrdinalIgnoreCase),
                         ExpandMacros = string.Equals((string?)c.Attribute("expandMacros"), "True", StringComparison.OrdinalIgnoreCase),
                         MacroEditing = string.Equals((string?)c.Attribute("macroEditing"), "False", StringComparison.OrdinalIgnoreCase) ? false
                             : string.Equals((string?)c.Attribute("macroEditing"), "True", StringComparison.OrdinalIgnoreCase) ? true : false,
                     };
                    if (comment.TextSize <= 0) comment.TextSize = 10;
                    Place(comment, c);
                    sheet.Elems.Add(comment);
                    break;
                }
            }
        }
    }

    private static string MapPouType(string? raw) => (raw ?? "").ToLowerInvariant() switch
    {
        "function" => "function",
        "program" => "program",
        _ => "functionBlock",
    };

    // ---------------------------------------------------------------- globals

    private void CollectReferencedGlobals(List<Pt2Block> blocks)
    {
        foreach (var b in blocks)
        {
            var declared = new HashSet<string>(b.Vars.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            var globals = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            if (b.St != null)
            {
                foreach (var t in _tags)
                    if (!declared.Contains(t.Name) && ContainsWord(b.St, t.Name)) globals.Add(t.Name);
            }
            else
            {
                foreach (var sheet in b.Sheets)
                    foreach (var v in sheet.Elems.OfType<Pt2VarElem>())
                    {
                        if (declared.Contains(v.Name)) continue;
                        if (IsLiteral(v.Name)) continue;
                        globals.Add(v.Name);
                    }
            }

            b.ReferencedGlobals.AddRange(globals);
        }
    }

    private static bool ContainsWord(string text, string word)
    {
        int i = 0;
        while ((i = text.IndexOf(word, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool lb = i == 0 || !IsIdent(text[i - 1]);
            int end = i + word.Length;
            bool rb = end >= text.Length || !IsIdent(text[end]);
            if (lb && rb) return true;
            i = end;
        }
        return false;
    }

    private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsLiteral(string name)
    {
        if (name.Length == 0) return true;
        if (name.StartsWith("16#", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("2#", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("8#", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return true;
        return double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    // ---------------------------------------------------------------- emit

    private string Translate(List<Pt2Block> blocks, string projName, bool referencedOnly)
    {
        var sb = new StringBuilder(1 << 22);
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<project xmlns:xhtml=\"http://www.w3.org/1999/xhtml\" ")
          .Append("xmlns:ts=\"http://www.triconex.com/controller/03/2008\" ")
          .Append("xmlns=\"http://www.plcopen.org/xml/tc6.xsd\">\n");
        sb.Append("  <fileHeader companyName=\"\" companyURL=\"\" productName=\"TriStation 1131\" ")
          .Append("productVersion=\"5.8.0.153\" productRelease=\"RC\" ")
          .Append("creationDateTime=\"").Append(Timestamp()).Append("\" ")
          .Append("contentDescription=\"\" prjSchemaVersion=\"5.5.0.216\" />\n");
        sb.Append("  <contentHeader name=\"").Append(Esc(projName)).Append("\" version=\"1.0\" ")
          .Append("downloadedversion=\"0.0\" downloadedtime=\"\" projectfilestatus=\"New or changed; not built\" ")
          .Append("modificationDateTime=\"").Append(Timestamp()).Append("\" ")
          .Append("author=\"TristationUser\" language=\"EN-US\">\n");
        sb.Append("    <coordinateInfo><pageSize x=\"0\" y=\"0\" /><fbd><scaling x=\"0\" y=\"0\" /></fbd>")
          .Append("<ld><scaling x=\"0\" y=\"0\" /></ld><sfc><scaling x=\"0\" y=\"0\" /></sfc></coordinateInfo>\n");
        sb.Append("  </contentHeader>\n");
        sb.Append("  <types>\n    <dataTypes />\n    <pous>\n");
        foreach (var b in blocks)
        {
            if (b.Sheets.Count == 0 && b.St == null) continue;
            try { EmitPou(sb, b); }
            catch (Exception ex) { Console.Error.WriteLine($"  block {b.Name}: {ex.Message}"); }
        }
        sb.Append("    </pous>\n  </types>\n");
        EmitInstances(sb, blocks, projName, referencedOnly);
        sb.Append("</project>\n");
        return sb.ToString();
    }

    private static string Timestamp() =>
        DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private void EmitPou(StringBuilder sb, Pt2Block b)
    {
        sb.Append("      <pou name=\"").Append(Esc(b.Name)).Append("\" version=\"1.0\" pouType=\"").Append(b.PouType).Append("\">\n");

        if (b.St != null)
        {
            if (b.PouType == "function" && b.ReturnType != null)
                sb.Append("        <interface><returnType>").Append(TypeInner(b.ReturnType)).Append("</returnType></interface>\n");
            else
                sb.Append("        <interface />\n");
            sb.Append("        <body><ST><![CDATA[").Append(CData(b.St)).Append("]]></ST></body>\n");
            sb.Append("      </pou>\n");
            return;
        }

        EmitInterface(sb, b);
        EmitFbdBody(sb, b);
        sb.Append("      </pou>\n");
    }

    private void EmitInterface(StringBuilder sb, Pt2Block b)
    {
        sb.Append("        <interface>\n");
        var vars = b.Vars.ToList();
        EmitSection(sb, "localVars name=\"Constants\" constant=\"true\"", vars.Where(v => v.Section == "constVars" || v.Section == "constant"));
        EmitSection(sb, "inputVars", vars.Where(v => v.Section == "input"));
        EmitSection(sb, "outputVars", vars.Where(v => v.Section == "output"));
        EmitSection(sb, "inOutVars", vars.Where(v => v.Section == "inOut"));
        EmitSection(sb, "localVars", vars.Where(v => v.Section == "local" || (v.Section != "input" && v.Section != "output" && v.Section != "inOut" && v.Section != "constVars" && v.Section != "constant")));

        if (b.PouType == "function" && b.ReturnType != null)
            sb.Append("          <returnType>").Append(TypeInner(b.ReturnType)).Append("</returnType>\n");

        if (b.ReferencedGlobals.Count > 0)
        {
            sb.Append("          <globalVars nonretain=\"true\" nonpersistent=\"true\">\n");
            foreach (var name in b.ReferencedGlobals)
            {
                sb.Append("            <variable name=\"").Append(Esc(name)).Append("\">\n");
                sb.Append("              ").Append(TypeBlock(TagType(name) ?? "BOOL")).Append('\n');
                sb.Append("            </variable>\n");
            }
            sb.Append("          </globalVars>\n");
        }
        sb.Append("        </interface>\n");
    }

    private static void EmitSection(StringBuilder sb, string tag, IEnumerable<Pt2Var> vars)
    {
        var list = vars.ToList();
        if (list.Count == 0)
        {
            sb.Append("          <").Append(tag).Append(" />\n");
            return;
        }
        sb.Append("          <").Append(tag).Append(">\n");
        foreach (var v in list)
        {
            sb.Append("            <variable name=\"").Append(Esc(v.Name)).Append("\">\n");
            sb.Append("              ").Append(TypeBlock(v.DataType)).Append('\n');
            if (v.Desc.Length > 0)
                sb.Append("              <documentation><![CDATA[").Append(CData(v.Desc)).Append("]]></documentation>\n");
            sb.Append("            </variable>\n");
        }
        sb.Append("          </").Append(tag).Append(">\n");
    }

    private void EmitFbdBody(StringBuilder sb, Pt2Block b)
    {
        sb.Append("        <body>\n          <FBD>\n");
        sb.Append("            <ts:SHEETS Width=\"").Append(b.HZoneCount * 20 + 2)
          .Append("\" Height=\"").Append(b.VZoneCount * 25 + 2).Append('"');
        if (b.SheetTemplate.Length > 0)
            sb.Append(" Template=\"").Append(Esc(b.SheetTemplate)).Append('"');
        sb.Append(">\n");
        foreach (var sheet in b.Sheets)
        {
            string title = string.IsNullOrWhiteSpace(sheet.Title)
                ? (sheet.Number == 1 ? b.Title : "")
                : sheet.Title;
            sb.Append("              <ts:SHEET Number=\"").Append(sheet.Number).Append("\">");
            foreach (var d in sheet.Dividers)
                sb.Append("<ts:NETWORKDIVIDER Type=\"").Append(Esc(d.Orientation))
                  .Append("\" LocalId=\"").Append(d.OldId)
                  .Append("\" X=\"0\" Y=\"").Append(d.Y).Append("\" />");
            for (int i = 0; i < 10; i++)
            {
                sb.Append("<ts:Macros Name=\"S").Append(i).Append("\">");
                if (sheet.Macros[i].Length > 0)
                    sb.Append("<ts:Value>").Append(Esc(sheet.Macros[i])).Append("</ts:Value>");
                else
                    sb.Append("<ts:Value />");
                sb.Append("</ts:Macros>");
            }
            if (title.Length > 0)
                sb.Append("<ts:Macros Name=\"SHEET_TITLE\"><ts:Value><![CDATA[")
                  .Append(CData(title)).Append("]]></ts:Value></ts:Macros>");
            else
                sb.Append("<ts:Macros Name=\"SHEET_TITLE\"><ts:Value /></ts:Macros>");
            sb.Append("</ts:SHEET>\n");
        }
        sb.Append("            </ts:SHEETS>\n");

        foreach (var sheet in b.Sheets) EmitSheet(sb, sheet);

        sb.Append("          </FBD>\n        </body>\n");
    }

    private void EmitSheet(StringBuilder sb, Pt2Sheet sheet)
    {
        var ctx = new FbdCtx();
        foreach (var e in sheet.Elems)
        {
            if (e is not Pt2CommentElem) ctx.ElemById[e.OldId] = e;
            if (e is Pt2BlockElem be)
            {
                ctx.BlockIds.Add(be.OldId);
                if (be.Collapsed) ctx.Collapsed.Add(be.OldId);
            }
        }
        foreach (var w in sheet.Wires) ctx.WireById[w.OldId] = w;

        foreach (var e in sheet.Elems)
        {
            switch (e)
            {
                case Pt2CommentElem c:
                    EmitComment(sb, c, sheet.Number);
                    break;

                case Pt2BlockElem blk:
                    EmitBlock(sb, blk, sheet, ctx);
                    break;

                case Pt2VarElem v:
                    EmitVar(sb, v, sheet, ctx);
                    break;
            }
        }
    }

    private void EmitBlock(StringBuilder sb, Pt2BlockElem blk, Pt2Sheet sheet, FbdCtx ctx)
    {
        sb.Append("            <block localId=\"").Append(blk.OldId).Append('"');
        sb.Append(" width=\"").Append(blk.W).Append("\" height=\"").Append(blk.Collapsed ? 4 : blk.H).Append('"');
        sb.Append(" typeName=\"").Append(Esc(blk.TypeName)).Append('"');
        if (blk.Instance.Length > 0) sb.Append(" instanceName=\"").Append(Esc(blk.Instance)).Append('"');
        sb.Append('>');
        AppendPosition(sb, blk.X, blk.Y, sheet.Number);

        if (blk.Collapsed)
        {
            sb.Append("<inputVariables /><inOutVariables /><outputVariables />");
            if (blk.DoubleSpace) sb.Append("<ts:DOUBLE_SPACE_TERMINAL>true</ts:DOUBLE_SPACE_TERMINAL>");
            sb.Append("</block>\n");
            return;
        }

        sb.Append("<inputVariables>");
        for (int i = 0; i < blk.Inputs.Count; i++)
        {
            var pin = blk.Inputs[i];
            sb.Append("<variable formalParameter=\"").Append(Esc(pin.Name)).Append("\">");
            sb.Append("<connectionPointIn><relPosition x=\"-1\" y=\"").Append(4 + 4 * i).Append("\" />");
            EmitResolved(sb, ctx, blk.OldId, pin.Name, pin.Wire, (blk.X - 1, blk.Y + 4 + 4 * i));
            sb.Append("</connectionPointIn></variable>");
        }
        sb.Append("</inputVariables>");
        sb.Append("<inOutVariables />");
        sb.Append("<outputVariables>");
        for (int i = 0; i < blk.Outputs.Count; i++)
        {
            sb.Append("<variable formalParameter=\"").Append(Esc(blk.Outputs[i].Name)).Append("\">");
            sb.Append("<connectionPointOut><relPosition x=\"").Append(blk.W + 1)
              .Append("\" y=\"").Append(4 + 4 * i).Append("\" /></connectionPointOut></variable>");
        }
        sb.Append("</outputVariables>");
        if (blk.DoubleSpace) sb.Append("<ts:DOUBLE_SPACE_TERMINAL>true</ts:DOUBLE_SPACE_TERMINAL>");
        sb.Append("</block>\n");
    }

    private void EmitVar(StringBuilder sb, Pt2VarElem v, Pt2Sheet sheet, FbdCtx ctx)
    {
        string tag = v.Kind.Length > 0 ? v.Kind : "inVariable";
        sb.Append("            <").Append(tag).Append(" localId=\"").Append(v.OldId).Append('"');
        sb.Append(" width=\"").Append(v.W).Append("\" height=\"").Append(v.H).Append('"');
        sb.Append('>');
        sb.Append("<ts:FBDITEM_EXT>");
        if (v.IsConstant)
            sb.Append("<ts:DATATYPE>").Append(Esc(v.DataType)).Append("</ts:DATATYPE>");
        else
            sb.Append("<ts:ANNOTATEMONITORVALUE>").Append(Upper(v.AnnotateMonitorValue)).Append("</ts:ANNOTATEMONITORVALUE>");
        sb.Append("<ts:ANNOTATE>").Append(v.Annotate ? "true" : "false").Append("</ts:ANNOTATE>");
        if (v.Annotation.Length > 0)
            sb.Append("<ts:TEXT>").Append(Esc(v.Annotation)).Append("</ts:TEXT>");
        else
            sb.Append("<ts:TEXT />");
        sb.Append("</ts:FBDITEM_EXT>");
        AppendPosition(sb, v.X, v.Y, sheet.Number);

        bool hasIn = tag == "inOutVariable" || tag == "outVariable";
        bool hasOut = tag == "inVariable" || tag == "inOutVariable";
        if (hasIn)
        {
            sb.Append("<connectionPointIn><relPosition x=\"-1\" y=\"1\" />");
            int start = v.InWire >= 0 ? v.InWire : FindSinkWire(ctx, v);
            EmitResolved(sb, ctx, v.OldId, v.Name, start, (v.X - 1, v.Y + 1));
            sb.Append("</connectionPointIn>");
        }
        if (hasOut)
            sb.Append("<connectionPointOut><relPosition x=\"").Append(v.W + 1)
              .Append("\" y=\"1\" /></connectionPointOut>");

        sb.Append("<expression>").Append(Esc(v.Name)).Append("</expression>");
        sb.Append("</").Append(tag).Append(">\n");
    }

    // ------------------------------------------------------------------
    // Connection resolution
    //
    // A sink pin stores connectId pointing at a wire OR (wireless link)
    // directly at an element.  Wire endpoints do not imply direction, so
    // the source is always derived by walking upstream from the sink:
    //   * start wire must contain the sink element as an endpoint
    //   * an endpoint whose connectId is another wire is a tap junction;
    //     the shared position must be a vertex of that wire, otherwise the
    //     link is stale and ref drops it
    //   * direction across a tap is chosen by pin type (output = upstream)
    //   * links whose source or sink is a collapsed block are dropped
    //   * intermediate collinear positions are removed
    // ------------------------------------------------------------------

    private static void EmitResolved(StringBuilder sb, FbdCtx ctx, int sinkId, string sinkPin,
        int startId, (int X, int Y) sinkPos)
    {
        if (!ResolveConn(ctx, sinkId, sinkPin, startId, sinkPos, out int srcId, out string srcPin,
                out var path, out string feedBack))
            return;

        sb.Append("<connection refLocalId=\"").Append(srcId).Append('"');
        if (ctx.BlockIds.Contains(srcId) && srcPin.Length > 0)
            sb.Append(" formalParameter=\"").Append(Esc(srcPin)).Append('"');
        if (feedBack.Length > 0)
            sb.Append(" ts:FeedBack=\"").Append(feedBack).Append('"');
        sb.Append('>');
        foreach (var q in path)
            sb.Append("<position x=\"").Append(q.X).Append("\" y=\"").Append(q.Y).Append("\" />");
        sb.Append("</connection>");
    }

    private static bool ResolveConn(FbdCtx ctx, int sinkId, string sinkPin, int startId,
        (int X, int Y) sinkPos, out int srcId, out string srcPin,
        out List<(int X, int Y)> path, out string feedBack)
    {
        srcId = -1; srcPin = ""; path = null!; feedBack = "";
        if (startId < 0) return false;
        if (ctx.Collapsed.Contains(sinkId)) return false;

        // ---- wireless link: startId names the source element itself ----
        if (!ctx.WireById.TryGetValue(startId, out var w0))
        {
            if (!ctx.ElemById.TryGetValue(startId, out var direct)) return false;
            if (ctx.Collapsed.Contains(startId)) return false;
            if (startId == sinkId) return false;
            var sp = SrcPortPos(direct, sinkId) ?? sinkPos;
            srcId = startId;
            srcPin = ctx.BlockIds.Contains(startId) ? DirectSrcPin(direct, sinkId) : "";
            path = new List<(int, int)> { sinkPos, sp };
            return true;
        }

        if (w0.Positions.Count == 0) return false;

        bool sinkIsA;
        if (w0.AId == sinkId && w0.BId != sinkId) sinkIsA = true;
        else if (w0.BId == sinkId && w0.AId != sinkId) sinkIsA = false;
        else if (w0.AId == sinkId && w0.BId == sinkId)
            sinkIsA = sinkPin.Length == 0 || w0.APin == sinkPin;
        else return false;                       // stale: wire never touches the sink

        var raw = new List<(int X, int Y)>();
        if (sinkIsA) raw.AddRange(w0.Positions);
        else for (int i = w0.Positions.Count - 1; i >= 0; i--) raw.Add(w0.Positions[i]);

        var seen = new HashSet<int> { w0.OldId };
        int othId = sinkIsA ? w0.BId : w0.AId;
        string othPin = sinkIsA ? w0.BPin : w0.APin;

        for (int guard = 0; ctx.WireById.ContainsKey(othId); guard++)
        {
            if (guard > 64) return false;
            var w2 = ctx.WireById[othId];
            if (w2.Positions.Count == 0) return false;
            if (!seen.Add(w2.OldId)) return false;

            var tap = raw[raw.Count - 1];
            int j = w2.Positions.IndexOf(tap);
            if (j < 0) return false;                       // stale junction

            int end = ChooseSourceEnd(ctx, w2);
            if (end < 0) return false;
            int sp = end == 0 ? 0 : w2.Positions.Count - 1;

            if (sp > j) for (int q = j + 1; q <= sp; q++) raw.Add(w2.Positions[q]);
            else if (sp < j) for (int q = j - 1; q >= sp; q--) raw.Add(w2.Positions[q]);

            othId = end == 0 ? w2.AId : w2.BId;
            othPin = end == 0 ? w2.APin : w2.BPin;
        }

        if (!ctx.ElemById.ContainsKey(othId)) return false;
        if (othId == sinkId) return false;
        if (ctx.Collapsed.Contains(othId)) return false;

        srcId = othId;
        srcPin = ctx.BlockIds.Contains(othId) ? othPin : "";
        feedBack = w0.FeedBack ? "true" : "false";

        path = new List<(int, int)>(raw.Count);
        foreach (var q in raw) path.Add((Pos(q.X), Pos(q.Y)));
        SimplifyCollinear(path);
        return path.Count >= 2;
    }

    /// <summary>Index (0 = positions[0], 1 = positions[^1]) of the upstream endpoint of a tap wire.</summary>
    private static int ChooseSourceEnd(FbdCtx ctx, Pt2Wire w)
    {
        int n = w.Positions.Count;
        int rA = RoleOf(ctx, w.AId, w.APin, w.Positions[0]);
        int rB = RoleOf(ctx, w.BId, w.BPin, w.Positions[n - 1]);
        if (rA == RoleSrc && rB != RoleSrc) return 0;
        if (rB == RoleSrc && rA != RoleSrc) return 1;
        if (rA == RoleWire && rB != RoleWire) return 0;
        if (rB == RoleWire && rA != RoleWire) return 1;
        return -1;
    }

    private static int RoleOf(FbdCtx ctx, int id, string pin, (int X, int Y) rawPos)
    {
        if (id >= 0 && ctx.ElemById.TryGetValue(id, out var e))
        {
            int px = Pos(rawPos.X);
            if (e is Pt2BlockElem b)
            {
                bool isIn = false, isOut = false;
                foreach (var i in b.Inputs) if (i.Name == pin) isIn = true;
                foreach (var o in b.Outputs) if (o.Name == pin) isOut = true;
                if (isIn != isOut) return isIn ? RoleSnk : RoleSrc;
                return px >= b.X + b.W ? RoleSrc : RoleSnk;
            }
            if (e is Pt2VarElem v)
            {
                if (v.Kind == "inVariable") return RoleSrc;
                if (v.Kind == "outVariable") return RoleSnk;
                return px >= v.X + v.W ? RoleSrc : RoleSnk;
            }
            return RoleUnknown;
        }
        if (id >= 0 && ctx.WireById.ContainsKey(id)) return RoleWire;
        return RoleUnknown;
    }

    /// <summary>Var input links are only present when inConnectId is stored; fall back to a scan.</summary>
    private static int FindSinkWire(FbdCtx ctx, Pt2VarElem v)
    {
        foreach (var kv in ctx.WireById)
        {
            var w = kv.Value;
            if (w.Positions.Count == 0) continue;
            if (w.AId == v.OldId && RoleOf(ctx, w.AId, w.APin, w.Positions[0]) == RoleSnk) return w.OldId;
            if (w.BId == v.OldId && RoleOf(ctx, w.BId, w.BPin, w.Positions[w.Positions.Count - 1]) == RoleSnk)
                return w.OldId;
        }
        return -1;
    }

    private static string DirectSrcPin(Pt2Elem src, int sinkId)
    {
        if (src is Pt2BlockElem b)
            foreach (var o in b.Outputs) if (o.Wire == sinkId) return o.Name;
        return "";
    }

    private static (int X, int Y)? SrcPortPos(Pt2Elem src, int sinkId)
    {
        if (src is Pt2BlockElem b)
        {
            for (int j = 0; j < b.Outputs.Count; j++)
                if (b.Outputs[j].Wire == sinkId) return (b.X + b.W + 1, b.Y + 4 + 4 * j);
            return null;
        }
        if (src is Pt2VarElem v) return (v.X + v.W + 1, v.Y + 1);
        return null;
    }

    private static void SimplifyCollinear(List<(int X, int Y)> p)
    {
        if (p.Count < 3) return;
        var keep = new List<(int X, int Y)> { p[0] };
        for (int i = 1; i < p.Count - 1; i++)
        {
            var a = keep[keep.Count - 1];
            var b = p[i];
            var c = p[i + 1];
            long cross = (long)(b.X - a.X) * (c.Y - a.Y) - (long)(b.Y - a.Y) * (c.X - a.X);
            if (cross != 0) keep.Add(b);
        }
        keep.Add(p[p.Count - 1]);
        p.Clear();
        p.AddRange(keep);
    }

    private static void EmitComment(StringBuilder sb, Pt2CommentElem c, int sheet)
    {
        sb.Append("            <comment localId=\"").Append(c.OldId)
          .Append("\" height=\"").Append(c.H).Append("\" width=\"").Append(c.W).Append("\">");
        sb.Append("<ts:COMMENT_EXT>");
        sb.Append("<ts:TEXTALIGNMENT>").Append(Esc(c.Align)).Append("</ts:TEXTALIGNMENT>");
        sb.Append("<ts:BORDER>").Append(Esc(c.Border)).Append("</ts:BORDER>");
        sb.Append("<ts:TEXTSIZE>").Append(c.TextSize).Append("</ts:TEXTSIZE>");
        sb.Append("<ts:EXPANDMACROS>").Append(Upper(c.ExpandMacros)).Append("</ts:EXPANDMACROS>");
        sb.Append("<ts:MACROEDITING>").Append(Upper(c.MacroEditing)).Append("</ts:MACROEDITING>");
        sb.Append("<ts:BEHINDLOGIC>").Append(Upper(c.BehindLogic)).Append("</ts:BEHINDLOGIC>");
        sb.Append("</ts:COMMENT_EXT>");
        AppendPosition(sb, c.X, c.Y, sheet);
        sb.Append("<content><![CDATA[").Append(CData(c.Text)).Append("]]></content>");
        sb.Append("</comment>\n");
    }

    private static string Upper(bool b) => b ? "TRUE" : "FALSE";

    private static void AppendPosition(StringBuilder sb, int x, int y, int sheet) =>
        sb.Append("<position x=\"").Append(x).Append("\" y=\"").Append(y)
          .Append("\" ts:SheetNumber=\"").Append(sheet).Append("\" />");

    // ---------------------------------------------------------------- instances / tags

    private void EmitInstances(StringBuilder sb, List<Pt2Block> blocks, string projName, bool referencedOnly)
    {
        sb.Append("  <instances>\n    <configurations>\n");
        sb.Append("      <configuration name=\"").Append(Esc(projName)).Append("\">\n");
        sb.Append("        <resource name=\"").Append(Esc(projName)).Append("\">\n");
        sb.Append("          <task name=\"MAIN\" priority=\"0\" interval=\"20\" />\n");

        var names = referencedOnly
            ? new SortedSet<string>(blocks.SelectMany(b => b.ReferencedGlobals), StringComparer.OrdinalIgnoreCase)
            : new SortedSet<string>(_tags.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        sb.Append("          <globalVars nonretain=\"false\" nonpersistent=\"false\">\n");
        foreach (var name in names)
        {
            var tag = _tags.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            sb.Append("            <variable name=\"").Append(Esc(name)).Append("\">\n");
            sb.Append("              ").Append(TypeBlock(tag?.Type is { Length: > 0 } tt ? tt : "BOOL")).Append('\n');
            sb.Append("              <initialValue><simpleValue value=\"")
              .Append(Esc(tag?.InitialValue ?? "")).Append("\" /></initialValue>\n");
            sb.Append("              <documentation><![CDATA[").Append(CData(tag?.Desc ?? "")).Append("]]></documentation>\n");
            if (tag != null) EmitTagName(sb, tag);
            sb.Append("            </variable>\n");
        }
        sb.Append("          </globalVars>\n");
        sb.Append("        </resource>\n");
        sb.Append("      </configuration>\n");
        sb.Append("    </configurations>\n  </instances>\n");
    }

    /// <summary>
    /// Emits the whole TriStation &lt;ts:TAGNAME&gt; block for one tag from its
    /// Current/Tagnames/Tagnames.csv row - the exact inverse of the XML-to-CSV mapping in
    /// TagGen.SourceMap(). Verified against the official XMLImportExport 5.8.0 export of the
    /// same container; the earlier stub (TAG_DECLARATION SharedRead + a made-up TAG_POINT)
    /// dropped the point assignment, scaling, display, location and I/O module parameters.
    /// </summary>
    private static void EmitTagName(StringBuilder sb, Pt2Tag t)
    {
        // POINT_ASSIGNMENT.PhysicalAddress in the official export equals the CSV Address column
        // (e.g. I/O point B3001: CSV Address=04.03.01 -> PhysicalAddress="04.03.01"), NOT the CSV
        // PhysicalAddress column (IBR.0001 is that internal register form). See TagGen.SourceMap.
        string pa = t.Address.Length > 0 ? t.Address : t.PhysicalAddress;

        sb.Append("              <ts:TAGNAME>\n");
        sb.Append("                <ts:TAG_DECLARATION Group1=\"").Append(Esc(t.Group1))
          .Append("\" Group2=\"").Append(Esc(t.Group2))
          .Append("\" Application=\"").Append(AppOf(t.IsSafety))
          .Append("\" SharedForRead=\"").Append(YesNo(t.SharedRead))
          .Append("\" EnableMultipleWrite=\"").Append(YesNo(t.EnableMultipleWrites))
          .Append("\" />\n");
        sb.Append("                <ts:POINT_ASSIGNMENT TagClass=\"").Append(ClassOf(t.Class))
          .Append("\" PointType=\"").Append(PointTypeOf(t.Location))
          .Append("\" AliasCategory=\"").Append(AliasCategoryOf(t.AliasNumberSelection))
          .Append("\" AliasType=\"").Append(t.AliasType == "1" ? "UNALIASED" : "ALIASED")
          .Append("\" AliasNumber=\"").Append(Esc(t.UserAliasLegacy))
          .Append("\" PhysicalAddress=\"").Append(Esc(pa))
          .Append("\" AliasNumberExt=\"").Append(Esc(t.UserAliasExtended))
          .Append("\" BinNumber=\"0\" BinOffset=\"0\" />\n");
        sb.Append("                <ts:TAG_SCALING MinSpan=\"").Append(Num(t.MinimumValue))
          .Append("\" MaxSpan=\"").Append(Num(t.MaximumValue))
          .Append("\" DisplayPrecision=\"").Append(Num(t.Precision))
          .Append("\" DisableScaling=\"").Append(YesNo(t.DisableScaling))
          .Append("\" />\n");
        sb.Append("                <ts:TAG_DISPLAY TrueStateName=\"").Append(Esc(t.StateNameTRUE))
          .Append("\" FalseStateName=\"").Append(Esc(t.StateNameFALSE))
          .Append("\" TrueStateColor=\"").Append(ColorOf(t.ColorTRUE))
          .Append("\" FalseStateColor=\"").Append(ColorOf(t.ColorFALSE))
          .Append("\" />\n");
        sb.Append("                <ts:TAG_LOCATION AreaName=\"").Append(Esc(t.Area))
          .Append("\" Equipment=\"").Append(Esc(t.Equipment))
          .Append("\" SIFName=\"").Append(Esc(t.SIFName))
          .Append("\" />\n");
        sb.Append("                <ts:SOE_BLOCKS_ASSIGNMENT>").Append(Esc(t.SOEFlags.Length > 0 ? t.SOEFlags : "0"))
          .Append("</ts:SOE_BLOCKS_ASSIGNMENT>\n");
        sb.Append("                <ts:NO_CONVERSION_FOR_REAL>false</ts:NO_CONVERSION_FOR_REAL>\n");
        sb.Append("                <ts:IO_MODULE_PARAMS IoModuleType=\"").Append(IoModuleOf(t.IOModuleType))
          .Append("\" PointOpt=\"NOT_USED\">\n");
        sb.Append("                  <ts:SDI_PARAMS TriggerState=\"NONE\" MinimumTriggerTime=\"").Append(Num(t.MinimumTriggerTime))
          .Append("\" SoeDeadTime=\"").Append(Num(t.SOEDeadTime))
          .Append("\" RParallel=\"0\" RSeries=\"0\" RETP=\"0\" RAdjust=\"").Append(Num(t.R_Adjust))
          .Append("\" />\n");
        sb.Append("                  <ts:SDO_PARAMS ShortedThreshold=\"").Append(Num(t.ShortedThreshold))
          .Append("\" OpenThreshold=\"").Append(Num(t.OpenThreshold))
          .Append("\" />\n");
        sb.Append("                  <ts:AI_PARAMS ClampEngValue=\"").Append(LcBool(t.AI_ClampEngineeringVal))
          .Append("\" FldFaultMon=\"").Append(LcBool(t.AI_FieldFaultMonitor))
          .Append("\" MinRawValue=\"").Append(Num(t.AI_MinimumRawValue))
          .Append("\" MaxRawValue=\"").Append(Num(t.AI_MaximumRawValue))
          .Append("\" MinEngValue=\"").Append(Num(t.AI_MinimumEngValue))
          .Append("\" MaxEngValue=\"").Append(Num(t.AI_MaximumEngValue))
          .Append("\" FilterType=\"").Append(FilterOf(t.AI_FilterType))
          .Append("\" FldFaultLoLimit=\"").Append(F32(t.AI_NamurLowLimit))
          .Append("\" FldFaultHiLimit=\"").Append(F32(t.AI_NamurHighLimit))
          .Append("\" />\n");
        sb.Append("                  <ts:HART_PARAMS Enable=\"").Append(LcBool(t.HART_Enable))
          .Append("\" DtmVendor=\"\" DtmName=\"\" MonConfigChanges=\"").Append(LcBool(t.HART_MonitorConfigChanges))
          .Append("\" MonDeviceHealth=\"").Append(LcBool(t.HART_MonitorDeviceHealth))
          .Append("\" LockConfig=\"").Append(LcBool(t.HART_LockConfiguration))
          .Append("\" />\n");
        sb.Append("                  <ts:PI_PARAMS PIPointType=\"").Append(Num(t.PI_PointType))
          .Append("\" GearTeeth=\"").Append(Num(t.PI_PointGearTeeth))
          .Append("\" GearRatio=\"").Append(Num(t.PI_PointGearRatio))
          .Append("\" />\n");
        sb.Append("                </ts:IO_MODULE_PARAMS>\n");
        sb.Append("              </ts:TAGNAME>\n");
    }

    // ---- tag field reversal (inverse of TagGen.SourceMap) ----

    private static string YesNo(string csv) =>
        csv.Equals("True", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";

    private static string LcBool(string csv) =>
        csv.Equals("True", StringComparison.OrdinalIgnoreCase) ? "true" : "false";

    // The reference dialect cannot tell "Safety" from "Control" (both import as IsSafety=True),
    // so a True flag round-trips as "Safety", the only value a genuine export was seen to use.
    private static string AppOf(string isSafety) =>
        isSafety.Equals("True", StringComparison.OrdinalIgnoreCase) ? "Safety" : "Control";

    private static string PointTypeOf(string location) =>
        location.Trim() switch { "2" => "INPUT", "3" => "OUTPUT", _ => "MEMORY" };

    private static string AliasCategoryOf(string sel) => sel.Trim() == "0" ? "NO_ALIAS" : "DEFAULT_ALIAS";

    private static string ColorOf(string csv) =>
        csv.Trim() switch { "255" => "RED", "65280" => "GREEN", "16711680" => "BLUE", _ => csv };

    private static string ClassOf(string csv) => csv.Trim() == "3" ? "USER_DEFINED" : csv;

    private static string IoModuleOf(string csv) => csv.Length == 0 || csv.Trim() == "0" ? "NA" : csv;

    private static string FilterOf(string csv) => csv.Length == 0 || csv.Trim() == "0" ? "NONE" : csv;

    /// <summary>Shortest round-trippable form of a numeric CSV column ("4.000000" -> "4").</summary>
    private static string Num(string csv) =>
        double.TryParse(csv, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d.ToString("R", CultureInfo.InvariantCulture) : csv;

    /// <summary>TriStation keeps the NAMUR field-fault limits in single precision and prints the
    /// widened double ("3.600000" -> "3.5999999046325684"), so reproduce that exactly.</summary>
    private static string F32(string csv) =>
        double.TryParse(csv, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? ((double)(float)d).ToString("R", CultureInfo.InvariantCulture) : csv;

    private string? TagType(string name) => _tagType.TryGetValue(name, out var t) ? t : null;

    // ---------------------------------------------------------------- type mapping

    private static string TypeBlock(string type)
    {
        string t = (type ?? "").Trim();
        if (t.Length == 0) return "<type><BOOL /></type>";
        string prim = TriPrim(t);
        if (prim.Length > 0) return "<type><" + prim + " /></type>";
        return "<type><derived name=\"" + Esc(t) + "\" /></type>";
    }

    // A function's <returnType> holds the type element directly (no <type> wrapper), unlike a
    // variable's <type> element. Match the reference dialect.
    private static string TypeInner(string type)
    {
        string block = TypeBlock(type);
        const string open = "<type>", close = "</type>";
        return block.StartsWith(open) && block.EndsWith(close)
            ? block.Substring(open.Length, block.Length - open.Length - close.Length)
            : block;
    }

    private static string TriPrim(string iec) => (iec ?? "").Trim().ToLowerInvariant() switch
    {
        "bool" => "BOOL",
        "int" => "INT",
        "dint" => "DINT",
        "sint" => "SINT",
        "usint" => "USINT",
        "uint" => "UINT",
        "udint" => "UDINT",
        "lint" => "LINT",
        "ulint" => "ULINT",
        "real" => "REAL",
        "lreal" => "LREAL",
        "time" => "TIME",
        "byte" => "BYTE",
        "word" => "WORD",
        "dword" => "DWORD",
        "char" => "CHAR",
        "string" => "STRING",
        "date" => "DATE",
        "tod" => "TOD",
        "dt" => "DATE_AND_TIME",
        _ => "",
    };

    // ---------------------------------------------------------------- xml helpers

    private static XElement? Child(XElement parent, string local, string nameAttr) =>
        parent.Elements().FirstOrDefault(e =>
            e.Name.LocalName == local &&
            string.Equals((string?)e.Attribute("Name"), nameAttr, StringComparison.Ordinal));

    private static int Int(XElement e, string attr) =>
        int.TryParse((string?)e.Attribute(attr), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static int AttrInt(XElement e, string attr) =>
        int.TryParse((string?)e.Attribute(attr), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;

    // The native FBD stores positions as 10*export+40 and sizes as 10*export (see FbdGenerator.Tx).
    private static int Pos(int native) => native / 10 - 4;

    private static int Size(int native) => native / 10;

    private static string Esc(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string CData(string s) => s.Replace("]]>", "]]]]><![CDATA[>");

    // ---------------------------------------------------------------- model

    /// <summary>One Current/Tagnames/Tagnames.csv row. Carries every column the TriStation
    /// export turns back into &lt;ts:TAGNAME&gt; children, not just name/type/desc/address.</summary>
    private sealed class Pt2Tag
    {
        public string Name = "";
        public string Type = "";
        public string Desc = "";
        public string Address = "";
        public string PhysicalAddress = "";
        public string Group1 = "";
        public string Group2 = "";
        public string InitialValue = "";
        public string IsSafety = "";
        public string SharedRead = "";
        public string EnableMultipleWrites = "";
        public string AliasNumberSelection = "";
        public string UserAliasLegacy = "";
        public string UserAliasExtended = "";
        public string AliasType = "";
        public string Location = "";
        public string MinimumValue = "";
        public string MaximumValue = "";
        public string Precision = "";
        public string DisableScaling = "";
        public string StateNameTRUE = "";
        public string ColorTRUE = "";
        public string StateNameFALSE = "";
        public string ColorFALSE = "";
        public string Area = "";
        public string Equipment = "";
        public string SIFName = "";
        public string IOModuleType = "";
        public string MinimumTriggerTime = "";
        public string SOEDeadTime = "";
        public string R_Adjust = "";
        public string ShortedThreshold = "";
        public string OpenThreshold = "";
        public string AI_FilterType = "";
        public string AI_ClampEngineeringVal = "";
        public string AI_FieldFaultMonitor = "";
        public string AI_NamurHighLimit = "";
        public string AI_NamurLowLimit = "";
        public string AI_MinimumRawValue = "";
        public string AI_MaximumRawValue = "";
        public string AI_MinimumEngValue = "";
        public string AI_MaximumEngValue = "";
        public string HART_Enable = "";
        public string HART_MonitorDeviceHealth = "";
        public string HART_MonitorConfigChanges = "";
        public string HART_LockConfiguration = "";
        public string Class = "";
        public string SOEFlags = "";
        public string PI_PointType = "";
        public string PI_PointGearTeeth = "";
        public string PI_PointGearRatio = "";
    }

    private sealed class Pt2Var
    {
        public string Name = "";
        public string DataType = "";
        public string Init = "";
        public string Desc = "";
        public string Section = "local";
        public bool IsInstance;
    }

    private sealed class Pt2Block
    {
        public string Name = "";
        public string PouType = "functionBlock";
        public string? ReturnType;
        public string? St;
        public string Title = "";
        public string DrawingNumber = "";
        public string DrawingTitle = "";
        public string DrawingSize = "";
        public string SheetTemplate = "";
        public string Revision = "";
        public int HZoneCount;
        public int VZoneCount;
        public List<Pt2Var> Vars = new();
        public List<Pt2Sheet> Sheets = new();
        public List<string> ReferencedGlobals = new();
    }

    private sealed class Pt2Sheet
    {
        public int Number;
        public string Title = "";
        public List<Pt2Elem> Elems = new();
        public List<Pt2Wire> Wires = new();
        public List<Pt2Divider> Dividers = new();
        public string[] Macros = new string[10];
    }

    private abstract class Pt2Elem
    {
        public int OldId;
        public int X, Y, W = 16, H = 6;
        /// <summary>Raw export units as stored in the container (lossless, incl. odd values).</summary>
        public int RawX, RawY, RawW = 160, RawH = 60;
    }

    private sealed class Pt2BlockElem : Pt2Elem
    {
        public string TypeName = "";
        public string Instance = "";
        public bool IsFunction;
        public bool DoubleSpace;
        /// <summary>Collapsed runtime marker (snapEventId="0"); exports with height 4 and no pins.</summary>
        public bool Collapsed;
        public List<Pt2Pin> Inputs = new();
        public List<Pt2Pin> Outputs = new();
    }

    /// <summary>Wire may reference a wire id or (for direct, wire-less links) an element id.</summary>
    private sealed class Pt2Pin { public string Name = ""; public int Wire = -1; }

    private sealed class Pt2VarElem : Pt2Elem
    {
        public string Name = "";
        public string VarType = "";
        public string Kind = "";
        public string DataType = "";
        public bool IsConstant;
        public bool AnnotateMonitorValue;
        public string Annotation = "";
        public bool Annotate;
        public int InWire = -1;
        public int OutWire = -1;
    }

    private sealed class Pt2CommentElem : Pt2Elem
    {
        public string Text = "";
        public string Border = "Single";
        public string Align = "Left";
        public int TextSize = 10;
        public bool BehindLogic;
        public bool ExpandMacros;
        public bool MacroEditing;
    }

    private sealed class Pt2Divider
    {
        public int OldId;
        public string Orientation = "HORIZONTAL";
        public int X, Y, W, H;
        public int RawX, RawY, RawW, RawH;
    }

    private sealed class Pt2Wire
    {
        public int OldId;
        /// <summary>Embed="input" endpoint — always positions[0]. Role is NOT implied.</summary>
        public int AId = -1;
        public string APin = "";
        /// <summary>Embed="output" endpoint — always positions[^1]. Role is NOT implied.</summary>
        public int BId = -1;
        public string BPin = "";
        /// <summary>Manhattan path exactly as stored (raw export units, no direction).</summary>
        public List<(int X, int Y)> Positions = new();
        /// <summary>Native feedBack flag; exported as ts:FeedBack for wire-backed links.</summary>
        public bool FeedBack;
    }

    private sealed class FbdCtx
    {
        public Dictionary<int, Pt2Wire> WireById = new();
        public Dictionary<int, Pt2Elem> ElemById = new();
        public HashSet<int> BlockIds = new();
        public HashSet<int> Collapsed = new();
    }

    private const int RoleUnknown = 0, RoleSrc = 1, RoleSnk = 2, RoleWire = 3;
}
