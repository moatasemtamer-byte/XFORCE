using System.Text;
using System.Xml.Linq;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// PHASE-1: insert a POU into the element catalog (Current/Project.xml) by cloning a
/// donor element's catalog entry and language stream under a freshly allocated
/// DogTag/FName. Unverified until opened in TriStation.
///
/// Known from runA_COPY.PT2X:
///   - catalog entry lives in Current/Project.xml  (root SerializableClass)
///   - element folder = Current/Elements/&lt;ElemName&gt;/&lt;FName&gt;&lt;Extension&gt; (+ sibling _crc)
///   - FName == DogTag printed as 8 lowercase hex digits  (0x167 == 359)
///   - NextElementDogTag == max(DogTag) at save time
///   - &lt;stream&gt;_crc == SHA-256 hex of the stream bytes
///
/// NOT touched (unknown semantics): Current/Bin/Project.EMA and top-level meta_crc.
/// NOT synthesised: the catalog entry's Declarations (CTS2Vars) are cloned from the
/// donor, so local/interface variables of an inserted POU are not yet generated.
/// </summary>
public static class AddPou
{
    public sealed class InsertResult
    {
        public int DogTag;
        public string FName = "";
        public string Extension = "";
        public string Donor = "";
        public List<string> Streams = new();
    }

    public static int Run(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine("usage: addpou <template.pt2x> <out.pt2x> <sourcePOU> <newName>");
            return 1;
        }
        string tmplPath = args[1], outPath = args[2], sourcePou = args[3], newName = args[4];

        if (File.Exists(outPath))
        {
            Console.Error.WriteLine("ERROR: output file already exists: " + outPath);
            return 3;
        }

        var tree = CompoundFile.Open(tmplPath).ListStreams()
            .Select(s => (Path: s.Path, Data: s.Data)).ToList();

