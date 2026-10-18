using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Pt2Conv.Cfb;

namespace Pt2Conv;

public static class FindTemplate
{
    private readonly record struct Candidate(string Path, int PouMatch, int TagDiff, int Streams, bool Stem, int PathLen);

    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: findtemplate <xml> <searchDir> [--json]");
            return 1;
        }
        string xmlPath = args[1];
        string searchDir = args[2];
        bool json = args.Any(a => a == "--json");

        if (!File.Exists(xmlPath)) { Console.Error.WriteLine("XML not found: " + xmlPath); return 2; }
        if (!Directory.Exists(searchDir)) { Console.Error.WriteLine("search dir not found: " + searchDir); return 2; }

        XDocument? doc = null;
        try { doc = XmlRepair.Load(xmlPath); }
        catch { doc = null; }

        List<string> pouNames;
        int xmlTags;
        if (doc != null)
        {
            pouNames = doc.Descendants()
                .Where(e => e.Name.LocalName == "pou")
                .Select(e => (string?)e.Attribute("name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            xmlTags = doc.Descendants().Count(e => e.Name.LocalName == "TAGNAME");
        }
        else
        {
            string txt = File.ReadAllText(xmlPath);
            pouNames = Regex.Matches(txt, @"<pou\s+name=""([^""]+)""")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            xmlTags = Regex.Matches(txt, @"<ts:TAGNAME").Count;
        }

        var results = new List<Candidate>();
        foreach (var f in EnumeratePtFiles(searchDir))
        {
            try
            {
                var cf = CompoundFile.Open(f);
                var streams = cf.ListStreams();
                var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in streams)
                {
                    var parts = s.Path.Split('/');
                    if (parts.Length >= 3 && parts[0] == "Current" && parts[1] == "Elements")
                        dirs.Add(parts[2]);
                }
                int pouMatch = pouNames.Count(p => dirs.Contains(p));
                if (pouMatch == 0) continue;
                int tagRows = 0;
                var t = streams.FirstOrDefault(s => s.Path.Equals("Current/Tagnames/Tagnames.csv", StringComparison.OrdinalIgnoreCase));
                if (t != null)
                {
                    string txt = Encoding.UTF8.GetString(t.Data);
                    tagRows = txt.Split('\n').Count(l => l.Trim().Length > 0);
                    if (tagRows > 0) tagRows--;
                }
                bool stem = Path.GetFileNameWithoutExtension(f)
                    .Equals(Path.GetFileNameWithoutExtension(xmlPath), StringComparison.OrdinalIgnoreCase);
                results.Add(new Candidate(f, pouMatch, Math.Abs(tagRows - xmlTags), streams.Count, stem, f.Length));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  skip " + f + ": " + ex.Message);
            }
        }

        var best = results
            .OrderByDescending(c => c.PouMatch)
            .ThenBy(c => c.TagDiff)
            .ThenByDescending(c => c.Streams)
            .ThenByDescending(c => c.Stem)
            .ThenBy(c => c.PathLen)
            .FirstOrDefault();

        if (best.Path == null)
        {
            Console.Error.WriteLine("no matching template found under " + searchDir);
            return 3;
        }
        if (json)
            Console.WriteLine($"{{\"path\":{System.Text.Json.JsonSerializer.Serialize(best.Path)},\"pouMatch\":{best.PouMatch},\"tagDiff\":{best.TagDiff},\"streams\":{best.Streams}}}");
        else
            Console.WriteLine(best.Path);
        return 0;
    }

    /// <summary>
    /// Recursively yields *.pt2x/*.pt2 under <paramref name="root"/> without throwing on
    /// unreadable directories (junctions such as "Application Data", "Default User", ...)
    /// and without descending into reparse points (which can loop or duplicate the tree).
    /// </summary>
    private static IEnumerable<string> EnumeratePtFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }
            foreach (var f in files)
            {
                var ext = Path.GetExtension(f);
                if (ext.Equals(".pt2x", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".pt2", StringComparison.OrdinalIgnoreCase))
                    yield return f;
            }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var s in subs)
            {
                try
                {
                    if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }
                stack.Push(s);
            }
        }
    }
}