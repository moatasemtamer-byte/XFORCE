using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Pt2Conv.Cfb;

namespace Pt2Conv;

public static class BuildXml
{
    public sealed class ManifestDoc
    {
        public string source { get; set; } = "";
        public int version { get; set; }
        public List<StreamRecord> streams { get; set; } = new();
        public sealed class StreamRecord
        {
            public string path { get; set; } = "";
            public long size { get; set; }
            public string sha256 { get; set; } = "";
        }
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: buildxml <xml> <out.pt2x> [<refExtractDir>] [<pouFilter>] [--tags] [--insert] [--export]");
            Console.Error.WriteLine("  pouFilter: comma-separated POU names to regenerate (default: all POUs from the XML present in the template).");
            Console.Error.WriteLine("  --tags:   also regenerate Current/Tagnames/Tagnames.csv from the XML.");
            Console.Error.WriteLine("  --insert: insert XML POUs missing from the template (clone donor entry + language stream).");
            Console.Error.WriteLine("  --export: address mode export (preserve PhysicalAddress verbatim)");
            return 1;
        }
        var pos = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        string xmlPath = pos[0];
        string outPath = pos[1];
        string refDir = pos.Count >= 3 && !string.IsNullOrWhiteSpace(pos[2]) ? pos[2] : null;
        string? filter = pos.Count >= 4 ? pos[3] : null;
        bool regenTags = args.Any(a => a.Equals("--tags", StringComparison.OrdinalIgnoreCase));
        bool insertMissing = args.Any(a => a.Equals("--insert", StringComparison.OrdinalIgnoreCase));
        var addrMode = args.Any(a => a.Equals("--export", StringComparison.OrdinalIgnoreCase))
            ? TagGen.AddrMode.Export : TagGen.AddrMode.Import;

        bool useEmbeddedBase = false;
        if (refDir == null)
        {
            refDir = EmbeddedBase.EnsureExtracted();
            useEmbeddedBase = true;
            insertMissing = true;
            regenTags = true;
            Console.WriteLine($"  using embedded base project: {refDir}");
        }

        if (File.Exists(outPath))
        {
            Console.Error.WriteLine("ERROR: output file already exists: " + outPath);
            Console.Error.WriteLine("       Refusing to overwrite. Choose a different output path so a real project file is never destroyed.");
            return 3;
        }

        var tree = new List<(string Path, byte[] Data)>();

        // Load reference manifest if present
        var refMan = Path.Combine(refDir, "_manifest.json");
        if (File.Exists(refMan))
        {
            var man = JsonSerializer.Deserialize<ManifestDoc>(
                File.ReadAllText(refMan), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            foreach (var r in man.streams)
            {
                string full = Path.Combine(refDir, r.path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full))
                    tree.Add((r.path, File.ReadAllBytes(full)));
            }
        }
        else
        {
            foreach (var f in Directory.EnumerateFiles(refDir, "*", SearchOption.AllDirectories))
            {
                string fn = Path.GetFileName(f);
                if (fn == "_manifest.json") continue;
                string rel = Path.GetRelativePath(refDir, f).Replace('\\', '/');
                tree.Add((rel, File.ReadAllBytes(f)));
            }
        }

        // Rewrite CRCs (preserve orphan meta_crc)
        RewriteCrcs(tree);