        InsertResult r;
        try { r = Insert(tree, newName, donor: sourcePou); }
        catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 2; }

        Program.RewriteCrcs(tree);
        Program.RewriteGlobalCrc(tree);

        var dirOut = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dirOut)) Directory.CreateDirectory(dirOut);
        CompoundFileWriter.Write(outPath, tree);

        Console.WriteLine($"cloned {r.Donor} -> {newName}");
        Console.WriteLine($"  DogTag {r.DogTag}  FName {r.FName}  Ext {r.Extension}");
        Console.WriteLine($"  element streams added: {r.Streams.Count}");
        foreach (var s in r.Streams) Console.WriteLine($"    {s}");
        Console.WriteLine($"Wrote {outPath} ({tree.Count} streams)");
        return 0;
    }

    /// <summary>
    /// Inserts a new POU into <paramref name="tree"/>'s catalog + element folder by cloning a donor.
    /// When <paramref name="donor"/> is null, a donor is chosen from the catalog by <paramref name="requireExt"/>
    /// (preferring a program, ElementSubType="0", when <paramref name="preferProgram"/>).
    /// The donor's language stream and its _crc are cloned; the language stream is renamed but its
    /// body is NOT rewritten (the caller is expected to regenerate it).
    /// </summary>
    public static InsertResult Insert(
        List<(string Path, byte[] Data)> tree,
        string newName,
        string? donor = null,
        string? requireExt = null,
        bool preferProgram = true,
        int? requireElementType = null)
    {
        int px = tree.FindIndex(t => t.Path.Equals("Current/Project.xml", StringComparison.OrdinalIgnoreCase));
        if (px < 0) throw new InvalidOperationException("no Current/Project.xml in template");

        string xml = Encoding.UTF8.GetString(tree[px].Data);
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException("no root element");

        var entries = root.Descendants()
            .Where(e => e.Name.LocalName == "Class" && e.Attribute("ElemName") != null).ToList();

        if (entries.Any(e => (string?)e.Attribute("ElemName") == newName))
            throw new InvalidOperationException($"new POU name already exists: {newName}");

        XElement src;
        if (donor != null)
        {
            src = entries.FirstOrDefault(e => (string?)e.Attribute("ElemName") == donor)
                  ?? throw new InvalidOperationException($"donor POU not found in catalog: {donor}");
        }
        else if (requireExt != null)
        {
            var cand = entries.Where(e =>
                string.Equals((string?)e.Attribute("Extension"), requireExt, StringComparison.OrdinalIgnoreCase)).ToList();
            if (requireElementType.HasValue)
                cand = cand.Where(e => string.Equals((string?)e.Attribute("ElementType"), requireElementType.Value.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (preferProgram)
                src = cand.FirstOrDefault(e => string.Equals((string?)e.Attribute("ElementSubType"), "0", StringComparison.OrdinalIgnoreCase) && string.Equals((string?)e.Attribute("ElementType"), "1", StringComparison.OrdinalIgnoreCase))
                      ?? cand.FirstOrDefault(e => string.Equals((string?)e.Attribute("ElementType"), "1", StringComparison.OrdinalIgnoreCase))
                      ?? cand.FirstOrDefault();
            else
                src = cand.FirstOrDefault(e => !string.Equals((string?)e.Attribute("ElementType"), "1", StringComparison.OrdinalIgnoreCase))
                      ?? cand.FirstOrDefault();
            if (src == null)
                throw new InvalidOperationException($"no catalog element with Extension {requireExt} to use as donor");
        }
        else throw new InvalidOperationException("donor or requireExt must be supplied");

        int maxDog = 0;
        foreach (var e in entries)
            if (int.TryParse((string?)e.Attribute("DogTag"), out int d) && d > maxDog) maxDog = d;
        int newDog = maxDog + 1;
        string newFName = newDog.ToString("x8");
        string ext = (string?)src.Attribute("Extension") ?? ".FBD";

        // ---- catalog entry clone ----
        var clone = new XElement(src);
        clone.SetAttributeValue("ElemName", newName);
        clone.SetAttributeValue("DogTag", newDog.ToString());
        clone.SetAttributeValue("FName", newFName);
        clone.SetAttributeValue("Owner", "1");
        clone.SetAttributeValue("EMOCRC32", "0");

        var parent = src.Parent ?? throw new InvalidOperationException("donor has no parent");
        var nextSibling = parent.Elements("Class")
            .FirstOrDefault(e => int.TryParse((string?)e.Attribute("DogTag"), out int d) && d > newDog);
        if (nextSibling != null) nextSibling.AddBeforeSelf(clone);
        else parent.Add(clone);

        root.SetAttributeValue("NextElementDogTag", newDog.ToString());

        tree[tree.FindIndex(t => t.Path.Equals("Current/Project.xml", StringComparison.OrdinalIgnoreCase))] =
            ("Current/Project.xml",
             Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                                    root.ToString(SaveOptions.DisableFormatting) + "\n"));

        // ---- element streams clone (language stream + its _crc only) ----
        string donorName = (string?)src.Attribute("ElemName") ?? "";
        string donorFName = (string?)src.Attribute("FName") ?? "";
        string oldPrefix = $"Current/Elements/{donorName}/";
        string newPrefix = $"Current/Elements/{newName}/";
        var additions = new List<(string Path, byte[] Data)>();
        foreach (var (p, d) in tree)
        {
            if (!p.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            string leaf = p[oldPrefix.Length..];
            string leafExt = (donorFName.Length > 0 && leaf.StartsWith(donorFName, StringComparison.OrdinalIgnoreCase))
                ? leaf[donorFName.Length..] : leaf;
            if (!leafExt.Equals(ext, StringComparison.OrdinalIgnoreCase) &&
                !leafExt.Equals(ext + "_crc", StringComparison.OrdinalIgnoreCase)) continue;
            additions.Add((newPrefix + newFName + leafExt, RenameInElement(p, d, donorName, newName)));
        }
        if (additions.Count == 0)
            throw new InvalidOperationException($"donor {donorName} has no {ext} stream under {oldPrefix}");
        tree.AddRange(additions);

        return new InsertResult
        {
            DogTag = newDog,
            FName = newFName,
            Extension = ext,
            Donor = donorName,
            Streams = additions.Select(a => a.Path).ToList(),
        };
    }

    /// <summary>
    /// Rewrites the POU's own name inside a cloned element stream:
    /// FBD root attribute pouName="..." and the IST/ST declaration line
    /// (FUNCTION_BLOCK|FUNCTION|PROGRAM &lt;name&gt;). Binary streams pass through.
    /// </summary>
    private static byte[] RenameInElement(string path, byte[] data, string src, string neu)
    {
        string ext = Path.GetExtension(path.Split('/').Last()).ToLowerInvariant();
        if (ext is not (".fbd" or ".ist" or ".st" or ".doc")) return (byte[])data.Clone();

        string text = Encoding.UTF8.GetString(data);
        string esc = System.Text.RegularExpressions.Regex.Escape(src);
        text = System.Text.RegularExpressions.Regex.Replace(text, $"pouName=\"{esc}\"", $"pouName=\"{neu}\"");
        text = System.Text.RegularExpressions.Regex.Replace(
            text, $"(?m)^(?<kw>FUNCTION_BLOCK|FUNCTION|PROGRAM)(?<ws>\\s+){esc}\\b",
            m => m.Groups["kw"].Value + m.Groups["ws"].Value + neu);
        return Encoding.UTF8.GetBytes(text);
    }
}
