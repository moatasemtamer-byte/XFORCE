using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Pt2Conv;

/// <summary>
/// Translates Siemens SimaticML block exports (TIA Openness <c>SW.Blocks.FB|FC|OB</c>, FBD flavour)
/// plus a tag table into a TriStation PLCopen XML that <see cref="BuildXml"/> can turn into a
/// <c>.pt2x</c>. This is the inverse of <see cref="FbdSimaticMlGenerator"/> in the engine: the
/// forward direction reads TriStation FBD and writes SimaticML; here we read SimaticML and write
/// the TriStation PLCopen dialect (<c>http://www.triconex.com/controller/03/2008</c>).
/// </summary>
public static class SimaticToPlcOpen
{
    // ---------------------------------------------------------------- CLI

    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: fromsimatic <simaticDirOrFile> <out.xml> [--tags <tagsXml>] [--name <projectName>]");
            Console.Error.WriteLine("  <simaticDirOrFile>: a *.xml SimaticML block export or a directory holding Blocks\\*.xml");
            Console.Error.WriteLine("  --tags:  a SW.Tags.PlcTagTable export (otherwise auto-detected among the inputs)");
            Console.Error.WriteLine("  --name:  contentHeader name (default TEMPLATES)");
            return 1;
        }
        string input = args[1], outPath = args[2];
        string? tagsArg = null;
        string projName = "TEMPLATES";
        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tags": if (i + 1 < args.Length) tagsArg = args[++i]; break;
                case "--name": if (i + 1 < args.Length) projName = args[++i]; break;
            }
        }

        var blocks = new List<SimBlock>();
        var tags = new List<SimTag>();
        foreach (var file in EnumerateXml(input))
            LoadFile(file, blocks, tags);
        if (tagsArg != null)
        {
            tags.Clear();
            LoadFile(tagsArg, blocks, tags);
        }

        var xml = Translate(blocks, tags, projName);
        File.WriteAllText(outPath, xml, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {outPath} ({xml.Length:N0} chars): {blocks.Count} POU(s), {tags.Count} tag(s)");
        return 0;
    }

    private static IEnumerable<string> EnumerateXml(string input)
    {
        if (File.Exists(input)) { yield return input; yield break; }
        if (!Directory.Exists(input))
            throw new FileNotFoundException($"input not found: {input}");
        foreach (var f in Directory.EnumerateFiles(input, "*.xml", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            yield return f;
    }

    // ---------------------------------------------------------------- model

    private sealed class SimVar
    {
        public string Name = "";
        public string DataType = "";
        public string Section = "";
        public string Comment = "";
    }

    private sealed class SimTag
    {
        public string Name = "";
        public string DataType = "";
        public string Address = "";
        public string Comment = "";
    }

    private sealed class SimBlock
    {
        public string Name = "";
        public string Kind = "FB";
        public string Language = "FBD";
        public List<SimVar> Vars = new();
        public List<XElement> Networks = new();
        public string? StText;
        public string Title = "";
        public string Comment = "";
    }

    private static string L(XElement e) => e.Name.LocalName;
    private static string A(XElement e, string n, string d = "") => (string?)e.Attribute(n) ?? d;
    private static XElement? Child(XElement e, string n) => e.Elements().FirstOrDefault(x => x.Name.LocalName == n);
    private static IEnumerable<XElement> Kids(XElement e, string n) => e.Elements().Where(x => x.Name.LocalName == n);

    // ---------------------------------------------------------------- load

    private static void LoadFile(string path, List<SimBlock> blocks, List<SimTag> tags)
    {
        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch { return; }
        var root = doc.Root;
        if (root == null) return;

        foreach (var el in root.Descendants())
        {
            string ln = el.Name.LocalName;
            if (ln is "SW.Blocks.FB" or "SW.Blocks.FC" or "SW.Blocks.OB")
            {
                var b = ParseBlock(el);
                if (b != null) blocks.Add(b);
            }
            else if (ln == "SW.Tags.PlcTagTable")
            {
                ParseTags(el, tags);
            }
        }
    }

    private static SimBlock? ParseBlock(XElement el)
    {
        var attr = Child(el, "AttributeList");
        if (attr == null) return null;
        var b = new SimBlock
        {
            Kind = el.Name.LocalName.Substring("SW.Blocks.".Length),
            Name = Child(attr, "Name")?.Value.Trim() ?? "",
            Language = (Child(attr, "ProgrammingLanguage")?.Value.Trim() ?? "FBD"),
        };
        if (b.Name.Length == 0) return null;

        var iface = Child(attr, "Interface");
        var sections = iface == null ? null : Child(iface, "Sections");
        if (sections != null)
        {
            foreach (var sec in Kids(sections, "Section"))
            {
                string secName = A(sec, "Name");
                foreach (var m in Kids(sec, "Member"))
                {
                    var v = new SimVar
                    {
                        Name = A(m, "Name"),
                        DataType = A(m, "Datatype"),
                        Section = secName,
                        Comment = TextOf(FirstLangComment(m)),
                    };
                    if (v.Name.Length > 0) b.Vars.Add(v);
                }
            }
        }

        foreach (var cu in el.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit"))
        {
            var src = cu.Descendants().FirstOrDefault(e => e.Name.LocalName == "NetworkSource");
            if (src == null) continue;
            var st = src.Descendants().FirstOrDefault(e => e.Name.LocalName == "StructuredText");
            if (st != null) { b.StText = (b.StText ?? "") + RenderStructuredText(st); continue; }
            if (src.Descendants().Any(e => e.Name.LocalName == "FlgNet")) b.Networks.Add(cu);
            else
            {
                // Non-FlgNet, non-StructuredText body: keep any text so ST fallback still works.
                var t = src.Value;
                if (!string.IsNullOrWhiteSpace(t)) b.StText = (b.StText ?? "") + t + "\n";
            }
        }

        // Block title (from the trailing MultilingualText CompositionName="Title").
        var title = el.Descendants().FirstOrDefault(e => e.Name.LocalName == "MultilingualText"
            && A(e, "CompositionName") == "Title");
        b.Title = title == null ? "" : TextOf(FirstLangComment(title));
        return b;
    }

    private static XElement? FirstLangComment(XElement e)
    {
        return e.Descendants().FirstOrDefault(d => d.Name.LocalName == "MultiLanguageText")
            ?? e.Descendants().FirstOrDefault(d => d.Name.LocalName == "Text");
    }

    private static string TextOf(XElement? e)
    {
        if (e == null) return "";
        if (e.Name.LocalName == "Text") return e.Value.Trim();
        return e.Value.Trim();
    }

    /// <summary>
    /// Rebuilds the SCL source text from TIA's tokenized <c>&lt;StructuredText&gt;</c> body
    /// (Blank/Token/Access/Instruction/Parameter). TIA never exports the SCL as plain text, so the
    /// token stream is the only representation. Falls back to the raw value for the (non-TIA)
    /// plain-text form.
    /// </summary>
    private static string RenderStructuredText(XElement st)
    {
        bool tokenized = st.Descendants().Any(e => e.Name.LocalName is "Token" or "Blank" or "Access");
        if (!tokenized) return (st.Value ?? "").Trim() + "\n";

        var sb = new StringBuilder();
        RenderStNodes(st, sb);
        string text = sb.ToString().TrimEnd();
        return text.Length == 0 ? "" : text + "\n";
    }

    private static void RenderStNodes(XElement parent, StringBuilder sb)
    {
        foreach (var e in parent.Elements()) RenderStNode(e, sb);
    }

    private static void RenderStNode(XElement e, StringBuilder sb)
    {
        switch (e.Name.LocalName)
        {
            case "Blank":
            {
                if (int.TryParse(A(e, "Num"), out int n))
                    sb.Append(' ', Math.Min(n, 40));
                break;
            }
            case "Token":
            case "Text":
            {
                sb.Append(A(e, "Text"));
                if (e.Name.LocalName == "Token" && A(e, "Text") == ";") sb.Append('\n');
                break;
            }
            case "Access":
                sb.Append(RenderAccess(e));
                break;
            case "NamelessParameter":
                RenderStNodes(e, sb);
                break;
            case "Parameter":
            {
                // The parameter name is followed by a Blank and a " :=" Token in the stream.
                sb.Append(A(e, "Name"));
                RenderStNodes(e, sb);
                break;
            }
            case "Instruction":
            {
                sb.Append(A(e, "Name"));
                RenderStNodes(e, sb);
                break;
            }
            default:
                RenderStNodes(e, sb);
                break;
        }
    }

    private static string RenderAccess(XElement acc)
    {
        string scope = A(acc, "Scope");
        if (scope.Equals("LiteralConstant", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("TypedConstant", StringComparison.OrdinalIgnoreCase))
        {
            var c = Child(acc, "Constant");
            return c == null ? "" : (Child(c, "ConstantValue")?.Value ?? "").Trim();
        }
        if (scope.Equals("Call", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            RenderStNodes(acc, sb);
            return sb.ToString();
        }
        var symbol = Child(acc, "Symbol");
        if (symbol != null)
        {
            var parts = Kids(symbol, "Component").Select(c => Unq(A(c, "Name"))).Where(s => s.Length > 0);
            return string.Join(".", parts);
        }
        return "";
    }

    private static void ParseTags(XElement table, List<SimTag> tags)
    {
        foreach (var t in table.Descendants().Where(e => e.Name.LocalName == "SW.Tags.PlcTag"))
        {
            var a = Child(t, "AttributeList");
            if (a == null) continue;
            string name = Child(a, "Name")?.Value.Trim() ?? "";
            if (name.Length == 0) continue;
            tags.Add(new SimTag
            {
                Name = name,
                DataType = Child(a, "DataTypeName")?.Value.Trim() ?? "",
                Address = Child(a, "LogicalAddress")?.Value.Trim() ?? "",
                Comment = FirstLangComment(t)?.Value.Trim() ?? "",
            });
        }
    }

    // ---------------------------------------------------------------- emit envelope

    private static string Translate(List<SimBlock> blocks, List<SimTag> tags, string projName)
    {
        var sb = new StringBuilder(1 << 20);
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<project xmlns:xhtml=\"http://www.w3.org/1999/xhtml\" ")
          .Append("xmlns:ts=\"http://www.triconex.com/controller/03/2008\" ")
          .Append("xmlns=\"http://www.plcopen.org/xml/tc6.xsd\">\n");
        sb.Append("  <fileHeader companyName=\"\" companyURL=\"\" productName=\"TriStation 1131\" ")
          .Append("productVersion=\"5.8.0.153\" productRelease=\"RC\" ")
          .Append("creationDateTime=\"").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)).Append("\" ")
          .Append("contentDescription=\"\" prjSchemaVersion=\"5.5.0.216\" />\n");
        sb.Append("  <contentHeader name=\"").Append(Esc(projName)).Append("\" version=\"1.0\" ")
          .Append("downloadedversion=\"0.0\" downloadedtime=\"\" projectfilestatus=\"New or changed; not built\" ")
          .Append("modificationDateTime=\"").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)).Append("\" ")
          .Append("author=\"TristationUser\" language=\"EN-US\">\n");
        sb.Append("    <coordinateInfo><pageSize x=\"0\" y=\"0\" /><fbd><scaling x=\"0\" y=\"0\" /></fbd>")
          .Append("<ld><scaling x=\"0\" y=\"0\" /></ld><sfc><scaling x=\"0\" y=\"0\" /></sfc></coordinateInfo>\n");
        sb.Append("  </contentHeader>\n");
        sb.Append("  <types>\n    <dataTypes />\n    <pous>\n");
        foreach (var b in blocks)
        {
            try { EmitPou(sb, b, tags); }
            catch (Exception ex) { Console.Error.WriteLine($"  block {b.Name}: {ex.Message}"); }
        }
        sb.Append("    </pous>\n  </types>\n");
        sb.Append("  <instances>\n    <configurations />\n  </instances>\n");
        EmitTags(sb, tags);
        sb.Append("</project>\n");
        return sb.ToString();
    }

    private static void EmitTags(StringBuilder sb, List<SimTag> tags)
    {
        if (tags.Count == 0) return;
        sb.Append("  <ts:GLOBALTAGS>\n");
        foreach (var t in tags)
        {
            sb.Append("    <variable name=\"").Append(Esc(t.Name)).Append("\">\n");
            sb.Append("      ").Append(TypeBlock(t.DataType)).Append('\n');
            sb.Append("      <initialValue><simpleValue value=\"\" /></initialValue>\n");
            sb.Append("      <documentation><![CDATA[").Append(CData(t.Comment)).Append("]]></documentation>\n");
            sb.Append("      <ts:TAGNAME>\n");
            sb.Append("        <ts:TAG_DECLARATION Application=\"Safety\" SharedForRead=\"No\" EnableMultipleWrite=\"No\" />\n");
            sb.Append("        <ts:POINT_ASSIGNMENT TagClass=\"USER_DEFINED\" PointType=\"MEMORY\" ")
              .Append("AliasCategory=\"DEFAULT_ALIAS\" AliasType=\"READWRITE_ALIASED\" PhysicalAddress=\"\" />\n");
            sb.Append("        <ts:TAG_LOCATION AreaName=\"\" Equipment=\"\" SIFName=\"\" />\n");
            sb.Append("      </ts:TAGNAME>\n");
            sb.Append("    </variable>\n");
        }
        sb.Append("  </ts:GLOBALTAGS>\n");
    }

    // ---------------------------------------------------------------- POU emission

    private static void EmitPou(StringBuilder sb, SimBlock b, List<SimTag> tags)
    {
        string pouType = b.Kind switch { "OB" => "program", "FC" => "function", _ => "functionBlock" };
        sb.Append("      <pou name=\"").Append(Esc(b.Name)).Append("\" pouType=\"").Append(pouType).Append("\">\n");

        // Which names are read/written anywhere (for globalVars collection) and body.
        var body = b.Networks.Count > 0 ? new FbdBody(b, tags) : null;
        var referencedGlobals = body?.ReferencedGlobals() ?? new List<string>();

        EmitInterface(sb, b, tags, referencedGlobals);

        if (body != null)
        {
            sb.Append("        <body>\n          <FBD>\n");
            body.Emit(sb);
            sb.Append("          </FBD>\n        </body>\n");
        }
        else
        {
            string st = b.StText ?? "(* empty *)\n";
            sb.Append("        <body>\n          <ST><![CDATA[").Append(CData(st)).Append("]]></ST>\n        </body>\n");
        }
        sb.Append("      </pou>\n");
    }

    private static void EmitInterface(StringBuilder sb, SimBlock b, List<SimTag> tags, List<string> referencedGlobals)
    {
        sb.Append("        <interface>\n");
        EmitSection(sb, "inputVars", b.Vars.Where(v => v.Section == "Input"));
        EmitSection(sb, "inOutVars", b.Vars.Where(v => v.Section == "InOut"));
        EmitSection(sb, "outputVars", b.Vars.Where(v => v.Section == "Output"));
        EmitSection(sb, "localVars", b.Vars.Where(v => v.Section is "Static" or "Temp" or "Constant"));

        if (referencedGlobals.Count > 0)
        {
            sb.Append("          <globalVars nonretain=\"true\" nonpersistent=\"true\">\n");
            foreach (var name in referencedGlobals)
            {
                string dt = tags.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.DataType ?? "";
                sb.Append("            <variable name=\"").Append(Esc(name)).Append("\">\n");
                sb.Append("              ").Append(TypeBlock(dt.Length > 0 ? dt : "Bool")).Append('\n');
                sb.Append("            </variable>\n");
            }
            sb.Append("          </globalVars>\n");
        }
        sb.Append("        </interface>\n");
    }

    private static void EmitSection(StringBuilder sb, string tag, IEnumerable<SimVar> vars)
    {
        var list = vars.ToList();
        if (list.Count == 0) { sb.Append("          <").Append(tag).Append(" />\n"); return; }
        sb.Append("          <").Append(tag).Append(">\n");
        foreach (var v in list)
        {
            sb.Append("            <variable name=\"").Append(Esc(v.Name)).Append("\">\n");
            sb.Append("              ").Append(TypeBlock(v.DataType)).Append('\n');
            if (v.Comment.Length > 0)
                sb.Append("              <documentation><![CDATA[").Append(CData(v.Comment)).Append("]]></documentation>\n");
            sb.Append("            </variable>\n");
        }
        sb.Append("          </").Append(tag).Append(">\n");
    }

    // ---------------------------------------------------------------- FBD body

    private sealed class FbdBody
    {
        private readonly SimBlock _block;
        private readonly List<SimTag> _tags;
        private int _nextId = 1;
        private readonly List<string> _rendered = new();
        private int _sheetCount;

        public FbdBody(SimBlock block, List<SimTag> tags) { _block = block; _tags = tags; }

        private string? TypeOf(string name)
        {
            var v = _block.Vars.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (v != null) return v.DataType;
            var t = _tags.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return t?.DataType;
        }

        public List<string> ReferencedGlobals()
        {
            var names = new List<string>();
            foreach (var cu in _block.Networks)
            {
                var flg = cu.Descendants().FirstOrDefault(e => e.Name.LocalName == "FlgNet");
                if (flg == null) continue;
                foreach (var acc in flg.Descendants().Where(e => e.Name.LocalName == "Access"))
                {
                    if (A(acc, "Scope") != "GlobalVariable") continue;
                    string expr = AccessExpr(acc);
                    if (expr.Length == 0) continue;
                    if (!names.Contains(expr, StringComparer.OrdinalIgnoreCase)) names.Add(expr);
                }
            }
            return names;
        }

        // ---- per-network element model ----
        private sealed class NConn { public int Id; public string Pin = ""; public int X, Y; }
        private sealed class NPinIn { public string Name = ""; public bool Neg; public NConn? Conn; }
        private sealed class NBlock
        {
            public int Id, Sheet, X, Y, W, H;
            public string TypeName = "", Instance = "";
            public List<NPinIn> Inputs = new();
            public List<string> Outputs = new();
        }
        private sealed class NVar
        {
            public int Id, Sheet, X, Y, W, H;
            public string Kind = "in";
            public string Expr = "";
            public string DataType = "";
            public bool IsConstant;
            public NConn? InConn;
        }

        public void Emit(StringBuilder sb)
        {
            // A <ts:SHEET> must exist for every sheet an element references, or FbdGenerator
            // silently drops that element when it emits per sheet.
            _sheetCount = Math.Max(1, _block.Networks.Count);
            sb.Append("            <ts:SHEETS>\n");
            for (int i = 1; i <= _sheetCount; i++)
            {
                sb.Append("              <ts:SHEET Number=\"").Append(i).Append("\">")
                  .Append("<ts:Macros><ts:Macro Name=\"SHEET_TITLE\"><Value>")
                  .Append(Esc(i == 1 ? _block.Title : "")).Append("</Value></ts:Macro></ts:Macros>")
                  .Append("</ts:SHEET>\n");
            }
            sb.Append("            </ts:SHEETS>\n");

            for (int n = 0; n < _block.Networks.Count; n++)
                EmitNetwork(sb, _block.Networks[n], n + 1);

            foreach (var s in _rendered) sb.Append(s);
        }

        private void EmitNetwork(StringBuilder sb, XElement cu, int sheet)
        {
            var flg = cu.Descendants().FirstOrDefault(e => e.Name.LocalName == "FlgNet");
            if (flg == null) return;

            // --- parse parts / accesses ---
            var accesses = new Dictionary<string, XElement>();
            var parts = new Dictionary<string, XElement>();
            var partOrder = new List<string>();
            foreach (var child in flg.Descendants())
            {
                string ln = child.Name.LocalName;
                if (ln == "Access") { accesses[A(child, "UId")] = child; }
                else if (ln is "Part" or "Call")
                {
                    string uid = A(child, "UId");
                    if (uid.Length > 0 && !parts.ContainsKey(uid)) { parts[uid] = child; partOrder.Add(uid); }
                }
            }

            // --- parse wires (first child = source, second = sink) ---
            var wires = new List<(Endpoint Src, Endpoint Sink, bool CoilOperand)>();
            foreach (var w in flg.Descendants().Where(e => e.Name.LocalName == "Wire"))
            {
                var conns = w.Elements().Where(e => e.Name.LocalName is "IdentCon" or "NameCon" or "OpenCon").ToList();
                if (conns.Count < 2) continue;
                var src = EndpointOf(conns[0]);
                var sink = EndpointOf(conns[1]);
                if (src == null || sink == null) continue;
                bool coilOperand = sink.IsPart &&
                                   parts.TryGetValue(sink.Id, out var pe) && A(pe, "Name") == "Coil" &&
                                   sink.Pin == "operand";
                wires.Add((src, sink, coilOperand));
            }

            // --- roles ---
            var readUids = new HashSet<string>();       // access used as a source (excluding coil operand)
            var writeSrc = new Dictionary<string, Endpoint>(); // access -> endpoint that writes it
            foreach (var (src, sink, coilOperand) in wires)
            {
                // A TIA `en` pin has no TriStation counterpart: its source must not become a variable.
                if (sink.IsPart && sink.Pin.Equals("en", StringComparison.OrdinalIgnoreCase)) continue;
                if (!src.IsPart && !coilOperand) readUids.Add(src.Id);
                if (!sink.IsPart) writeSrc[sink.Id] = src;
            }
            // Coil: operand access is the write target; its `in` source is the value.
            foreach (var uid in parts.Keys.ToList())
            {
                var p = parts[uid];
                if (A(p, "Name") != "Coil") continue;
                var operandWire = wires.FirstOrDefault(w => w.Sink.IsPart && w.Sink.Id == uid && w.Sink.Pin == "operand");
                var inWire = wires.FirstOrDefault(w => w.Sink.IsPart && w.Sink.Id == uid && w.Sink.Pin == "in");
                if (operandWire.Sink == null || inWire.Src == null) continue;
                if (operandWire.Src.IsPart) continue;
                writeSrc[operandWire.Src.Id] = inWire.Src;
            }

            // --- build elements ---
            var blockByUid = new Dictionary<string, NBlock>();
            var varByUid = new Dictionary<string, NVar>();
            var elems = new List<object>();

            var pos = new Layout();

            foreach (var uid in partOrder)
            {
                var p = parts[uid];
                if (A(p, "Name") == "Coil") continue;
                var nb = BuildBlock(p);
                if (nb == null) continue;
                nb.Sheet = sheet;
                nb.Id = _nextId++;
                nb.X = pos.NextX(); nb.Y = pos.NextY();
                nb.W = 16; nb.H = 6 + 4 * Math.Max(1, nb.Inputs.Count);
                blockByUid[uid] = nb;
                elems.Add(nb);
            }

            // Variables: every access that is read and/or written, except instance-DB symbols
            // (an <Instance><Component> name is a state variable, not a value element).
            var instNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parts.Values)
            {
                var inst = Child(p, "Instance");
                if (inst != null)
                {
                    var comp = Child(inst, "Component");
                    if (comp != null) instNames.Add(((string?)comp.Attribute("Name") ?? "").Trim('"'));
                }
            }

            foreach (var kv in accesses)
            {
                string uid = kv.Key;
                bool read = readUids.Contains(uid);
                bool written = writeSrc.ContainsKey(uid);
                if (!read && !written) continue;

                var acc = kv.Value;
                string expr = AccessExpr(acc);
                if (expr.Length == 0) continue;
                bool isLiteral = Child(acc, "Constant") != null;
                if (isLiteral && written) { /* a literal is never a target */ }

                var nv = new NVar
                {
                    Id = _nextId++,
                    Sheet = sheet,
                    Expr = expr,
                    IsConstant = isLiteral,
                    DataType = isLiteral ? ConstType(acc) : (TypeOf(expr) ?? ""),
                    Kind = read && written ? "inOut" : written ? "out" : "in",
                };
                nv.X = pos.NextX(); nv.Y = pos.NextY();
                nv.W = 8; nv.H = 6;
                varByUid[uid] = nv;
                elems.Add(nv);
            }

            // Resolve variable write connections only after every variable has an id.
            foreach (var kv in writeSrc)
            {
                if (varByUid.TryGetValue(kv.Key, out var nv))
                    nv.InConn = ResolveConn(kv.Value, blockByUid, varByUid);
            }

            // --- second pass: wire block input pins (needs all element ids) ---
            foreach (var uid in partOrder)
            {
                if (!blockByUid.TryGetValue(uid, out var nb)) continue;
                var p = parts[uid];
                foreach (var pin in InputPinsOf(p, nb.TypeName))
                {
                    var wire = wires.FirstOrDefault(w => w.Sink.IsPart && w.Sink.Id == uid && w.Sink.Pin == pin.Tia);
                    if (wire.Src == null) { nb.Inputs.Add(new NPinIn { Name = pin.Tri, Neg = pin.Neg }); continue; }
                    var conn = ResolveConn(wire.Src, blockByUid, varByUid);
                    nb.Inputs.Add(new NPinIn { Name = pin.Tri, Neg = pin.Neg, Conn = conn });
                }
            }

            // --- render ---
            foreach (var e in elems)
            {
                if (e is NVar nv) _rendered.Add(RenderVar(nv));
                else if (e is NBlock nb) _rendered.Add(RenderBlock(nb));
            }
        }

        private static Endpoint? EndpointOf(XElement conn)
        {
            string ln = conn.Name.LocalName;
            if (ln == "IdentCon") return new Endpoint { Id = A(conn, "UId"), IsPart = false };
            if (ln == "NameCon") return new Endpoint { Id = A(conn, "UId"), Pin = A(conn, "Name"), IsPart = true };
            return null; // OpenCon
        }

        private sealed class Endpoint
        {
            public string Id = "";
            public string Pin = "";
            public bool IsPart; // true for NameCon (a Part/Call pin); false for IdentCon (an Access)
        }

        private NConn? ResolveConn(Endpoint src, Dictionary<string, NBlock> blocks, Dictionary<string, NVar> vars)
        {
            if (src.IsPart)
            {
                if (blocks.TryGetValue(src.Id, out var sb))
                    return new NConn { Id = sb.Id, Pin = TriOutMap(sb, src.Pin), X = sb.X, Y = sb.Y };
                return null;
            }
            if (vars.TryGetValue(src.Id, out var sv))
                return new NConn { Id = sv.Id, Pin = "", X = sv.X, Y = sv.Y };
            return null;
        }

        /// <summary>The TriStation output pin for a block, given the TIA source pin name. Function
        /// boxes always carry the operator-symbol pin; instance/user blocks keep their own pin name.</summary>
        private static string TriOutMap(NBlock b, string tiaPin)
        {
            if (IsFunctionBox(b.TypeName) || b.Instance.Length == 0 && tiaPin.Equals("out", StringComparison.OrdinalIgnoreCase))
                return TriOutPinOf(b.TypeName);
            if (tiaPin.Length > 0) return tiaPin;
            return b.Outputs.Count > 0 ? b.Outputs[0] : (IsFunctionBox(b.TypeName) ? TriOutPinOf(b.TypeName) : "Ret_Val");
        }

        private NBlock? BuildBlock(XElement p)
        {
            bool isCall = p.Name.LocalName == "Call";
            string rawName;
            string instance = "";
            if (isCall)
            {
                var ci = Child(p, "CallInfo");
                rawName = ci == null ? "" : A(ci, "Name");
                var inst = ci == null ? null : Child(ci, "Instance");
                instance = InstanceName(inst);
            }
            else
            {
                rawName = A(p, "Name");
                instance = InstanceName(Child(p, "Instance"));
            }
            if (rawName.Length == 0) return null;

            var nb = new NBlock { Instance = instance };
            if (isCall)
            {
                nb.TypeName = rawName;
            }
            else if (rawName == "CONVERT")
            {
                nb.TypeName = ConvertTypeName(p);
            }
            else if (TryBoxInverse(rawName, out var tri))
            {
                nb.TypeName = tri;
            }
            else
            {
                nb.TypeName = rawName; // instance part; keep the name
            }

            // Output pins.
            if (instance.Length > 0 || !IsFunctionBox(nb.TypeName))
                nb.Outputs.AddRange(OutputPinsOf(nb.TypeName));
            else
                nb.Outputs.Add(TriOutPinOf(nb.TypeName));

            if (nb.Outputs.Count == 0) nb.Outputs.Add(IsFunctionBox(nb.TypeName) ? TriOutPinOf(nb.TypeName) : "Ret_Val");
            return nb;
        }

        private string ConvertTypeName(XElement p)
        {
            string from = "", to = "";
            foreach (var tv in Kids(p, "TemplateValue"))
            {
                string n = A(tv, "Name");
                if (n.Equals("SrcType", StringComparison.OrdinalIgnoreCase)) from = tv.Value.Trim();
                else if (n.Equals("DestType", StringComparison.OrdinalIgnoreCase)) to = tv.Value.Trim();
            }
            if (from.Length == 0) from = "DWord";
            if (to.Length == 0) to = "DInt";
            return IecToTri(from) + "_TO_" + IecToTri(to);
        }

        private static string InstanceName(XElement? inst)
        {
            if (inst == null) return "";
            var comp = Child(inst, "Component");
            return comp == null ? "" : ((string?)comp.Attribute("Name") ?? "").Trim('"');
        }

        private readonly struct PinDef
        {
            public readonly string Tia, Tri;
            public readonly bool Neg;
            public PinDef(string tia, string tri, bool neg) { Tia = tia; Tri = tri; Neg = neg; }
        }

        private IEnumerable<PinDef> InputPinsOf(XElement p, string triType)
        {
            var negated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ng in Kids(p, "Negated")) negated.Add(A(ng, "Name"));

            var ordered = new List<string>();
            foreach (var pin in WiredInputPins(p)) ordered.Add(pin);
            // keep declaration/wire order stable
            foreach (var tia in ordered)
            {
                if (tia.Equals("en", StringComparison.OrdinalIgnoreCase)) continue; // TIA enable pin: no TriStation counterpart
                yield return new PinDef(tia, TriInPin(triType, tia), negated.Contains(tia));
            }
        }

        private IEnumerable<string> WiredInputPins(XElement p)
        {
            var seen = new List<string>();
            var uid = A(p, "UId");
            var flg = p.Ancestors().FirstOrDefault(e => e.Name.LocalName == "FlgNet");
            if (flg == null) return seen;
            foreach (var w in flg.Descendants().Where(e => e.Name.LocalName == "Wire"))
            {
                var conns = w.Elements().Where(e => e.Name.LocalName is "IdentCon" or "NameCon").ToList();
                if (conns.Count < 2) continue;
                var sink = conns[1];
                if (sink.Name.LocalName != "NameCon") continue;
                if (A(sink, "UId") != uid) continue;
                string name = A(sink, "Name");
                if (!seen.Contains(name, StringComparer.OrdinalIgnoreCase)) seen.Add(name);
            }
            return seen;
        }

        // ---- rendering ----
        private string RenderVar(NVar v)
        {
            string tagName = v.Kind == "in" ? "inVariable" : v.Kind == "out" ? "outVariable" : "inOutVariable";
            var sb = new StringBuilder();
            sb.Append("            <").Append(tagName).Append(" localId=\"").Append(v.Id).Append('"');
            sb.Append(" width=\"").Append(v.W).Append("\" height=\"").Append(v.H).Append("\">");
            sb.Append("<position x=\"").Append(v.X).Append("\" y=\"").Append(v.Y)
              .Append("\" ts:SheetNumber=\"").Append(v.Sheet).Append("\" />");
            sb.Append("<expression>").Append(Esc(v.Expr)).Append("</expression>");
            if (v.DataType.Length > 0)
                sb.Append("<FBDITEM_EXT><DATATYPE>").Append(Esc(TriPrim(v.DataType))).Append("</DATATYPE></FBDITEM_EXT>");
            if (v.InConn != null)
            {
                if (v.IsConstant) { /* constants are sources only */ }
                sb.Append("<connectionPointIn><relPosition x=\"").Append(v.X).Append("\" y=\"").Append(v.Y).Append("\" />");
                AppendConnection(sb, v.InConn, v.X, v.Y);
                sb.Append("</connectionPointIn>");
            }
            if (v.IsConstant)
            {
                // constants read by the logic carry an out-connection to their consumer in the
                // reference; the source side is enough for the generator.
            }
            sb.Append("</").Append(tagName).Append(">\n");
            return sb.ToString();
        }

        private string RenderBlock(NBlock b)
        {
            var sb = new StringBuilder();
            sb.Append("            <block localId=\"").Append(b.Id).Append("\" typeName=\"").Append(Esc(b.TypeName)).Append('"');
            if (b.Instance.Length > 0) sb.Append(" instanceName=\"").Append(Esc(b.Instance)).Append('"');
            sb.Append(" width=\"").Append(b.W).Append("\" height=\"").Append(b.H).Append("\">");
            sb.Append("<position x=\"").Append(b.X).Append("\" y=\"").Append(b.Y)
              .Append("\" ts:SheetNumber=\"").Append(b.Sheet).Append("\" />");
            sb.Append("<inputVariables>");
            foreach (var pin in b.Inputs)
            {
                sb.Append("<variable formalParameter=\"").Append(Esc(pin.Name)).Append('"');
                if (pin.Neg) sb.Append(" negated=\"true\"");
                if (pin.Conn != null)
                {
                    sb.Append("><connectionPointIn><relPosition x=\"").Append(b.X).Append("\" y=\"").Append(b.Y).Append("\" />");
                    AppendConnection(sb, pin.Conn, b.X, b.Y);
                    sb.Append("</connectionPointIn></variable>");
                }
                else sb.Append(" />");
            }
            sb.Append("</inputVariables>");
            sb.Append("<inOutVariables />");
            sb.Append("<outputVariables>");
            foreach (var op in b.Outputs)
                sb.Append("<variable formalParameter=\"").Append(Esc(op))
                  .Append("\"><connectionPointOut><relPosition x=\"").Append(b.X).Append("\" y=\"").Append(b.Y)
                  .Append("\" /></connectionPointOut></variable>");
            sb.Append("</outputVariables>");
            sb.Append("</block>\n");
            return sb.ToString();
        }

        private static void AppendConnection(StringBuilder sb, NConn c, int tx, int ty)
        {
            sb.Append("<connection refLocalId=\"").Append(c.Id).Append('"');
            if (c.Pin.Length > 0) sb.Append(" formalParameter=\"").Append(Esc(c.Pin)).Append('"');
            sb.Append(" ts:FeedBack=\"false\">");
            sb.Append("<position x=\"").Append(c.X).Append("\" y=\"").Append(c.Y).Append("\" />");
            sb.Append("<position x=\"").Append(tx).Append("\" y=\"").Append(ty).Append("\" />");
            sb.Append("</connection>");
        }

        private sealed class Layout
        {
            private int _i;
            public int NextX() => 8 + (_i % 4) * 30;
            public int NextY() => 6 + ((_i++ / 4) % 18) * 9;
        }
    }

    // ---------------------------------------------------------------- access helpers

    private static string AccessExpr(XElement acc)
    {
        var symbol = Child(acc, "Symbol");
        if (symbol != null)
        {
            var parts = Kids(symbol, "Component").Select(c => Unq(A(c, "Name"))).Where(s => s.Length > 0);
            return string.Join(".", parts);
        }
        var c = Child(acc, "Constant");
        if (c != null) return (Child(c, "ConstantValue")?.Value ?? "").Trim();
        return "";
    }

    private static string ConstType(XElement acc)
    {
        var c = Child(acc, "Constant");
        string t = c == null ? "" : (Child(c, "ConstantType")?.Value ?? "").Trim();
        return IecToTri(t);
    }

    private static string Unq(string s) => s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s.Substring(1, s.Length - 2) : s;

    // ---------------------------------------------------------------- vocabulary (inverse)

    private static bool TryBoxInverse(string part, out string tri)
    {
        tri = part switch
        {
            // Genuine TIA emits the Boolean gates in two spellings: the untyped single-letter form
            // ("A"/"O"/"X", Card only) and the typed form ("And"/"Or"/"Xor", Card + SrcType). Both
            // must map back to the same TriStation primitive.
            "A" => "AND", "O" => "OR", "X" => "XOR",
            "And" => "AND", "Or" => "OR", "Xor" => "XOR",
            "Not" => "NOT", "Move" => "MOVE",
            "Eq" => "EQ", "Ne" => "NE", "Gt" => "GT", "Ge" => "GE", "Lt" => "LT", "Le" => "LE",
            "Add" => "ADD", "Sub" => "SUB", "Mul" => "MUL", "Div" => "DIV", "Mod" => "MOD",
            "Sel" => "SEL", "Mux" => "MUX", "Limit" => "LIMIT", "Min" => "MIN", "Max" => "MAX",
            "Sqrt" => "SQRT", "Abs" => "ABS", "Sin" => "SIN", "Cos" => "COS", "Tan" => "TAN",
            "Exp" => "EXP", "Ln" => "LN", "Log" => "LOG", "Expt" => "POW",
            "SHL" => "SHL", "SHR" => "SHR", "ROL" => "ROL", "ROR" => "ROR",
            _ => "",
        };
        return tri.Length > 0;
    }

    private static bool IsFunctionBox(string tri) => tri switch
    {
        "AND" or "OR" or "XOR" or "NOT" or "MOVE" or "EQ" or "NE" or "GT" or "GE" or "LT" or "LE" or
        "ADD" or "SUB" or "MUL" or "DIV" or "MOD" or "SEL" or "MUX" or "LIMIT" or "MIN" or "MAX" or
        "SQRT" or "ABS" or "SIN" or "COS" or "TAN" or "EXP" or "LN" or "LOG" or "POW" or
        "SHL" or "SHR" or "ROL" or "ROR" => true,
        _ => tri.EndsWith("_TO_", StringComparison.Ordinal) || tri.Contains("_TO_"),
    };

    private static string TriInPin(string tri, string tiaPin)
    {
        if (tri == "LIMIT")
            return tiaPin switch { "MN" => "MN", "in" => "IN", "MX" => "MX", _ => tiaPin };
        if (tiaPin.Length > 2 && tiaPin.StartsWith("in", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(tiaPin.Substring(2), out int n))
            return "IN" + n.ToString(CultureInfo.InvariantCulture);
        if (tiaPin.Equals("in", StringComparison.OrdinalIgnoreCase)) return "IN1";
        return tiaPin;
    }

    private static string TriOutPinOf(string tri) => tri switch
    {
        "AND" => "AND", "OR" => "OR", "XOR" => "XOR", "NOT" => "NOT",
        "EQ" => "=", "NE" => "<>", "GT" => ">", "GE" => ">=", "LT" => "<", "LE" => "<=",
        "ADD" => "+", "SUB" => "-", "MUL" => "*", "DIV" => "/", "MOD" => "MOD",
        _ => "OUT1",
    };

    private static IEnumerable<string> OutputPinsOf(string tri)
    {
        switch (tri)
        {
            case "R_TRIG" or "F_TRIG" or "P_TRIG": return new[] { "Q" };
            case "TON" or "TOF" or "TP": return new[] { "Q", "ET" };
            case "SR" or "RS": return new[] { "Q1" };
            case "CTU" or "CTD": return new[] { "Q", "CV" };
            case "CTUD": return new[] { "QU", "QD", "CV" };
            case "TDD_I": return new[] { "Q1" };
            case "TP_I": return new[] { "Q" };
            default: return System.Array.Empty<string>();
        }
    }

    // ---------------------------------------------------------------- type mapping

    private static string TypeBlock(string simType)
    {
        string t = (simType ?? "").Trim();
        if (t.Length == 0) return "<type><BOOL /></type>";
        if (t.StartsWith("Array", StringComparison.OrdinalIgnoreCase))
        {
            // Array[a..b] of T
            int of = t.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
            if (of > 0)
            {
                string elem = TypeBlock(t.Substring(of + 4).Trim());
                return "<type><array><dimension lower=\"0\" upper=\"0\" />" + InnerType(elem) + "</array></type>";
            }
        }
        if (t.StartsWith("\"") && t.EndsWith("\"") && t.Length > 1)
            return "<type><derived name=\"" + Esc(t.Substring(1, t.Length - 2)) + "\" /></type>";
        string tri = IecToTri(t);
        if (tri.Length == 0) return "<type><derived name=\"" + Esc(t) + "\" /></type>";
        return "<type><" + tri + " /></type>";
    }

    private static string InnerType(string typeBlock)
    {
        int a = typeBlock.IndexOf('>', typeBlock.IndexOf("<type")) + 1;
        int b = typeBlock.LastIndexOf("</type>", StringComparison.Ordinal);
        return typeBlock.Substring(a, b - a);
    }

    private static string IecToTri(string iec)
    {
        string t = (iec ?? "").Trim();
        return t.ToLowerInvariant() switch
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
            "dtl" => "DATE_AND_TIME",
            _ => "",
        };
    }

    private static string TriPrim(string simType)
    {
        string t = IecToTri(simType);
        return t.Length > 0 ? t : "BOOL";
    }

    // ---------------------------------------------------------------- xml helpers

    private static string Esc(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string CData(string s) => s.Replace("]]>", "]]]]><![CDATA[>");
}