        // Snapshot of the template tree (after CRC normalisation) for the fast path: when the
        // converted project ends up identical to the template, the template container can be
        // reused as-is instead of being re-packed with a different sector layout.
        var baseStreams = tree.ToDictionary(t => t.Path, t => (byte[])t.Data.Clone(), StringComparer.OrdinalIgnoreCase);
        string? baseSource = null;
        if (File.Exists(refMan))
        {
            try
            {
                var man = JsonSerializer.Deserialize<ManifestDoc>(
                    File.ReadAllText(refMan), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (!string.IsNullOrWhiteSpace(man?.source) && File.Exists(man.source)) baseSource = man.source;
            }
            catch { /* optional */ }
        }

        // Remember which user elements the embedded base ships with (LibID=1, ElementType 1/2;
        // library elements have LibID 6..8), so foreign POUs can be stripped from the output.
        var baseUserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (useEmbeddedBase)
        {
            int bp = tree.FindIndex(t => t.Path.Equals("Current/Project.xml", StringComparison.OrdinalIgnoreCase));
            if (bp >= 0)
            {
                var bd = XDocument.Parse(Encoding.UTF8.GetString(tree[bp].Data), LoadOptions.PreserveWhitespace);
                foreach (var e in bd.Descendants())
                {
                    if (e.Name.LocalName != "Class") continue;
                    var et = e.Attribute("ElementType");
                    var lib = e.Attribute("LibID");
                    var nm = e.Attribute("ElemName");
                    if (et != null && (et.Value == "1" || et.Value == "2") && lib?.Value == "1"
                        && nm != null && !string.IsNullOrEmpty(nm.Value))
                        baseUserNames.Add(nm.Value);
                }
            }
        }

        // Regenerate every user POU's language stream from the PLCopen XML.
        var wants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (filter != null)
        {
            foreach (var f in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                wants.Add(f);
        }

        var doc = XmlRepair.Load(xmlPath);
        var pouEls = doc.Descendants().Where(e => e.Name.LocalName == "pou").ToList();

        var referencedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in doc.Descendants())
        {
            var tn = e.Attribute("typeName");
            if (tn != null && !string.IsNullOrEmpty(tn.Value)) referencedTypes.Add(tn.Value);
        }
        // Duplicate POU elements in the export (same name, identical body) must only be
        // generated once, otherwise we overwrite the stream with the same content twice.
        var pouNames = pouEls
            .Select(e => (string?)e.Attribute("name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Guard: the XML's program POU must exist in the template, otherwise the
        // template belongs to a different project and the result mixes foreign
        // catalog/compiled data with the XML's sources -> TriStation rejects it.
        var programNames = pouEls
            .Where(e => string.Equals((string?)e.Attribute("pouType"), "program", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Descendants().Any(d => d.Name.LocalName == "body"))
            .Select(e => (string?)e.Attribute("name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();
        if (programNames.Count > 0)
        {
            // A matching template contains at least one of the XML's program POUs.
            // (Some PLCopen exports carry template/helper programs that TriStation
            // does not persist as elements, so "all present" is too strict.)
            bool anyPresent = programNames.Any(n => TryGetElementDir(tree, n, out _));
            if (!anyPresent && !insertMissing)
                throw new InvalidOperationException(
                    "Template mismatch: none of the XML's program POU(s) [" + string.Join(", ", programNames) + "] " +
                    "are present under " + Path.Combine(refDir, "Current", "Elements") + ".\n" +
                    "This template belongs to a different project. Extract the matching .pt2x and use that as the template.");
        }

        var tagDesc = LoadTagDescriptions(tree);
        LoadLibraryFromTemplate(tree);
        // TriStation's importer creates function blocks first, then the programs; within the
        // programs it appends them in reverse export order (verified against a reference
        // import: P01_FB, TEMPLATES, SeimensLogic). DogTags/stream FNames follow that order.
        var programSet = new HashSet<string>(programNames, StringComparer.OrdinalIgnoreCase);
        var orderedPous = pouNames.Where(p => !programSet.Contains(p)).ToList();
        orderedPous.AddRange(pouNames.Where(programSet.Contains).Reverse());
        // Snapshots for project-defined function blocks: a program that instantiates FB04_DI needs
        // that FB's catalogue identity and interface in its own FBD stream, otherwise TriStation
        // shows the instance as unresolved.
        var projectTypes = BuildProjectTypes(tree, xmlPath, pouEls);

        foreach (var pou in orderedPous.Where(p => filter == null || wants.Contains(p)))
        {
            string lang = PromotesToSt(xmlPath, pou) ? "ST" : "FBD";
            if (!TryGetElementDir(tree, pou, out var dirPrefix))
            {
                if (!insertMissing) { Console.WriteLine($"  skip {pou}: not in template"); continue; }
                // The CFB writer folds directory names case-insensitively, so two element
                // folders differing only by case would collapse into one. Skip instead of
                // emitting a catalog entry whose stream path cannot exist.
                if (tree.Any(t => t.Path.StartsWith("Current/Elements/" + pou + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"  skip {pou}: element name collides case-insensitively with an existing element");
                    continue;
                }
                try
                {
                    bool isProgram = programNames.Contains(pou);
                    // Imported POUs always use the importer's catalog shape; a donor clone
                    // would carry the library element's flags/LibID and not match a real
                    // TriStation import.
                    var r = AddPou.InsertSynth(tree, pou, isProgram, "." + lang);
                    Console.WriteLine($"  insert {pou}: DogTag {r.DogTag} FName {r.FName} ({r.Streams.Count} stream(s))");
                }
                catch (Exception ex) { Console.WriteLine($"  skip {pou}: {ex.Message}"); continue; }
                TryGetElementDir(tree, pou, out dirPrefix);
            }
            try
            {
                var target = tree.FindIndex(t => t.Path.StartsWith(dirPrefix, StringComparison.Ordinal) &&
                                                 t.Path.EndsWith("." + lang, StringComparison.OrdinalIgnoreCase));
                if (target < 0) { Console.WriteLine($"  skip {pou}: no {lang} stream"); continue; }
                // The exporter writes one <pou> element per stored diagram copy: a POU that
                // appears twice is stored as primary + secondary sheet bodies. The decompiler
                // emits a single definition and marks the stored-twice case with a comment.
                bool duplicated = pouEls.Count(e => string.Equals((string?)e.Attribute("name"), pou, StringComparison.OrdinalIgnoreCase)) > 1
                    || pouEls.Any(e => string.Equals((string?)e.Attribute("name"), pou, StringComparison.OrdinalIgnoreCase)
                                       && e.Nodes().OfType<System.Xml.Linq.XComment>()
                                           .Any(c => c.Value.Contains("ts:diagramCopies=2", StringComparison.Ordinal)));
                byte[] gen = lang == "FBD"
                    ? FbdGenerator.GenerateBytes(xmlPath, pou, tagDesc, referencedTypes, CountSheets(tree[target].Data), duplicated, projectTypes)
                    : StGenerator.Generate(xmlPath, pou);
                long oldLen = tree[target].Data.Length;
                byte[] templateBytes = tree[target].Data;
                bool unchanged = lang == "FBD"
                    ? FbdGenerator.ContentMatchesTemplate(Encoding.UTF8.GetString(gen), Encoding.UTF8.GetString(templateBytes))
                    : gen.AsSpan().SequenceEqual(templateBytes);
                if (unchanged)
                {
                    // Same content as the template project: keep its exact stream (localIds,
                    // network handles, emission order, timestamps) instead of regenerating.
                    Console.WriteLine($"  {pou}: {(lang == "FBD" ? "FBD" : "ST ")} unchanged (template kept)");
                    continue;
                }
                tree[target] = (tree[target].Path, gen);
                if (lang == "FBD") PopulateUnresolvedTagnames(tree, pou, Encoding.UTF8.GetString(gen));
                ReportDroppedPlacements(doc, pou, lang);
                // TriStation also stores a CTS2Doc for ST POUs (the documentation page);
                // FBD POUs have none in the reference import.
                if (lang == "ST")
                {
                    string stPath = tree[target].Path;
                    string docPath = stPath[..^3] + ".DOC";
                    byte[] docBytes = DocGenerator.Generate(xmlPath, pou);
                    int di = tree.FindIndex(t => t.Path.Equals(docPath, StringComparison.OrdinalIgnoreCase));
                    if (di >= 0) tree[di] = (tree[di].Path, docBytes);
                    else
                    {
                        tree.Add((docPath, docBytes));
                        tree.Add((docPath + "_crc", Array.Empty<byte>()));
                    }
                }
                Console.WriteLine($"  {pou}: {(lang == "FBD" ? "FBD" : "ST ")} {oldLen} -> {gen.Length} B");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  skip {pou}: {ex.Message}");
            }
        }

        // Remove leftover base user elements that the XML does not define (only when using
        // the embedded base; with a user-supplied template, its own POUs are intentional).
        if (useEmbeddedBase && baseUserNames.Count > 0)
        {
            var toRemove = baseUserNames.Where(b => !pouNames.Contains(b, StringComparer.OrdinalIgnoreCase)).ToList();
            if (toRemove.Count > 0)
            {
                RemoveBaseElements(tree, toRemove);
                Console.WriteLine($"  removed base element(s): {string.Join(", ", toRemove)}");
            }
        }

        // Phase 2: regenerate the tag database from the XML's <ts:TAGNAME> blocks.
        if (regenTags)
        {
            int ti = tree.FindIndex(t => t.Path.Equals("Current/Tagnames/Tagnames.csv", StringComparison.OrdinalIgnoreCase));
            if (ti < 0) Console.WriteLine("  tags: template has no Current/Tagnames/Tagnames.csv; skipped");
            else
            {
                try
                {
                    string csv = TagGen.GenerateCsv(xmlPath, Encoding.UTF8.GetString(tree[ti].Data), addrMode, out string report);
                    tree[ti] = (tree[ti].Path, new UTF8Encoding(false).GetBytes(csv));
                    Console.Write(report);
                }
                catch (Exception ex) { Console.WriteLine($"  tags: skipped ({ex.Message})"); }
            }
        }

        // Phase 2b: adopt the project's library binaries from the XML's LIBRARY_DATA blobs.
        // TriStation embeds each library as a nested compound file; when the XML comes from a
        // project whose libraries differ from the template's, the template binaries would link
        // the wrong (older) library. Same-size streams are left alone because TriStation keeps
        // its own copy for the current install.
        AdoptLibraryBinaries(tree, doc);

        // The importer writes the XML's contentDescription into the project header.
        string contentDescription = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "fileHeader")
            ?.Attribute("contentDescription")?.Value ?? "";
        if (contentDescription.Length > 0) SetProjectDescription(tree, contentDescription);

        // Phase 2c: rebuild the hardware configuration when the XML's chassis/board layout differs
        // from the template's (module setup blocks are module-library data, captured per module id).
        HwConfig.Apply(tree, doc);

        // Recompute CRC for newly generated streams.
        RewriteCrcs(tree);
        Program.RewriteGlobalCrc(tree);
        var dirOut = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dirOut)) Directory.CreateDirectory(dirOut);

        bool identical = tree.Count == baseStreams.Count && tree.All(t =>
            baseStreams.TryGetValue(t.Path, out var b) && t.Data.AsSpan().SequenceEqual(b));
        if (identical)
        {
            if (useEmbeddedBase)
            {
                File.WriteAllBytes(outPath, EmbeddedBase.LoadPt2xBytes());
                Console.WriteLine($"Wrote {outPath} (unchanged: template container reused)");
                return 0;
            }
            if (baseSource != null && !string.Equals(Path.GetFullPath(baseSource), Path.GetFullPath(outPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(baseSource, outPath);
                Console.WriteLine($"Wrote {outPath} (unchanged: template container reused)");
                return 0;
            }
        }

        CompoundFileWriter.Write(outPath, tree);
        Console.WriteLine($"Wrote {outPath} ({tree.Count} streams)");
        return 0;
    }

    /// <summary>
    /// Mirrors the importer's per-element xref: a program's declarations whose flags are
    /// 65536 are program-local names not present in the tag database, and TriStation lists
    /// them in the catalog entry's UnresolvedTagnames collection (used by the cross-reference
    /// report). Function blocks have none.
    /// </summary>
    private static void PopulateUnresolvedTagnames(List<(string Path, byte[] Data)> tree, string pou, string fbdXml)
    {
        int ds = fbdXml.IndexOf("<Collection Name=\"declarations\"", StringComparison.Ordinal);
        if (ds < 0) return;
        int de = fbdXml.IndexOf("</Collection>", ds, StringComparison.Ordinal);
        var decl = de < 0 ? fbdXml[ds..] : fbdXml[ds..de];
        var names = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(decl,
                     "<Class name=\"([^\"]*)\"[^>]*? flags=\"65536\""))
        {
            string n = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            if (n.Length > 0) names.Add(n);
        }

        int px = tree.FindIndex(t => t.Path.Equals("Current/Project.xml", StringComparison.OrdinalIgnoreCase));
        if (px < 0) return;
        var doc = XDocument.Parse(Encoding.UTF8.GetString(tree[px].Data), LoadOptions.PreserveWhitespace);
        var entry = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "Class" &&
            string.Equals((string?)e.Attribute("ElemName"), pou, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;
        var col = entry.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "Collection" && (string?)e.Attribute("Name") == "UnresolvedTagnames");
        if (col == null) return;
        var existing = col.Elements().Where(e => e.Name.LocalName == "Class")
            .Select(e => (string?)e.Attribute("Value") ?? "").ToList();
        if (existing.Count == names.Count && existing.SequenceEqual(names, StringComparer.Ordinal)) return;
        col.RemoveNodes();
        foreach (var n in names) col.Add(new XElement("Class", new XAttribute("Value", n)));
        tree[px] = ("Current/Project.xml", AddPou.SerializeProject(doc.Root!));
    }

    /// <summary>Sets the project header description, preserving the single-line format.</summary>
    private static void SetProjectDescription(List<(string Path, byte[] Data)> tree, string description)
    {
        int px = tree.FindIndex(t => t.Path.Equals("Current/Project.xml", StringComparison.OrdinalIgnoreCase));
        if (px < 0) return;
        var doc = XDocument.Parse(Encoding.UTF8.GetString(tree[px].Data), LoadOptions.PreserveWhitespace);
        if (doc.Root == null) return;
        if ((string?)doc.Root.Attribute("Description") == description) return;
        doc.Root.SetAttributeValue("Description", description);
        tree[px] = ("Current/Project.xml", AddPou.SerializeProject(doc.Root));
    }

    /// <summary>
    /// Collects the interface (pins) of every project function block plus its catalogue identity
    /// (DogTag, ElementType/SubType, flags, event). Used to write the snapshot of an FB instance
    /// inside another POU; without it the instance is unresolved when the project is opened.
    /// </summary>
    private static Dictionary<string, FbdGenerator.ProjectTypeInfo> BuildProjectTypes(
        List<(string Path, byte[] Data)> tree, string xmlPath, List<XElement> pouEls)
    {
        var map = new Dictionary<string, FbdGenerator.ProjectTypeInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in pouEls)
        {
            string name = (string?)e.Attribute("name") ?? "";
            string pouType = (string?)e.Attribute("pouType") ?? "";
            if (name.Length == 0 || pouType == "program") continue;
            try { map[name] = FbdGenerator.BuildProjectType(xmlPath, name); }
            catch { /* a malformed POU keeps the unknown-type fallback */ }
        }
        if (map.Count == 0) return map;

        int px = tree.FindIndex(t => t.Path.Equals("Current/Project.xml", StringComparison.OrdinalIgnoreCase));
        if (px < 0) return map;
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(tree[px].Data), LoadOptions.PreserveWhitespace);
            foreach (var c in doc.Descendants().Where(c => c.Name.LocalName == "Class" && c.Attribute("ElemName") != null))
            {
                string n = (string)c.Attribute("ElemName")!;
                if (!map.TryGetValue(n, out var info)) continue;
                if (int.TryParse((string?)c.Attribute("DogTag"), out int dog)) info.DogTag = dog;
                if (int.TryParse((string?)c.Attribute("ElementType"), out int et)) info.ElemType = et;
                if (int.TryParse((string?)c.Attribute("ElementSubType"), out int st)) info.SubType = st;
                if (int.TryParse((string?)c.Attribute("ExtOptionFlags"), out int xo)) info.ExtOptFlags = xo;
                if (int.TryParse((string?)c.Attribute("CemEventInputs"), out int ci)) info.CemEI = ci;
                if (int.TryParse((string?)c.Attribute("CemActionOutputs"), out int co)) info.CemAO = co;
                var ev = c.Elements().FirstOrDefault(x =>
                    x.Name.LocalName == "Class" && (string?)x.Attribute("Embed") == "EventConnectionsChanged");
                if (ev != null)
                {
                    if (int.TryParse((string?)ev.Attribute("eventId"), out int eid)) info.EventId = eid;
                    if (int.TryParse((string?)ev.Attribute("user"), out int usr)) info.User = usr;
                    info.Date = (string?)ev.Attribute("date") ?? "";
                }
            }
        }
        catch { /* catalogue is best-effort */ }
        return map;
    }

    /// <summary>
    /// Reports drawn placements TriStation cannot represent (empty, numeric, "dir/…", "L%4102",
    /// or reserved names). The importer drops them silently, so surfacing the count keeps the loss
    /// visible instead of letting the conversion look lossless.
    /// </summary>
    private static void ReportDroppedPlacements(XDocument doc, string pou, string lang)
    {
        if (lang != "FBD") return;
        var el = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "pou"
            && string.Equals((string?)e.Attribute("name"), pou, StringComparison.Ordinal));
        if (el == null) return;

        var dropped = new List<string>();
        foreach (var v in el.Descendants().Where(e => e.Name.LocalName is "inVariable" or "outVariable" or "inOutVariable"))
        {
            string expr = v.Elements().FirstOrDefault(e => e.Name.LocalName == "expression")?.Value?.Trim() ?? "";
            bool constant = v.Name.LocalName == "inVariable" &&
                            (double.TryParse(expr, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                             || expr.StartsWith("16#", StringComparison.OrdinalIgnoreCase)
                             || expr.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                             || expr.Equals("FALSE", StringComparison.OrdinalIgnoreCase));
            if (!FbdGenerator.IsDeclarableName(expr) && !constant) dropped.Add(expr);
        }
        if (dropped.Count == 0) return;
        var sample = string.Join(", ", dropped.Distinct().Take(4).Select(s => s.Length == 0 ? "(empty)" : s));
        Console.WriteLine($"  {pou}: {dropped.Count} placement(s) with a name TriStation cannot store were dropped ({sample})");
    }

    /// <summary>
    /// Copies the compiled library binaries (<c>Current/Bin/*</c> .EMA/.NCA/.emo/.nco) out of
    /// the XML's embedded <c>LIBRARY_DATA</c> compound files when they differ in size from the
    /// template's copy — that means the XML was exported from a TriStation with a different
    /// library build. Same-size streams are kept as-is: TriStation itself keeps the installed
    /// copy on import, so replacing them would only diverge from the reference import.
    /// </summary>
    private static void AdoptLibraryBinaries(List<(string Path, byte[] Data)> tree, XDocument doc)
    {
        var config = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "LIBRARY_CONFIGURATION");
        if (config == null) return;

        var current = tree.ToDictionary(t => t.Path, t => t.Data, StringComparer.OrdinalIgnoreCase);
        foreach (var lib in config.Elements().Where(e => e.Name.LocalName == "LIBRARY"))
        {
            string name = (string?)lib.Attribute("Name") ?? "";
            var dataEl = lib.Elements().FirstOrDefault(e => e.Name.LocalName == "LIBRARY_DATA");
            if (dataEl == null || dataEl.Value.Length == 0) continue;

            byte[] blob;
            try { blob = Convert.FromBase64String(dataEl.Value.Trim()); } catch { continue; }
            if (blob.Length < 8 || blob[0] != 0xD0 || blob[1] != 0xCF) continue;

            List<Pt2Conv.Cfb.StreamContent> streams;
            try { streams = new CompoundFile(blob).ListStreams(); } catch { continue; }
            var bins = streams
                .Where(s => s.Path.StartsWith("Current/Bin/", StringComparison.OrdinalIgnoreCase)
                            && s.Data.Length > 0
                            && !s.Path.EndsWith("_crc", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (bins.Count == 0) continue;

            // Adopt the whole library when any binary has a different size (newer library build).
            bool adopt = bins.Any(s => !current.TryGetValue(s.Path, out var old) || old.Length != s.Data.Length);
            if (!adopt) continue;

            foreach (var s in bins)
            {
                int i = tree.FindIndex(t => t.Path.Equals(s.Path, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) tree[i] = (tree[i].Path, s.Data);
                else
                {
                    tree.Add((s.Path, s.Data));
                    tree.Add((s.Path + "_crc", Array.Empty<byte>()));
                }
                current[s.Path] = s.Data;
            }
            Console.WriteLine($"  library {name}: adopted {bins.Count} binary stream(s) from the XML");
        }
    }

    private static void RewriteCrcs(List<(string Path, byte[] Data)> streams)
    {
        var byName = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < streams.Count; i++)
            byName[streams[i].Path] = streams[i].Data;

        for (int i = 0; i < streams.Count; i++)
        {
            var p = streams[i].Path;
            if (!p.EndsWith("_crc", StringComparison.OrdinalIgnoreCase)) continue;
            string sib = p[..^4];
            if (!byName.TryGetValue(sib, out var b)) continue;
            streams[i] = (p, Encoding.ASCII.GetBytes(Sha256Hex(b)));
        }
    }

    /// <summary>
    /// Removes the catalog entries and element streams for the given base element names.
    /// Only used when the embedded base is a project template, so that its user POUs do not
    /// remain in the output alongside the XML's own POUs.
    /// </summary>
    private static void RemoveBaseElements(List<(string Path, byte[] Data)> tree, List<string> names)
    {
        var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var keep = new List<(string Path, byte[] Data)>();
        foreach (var s in tree)
        {
            if (s.Path.Equals("Current/Project.xml", StringComparison.OrdinalIgnoreCase))
            {
                var doc = XDocument.Parse(Encoding.UTF8.GetString(s.Data), LoadOptions.PreserveWhitespace);
                var root = doc.Root;
                if (root != null)
                {
                    var els = root.Descendants().FirstOrDefault(e =>
                        e.Name.LocalName == "Collection" &&
                        (string?)e.Attribute("Name") == "Elements");
                    if (els != null)
                    {
                        foreach (var cls in els.Elements().ToList())
                        {
                            if (cls.Name.LocalName != "Class") continue;
                            string et = cls.Attribute("ElementType")?.Value ?? "";
                            if ((et == "1" || et == "2") &&
                                nameSet.Contains(cls.Attribute("ElemName")?.Value ?? ""))
                                cls.Remove();
                        }
                        int maxDog = 0;
                        foreach (var cls in els.Elements().Where(e => e.Name.LocalName == "Class"))
                            if (int.TryParse(cls.Attribute("DogTag")?.Value, out int d) && d > maxDog) maxDog = d;
                        root.SetAttributeValue("NextElementDogTag", maxDog.ToString());
                    }
                }
                keep.Add((s.Path, AddPou.SerializeProject(root)));
                continue;
            }
            keep.Add(s);
        }
        // remove element streams for stripped programs
        var stripped = new List<(string Path, byte[] Data)>();
        foreach (var s in keep)
        {
            if (s.Path.StartsWith("Current/Elements/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = s.Path.Substring("Current/Elements/".Length);
                var seg = rest.Split('/')[0];
                if (nameSet.Contains(seg)) continue;
            }
            stripped.Add(s);
        }
        tree.Clear();
        tree.AddRange(stripped);
    }

    /// <summary>Learns FBD library-block metadata from every template FBD stream.</summary>
    private static void LoadLibraryFromTemplate(List<(string Path, byte[] Data)> streams)
    {
        var proj = streams.Find(t => t.Path.Equals("Current/Project.xml", StringComparison.OrdinalIgnoreCase));
        if (proj.Data != null)
            try { FbdGenerator.LoadLibraryNames(Encoding.UTF8.GetString(proj.Data)); } catch { }
        foreach (var s in streams)
            if (s.Path.EndsWith(".FBD", StringComparison.OrdinalIgnoreCase))
                FbdGenerator.LoadLibraryFromFbd(Encoding.UTF8.GetString(s.Data));
    }

    /// <summary>Reads Tagname-&gt;(DataType,Description) from the template's Tagnames.csv for FBD declarations.</summary>
    private static Dictionary<string, FbdGenerator.TagInfo> LoadTagDescriptions(List<(string Path, byte[] Data)> streams)
    {
        var d = new Dictionary<string, FbdGenerator.TagInfo>(StringComparer.OrdinalIgnoreCase);
        int ti = streams.FindIndex(t => t.Path.Equals("Current/Tagnames/Tagnames.csv", StringComparison.OrdinalIgnoreCase));
        if (ti < 0) return d;
        var lines = Encoding.UTF8.GetString(streams[ti].Data).Replace("\r", "").Split('\n');
        for (int i = 2; i < lines.Length; i++)
        {
            var ln = lines[i];
            if (ln.Length == 0) continue;
            if (ln.StartsWith("\"")) ln = ln[1..];
            if (ln.EndsWith("\"")) ln = ln[..^1];
            var f = ln.Split("\",\"");
            if (f.Length >= 5) d[f[0]] = new FbdGenerator.TagInfo(f[1], f[4]);
        }
        return d;
    }

    /// <summary>Finds the stream path prefix for element dir <paramref name="pou"/> (e.g. "Current/Elements/FB05_DCS_CMD").</summary>
    private static bool TryGetElementDir(List<(string Path, byte[] Data)> streams, string pou, out string dirPrefix)
    {
        string probe = "Current/Elements/" + pou + "/";
        foreach (var t in streams)
        {
            if (t.Path.StartsWith(probe, StringComparison.Ordinal))
            {
                dirPrefix = probe;
                return true;
            }
        }
        dirPrefix = probe;
        return false;
    }

    /// <summary>True if the POU has an ST body (rather than an FBD body).</summary>
    private static bool PromotesToSt(string xmlPath, string pou)
    {
        var d = XmlRepair.Load(xmlPath);
        var p = d.Descendants().FirstOrDefault(e => e.Name.LocalName == "pou" && (string?)e.Attribute("name") == pou);
        return p != null &&
               p.Descendants().FirstOrDefault(e => e.Name.LocalName == "ST") != null &&
               p.Descendants().FirstOrDefault(e => e.Name.LocalName == "FBD") == null;
    }

    /// <summary>Number of sheet entries in an existing FBD stream (0 when unparseable).</summary>
    private static int CountSheets(byte[] data)
    {
        try
        {
            var d = XDocument.Parse(Encoding.UTF8.GetString(data));
            var col = d.Root?.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "Collection" && (string?)e.Attribute("Name") == "sheets");
            return col == null ? 0 : col.Elements().Count(e => e.Name.LocalName == "Class");
        }
        catch { return 0; }
    }

    private static string Sha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        var h = sha.ComputeHash(data);
        var sb = new StringBuilder(h.Length * 2);
        foreach (byte x in h) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }
}