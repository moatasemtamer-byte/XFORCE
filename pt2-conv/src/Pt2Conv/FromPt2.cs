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
    private List<string>? _projectElems;

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
            Console.Error.WriteLine("  --name: contentHeader name (default: PT2X file name)");
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
        string projName = nameArg ?? Path.GetFileNameWithoutExtension(input);

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
        var xml = self.Translate(blocks, projName, referencedOnly);
        if (xml.EndsWith("\n", StringComparison.Ordinal)) xml = xml.Substring(0, xml.Length - 1);
        xml = ToCrlf(xml);
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
        string projName = Path.GetFileNameWithoutExtension(input);

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

    /// <summary>
    /// Decompiles a container into the flat <see cref="PdfProject"/> model the PDF
    /// renderer draws. Reuses the same decoders as <see cref="ToJson"/>, so the two
    /// views never drift: same POUs, same sheets, same geometry.
    /// </summary>
    public static PdfProject BuildPdfModel(string input, string? projectName = null)
    {
        var cf = CompoundFile.Open(input);
        var streams = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in cf.ListStreams()) streams[s.Path] = s.Data;

        var tags = LoadTags(streams);
        var self = new FromPt2(streams, tags);
        string projName = projectName ?? Path.GetFileNameWithoutExtension(input);

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

        var project = new PdfProject
        {
            Name = projName,
            Description = self.ProjectDescription(),
        };

        foreach (var b in blocks.Where(b => b.Sheets.Count > 0 || b.St != null))
        {
            var pou = new PdfPou
            {
                Name = b.Name,
                PouType = b.PouType,
                ReturnType = b.ReturnType,
                Title = b.Title,
                Drawing = new PdfDrawing
                {
                    Number = b.DrawingNumber,
                    Title = b.DrawingTitle,
                    Size = string.IsNullOrWhiteSpace(b.DrawingSize) ? "D" : b.DrawingSize,
                    SheetTemplate = b.SheetTemplate,
                    Rev = b.Revision,
                    HZones = b.HZoneCount > 0 ? b.HZoneCount : 16,
                    VZones = b.VZoneCount > 0 ? b.VZoneCount : 8,
                },
                St = b.St,
                ReferencedGlobals = b.ReferencedGlobals.ToList(),
                Vars = b.Vars.Select(v => new PdfVar
                {
                    Name = v.Name,
                    DataType = v.DataType,
                    Init = v.Init,
                    Desc = v.Desc,
                    Section = v.Section,
                    IsInstance = v.IsInstance,
                }).ToList(),
            };

            foreach (var s in b.Sheets)
            {
                var sheet = new PdfSheet { Number = s.Number, Title = s.Title };
                foreach (var e in s.Elems) sheet.Elems.Add(BuildPdfElem(e, b, s, projName, b.Sheets.Count));
                foreach (var w in s.Wires)
                {
                    sheet.Wires.Add(new PdfWire
                    {
                        Id = w.OldId,
                        Positions = w.Positions.Select(p => ((double)p.X, (double)p.Y)).ToList(),
                    });
                }
                pou.Sheets.Add(sheet);
            }

            project.Pous.Add(pou);
        }

        project.Tags = tags.Select(t => new PdfTag
        {
            Name = t.Name,
            Type = t.Type,
            Desc = t.Desc,
            Address = t.Address,
        }).ToList();

        return project;
    }

    private static PdfElem BuildPdfElem(Pt2Elem e, Pt2Block b, Pt2Sheet s, string projName, int sheetCount)
    {
        switch (e)
        {
            case Pt2BlockElem blk:
                return new PdfBlockElem
                {
                    Id = blk.OldId,
                    X = blk.RawX, Y = blk.RawY, W = blk.RawW, H = blk.RawH,
                    TypeName = blk.TypeName,
                    Instance = blk.Instance,
                    IsFunction = blk.IsFunction,
                    Inputs = blk.Inputs.Select(p => new PdfPin { Name = p.Name, Wire = p.Wire }).ToList(),
                    Outputs = blk.Outputs.Select(o => o.Name).ToList(),
                };

            case Pt2VarElem v:
            {
                string annotation = v.Annotation.Contains("DESCRIPTION")
                    ? ExpandMacros(v.Annotation, b, s, projName, sheetCount)
                        .Replace("%DESCRIPTION", b.Vars.FirstOrDefault(x => string.Equals(x.Name, v.Name, StringComparison.OrdinalIgnoreCase))?.Desc ?? "")
                        .Trim()
                    : ExpandMacros(v.Annotation, b, s, projName, sheetCount);
                return new PdfVarElem
                {
                    Id = v.OldId,
                    X = v.RawX, Y = v.RawY, W = v.RawW, H = v.RawH,
                    Name = v.Name,
                    VarType = v.VarType,
                    Annotation = annotation,
                    Annotate = v.Annotate,
                    HasInWire = v.InWire > 0,
                    HasOutWire = v.OutWire > 0,
                };
            }

            case Pt2CommentElem c:
                return new PdfCommentElem
                {
                    Id = c.OldId,
                    X = c.RawX, Y = c.RawY, W = c.RawW, H = c.RawH,
                    Text = c.ExpandMacros ? ExpandMacros(c.Text, b, s, projName, sheetCount) : c.Text,
                    Border = c.Border,
                    Align = c.Align,
                    TextSize = c.TextSize,
                    BehindLogic = c.BehindLogic,
                };

            default:
                return new PdfUnknownElem
                {
                    Id = e.OldId,
                    X = e.RawX, Y = e.RawY, W = e.RawW, H = e.RawH,
                };
        }
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
            if (project != null && !project.Exists(p => p.Equals(folder, StringComparison.OrdinalIgnoreCase))) continue;
            folders.Add(folder);
        }
        return folders;
    }

    /// <summary>Names of project-local elements (LibID="1") from Current/Project.xml, in document
    /// order (the ref export's POU order), or null when the catalog is unavailable (in which case
    /// every element folder is treated as a POU).</summary>
    private List<string>? ProjectElementNames()
    {
        if (_projectElems != null) return _projectElems.Count == 0 ? null : _projectElems;
        _projectElems = new List<string>();
        if (!_streams.TryGetValue("Current/Project.xml", out var data)) return null;
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
            foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "Class" && e.Attribute("ElemName") != null))
            {
                if ((string?)e.Attribute("LibID") == "1")
                {
                    string name = (string)e.Attribute("ElemName")!;
                    if (!_projectElems.Contains(name))
                        _projectElems.Add(name);
                }
            }
        }
        catch { /* ignore - fall back to all folders */ }
        return _projectElems.Count == 0 ? null : _projectElems;
    }

    /// <summary>POUs in Project.xml element order. The folder enumeration is alphabetical, but the
    /// ref export follows the LibID=1 catalog sequence (FB04_DI, FB03_TC, STFB1, ...).</summary>
    private List<Pt2Block> OrderPous(List<Pt2Block> blocks)
    {
        var order = ProjectElementNames();
        if (order == null) return blocks;
        return blocks.OrderBy(b =>
        {
            int i = order.FindIndex(x => x.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? int.MaxValue : i;
        }).ToList();
    }

    /// <summary>Project comment: the Project.xml root Description (ref export writes it as
    /// fileHeader contentDescription with &#xA; escapes and as a contentHeader &lt;Comment&gt; child). The
    /// container stores no project name; the ref export uses the PT2X file name instead.</summary>
    private string ProjectDescription()
    {
        if (!_streams.TryGetValue("Current/Project.xml", out var data)) return "";
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
            return (string?)doc.Root?.Attribute("Description") ?? "";
        }
        catch { /* ignore */ }
        return "";
    }

    /// <summary>fileHeader previousTSVersion: ProjectInfo/ProjectInfo.Xml (compound root)
    /// PreviousTSVersion (ref: 4.15.0.260 for this container); falls back to productVersion.</summary>
    private string PreviousTsVersion(string fallback)
    {
        if (!_streams.TryGetValue("ProjectInfo/ProjectInfo.Xml", out var data)) return fallback;
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
            string v = (string?)doc.Root?.Attribute("PreviousTSVersion") ?? "";
            return v.Length > 0 ? v : fallback;
        }
        catch { /* ignore */ }
        return fallback;
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
        string? fbd = null, st = null, doc = null;
        foreach (var path in _streams.Keys)
        {
            var parts = path.Split('/');
            if (parts.Length != 4) continue;
            if (!parts[2].Equals(folder, StringComparison.OrdinalIgnoreCase)) continue;
            if (parts[3].EndsWith(".FBD", StringComparison.OrdinalIgnoreCase)) fbd = path;
            else if (parts[3].EndsWith(".ST", StringComparison.OrdinalIgnoreCase)) st = path;
            else if (parts[3].EndsWith(".DOC", StringComparison.OrdinalIgnoreCase)) doc = path;
        }

        if (fbd != null) return ParseFbd(folder, _streams[fbd]);
        if (st != null) return ParseSt(folder, _streams[st], doc != null ? _streams[doc] : null);
        return null;
    }

    private static Pt2Block ParseSt(string folder, byte[] data, byte[]? docData = null)
    {
        string text = DecodeText(data);
        var (name, pouType, returnType) = ParseStHeader(text, folder);
        var b = new Pt2Block
        {
            Name = name,
            PouType = pouType,
            ReturnType = returnType,
            St = text,
        };
        if (docData != null)
        {
            try
            {
                var doc = XDocument.Parse(Encoding.UTF8.GetString(docData));
                var root = doc.Root;
                b.DrawingNumber = (string?)root?.Attribute("drawingNumber") ?? "";
                b.DrawingTitle = (string?)root?.Attribute("drawingTitle") ?? "";
                b.ApprovedName = (string?)root?.Attribute("approvedName") ?? "";
                b.ApprovedTime = (string?)root?.Attribute("approvedTime") ?? "";
                b.Revision = (string?)root?.Attribute("rev") ?? "";
                b.ExtOptFlags = Int(root, "extOptFlags");
                b.CemEventInputs = Int(root, "cemEventInputs");
                b.CemActionOutputs = Int(root, "cemActionOutputs");
                var macros = root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Collection" && (string?)e.Attribute("Name") == "userMacros");
                if (macros != null)
                {
                    int mi = 0;
                    foreach (var mc in macros.Elements().Where(e => e.Name.LocalName == "Class"))
                    {
                        if (mi > 9) break;
                        b.E09[mi++] = (string?)mc.Attribute("Value") ?? "";
                    }
                }
            }
            catch { /* ignore */ }
        }
        if (b.ExtOptFlags == 0) b.ExtOptFlags = 3;
        b.ColorMonitoring = (b.ExtOptFlags & 0x200) != 0;
        return b;
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
            ApprovedName = (string?)root.Attribute("approvedName") ?? "",
            ApprovedTime = (string?)root.Attribute("approvedTime") ?? "",
            ExtOptFlags = Int(root, "extOptFlags"),
            CemEventInputs = Int(root, "cemEventInputs"),
            CemActionOutputs = Int(root, "cemActionOutputs"),
            HZoneCount = Int(root, "hZoneCount"),
            VZoneCount = Int(root, "vZoneCount"),
        };
        if (block.ExtOptFlags == 0) block.ExtOptFlags = 3;
        block.ColorMonitoring = (block.ExtOptFlags & 0x200) != 0;
        for (int i = 0; i < 10; i++)
            block.E09[i] = (string?)root.Attribute("D" + i) ?? "";

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
                    "5" => "global",
                    _ => "local",
                };
                var pv = new Pt2Var
                {
                    Name = name,
                    DataType = dt,
                    Init = (string?)c.Attribute("initVal") ?? "",
                    Desc = (string?)c.Attribute("desc") ?? "",
                    Section = section,
                    IsInstance = isInstance,
                };
                if (section == "global") block.DeclaredGlobals.Add(pv);
                else block.Vars.Add(pv);
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
                                Negated = string.Equals((string?)pin.Attribute("invertedPin"), "True", StringComparison.OrdinalIgnoreCase),
                            });
                    var outputs = Child(c, "Collection", "outputs");
                    if (outputs != null)
                        foreach (var pin in outputs.Elements().Where(e => e.Name.LocalName == "Class"))
                            b.Outputs.Add(new Pt2Pin
                            {
                                Name = (string?)pin.Attribute("formalParam") ?? "",
                                Wire = AttrInt(pin, "connectId"),
                                Negated = string.Equals((string?)pin.Attribute("invertedPin"), "True", StringComparison.OrdinalIgnoreCase),
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
                        IsConstant = string.Equals((string?)c.Attribute("varType"), "CONSTANT", StringComparison.OrdinalIgnoreCase),
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
        string desc = ProjectDescription();
        sb.Append("  <fileHeader companyName=\"Schneider Electric\" companyURL=\"www.schneider-electric.com\" productName=\"TriStation 1131\" ")
          .Append("productVersion=\"5.8.0.153\" productRelease=\"RC\" ")
          .Append("creationDateTime=\"").Append(Timestamp()).Append("\" ")
          .Append("contentDescription=\"").Append(Esc(desc)).Append("\" ")
          .Append("prjSchemaVersion=\"5.8.0.153\" previousTSVersion=\"").Append(PreviousTsVersion("5.8.0.153")).Append("\" />\n");
        sb.Append("  <contentHeader name=\"").Append(Esc(projName)).Append("\" version=\"1.0\" ")
          .Append("downloadedversion=\"0.0\" downloadedtime=\"\" projectfilestatus=\"New or changed; not built\" ")
          .Append("modificationDateTime=\"").Append(Timestamp()).Append("\" ")
          .Append("author=\"TristationUser\" language=\"EN-US\">\n");
        sb.Append(desc.Length > 0
            ? "    <Comment>" + EscText(desc) + "</Comment>\n"
            : "    <Comment />\n");
        sb.Append("    <coordinateInfo>\n")
          .Append("      <pageSize x=\"0\" y=\"0\" />\n")
          .Append("      <fbd>\n        <scaling x=\"0\" y=\"0\" />\n      </fbd>\n")
          .Append("      <ld>\n        <scaling x=\"0\" y=\"0\" />\n      </ld>\n")
          .Append("      <sfc>\n        <scaling x=\"0\" y=\"0\" />\n      </sfc>\n")
          .Append("    </coordinateInfo>\n");
        sb.Append("  </contentHeader>\n");
        sb.Append("  <types>\n    <dataTypes />\n    <pous>\n");
        foreach (var b in blocks)
        {
            if (b.Sheets.Count == 0 && b.St == null) continue;
            try { EmitPou(sb, b); }
            catch (Exception ex) { Console.Error.WriteLine($"  block {b.Name}: {ex.Message}"); }
        }
        sb.Append("    </pous>\n  </types>\n");
        EmitInstances(sb, blocks, referencedOnly);
        EmitProjectExt(sb);
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
            sb.Append("        <body>\n          <ST><![CDATA[").Append(CData(b.St)).Append("]]></ST>\n        </body>\n");
            sb.Append("        <documentation />\n");
            EmitPouExt(sb, b);
            sb.Append("      </pou>\n");
            return;
        }

        EmitInterface(sb, b);
        EmitFbdBody(sb, b);
        sb.Append("        <documentation />\n");
        EmitPouExt(sb, b);
            sb.Append("      </pou>\n");
    }

    private void EmitInterface(StringBuilder sb, Pt2Block b)
    {
        sb.Append("        <interface>\n");
        var vars = b.Vars.ToList();
        var consts = b.Sheets
            .SelectMany(s => s.Elems)
            .OfType<Pt2VarElem>()
            .Where(v => v.IsConstant)
            .OrderByDescending(v => v.OldId)
            .Select(v => new Pt2Var { Name = v.Name, DataType = v.DataType })
            .ToList();
        EmitSection(sb, "localVars name=\"Constants\" constant=\"true\"", consts, false);
        EmitSection(sb, "inputVars", vars.Where(v => v.Section == "input"), true);
        EmitSection(sb, "inOutVars", vars.Where(v => v.Section == "inOut"), true);
        EmitSection(sb, "localVars", vars.Where(v => v.Section == "local"), true);
        EmitSection(sb, "outputVars", vars.Where(v => v.Section == "output"), true);

        if (b.PouType == "function" && b.ReturnType != null)
            sb.Append("          <returnType>").Append(TypeInner(b.ReturnType)).Append("</returnType>\n");

        var gset = new SortedSet<string>(StringComparer.Ordinal);
        var gdecl = new Dictionary<string, Pt2Var>(StringComparer.Ordinal);
        foreach (var g in b.DeclaredGlobals)
        {
            gset.Add(g.Name);
            gdecl[g.Name] = g;
        }
        foreach (var n in b.ReferencedGlobals) gset.Add(n);
        if (gset.Count > 0)
        {
            sb.Append("          <globalVars nonretain=\"true\" nonpersistent=\"true\">\n");
            foreach (var name in gset)
            {
                string dt = gdecl.TryGetValue(name, out var dv) && dv.DataType.Length > 0
                    ? dv.DataType
                    : TagType(name) ?? "BOOL";
                sb.Append("            <variable name=\"").Append(Esc(name)).Append("\">\n");
                sb.Append(TypeBlock(dt, "              ")).Append('\n');
                sb.Append("            </variable>\n");
            }
            sb.Append("          </globalVars>\n");
        }
        sb.Append("        </interface>\n");
    }

    private static void EmitSection(StringBuilder sb, string tag, IEnumerable<Pt2Var> vars, bool declVars)
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
            sb.Append(TypeBlock(v.DataType, "              ")).Append('\n');
            if (declVars)
            {
                sb.Append("              <initialValue>\n");
                sb.Append("                <simpleValue value=\"").Append(Esc(v.Init)).Append("\" />\n");
                sb.Append("              </initialValue>\n");
            }
            if (v.Desc.Length > 0)
                sb.Append("              <documentation><![CDATA[").Append(CData(v.Desc)).Append("]]></documentation>\n");
            else if (declVars)
                sb.Append("              <documentation />\n");
            sb.Append("            </variable>\n");
        }
        sb.Append("          </").Append(tag.Split(' ')[0]).Append(">\n");
    }

    private void EmitFbdBody(StringBuilder sb, Pt2Block b)
    {
        Ln(sb, 8, "<body>");
        Ln(sb, 10, "<FBD>");
        string sheetsOpen = "<ts:SHEETS Width=\"" + (b.HZoneCount * 20 + 2) + "\" Height=\"" + (b.VZoneCount * 25 + 2) + "\"";
        if (b.SheetTemplate.Length > 0)
            sheetsOpen += " Template=\"" + Esc(b.SheetTemplate) + "\"";
        Ln(sb, 12, sheetsOpen + ">");
        foreach (var sheet in b.Sheets)
        {
            string title = string.IsNullOrWhiteSpace(sheet.Title)
                ? (sheet.Number == 1 ? b.Title : "")
                : sheet.Title;
            Ln(sb, 14, "<ts:SHEET Number=\"" + sheet.Number + "\">");
            foreach (var d in sheet.Dividers)
                Ln(sb, 16, "<ts:NETWORKDIVIDER Type=\"" + Esc(d.Orientation) + "\" LocalId=\"" + d.OldId
                    + "\" X=\"0\" Y=\"" + d.Y + "\" />");
            for (int i = 0; i < 10; i++)
            {
                Ln(sb, 16, "<ts:Macros Name=\"S" + i + "\">");
                if (sheet.Macros[i].Length > 0)
                    Ln(sb, 18, "<ts:Value><![CDATA[" + CData(sheet.Macros[i]) + "]]></ts:Value>");
                else
                    Ln(sb, 18, "<ts:Value />");
                Ln(sb, 16, "</ts:Macros>");
            }
            Ln(sb, 16, "<ts:Macros Name=\"SHEET_TITLE\">");
            if (title.Length > 0)
                Ln(sb, 18, "<ts:Value><![CDATA[" + CData(title) + "]]></ts:Value>");
            else
                Ln(sb, 18, "<ts:Value />");
            Ln(sb, 16, "</ts:Macros>");
            Ln(sb, 14, "</ts:SHEET>");
        }
        Ln(sb, 12, "</ts:SHEETS>");

        foreach (var sheet in b.Sheets) EmitSheet(sb, sheet);

        Ln(sb, 10, "</FBD>");
        Ln(sb, 8, "</body>");
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
        string open = "<block localId=\"" + blk.OldId + "\" width=\"" + blk.W + "\" height=\""
            + (blk.Collapsed ? 4 : blk.H) + "\" typeName=\"" + Esc(blk.TypeName) + "\"";
        if (blk.Instance.Length > 0 && !blk.Collapsed) open += " instanceName=\"" + Esc(blk.Instance) + "\"";
        Ln(sb, 12, open + ">");
        AppendPosition(sb, 14, blk.X, blk.Y, sheet.Number);

        if (blk.Collapsed)
        {
            Ln(sb, 14, "<inputVariables />");
            Ln(sb, 14, "<inOutVariables />");
            Ln(sb, 14, "<outputVariables />");
            if (blk.DoubleSpace) Ln(sb, 14, "<ts:DOUBLE_SPACE_TERMINAL>true</ts:DOUBLE_SPACE_TERMINAL>");
            Ln(sb, 12, "</block>");
            return;
        }

        int pinBase = blk.IsFunction ? 4 : 6;
        int pinSpacing = blk.DoubleSpace ? 4 : 2;

        if (blk.Inputs.Count == 0)
            Ln(sb, 14, "<inputVariables />");
        else
        {
            Ln(sb, 14, "<inputVariables>");
            for (int i = 0; i < blk.Inputs.Count; i++)
            {
                var pin = blk.Inputs[i];
                int py = pinBase + pinSpacing * i;
                string vopen = "<variable formalParameter=\"" + Esc(ExportInPinName(blk.TypeName, pin.Name)) + "\"";
                if (pin.Negated) vopen += " negated=\"true\"";
                Ln(sb, 16, vopen + ">");
                Ln(sb, 18, "<connectionPointIn>");
                Ln(sb, 20, "<relPosition x=\"-1\" y=\"" + py + "\" />");
                EmitResolved(sb, ctx, blk.OldId, pin.Name, pin.Wire, (blk.X - 1, blk.Y + py), 20);
                Ln(sb, 18, "</connectionPointIn>");
                Ln(sb, 16, "</variable>");
            }
            Ln(sb, 14, "</inputVariables>");
        }
        Ln(sb, 14, "<inOutVariables />");
        if (blk.Outputs.Count == 0)
            Ln(sb, 14, "<outputVariables />");
        else
        {
            Ln(sb, 14, "<outputVariables>");
            for (int i = 0; i < blk.Outputs.Count; i++)
            {
                int py = pinBase + pinSpacing * i;
                string vopen = "<variable formalParameter=\"" + Esc(blk.Outputs[i].Name) + "\"";
                if (blk.Outputs[i].Negated) vopen += " negated=\"true\"";
                Ln(sb, 16, vopen + ">");
                Ln(sb, 18, "<connectionPointOut>");
                Ln(sb, 20, "<relPosition x=\"" + (blk.W + 1) + "\" y=\"" + py + "\" />");
                Ln(sb, 18, "</connectionPointOut>");
                Ln(sb, 16, "</variable>");
            }
            Ln(sb, 14, "</outputVariables>");
        }
        if (blk.DoubleSpace) Ln(sb, 14, "<ts:DOUBLE_SPACE_TERMINAL>true</ts:DOUBLE_SPACE_TERMINAL>");
        Ln(sb, 12, "</block>");
    }

    private static string ExportInPinName(string typeName, string name)
    {
        if (typeName == "MUX" && name.Length > 2 && name.StartsWith("IN", StringComparison.Ordinal)
            && int.TryParse(name.Substring(2), out int n))
            return "IN" + (n + 1).ToString();
        return name;
    }

    private void EmitVar(StringBuilder sb, Pt2VarElem v, Pt2Sheet sheet, FbdCtx ctx)
    {
        string tag = v.Kind.Length > 0 ? v.Kind : "inVariable";
        Ln(sb, 12, "<" + tag + " localId=\"" + v.OldId + "\" height=\"" + v.H + "\" width=\"" + v.W + "\">");
        Ln(sb, 14, "<ts:FBDITEM_EXT>");
        if (v.IsConstant)
            Ln(sb, 16, "<ts:DATATYPE>" + Esc(v.DataType) + "</ts:DATATYPE>");
        else
            Ln(sb, 16, "<ts:ANNOTATEMONITORVALUE>" + Upper(v.AnnotateMonitorValue) + "</ts:ANNOTATEMONITORVALUE>");
        Ln(sb, 16, "<ts:ANNOTATE>" + (v.Annotate ? "true" : "false") + "</ts:ANNOTATE>");
        if (v.Annotation.Length > 0)
            Ln(sb, 16, "<ts:TEXT>" + Esc(v.Annotation) + "</ts:TEXT>");
        else
            Ln(sb, 16, "<ts:TEXT />");
        Ln(sb, 14, "</ts:FBDITEM_EXT>");
        AppendPosition(sb, 14, v.X, v.Y, sheet.Number);

        bool hasIn = tag == "inOutVariable" || tag == "outVariable";
        bool hasOut = tag == "inVariable" || tag == "inOutVariable";
        if (hasIn)
        {
            Ln(sb, 14, "<connectionPointIn>");
            Ln(sb, 16, "<relPosition x=\"-1\" y=\"1\" />");
            int start = v.InWire >= 0 ? v.InWire : FindSinkWire(ctx, v);
            EmitResolved(sb, ctx, v.OldId, v.Name, start, (v.X - 1, v.Y + 1), 16);
            Ln(sb, 14, "</connectionPointIn>");
        }
        if (hasOut)
        {
            Ln(sb, 14, "<connectionPointOut>");
            Ln(sb, 16, "<relPosition x=\"" + (v.W + 1) + "\" y=\"1\" />");
            Ln(sb, 14, "</connectionPointOut>");
        }

        Ln(sb, 14, "<expression>" + Esc(v.Name) + "</expression>");
        Ln(sb, 12, "</" + tag + ">");
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
        int startId, (int X, int Y) sinkPos, int indent)
    {
        if (!ResolveConn(ctx, sinkId, sinkPin, startId, sinkPos, out int srcId, out string srcPin,
                out var path, out string feedBack))
            return;

        string open = "<connection refLocalId=\"" + srcId + "\"";
        if (ctx.BlockIds.Contains(srcId) && srcPin.Length > 0)
            open += " formalParameter=\"" + Esc(srcPin) + "\"";
        if (feedBack.Length > 0)
            open += " ts:FeedBack=\"" + feedBack + "\"";
        Ln(sb, indent, open + ">");
        foreach (var q in path)
            Ln(sb, indent + 2, "<position x=\"" + q.X + "\" y=\"" + q.Y + "\" />");
        Ln(sb, indent, "</connection>");
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

        for (int guard = 0; guard < 128; guard++)
        {
            if (ctx.WireById.ContainsKey(othId))
            {
                var w2 = ctx.WireById[othId];
                if (w2.Positions.Count == 0) break;
                if (!seen.Add(w2.OldId)) break;

                var tap = raw[raw.Count - 1];
                int j = w2.Positions.IndexOf(tap);
                if (j < 0) break;                       // stale junction

                int end = ChooseSourceEnd(ctx, w2);
                if (end < 0)
                {
                    // Both ends are sinks/elements with no clear upstream; include the rest of wire in both directions from tap
                    // to cover all middle taps
                    if (j > 0) for (int q = j - 1; q >= 0; q--) raw.Add(w2.Positions[q]);
                    if (j < w2.Positions.Count - 1) for (int q = j + 1; q < w2.Positions.Count; q++) raw.Add(w2.Positions[q]);
                    // can't follow chain further
                    break;
                }
                int spIdx = end == 0 ? 0 : w2.Positions.Count - 1;

                if (spIdx > j) for (int q = j + 1; q <= spIdx; q++) raw.Add(w2.Positions[q]);
                else if (spIdx < j) for (int q = j - 1; q >= spIdx; q--) raw.Add(w2.Positions[q]);

                othId = end == 0 ? w2.AId : w2.BId;
                othPin = end == 0 ? w2.APin : w2.BPin;
                continue;
            }
            // othId is not a wire - check if it's an element
            if (ctx.ElemById.ContainsKey(othId))
            {
                // If it's an element but not a source in a usable way, look for middle taps into raw path
                break;
            }
            break;
        }

        // If we ended at a non-source element (sink/wire-role element), look for middle taps
        if (!ctx.ElemById.ContainsKey(othId) || !IsSourceElement(ctx, othId, othPin))
        {
            // Look for any other wire not seen that connects to some point on raw path
            // and whose other endpoint is a source element
            foreach (var kv in ctx.WireById)
            {
                var wt = kv.Value;
                if (seen.Contains(wt.OldId)) continue;
                if (wt.Positions.Count == 0) continue;
                // Check if any position of wt lies on raw path
                for (int p = 0; p < wt.Positions.Count; p++)
                {
                    var pt = wt.Positions[p];
                    bool onPath = false;
                    for (int r = 0; r < raw.Count; r++)
                    {
                        if (raw[r] == pt) { onPath = true; break; }
                    }
                    if (!onPath) continue;
                    // Found junction at pt on raw; determine which end of wt is the source end
                    int endSrc = ChooseSourceEnd(ctx, wt);
                    if (endSrc < 0) continue;
                    int srcElem = endSrc == 0 ? wt.AId : wt.BId;
                    string srcPinElem = endSrc == 0 ? wt.APin : wt.BPin;
                    if (srcElem != sinkId && ctx.ElemById.ContainsKey(srcElem) && IsSourceElement(ctx, srcElem, srcPinElem))
                    {
                        int rptIdx = -1;
                        for (int r = 0; r < raw.Count; r++) { if (raw[r] == pt) { rptIdx = r; break; } }
                        if (rptIdx < 0) continue;
                        var pathCand = new List<(int X, int Y)>(raw.Count);
                        for (int r = 0; r <= rptIdx; r++) pathCand.Add(raw[r]);
                        int jWtIdx = p;
                        int spWtIdx = endSrc == 0 ? 0 : wt.Positions.Count - 1;
                        if (spWtIdx > jWtIdx) for (int q = jWtIdx + 1; q <= spWtIdx; q++) pathCand.Add(wt.Positions[q]);
                        else if (spWtIdx < jWtIdx) for (int q = jWtIdx - 1; q >= spWtIdx; q--) pathCand.Add(wt.Positions[q]);
                        if (pathCand.Count < 2) continue;
                        srcId = srcElem;
                        srcPin = ctx.BlockIds.Contains(srcElem) ? srcPinElem : "";
                        feedBack = w0.FeedBack ? "true" : "false";
                        path = new List<(int X, int Y)>(pathCand.Count);
                        foreach (var qq in pathCand) path.Add((Pos(qq.X), Pos(qq.Y)));
                        SimplifyCollinear(path);
                        return path.Count >= 2;
                    }
                }
            }
            return false;
        }

        if (othId == sinkId) return false;
        if (ctx.Collapsed.Contains(othId)) return false;

        srcId = othId;
        srcPin = ctx.BlockIds.Contains(othId) ? othPin : "";
        feedBack = w0.FeedBack ? "true" : "false";

        path = new List<(int X, int Y)>(raw.Count);
        foreach (var q in raw) path.Add((Pos(q.X), Pos(q.Y)));
        SimplifyCollinear(path);
        return path.Count >= 2;
    }    private static bool IsSourceElement(FbdCtx ctx, int id, string pin)
    {
        if (!ctx.ElemById.TryGetValue(id, out var e)) return false;
        if (e is Pt2BlockElem b)
        {
            foreach (var o in b.Outputs) if (o.Name == pin) return true;
            // if pin not found by name, guess by position? but better check
            return false;
        }
        if (e is Pt2VarElem v)
        {
            if (v.Kind == "inVariable" || v.Kind == "inOutVariable") return true;
            return false;
        }
        return false;
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
        Ln(sb, 12, "<comment localId=\"" + c.OldId + "\" height=\"" + c.H + "\" width=\"" + c.W + "\">");
        Ln(sb, 14, "<ts:COMMENT_EXT>");
        Ln(sb, 16, "<ts:TEXTALIGNMENT>" + Esc(c.Align) + "</ts:TEXTALIGNMENT>");
        Ln(sb, 16, "<ts:BORDER>" + Esc(c.Border) + "</ts:BORDER>");
        Ln(sb, 16, "<ts:TEXTSIZE>" + c.TextSize + "</ts:TEXTSIZE>");
        Ln(sb, 16, "<ts:EXPANDMACROS>" + Upper(c.ExpandMacros) + "</ts:EXPANDMACROS>");
        Ln(sb, 16, "<ts:MACROEDITING>" + Upper(c.MacroEditing) + "</ts:MACROEDITING>");
        Ln(sb, 16, "<ts:BEHINDLOGIC>" + Upper(c.BehindLogic) + "</ts:BEHINDLOGIC>");
        Ln(sb, 14, "</ts:COMMENT_EXT>");
        AppendPosition(sb, 14, c.X, c.Y, sheet);
        Ln(sb, 14, "<content><![CDATA[" + CData(c.Text) + "]]></content>");
        Ln(sb, 12, "</comment>");
    }

    private static string Upper(bool b) => b ? "TRUE" : "FALSE";

    private static void AppendPosition(StringBuilder sb, int indent, int x, int y, int sheet) =>
        Ln(sb, indent, "<position x=\"" + x + "\" y=\"" + y + "\" ts:SheetNumber=\"" + sheet + "\" />");

    // ---------------------------------------------------------------- instances / tags

    // Names/order of the container's library references (Project.xml "Libraries" collection).
    private List<string> LibraryRefs()
    {
        var libs = new List<string>();
        if (!_streams.TryGetValue("Current/Project.xml", out var data)) return libs;
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
            foreach (var c in doc.Descendants().Where(c => c.Name.LocalName == "Class" && c.Attribute("LibName") != null))
                libs.Add((string)c.Attribute("LibName")!);
        }
        catch { /* ignore */ }
        return libs;
    }

    /// <summary>Ref writes one self-closing &lt;LIBRARY&gt; per reference at 12sp; the embedded
    /// OLE LIBRARY_DATA blobs (LibType 2/3) are reproduced only in Stage 4.</summary>
    private void EmitLibraryConfiguration(StringBuilder sb)
    {
        var libs = LibraryRefs();
        if (libs.Count == 0) return;
        sb.Append("          <LIBRARY_CONFIGURATION>\n");
        foreach (var l in libs)
            sb.Append("            <LIBRARY Name=\"").Append(Esc(l)).Append("\" />\n");
        sb.Append("          </LIBRARY_CONFIGURATION>\n");
    }

    // ------------------------------------------------------------- controller config

    private static readonly (string Code, string Tag)[] MemoryMap =
    {
        ("MBN", "MEMORY_UNALIASED_BOOL"),
        ("MIN", "MEMORY_UNALIASED_DINT"),
        ("MRN", "MEMORY_UNALIASED_REAL"),
        ("MBR", "MEMORY_READALIASED_BOOL"),
        ("MIR", "MEMORY_READALIASED_DINT"),
        ("MRR", "MEMORY_READALIASED_REAL"),
        ("MBW", "MEMORY_READWRITEALIASED_BOOL"),
        ("MIW", "MEMORY_READWRITEALIASED_DINT"),
        ("MRW", "MEMORY_READWRITEALIASED_REAL"),
        ("IBR", "INPUT_BOOL"),
        ("IIR", "INPUT_DINT"),
        ("IRR", "INPUT_REAL"),
        ("QBW", "OUTPUT_BOOL"),
        ("QIW", "OUTPUT_DINT"),
    };

    private XElement? LoadConfig(string path)
    {
        if (!_streams.TryGetValue(path, out var data)) return null;
        try { return XDocument.Parse(Encoding.UTF8.GetString(data).Replace("\0", "")).Root; }
        catch { return null; }
    }

    private static bool CfgBool(string? s) => string.Equals(s, "True", StringComparison.OrdinalIgnoreCase);

    private static string LBool(bool b) => b ? "true" : "false";

    private static void EmitLeaf(StringBuilder sb, int indent, string name, string value)
    {
        string pad = new string(' ', indent);
        if (value.Length == 0)
            sb.Append(pad).Append("<ts:").Append(name).Append(" />\n");
        else
            sb.Append(pad).Append("<ts:").Append(name).Append('>').Append(Esc(value)).Append("</ts:").Append(name).Append(">\n");
    }

    /// <summary>Emits the &lt;ts:CONTROLLER&gt; block that TriStation derives from the project's
    /// configuration streams (OpParams/TSCommunication/SOEGlobals/GlobalMemory/HwConfiguration,
    /// Platform.Xml, Workspace.Xml). Per-module SETUP_PARAMETERS/MEMORYALLOCATION_PARAMETERS module
    /// descriptors live in TriStation's embedded platform database and are NOT recoverable from the
    /// .pt2x, so those leaves are emitted empty.</summary>
    private void EmitController(StringBuilder sb)
    {
        var op = LoadConfig("Current/Elements/Configuration/OpParams.xml");
        var comm = LoadConfig("Current/Elements/Configuration/TSCommunication.xml");
        var soe = LoadConfig("Current/Elements/Configuration/SOEGlobals.xml");
        var mem = LoadConfig("Current/Elements/Configuration/GlobalMemory.xml");
        var hw = LoadConfig("Current/Elements/Configuration/HwConfiguration.xml");
        var plat = LoadConfig("Current/Platform/Platform.Xml");
        var ws = LoadConfig("Workspace.Xml");

        string platform = (string?)ws?.Attribute("Platform") ?? "TRICON_CX";
        string mpId = MainProcessorId(hw);

        string nodeName = (string?)comm?.Attribute("NetNodeName") ?? "";
        string nodeNumber = (string?)op?.Attribute("NodeNumber") ?? "0";
        string ip = "0.0.0.0";
        if (plat != null && nodeName.Length > 0)
        {
            var v = plat.Descendants("Class")
                .Select(c => (string?)c.Attribute("Value"))
                .FirstOrDefault(s => s != null && s.StartsWith(nodeName + "=", StringComparison.OrdinalIgnoreCase));
            if (v != null)
            {
                var rhs = v.Substring(v.IndexOf('=') + 1);
                var parts = rhs.Split(',');
                if (parts.Length > 0 && parts[0].Length > 0) ip = parts[0];
                if (parts.Length > 1 && parts[1].Length > 0) nodeNumber = parts[1];
            }
        }

        sb.Append("      <ts:CONTROLLER>\n");
        sb.Append("        <ts:PPD_CONFIGURATION Name=\"").Append(Esc(platform)).Append('"');
        sb.Append(" MajorVersion=\"11\" MinorVersion=\"3\"");
        sb.Append(" MainProcessorId=\"").Append(Esc(mpId)).Append("\">\n");

        sb.Append("          <ts:OPERATING_PARAMETERS>\n");
        EmitLeaf(sb, 12, "DISABLE_STOP_ONKEYSWITCH", LBool(CfgBool((string?)op?.Attribute("DisableSTOP"))));
        EmitLeaf(sb, 12, "DISABLE_REMOTE_CHANGE", LBool(CfgBool((string?)op?.Attribute("DisableRemoteChanges"))));
        EmitLeaf(sb, 12, "ALLOW_DISABLING_POINTS", LBool(CfgBool((string?)op?.Attribute("AllowDisablePoints"))));
        EmitLeaf(sb, 12, "ENABLE_TIMESYNC", LBool(CfgBool((string?)op?.Attribute("TimeSync"))));
        EmitLeaf(sb, 12, "ALLOW_EXPIRED_CERT", LBool(CfgBool((string?)op?.Attribute("AllowExpiredCertificate"))));
        EmitLeaf(sb, 12, "GEN_ALARM_WHEN_CERT_EXPIRED", LBool(CfgBool((string?)op?.Attribute("GenerateAlarmForExpiredCertificate"))));
        EmitLeaf(sb, 12, "NUM_OF_DAYS_BEFORE_ALARM_GENERATED", (string?)op?.Attribute("NumberOfDaysBeforeAlarmGenerated") ?? "0");
        sb.Append("          </ts:OPERATING_PARAMETERS>\n");

        sb.Append("          <ts:COMMUNICATION>\n");
        EmitLeaf(sb, 12, "NODENAME", nodeName);
        EmitLeaf(sb, 12, "NODENUMBER", nodeNumber);
        EmitLeaf(sb, 12, "UDPPORT", (string?)comm?.Attribute("UDPPortNumber") ?? "0");
        sb.Append("            <ts:CONNECTION_PARAMETERS>\n");
        EmitLeaf(sb, 14, "IP_ADDRESS", ip);
        EmitLeaf(sb, 14, "SERIAL_PORT_NAME", (string?)plat?.Attribute("DefSerialPort") ?? "");
        EmitLeaf(sb, 14, "SERIAL_BAUDRATE", (string?)comm?.Attribute("BaudRate") ?? "0");
        EmitLeaf(sb, 14, "DEFAULT_CONNECTION_TYPE", CfgBool((string?)comm?.Attribute("UseNetConnection")) ? "Network" : "Serial");
        EmitLeaf(sb, 14, "NETWORK_CONNECTION", LBool(CfgBool((string?)comm?.Attribute("UseNetConnection"))));
        EmitLeaf(sb, 14, "SERIAL_CONNECTION", LBool(CfgBool((string?)comm?.Attribute("UseSerConnection"))));
        sb.Append("            </ts:CONNECTION_PARAMETERS>\n");
        sb.Append("          </ts:COMMUNICATION>\n");

        sb.Append("          <ts:HARDWARE_ALLOCATION>\n");
        sb.Append("            <ts:CHASSIS_CONFIGURATION>\n");
        EmitChassis(sb, hw);
        sb.Append("            </ts:CHASSIS_CONFIGURATION>\n");
        sb.Append("          </ts:HARDWARE_ALLOCATION>\n");

        sb.Append("          <ts:MEMORY_ALLOCATION>\n");
        foreach (var (code, tag) in MemoryMap)
        {
            string v = mem?.Descendants("Class").FirstOrDefault(c => (string?)c.Attribute("Description") == code) is { } el
                ? (string?)el.Attribute("MaxAllocated") ?? "0"
                : "0";
            EmitLeaf(sb, 12, tag, v);
        }
        sb.Append("          </ts:MEMORY_ALLOCATION>\n");
        sb.Append("        </ts:PPD_CONFIGURATION>\n");

        sb.Append("        <ts:SOE_CONFIGURATION>\n");
        var blocks = soe?.Descendants("Class").Where(c => c.Attribute("SoeTitle") != null).ToList();
        for (int i = 0; i < 16; i++)
        {
            var c = blocks != null && i < blocks.Count ? blocks[i] : null;
            sb.Append("          <ts:SOE_BLOCK ID=\"").Append(i + 1).Append("\">\n");
            EmitLeaf(sb, 12, "SOE_BLOCK_NAME", (string?)c?.Attribute("SoeTitle") ?? "");
            EmitLeaf(sb, 12, "SOE_BLOCK_TYPE", SoeTypeName((string?)c?.Attribute("SoeBlockType")));
            EmitLeaf(sb, 12, "SOE_BUFFER_SIZE", (string?)c?.Attribute("SoeBuffSize") ?? "0");
            EmitLeaf(sb, 12, "SOE_BLOCK_MANUAL", CfgBool((string?)c?.Attribute("Manual")) ? "1" : "0");
            EmitLeaf(sb, 12, "SOE_BLOCK_REDUNDANT", CfgBool((string?)c?.Attribute("Redundant")) ? "1" : "0");
            EmitLeaf(sb, 12, "SOE_USE_UTC", LBool(CfgBool((string?)c?.Attribute("UseUTC"))));
            sb.Append("          </ts:SOE_BLOCK>\n");
        }
        sb.Append("          <ts:SOE_TRIP>\n");
        EmitLeaf(sb, 12, "SOE_TRIP_TAGNAME", (string?)soe?.Attribute("TripTagName") ?? "");
        EmitLeaf(sb, 12, "SOE_TRIP_STATE", ((string?)soe?.Attribute("TripState") ?? "FALSE").ToUpperInvariant());
        EmitLeaf(sb, 12, "SOE_TIME_AFTER_TRIP", (string?)soe?.Attribute("TimeAfterTrip") ?? "0");
        EmitLeaf(sb, 12, "SOE_TIME_BEFORE_TRIP", (string?)soe?.Attribute("TimeBeforeTrip") ?? "0");
        EmitLeaf(sb, 12, "DEADBAND", (string?)soe?.Attribute("TripDeadband") ?? "0");
        sb.Append("          </ts:SOE_TRIP>\n");
        sb.Append("        </ts:SOE_CONFIGURATION>\n");

        sb.Append("        <ts:PEER_CONFIGURATION>\n");
        EmitLeaf(sb, 10, "MAXSENDS", (string?)op?.Attribute("PeerMaxSend") ?? "0");
        EmitLeaf(sb, 10, "MAXRECEIVES", (string?)op?.Attribute("PeerMaxReceive") ?? "0");
        sb.Append("        </ts:PEER_CONFIGURATION>\n");
        sb.Append("      </ts:CONTROLLER>\n");
    }

    private void EmitChassis(StringBuilder sb, XElement? hw)
    {
        if (hw == null) return;
        var chassisCol = hw.Elements("Collection").FirstOrDefault(e => (string?)e.Attribute("Name") == "HwChassis")
                         ?? hw.Descendants("Collection").FirstOrDefault(e => (string?)e.Attribute("Name") == "HwChassis");
        if (chassisCol == null) return;
        foreach (var ch in chassisCol.Elements("Class"))
        {
            if (!int.TryParse((string?)ch.Attribute("ChassisNum"), out int num) || num == -1) continue;
            string id = (string?)ch.Attribute("ChassisID") ?? "";
            string type = ChassisType((string?)ch.Attribute("Name") ?? "");
            string desc = ChassisDesc(type);
            if (num == 0)
                sb.Append("              <ts:MAIN_CHASSIS Type=\"").Append(Esc(type)).Append("\" Description=\"").Append(Esc(desc)).Append("\">\n");
            else
                sb.Append("              <ts:EXP_CHASSIS ChassisNumber=\"").Append(num).Append("\" Id=\"").Append(Esc(id))
                  .Append("\" Type=\"").Append(Esc(type)).Append("\" Description=\"").Append(Esc(desc)).Append("\">\n");

            var boards = ch.Elements("Collection").FirstOrDefault(e => (string?)e.Attribute("Name") == "Boards");
            if (boards != null)
            {
                var boardList = boards.Elements("Class").ToList();
                // SLOT_ADDRESS is a 1-based slot within the chassis; the source SlotID is 0-based on
                // some chassis types (e.g. HD_EXP) and 1-based on others, so normalise to the minimum.
                int baseSlot = -1;
                foreach (var b in boardList)
                    if (int.TryParse((string?)b.Attribute("SlotID"), out int s) && s >= 0 && (baseSlot < 0 || s < baseSlot))
                        baseSlot = s;
                if (baseSlot < 0) baseSlot = 0;
                foreach (var b in boardList)
                {
                    if (!int.TryParse((string?)b.Attribute("SlotID"), out int slot)) continue;
                    if (!int.TryParse((string?)b.Attribute("ModuleID"), out int mid)) continue;
                    if (slot < 0 || mid == 259) continue;
                    sb.Append("                <ts:MODULE ID=\"").Append(mid).Append("\">\n");
                    sb.Append("                  <ts:SLOT_ADDRESS>").Append(slot - baseSlot + 1).Append("</ts:SLOT_ADDRESS>\n");
                    sb.Append("                  <ts:SETUP_PARAMETERS />\n");
                    sb.Append("                  <ts:MEMORYALLOCATION_PARAMETERS />\n");
                    sb.Append("                </ts:MODULE>\n");
                }
            }
            sb.Append(num == 0 ? "              </ts:MAIN_CHASSIS>\n" : "              </ts:EXP_CHASSIS>\n");
        }
    }

    private static string ChassisType(string name)
    {
        int i = name.LastIndexOf(": ", StringComparison.Ordinal);
        return i >= 0 ? name.Substring(i + 2) : name;
    }

    private static string ChassisDesc(string type) => type switch
    {
        "8120X_MAIN" => "Tricon CX Main Chassis",
        "HD_EXP" => "High Density Expansion Chassis",
        "LD_EXP" => "Low Density Expansion Chassis",
        _ => type,
    };

    private static string SoeTypeName(string? v) => v switch
    {
        "2" => "HISTORY",
        _ => "UNASSIGNED",
    };

    private static string MainProcessorId(XElement? hw)
    {
        if (hw == null) return "";
        var chassisCol = hw.Elements("Collection").FirstOrDefault(e => (string?)e.Attribute("Name") == "HwChassis");
        if (chassisCol == null) return "";
        foreach (var ch in chassisCol.Elements("Class"))
        {
            if ((string?)ch.Attribute("ChassisNum") != "0") continue;
            var boards = ch.Elements("Collection").FirstOrDefault(e => (string?)e.Attribute("Name") == "Boards");
            if (boards == null) continue;
            foreach (var b in boards.Elements("Class"))
                if (int.TryParse((string?)b.Attribute("SlotID"), out int slot) && slot < 0)
                    return (string?)b.Attribute("ModuleID") ?? "";
        }
        return "";
    }

    private void EmitInstances(StringBuilder sb, List<Pt2Block> blocks, bool referencedOnly)
    {
        sb.Append("  <instances>\n    <configurations>\n");
        sb.Append("      <configuration name=\"MainConfiguration\">\n");
        sb.Append("        <resource name=\"MainResource\">\n");
        sb.Append("          <task name=\"ExecutionList\" interval=\"100\" priority=\"1\" />\n");
        sb.Append("          <AES_MANAGEDLIST />\n");
        EmitLibraryConfiguration(sb);
        sb.Append("        </resource>\n");

        var names = referencedOnly
            ? new SortedSet<string>(blocks.SelectMany(b => b.ReferencedGlobals), StringComparer.OrdinalIgnoreCase)
            : new SortedSet<string>(_tags.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        // Ref splits the tag table at configuration level into two globalVars by Tagnames.csv
        // Location: "1" (memory points) -> nonpersistent, "2"/"3" (I/O) -> persistent (622/399 here).
        var resolved = names
            .Select(n => (Name: n, Tag: _tags.FirstOrDefault(t => t.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        EmitInstanceGlobals(sb, "nonretain=\"true\" nonpersistent=\"true\"",
            resolved.Where(x => (x.Tag?.Location ?? "1") == "1").ToList());
        EmitInstanceGlobals(sb, "nonretain=\"true\" persistent=\"true\"",
            resolved.Where(x => (x.Tag?.Location ?? "1") != "1").ToList());

        sb.Append("      </configuration>\n");
        EmitController(sb);
        sb.Append("    </configurations>\n  </instances>\n");
    }

    private void EmitInstanceGlobals(StringBuilder sb, string attrs, List<(string Name, Pt2Tag? Tag)> items)
    {
        if (items.Count == 0)
        {
            sb.Append("        <globalVars ").Append(attrs).Append(" />\n");
            return;
        }
        sb.Append("        <globalVars ").Append(attrs).Append(">\n");
        foreach (var (name, tag) in items)
        {
            sb.Append("          <variable name=\"").Append(Esc(name)).Append("\">\n");
            sb.Append(TypeBlock(tag?.Type is { Length: > 0 } tt ? tt : "BOOL", "            ")).Append('\n');
            sb.Append("            <initialValue>\n");
            sb.Append("              <simpleValue value=\"").Append(Esc(tag?.InitialValue ?? "")).Append("\" />\n");
            sb.Append("            </initialValue>\n");
            sb.Append("            <documentation><![CDATA[").Append(CData(tag?.Desc ?? "")).Append("]]></documentation>\n");
            if (tag != null) EmitTagName(sb, tag);
            sb.Append("          </variable>\n");
        }
        sb.Append("        </globalVars>\n");
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
        // Indents assume the variable sits at depth 10 (configuration-level globalVars).
        string pa = t.Address.Length > 0 ? t.Address : t.PhysicalAddress;

        sb.Append("            <ts:TAGNAME>\n");
        sb.Append("              <ts:TAG_DECLARATION Group1=\"").Append(Esc(t.Group1))
          .Append("\" Group2=\"").Append(Esc(t.Group2))
          .Append("\" Application=\"").Append(AppOf(t.IsSafety))
          .Append("\" SharedForRead=\"").Append(YesNo(t.SharedRead))
          .Append("\" EnableMultipleWrite=\"").Append(YesNo(t.EnableMultipleWrites))
          .Append("\" />\n");
        sb.Append("              <ts:POINT_ASSIGNMENT TagClass=\"").Append(ClassOf(t.Class))
          .Append("\" PointType=\"").Append(PointTypeOf(t.Location))
          .Append("\" AliasCategory=\"").Append(AliasCategoryOf(t.AliasNumberSelection))
          .Append("\" AliasType=\"").Append(t.AliasType == "1" ? "UNALIASED" : "ALIASED")
          .Append("\" AliasNumber=\"").Append(Esc(t.UserAliasLegacy))
          .Append("\" PhysicalAddress=\"").Append(Esc(pa))
          .Append("\" AliasNumberExt=\"").Append(Esc(t.UserAliasExtended))
          .Append("\" BinNumber=\"0\" BinOffset=\"0\" />\n");
        sb.Append("              <ts:TAG_SCALING MinSpan=\"").Append(Num(t.MinimumValue))
          .Append("\" MaxSpan=\"").Append(Num(t.MaximumValue))
          .Append("\" DisplayPrecision=\"").Append(Num(t.Precision))
          .Append("\" DisableScaling=\"").Append(YesNo(t.DisableScaling))
          .Append("\" />\n");
        sb.Append("              <ts:TAG_DISPLAY TrueStateName=\"").Append(Esc(t.StateNameTRUE))
          .Append("\" FalseStateName=\"").Append(Esc(t.StateNameFALSE))
          .Append("\" TrueStateColor=\"").Append(ColorOf(t.ColorTRUE))
          .Append("\" FalseStateColor=\"").Append(ColorOf(t.ColorFALSE))
          .Append("\" />\n");
        sb.Append("              <ts:TAG_LOCATION AreaName=\"").Append(Esc(t.Area))
          .Append("\" Equipment=\"").Append(Esc(t.Equipment))
          .Append("\" SIFName=\"").Append(Esc(t.SIFName))
          .Append("\" />\n");
        sb.Append("              <ts:SOE_BLOCKS_ASSIGNMENT>").Append(Esc(t.SOEFlags.Length > 0 ? t.SOEFlags : "0"))
          .Append("</ts:SOE_BLOCKS_ASSIGNMENT>\n");
        sb.Append("              <ts:NO_CONVERSION_FOR_REAL>false</ts:NO_CONVERSION_FOR_REAL>\n");
        sb.Append("              <ts:IO_MODULE_PARAMS IoModuleType=\"").Append(IoModuleOf(t.IOModuleType))
          .Append("\" PointOpt=\"NOT_USED\">\n");
        sb.Append("                <ts:SDI_PARAMS TriggerState=\"NONE\" MinimumTriggerTime=\"").Append(Num(t.MinimumTriggerTime))
          .Append("\" SoeDeadTime=\"").Append(Num(t.SOEDeadTime))
          .Append("\" RParallel=\"0\" RSeries=\"0\" RETP=\"0\" RAdjust=\"").Append(Num(t.R_Adjust))
          .Append("\" />\n");
        sb.Append("                <ts:SDO_PARAMS ShortedThreshold=\"").Append(Num(t.ShortedThreshold))
          .Append("\" OpenThreshold=\"").Append(Num(t.OpenThreshold))
          .Append("\" />\n");
        sb.Append("                <ts:AI_PARAMS ClampEngValue=\"").Append(LcBool(t.AI_ClampEngineeringVal))
          .Append("\" FldFaultMon=\"").Append(LcBool(t.AI_FieldFaultMonitor))
          .Append("\" MinRawValue=\"").Append(Num(t.AI_MinimumRawValue))
          .Append("\" MaxRawValue=\"").Append(Num(t.AI_MaximumRawValue))
          .Append("\" MinEngValue=\"").Append(Num(t.AI_MinimumEngValue))
          .Append("\" MaxEngValue=\"").Append(Num(t.AI_MaximumEngValue))
          .Append("\" FilterType=\"").Append(FilterOf(t.AI_FilterType))
          .Append("\" FldFaultLoLimit=\"").Append(F32(t.AI_NamurLowLimit))
          .Append("\" FldFaultHiLimit=\"").Append(F32(t.AI_NamurHighLimit))
          .Append("\" />\n");
        sb.Append("                <ts:HART_PARAMS Enable=\"").Append(LcBool(t.HART_Enable))
          .Append("\" DtmVendor=\"\" DtmName=\"\" MonConfigChanges=\"").Append(LcBool(t.HART_MonitorConfigChanges))
          .Append("\" MonDeviceHealth=\"").Append(LcBool(t.HART_MonitorDeviceHealth))
          .Append("\" LockConfig=\"").Append(LcBool(t.HART_LockConfiguration))
          .Append("\" />\n");
        sb.Append("                <ts:PI_PARAMS PIPointType=\"").Append(Num(t.PI_PointType))
          .Append("\" GearTeeth=\"").Append(Num(t.PI_PointGearTeeth))
          .Append("\" GearRatio=\"").Append(Num(t.PI_PointGearRatio))
          .Append("\" />\n");
        sb.Append("              </ts:IO_MODULE_PARAMS>\n");
        sb.Append("            </ts:TAGNAME>\n");
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

    /// <summary>Variable type wrapper: ref writes it across three lines with the inner type
    /// indented two columns past the &lt;type&gt; line (all call sites sit at known depths).</summary>
    private static string TypeBlock(string type, string indent) =>
        indent + "<type>\n" + indent + "  " + TypeInner(type) + "\n" + indent + "</type>";

    // A function's <returnType> holds the type element directly (no <type> wrapper), unlike a
    // variable's <type> element. Match the reference dialect.
    private static string AppTypeName(int extOptFlags)
    {
        bool safety = (extOptFlags & 1) != 0;
        bool control = (extOptFlags & 2) != 0;
        if (safety && control) return "SAFETY and CONTROL";
        if (safety) return "SAFETY";
        if (control) return "CONTROL";
        return "SAFETY and CONTROL"; // default observed
    }

    private void EmitPouExt(StringBuilder sb, Pt2Block b)
    {
        sb.Append("        <ts:POU_EXT>\n");
        if (b.St == null)
        {
            EmitPouMacro(sb, "APPROVED_NAME", b.ApprovedName);
            EmitPouMacro(sb, "APPROVED_TIME", b.ApprovedTime);
            EmitPouMacro(sb, "DRAWING_NUMBER", b.DrawingNumber);
            EmitPouMacro(sb, "DRAWING_TITLE", b.DrawingTitle.Length > 0 ? b.DrawingTitle : b.Title);
            for (int i = 0; i < 10; i++)
                EmitPouMacro(sb, "E" + i, b.E09[i]);
            EmitPouMacro(sb, "REVISION", b.Revision);
        }
        sb.Append("          <ts:ApplicationType>").Append(AppTypeName(b.ExtOptFlags)).Append("</ts:ApplicationType>\n");
        sb.Append("          <ts:DocumentCategory />\n");
        sb.Append("          <ts:DocumentDescription />\n");
        sb.Append("          <ts:DocumentAccess>Read/Write</ts:DocumentAccess>\n");
        if (b.ColorMonitoring)
            sb.Append("          <ts:ColorMonitoring>TRUE</ts:ColorMonitoring>\n");
        else
        {
            sb.Append("          <ts:CEMCauseRowInputs>").Append(b.CemEventInputs).Append("</ts:CEMCauseRowInputs>\n");
            sb.Append("          <ts:CEMEffectColOutputs>").Append(b.CemActionOutputs).Append("</ts:CEMEffectColOutputs>\n");
            sb.Append("          <ts:CEMIntersections>FALSE</ts:CEMIntersections>\n");
            sb.Append("          <ts:CEMAppDefinedStates>FALSE</ts:CEMAppDefinedStates>\n");
            sb.Append("          <ts:DocumentUsage>NONE</ts:DocumentUsage>\n");
        }
        sb.Append("        </ts:POU_EXT>\n");
    }

    /// <summary>Project-level macro block emitted after &lt;/instances&gt;. Values come from the
    /// container's Workspace.Xml (CompanyInfo1..3, MacroFB*, MacroPgm*, UserDefinition P0..P9) and the
    /// Project.xml root Description (PROJECT_NAME).</summary>
    private void EmitProjectExt(StringBuilder sb)
    {
        string c1 = "", c2 = "", c3 = "", fbInput = "", fbIO = "", fbLocal = "", fbOutput = "";
        string pgmLocal = "", pgmTagname = "";
        var p = new string[10];
        for (int i = 0; i < 10; i++) p[i] = "";
        if (_streams.TryGetValue("Workspace.Xml", out var wsd))
        {
            try
            {
                var root = XDocument.Parse(Encoding.UTF8.GetString(wsd).Replace("\0", "")).Root;
                if (root != null)
                {
                    c1 = (string?)root.Attribute("CompanyInfo1") ?? "";
                    c2 = (string?)root.Attribute("CompanyInfo2") ?? "";
                    c3 = (string?)root.Attribute("CompanyInfo3") ?? "";
                    fbInput = (string?)root.Attribute("MacroFBInput") ?? "";
                    fbIO = (string?)root.Attribute("MacroFBIO") ?? "";
                    fbLocal = (string?)root.Attribute("MacroFBLocal") ?? "";
                    fbOutput = (string?)root.Attribute("MacroFBOutput") ?? "";
                    pgmLocal = (string?)root.Attribute("MacroPgmLocal") ?? "";
                    pgmTagname = (string?)root.Attribute("MacroPgmTagname") ?? "";
                    var ud = root.Descendants().FirstOrDefault(e =>
                        e.Name.LocalName == "Collection" && (string?)e.Attribute("Name") == "UserDefinition");
                    if (ud != null)
                    {
                        int k = 0;
                        foreach (var cls in ud.Elements().Where(e => e.Name.LocalName == "Class"))
                        {
                            if (k < 10) p[k] = (string?)cls.Attribute("Value") ?? "";
                            k++;
                        }
                    }
                }
            }
            catch { /* ignore */ }
        }

        sb.Append("  <ts:PROJECT_EXT>\n");
        EmitProjectMacro(sb, "COMPANY_INFO1", c1);
        EmitProjectMacro(sb, "COMPANY_INFO2", c2);
        EmitProjectMacro(sb, "COMPANY_INFO3", c3);
        EmitProjectMacro(sb, "FB_INPUT", fbInput);
        EmitProjectMacro(sb, "FB_IO", fbIO);
        EmitProjectMacro(sb, "FB_LOCAL", fbLocal);
        EmitProjectMacro(sb, "FB_OUTPUT", fbOutput);
        for (int i = 0; i < 10; i++) EmitProjectMacro(sb, "P" + i, p[i]);
        EmitProjectMacro(sb, "PGM_LOCAL", pgmLocal);
        EmitProjectMacro(sb, "PGM_TAGNAME", pgmTagname);
        EmitProjectMacro(sb, "PROJECT_NAME", ProjectDescription());
        sb.Append("  </ts:PROJECT_EXT>\n");
    }

    private static void EmitProjectMacro(StringBuilder sb, string name, string value)
    {
        sb.Append("    <ts:Macros Name=\"").Append(name).Append("\">\n");
        if (value.Length > 0)
            sb.Append("      <ts:Value><![CDATA[").Append(CData(value)).Append("]]></ts:Value>\n");
        else
            sb.Append("      <ts:Value />\n");
        sb.Append("    </ts:Macros>\n");
    }

    private static void EmitPouMacro(StringBuilder sb, string name, string value)
    {
        sb.Append("          <ts:Macros Name=\"").Append(name).Append("\">\n");
        if (value != null && value.Length > 0)
            sb.Append("            <ts:Value><![CDATA[").Append(CData(value)).Append("]]></ts:Value>\n");
        else
            sb.Append("            <ts:Value />\n");
        sb.Append("          </ts:Macros>\n");
    }

    private static string TypeInner(string type)
    {
        string t = (type ?? "").Trim();
        if (t.Length == 0) return "<BOOL />";
        string prim = TriPrim(t);
        return prim.Length > 0 ? "<" + prim + " />" : "<derived name=\"" + Esc(t) + "\" />";
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
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")
        .Replace("\r", "&#xD;").Replace("\n", "&#xA;");

    /// <summary>Element-content escaping: keeps raw newlines (ref &lt;Comment&gt; text uses them).</summary>
    private static string EscText(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string CData(string s) => s.Replace("]]>", "]]]]><![CDATA[>");

    /// <summary>Appends one pretty-printed line: indent spaces + text + '\n'.</summary>
    private static void Ln(StringBuilder sb, int indent, string text) =>
        sb.Append(' ', indent).Append(text).Append('\n');

    /// <summary>Converts structural newlines to CRLF (TriStation's line ending) while leaving
    /// verbatim CDATA bodies and the contentHeader &lt;Comment&gt; text untouched (those keep
    /// the raw LF that TriStation writes inside element content).</summary>
    private static string ToCrlf(string xml)
    {
        var sb = new StringBuilder(xml.Length + xml.Length / 16);
        int i = 0, n = xml.Length;
        while (i < n)
        {
            int cdata = xml.IndexOf("<![CDATA[", i, StringComparison.Ordinal);
            int comment = xml.IndexOf("<Comment>", i, StringComparison.Ordinal);
            int next;
            if (cdata < 0) next = comment;
            else if (comment < 0) next = cdata;
            else next = Math.Min(cdata, comment);
            if (next < 0) { AppendCrlf(sb, xml.Substring(i)); break; }

            AppendCrlf(sb, xml.Substring(i, next - i));
            int end;
            if (next == cdata)
            {
                end = xml.IndexOf("]]>", next, StringComparison.Ordinal);
                end = end < 0 ? n : end + 3;
            }
            else
            {
                end = xml.IndexOf("</Comment>", next, StringComparison.Ordinal);
                end = end < 0 ? n : end + 10;
            }
            sb.Append(xml, next, end - next);
            i = end;
        }
        return sb.ToString();
    }

    private static void AppendCrlf(StringBuilder sb, string s)
    {
        int start = 0;
        for (int k = 0; k < s.Length; k++)
        {
            if (s[k] == '\n') { sb.Append(s, start, k - start).Append("\r\n"); start = k + 1; }
        }
        sb.Append(s, start, s.Length - start);
    }

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
        public string ApprovedName = "";
        public string ApprovedTime = "";
        public int ExtOptFlags = 3;
        public int CemEventInputs = 0;
        public int CemActionOutputs = 0;
        public bool ColorMonitoring = false;
        public string[] E09 = new string[10];
        public int HZoneCount;
        public int VZoneCount;
        public List<Pt2Var> Vars = new();
        public List<Pt2Var> DeclaredGlobals = new();
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
    private sealed class Pt2Pin { public string Name = ""; public int Wire = -1; public bool Negated; }

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
