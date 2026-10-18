using System.Globalization;
using System.Text;
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

    // ---------------------------------------------------------------- container

    private IEnumerable<string> PouFolders()
    {
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
            folders.Add(folder);
        }
        return folders;
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
        int idx(string col) => Array.FindIndex(header, h => h.Equals(col, StringComparison.OrdinalIgnoreCase));
        int iName = idx("Tagname"), iType = idx("DataType"), iDesc = idx("Description"), iAddr = idx("Address");
        if (iName < 0) return tags;

        for (int i = 2; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = CsvFields(lines[i]);
            if (f.Length <= iName) continue;
            string name = f[iName];
            if (string.IsNullOrWhiteSpace(name)) continue;
            tags.Add(new Pt2Tag
            {
                Name = name,
                Type = iType >= 0 && f.Length > iType ? f[iType] : "",
                Desc = iDesc >= 0 && f.Length > iDesc ? f[iDesc] : "",
                Address = iAddr >= 0 && f.Length > iAddr ? f[iAddr] : "",
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
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
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
        };

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
                var disp = Child(sheetClass, "Collection", "displayables");
                if (disp != null) ReadSheet(sheet, disp);
                if (sheet.Elems.Count > 0) block.Sheets.Add(sheet);
            }
        }

        return block;
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
                        X = Pos(Int(c, "posX")),
                        Y = Pos(Int(c, "posY")),
                        W = Math.Max(1, Size(Int(c, "width"))),
                        H = Math.Max(1, Size(Int(c, "height"))),
                        TypeName = (string?)c.Attribute("typeName") ?? "",
                        Instance = (string?)c.Attribute("instanceName") ?? "",
                    };
                    var inputs = Child(c, "Collection", "inputs");
                    if (inputs != null)
                        foreach (var pin in inputs.Elements().Where(e => e.Name.LocalName == "Class"))
                            b.Inputs.Add(new Pt2Pin
                            {
                                Name = (string?)pin.Attribute("formalParam") ?? "",
                                Wire = Int(pin, "connectId"),
                            });
                    var outputs = Child(c, "Collection", "outputs");
                    if (outputs != null)
                        foreach (var pin in outputs.Elements().Where(e => e.Name.LocalName == "Class"))
                            b.Outputs.Add((string?)pin.Attribute("formalParam") ?? "");
                    sheet.Elems.Add(b);
                    break;
                }
                case "CFBDInVarXml":
                case "CFBDOutVarXml":
                case "CFBDInOutVarXml":
                {
                    sheet.Elems.Add(new Pt2VarElem
                    {
                        OldId = Int(c, "localId"),
                        X = Pos(Int(c, "posX")),
                        Y = Pos(Int(c, "posY")),
                        W = Math.Max(1, Size(Int(c, "width"))),
                        H = Math.Max(1, Size(Int(c, "height"))),
                        Name = (string?)c.Attribute("varName") ?? "",
                        InWire = AttrInt(c, "inConnectId"),
                        OutWire = AttrInt(c, "outConnectId"),
                    });
                    break;
                }
                case "CDGMWireXml":
                {
                    var w = new Pt2Wire { OldId = Int(c, "localId") };
                    foreach (var ep in c.Elements().Where(e => e.Name.LocalName == "Class"))
                    {
                        string embed = (string?)ep.Attribute("Embed") ?? "";
                        int id = Int(ep, "connectId");
                        string pin = (string?)ep.Attribute("effectiveParam") ?? "";
                        if (embed == "input") { w.SrcId = id; w.SrcPin = pin; }
                        else if (embed == "output") { w.SinkId = id; w.SinkPin = pin; }
                    }
                    sheet.Wires.Add(w);
                    break;
                }
                case "CDGMCommentXml":
                {
                    sheet.Elems.Add(new Pt2CommentElem
                    {
                        OldId = Int(c, "localId"),
                        X = Pos(Int(c, "posX")),
                        Y = Pos(Int(c, "posY")),
                        W = Math.Max(1, Size(Int(c, "width"))),
                        H = Math.Max(1, Size(Int(c, "height"))),
                        Text = c.Descendants().FirstOrDefault(e => e.Name.LocalName == "CData")?.Value.Trim() ?? "",
                    });
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
        sb.Append("      <pou name=\"").Append(Esc(b.Name)).Append("\" pouType=\"").Append(b.PouType).Append("\">\n");

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
        EmitSection(sb, "inputVars", b.Vars.Where(v => v.Section == "input"));
        EmitSection(sb, "outputVars", b.Vars.Where(v => v.Section == "output"));
        EmitSection(sb, "inOutVars", b.Vars.Where(v => v.Section == "inOut"));
        EmitSection(sb, "localVars", b.Vars.Where(v => v.Section == "local"));

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
        if (list.Count == 0) { sb.Append("          <").Append(tag).Append(" />\n"); return; }
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
        sb.Append("            <ts:SHEETS>\n");
        foreach (var sheet in b.Sheets)
        {
            string title = string.IsNullOrWhiteSpace(sheet.Title)
                ? (sheet.Number == 1 ? b.Title : "")
                : sheet.Title;
            sb.Append("              <ts:SHEET Number=\"").Append(sheet.Number).Append("\">")
              .Append("<ts:Macros><ts:Macro Name=\"SHEET_TITLE\"><ts:Value>")
              .Append(Esc(title)).Append("</ts:Value></ts:Macro></ts:Macros>")
              .Append("</ts:SHEET>\n");
        }
        sb.Append("            </ts:SHEETS>\n");

        int nextId = 1;
        foreach (var sheet in b.Sheets) EmitSheet(sb, sheet, ref nextId);

        sb.Append("          </FBD>\n        </body>\n");
    }

    private void EmitSheet(StringBuilder sb, Pt2Sheet sheet, ref int nextId)
    {
        var newId = new Dictionary<int, int>();
        var pos = new Dictionary<int, (int X, int Y)>();
        foreach (var e in sheet.Elems)
        {
            if (e is Pt2CommentElem) continue;
            newId[e.OldId] = nextId++;
            pos[e.OldId] = (e.X, e.Y);
        }

        var wireById = new Dictionary<int, Pt2Wire>();
        foreach (var w in sheet.Wires) wireById[w.OldId] = w;

        var writeWire = new Dictionary<int, Pt2Wire>();
        foreach (var w in sheet.Wires)
            if (w.SinkId >= 0) writeWire[w.SinkId] = w;
        var isSource = new HashSet<int>(sheet.Wires.Where(w => w.SrcId >= 0).Select(w => w.SrcId));

        foreach (var e in sheet.Elems)
        {
            switch (e)
            {
                case Pt2CommentElem c:
                    if (c.Text.Length > 0) EmitComment(sb, c, sheet.Number, nextId++);
                    break;

                case Pt2BlockElem blk:
                {
                    sb.Append("            <block localId=\"").Append(newId[blk.OldId]).Append('"');
                    sb.Append(" typeName=\"").Append(Esc(blk.TypeName)).Append('"');
                    if (blk.Instance.Length > 0) sb.Append(" instanceName=\"").Append(Esc(blk.Instance)).Append('"');
                    sb.Append(" width=\"").Append(blk.W).Append("\" height=\"").Append(blk.H).Append("\">");
                    AppendPosition(sb, blk.X, blk.Y, sheet.Number);

                    sb.Append("<inputVariables>");
                    foreach (var pin in blk.Inputs)
                    {
                        sb.Append("<variable formalParameter=\"").Append(Esc(pin.Name)).Append('"');
                        Pt2Wire? w = null;
                        if (pin.Wire >= 0 && wireById.TryGetValue(pin.Wire, out var byId)) w = byId;
                        if (w == null && TryPinWire(sheet, blk.OldId, pin.Name, out var byPin)) w = byPin;
                        if (w != null && w.SrcId >= 0 && newId.ContainsKey(w.SrcId))
                        {
                            sb.Append("><connectionPointIn><relPosition x=\"").Append(blk.X).Append("\" y=\"").Append(blk.Y).Append("\" />");
                            AppendConnection(sb, newId[w.SrcId], w.SrcPin, pos[w.SrcId], (blk.X, blk.Y));
                            sb.Append("</connectionPointIn></variable>");
                        }
                        else sb.Append(" />");
                    }
                    sb.Append("</inputVariables>");
                    sb.Append("<inOutVariables />");
                    sb.Append("<outputVariables>");
                    foreach (var op in blk.Outputs)
                        sb.Append("<variable formalParameter=\"").Append(Esc(op))
                          .Append("\"><connectionPointOut><relPosition x=\"").Append(blk.X).Append("\" y=\"").Append(blk.Y)
                          .Append("\" /></connectionPointOut></variable>");
                    sb.Append("</outputVariables>");
                    sb.Append("</block>\n");
                    break;
                }

                case Pt2VarElem v:
                {
                    bool written = v.InWire >= 0 || writeWire.ContainsKey(v.OldId);
                    bool read = v.OutWire >= 0 || isSource.Contains(v.OldId);
                    string tag = written && read ? "inOutVariable" : written ? "outVariable" : "inVariable";
                    sb.Append("            <").Append(tag).Append(" localId=\"").Append(newId[v.OldId]).Append('"');
                    sb.Append(" width=\"").Append(v.W).Append("\" height=\"").Append(v.H).Append("\">");
                    AppendPosition(sb, v.X, v.Y, sheet.Number);
                    sb.Append("<expression>").Append(Esc(v.Name)).Append("</expression>");
                    if (written && writeWire.TryGetValue(v.OldId, out var sw) && sw.SrcId >= 0 && newId.ContainsKey(sw.SrcId))
                    {
                        sb.Append("<connectionPointIn><relPosition x=\"").Append(v.X).Append("\" y=\"").Append(v.Y).Append("\" />");
                        AppendConnection(sb, newId[sw.SrcId], sw.SrcPin, pos[sw.SrcId], (v.X, v.Y));
                        sb.Append("</connectionPointIn>");
                    }
                    sb.Append("</").Append(tag).Append(">\n");
                    break;
                }
            }
        }
    }

    private static bool TryPinWire(Pt2Sheet sheet, int blockId, string pin, out Pt2Wire wire)
    {
        foreach (var w in sheet.Wires)
            if (w.SinkId == blockId && string.Equals(w.SinkPin, pin, StringComparison.Ordinal))
            {
                wire = w;
                return true;
            }
        wire = null!;
        return false;
    }

    private static void EmitComment(StringBuilder sb, Pt2CommentElem c, int sheet, int newId)
    {
        sb.Append("            <comment localId=\"").Append(newId).Append("\" width=\"").Append(c.W)
          .Append("\" height=\"").Append(c.H).Append("\">");
        AppendPosition(sb, c.X, c.Y, sheet);
        sb.Append("<content><![CDATA[").Append(CData(c.Text)).Append("]]></content>");
        sb.Append("</comment>\n");
    }

    private static void AppendPosition(StringBuilder sb, int x, int y, int sheet) =>
        sb.Append("<position x=\"").Append(x).Append("\" y=\"").Append(y)
          .Append("\" ts:SheetNumber=\"").Append(sheet).Append("\" />");

    private static void AppendConnection(StringBuilder sb, int refId, string pin, (int X, int Y) src, (int X, int Y) target)
    {
        sb.Append("<connection refLocalId=\"").Append(refId).Append('"');
        if (pin.Length > 0) sb.Append(" formalParameter=\"").Append(Esc(pin)).Append('"');
        sb.Append(" ts:FeedBack=\"false\">");
        sb.Append("<position x=\"").Append(src.X).Append("\" y=\"").Append(src.Y).Append("\" />");
        sb.Append("<position x=\"").Append(target.X).Append("\" y=\"").Append(target.Y).Append("\" />");
        sb.Append("</connection>");
    }

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
            if (!string.IsNullOrWhiteSpace(tag?.Desc))
                sb.Append("              <documentation><![CDATA[").Append(CData(tag!.Desc)).Append("]]></documentation>\n");
            if (!string.IsNullOrWhiteSpace(tag?.Address))
                sb.Append("              <address>").Append(Esc(tag!.Address)).Append("</address>\n");
            if (tag != null)
            {
                sb.Append("              <ts:TAGNAME>")
                  .Append("<ts:TAG_DECLARATION SharedRead=\"False\" />")
                  .Append("<ts:TAG_POINT Address=\"").Append(Esc(tag.Address)).Append("\" />")
                  .Append("</ts:TAGNAME>\n");
            }
            sb.Append("            </variable>\n");
        }
        sb.Append("          </globalVars>\n");
        sb.Append("        </resource>\n");
        sb.Append("      </configuration>\n");
        sb.Append("    </configurations>\n  </instances>\n");
    }

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

    private sealed class Pt2Tag { public string Name = ""; public string Type = ""; public string Desc = ""; public string Address = ""; }

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
    }

    private abstract class Pt2Elem
    {
        public int OldId;
        public int X, Y, W = 16, H = 6;
    }

    private sealed class Pt2BlockElem : Pt2Elem
    {
        public string TypeName = "";
        public string Instance = "";
        public List<Pt2Pin> Inputs = new();
        public List<string> Outputs = new();
    }

    private sealed class Pt2Pin { public string Name = ""; public int Wire = -1; }

    private sealed class Pt2VarElem : Pt2Elem
    {
        public string Name = "";
        public int InWire = -1;
        public int OutWire = -1;
    }

    private sealed class Pt2CommentElem : Pt2Elem { public string Text = ""; }

    private sealed class Pt2Wire
    {
        public int OldId;
        public int SrcId = -1;
        public string SrcPin = "";
        public int SinkId = -1;
        public string SinkPin = "";
    }
}
