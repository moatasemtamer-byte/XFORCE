using System.Text;
using System.Xml.Linq;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// Parses the POU catalog stream (Current/Project.xml) of a .pt2/.pt2x and
/// cross-references each element against its CFB folder + streams.
/// Also tests whether the per-element EMOCRC32 field equals a plain CRC32 of one
/// of the element's streams (HYPOTHESIS — report matches only, no guessing).
/// </summary>
public static class Catalog
{
    // ---- standard CRC32 (IEEE / zlib / PNG), reflected ----
    private static readonly uint[] Table = BuildTable();
    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: catalog <file.pt2x>"); return 1; }
        var cf = new CompoundFile(args[1]);
        var map = cf.ListStreams()
            .GroupBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Data, StringComparer.OrdinalIgnoreCase);

        if (!map.TryGetValue("Current/Project.xml", out var projBytes))
        {
            Console.Error.WriteLine("no Current/Project.xml stream");
            return 1;
        }

        string xmlText = Decode(projBytes);
        var doc = XDocument.Parse(xmlText);
        var root = doc.Root!;

        Console.WriteLine($"file            : {args[1]}");
        Console.WriteLine($"Project.xml     : {projBytes.Length:N0} bytes");
        Console.WriteLine($"root attrs      : NextElementDogTag={Attr(root, "NextElementDogTag")}  " +
                          $"LibID={Attr(root, "LibID")}  LibType={Attr(root, "LibType")}  " +
                          $"Description={(Attr(root, "Description") ?? "").Replace("\n", " ")}");

        var elems = doc.Descendants()
            .Where(e => e.Name.LocalName == "Class" && e.Attribute("ElemName") != null)
            .ToList();
        Console.WriteLine($"catalog entries : {elems.Count}");
        string? libFilter = args.Length >= 3 ? args[2] : null;
        Console.WriteLine("by LibID        : " + string.Join(", ",
            elems.GroupBy(e => Attr(e, "LibID") ?? "?")
                 .OrderBy(g => g.Key, StringComparer.Ordinal)
                 .Select(g => $"Lib{g.Key}={g.Count()}")));
        Console.WriteLine();

        // CFB element folders
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in cf.ListStreams())
        {
            // path like Current/Elements/<name>/<file>
            var seg = s.Path.Split('/');
            if (seg.Length >= 3 && seg[1].Equals("Elements", StringComparison.OrdinalIgnoreCase))
                folders.Add(seg[2]);
        }
        var catalogNames = new HashSet<string>(elems.Select(e => Attr(e, "ElemName")!), StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"CFB element dirs: {folders.Count}");
        Console.WriteLine($"  in catalog, no dir : [{string.Join(", ", catalogNames.Where(n => !folders.Contains(n)).Take(20))}]");
        Console.WriteLine($"  dir, not in catalog: [{string.Join(", ", folders.Where(n => !catalogNames.Contains(n)).Take(20))}]");
        Console.WriteLine();

        Console.WriteLine("ElemName            FName     Ext   DogTag  LibID ElemType SubType EMOCRC32     crc32(.lang) match");
        int matches = 0, tested = 0;
        string? libidFilter = args.Length >= 3 ? args[2] : null;
        foreach (var e in elems.OrderBy(e => Attr(e, "DogTag")))
        {
            if (libidFilter != null && Attr(e, "LibID") != libidFilter) continue;
            string name = Attr(e, "ElemName") ?? "";
            string fname = Attr(e, "FName") ?? "";
            string ext = Attr(e, "Extension") ?? "";
            string dog = Attr(e, "DogTag") ?? "";
            string lib = Attr(e, "LibID") ?? "";
            string etype = Attr(e, "ElementType") ?? "";
            string esub = Attr(e, "ElementSubType") ?? "";
            string emo = Attr(e, "EMOCRC32") ?? "";

            string? match = "";
            uint emoU = ParseU(emo);
            if (ext.Length > 0 && fname.Length > 0)
            {
                if (map.TryGetValue($"Current/Elements/{name}/{fname}{ext}", out var sb))
                {
                    uint c = Crc32(sb);
                    tested++;
                    match = (c == emoU) ? "YES" : "no";
                    if (c == emoU) matches++;
                }
                else match = "(nostream)";
            }
            Console.WriteLine($"{Trunc(name, 20),-20}{fname,-9} {Trunc(ext, 6),-5} {dog,6}  {lib,4}  {etype,7} {esub,7} {emo,10}  {(match ?? ""),-12}");
        }
        Console.WriteLine();
        Console.WriteLine($"EMOCRC32 == crc32(element stream): tested={tested}, matches={matches}");
        return 0;
    }

    private static string Decode(byte[] b)
    {
        // Project.xml may be UTF-8 with BOM
        int off = (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) ? 3 : 0;
        return Encoding.UTF8.GetString(b, off, b.Length - off);
    }

    private static string? Attr(XElement e, string n) => (string?)e.Attribute(n);
    private static uint ParseU(string s) => uint.TryParse(s, out var v) ? v : 0;
    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "~";
}
