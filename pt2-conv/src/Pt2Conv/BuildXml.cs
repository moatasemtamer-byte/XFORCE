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

        // Remember which programs the embedded base ships with, so we can strip the ones
        // the XML does not define once every XML program has been inserted/regenerated.
        var baseProgNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    var nm = e.Attribute("ElemName");
                    if (et != null && et.Value == "1" && nm != null && !string.IsNullOrEmpty(nm.Value))
                        baseProgNames.Add(nm.Value);
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
        foreach (var pou in pouNames.Where(p => filter == null || wants.Contains(p)))
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
                    var r = AddPou.Insert(tree, pou, donor: null, requireExt: "." + lang,
                                          preferProgram: isProgram,
                                          requireElementType: isProgram ? 1 : 2);
                    Console.WriteLine($"  insert {pou}: donor {r.Donor} DogTag {r.DogTag} FName {r.FName} ({r.Streams.Count} stream(s))");
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
                // appears twice is stored as primary + secondary sheet bodies.
                bool duplicated = pouEls.Count(e => string.Equals((string?)e.Attribute("name"), pou, StringComparison.OrdinalIgnoreCase)) > 1;
                byte[] gen = lang == "FBD"
                    ? FbdGenerator.GenerateBytes(xmlPath, pou, tagDesc, referencedTypes, CountSheets(tree[target].Data), duplicated)
                    : StGenerator.Generate(xmlPath, pou);
                long oldLen = tree[target].Data.Length;
                tree[target] = (tree[target].Path, gen);
                Console.WriteLine($"  {pou}: {(lang == "FBD" ? "FBD" : "ST ")} {oldLen} -> {gen.Length} B");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  skip {pou}: {ex.Message}");
            }
        }

        // Remove leftover base programs that the XML does not define (only when using the
        // embedded base, since a user-supplied template is expected to keep its own layout).
        if (useEmbeddedBase && baseProgNames.Count > 0)
        {
            var toRemove = baseProgNames.Where(b => !pouNames.Contains(b, StringComparer.OrdinalIgnoreCase)).ToList();
            if (toRemove.Count > 0)
            {
                RemoveBasePrograms(tree, toRemove);
                Console.WriteLine($"  removed base program(s): {string.Join(", ", toRemove)}");
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

        // Recompute CRC for newly generated streams.
        RewriteCrcs(tree);
        Program.RewriteGlobalCrc(tree);

        var dirOut = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dirOut)) Directory.CreateDirectory(dirOut);
        CompoundFileWriter.Write(outPath, tree);
        Console.WriteLine($"Wrote {outPath} ({tree.Count} streams)");
        return 0;
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
    /// Removes the catalog entries and element streams for the given base program names.
    /// Only used when the embedded base is the reference so that a foreign project's
    /// programs do not remain in the output alongside the XML's own programs.
    /// </summary>
    private static void RemoveBasePrograms(List<(string Path, byte[] Data)> tree, List<string> names)
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
                            if (cls.Name.LocalName == "Class" &&
                                cls.Attribute("ElementType")?.Value == "1" &&
                                nameSet.Contains(cls.Attribute("ElemName")?.Value ?? ""))
                            {
                                cls.Remove();
                            }
                        }
                    }
                }
                keep.Add((s.Path, Encoding.UTF8.GetBytes(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + root + "\n")));
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