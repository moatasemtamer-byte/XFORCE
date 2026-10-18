using System.Security.Cryptography;
using System.Text;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// Reverse-engineers the orphan top-level "meta_crc" stream by hashing many candidate
/// byte buffers and reporting which one reproduces the stored value.
/// </summary>
public static class Metacrc
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: metacrc <file>"); return 1; }
        var cf = new CompoundFile(args[1]);
        var streams = cf.ListStreams();

        string? target = null;
        foreach (var s in streams)
            if (s.Path.Equals("meta_crc", StringComparison.OrdinalIgnoreCase) ||
                s.Path.Equals("Root/meta_crc", StringComparison.OrdinalIgnoreCase) ||
                s.Path.EndsWith("/meta_crc", StringComparison.OrdinalIgnoreCase) ||
                s.Path.EndsWith("meta_crc", StringComparison.OrdinalIgnoreCase))
                target = Encoding.ASCII.GetString(s.Data).Trim().ToLowerInvariant();
        if (target == null) { Console.WriteLine("no meta_crc stream found"); return 1; }
        Console.WriteLine($"meta_crc = {target}");
        Console.WriteLine();

        var data = new List<(string Path, byte[] Bytes)>();
        foreach (var s in streams)
            if (!s.Path.EndsWith("_crc", StringComparison.OrdinalIgnoreCase))
                data.Add((s.Path, s.Data));
        var sorted = data.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();

        var candidates = new List<(string Name, byte[] Bytes)>();
        void Add(string name, byte[] b) => candidates.Add((name, b));
        void AddStr(string name, string s) => candidates.Add((name, Encoding.ASCII.GetBytes(s)));

        Add("whole-file", cf.RawBytes);
        Add("whole-file-minus-header", cf.RawBytes.AsSpan(512).ToArray());

        Add("all-stream-data (list order)", Concat(data, d => d.Bytes));
        Add("all-stream-data (sorted)", Concat(sorted, d => d.Bytes));
        Add("all-stream-data+names (sorted)", Concat(sorted, d => Encoding.ASCII.GetBytes(d.Path).Concat(d.Bytes).ToArray()));

        Add("all-crc-bytes (list order)", Concat(streams, s => s.Path.EndsWith("_crc", StringComparison.OrdinalIgnoreCase) ? s.Data : Array.Empty<byte>()));
        Add("all-crc-bytes (sorted)", Concat(streams.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList(), s => s.Path.EndsWith("_crc", StringComparison.OrdinalIgnoreCase) ? s.Data : Array.Empty<byte>()));
        Add("all-crc-hex (sorted)", Encoding.ASCII.GetBytes(string.Concat(streams.Where(s => s.Path.EndsWith("_crc", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(s => Encoding.ASCII.GetString(s.Data).Trim().ToLowerInvariant()))));
        Add("all-crc-hex (list order)", Encoding.ASCII.GetBytes(string.Concat(streams.Where(s => s.Path.EndsWith("_crc", StringComparison.OrdinalIgnoreCase)).Select(s => Encoding.ASCII.GetString(s.Data).Trim().ToLowerInvariant()))));

        Add("names-only (sorted)", Encoding.ASCII.GetBytes(string.Join("\n", sorted.Select(d => d.Path))));
        Add("name+sha256 (sorted)", Encoding.ASCII.GetBytes(string.Join("\n", sorted.Select(d => d.Path + "=" + Sha(d.Bytes)))));

        foreach (var (p, b) in data)
        {
            if (b.Length > 200_000) continue;
            Add($"stream:{p}", b);
        }

        // ---- MFC CMapStringToString iteration-order emulation ----
        var allStreams = new List<(string Path, byte[] Bytes)>();
        foreach (var s in streams) allStreams.Add((s.Path, s.Data));

        var pathById = new Dictionary<uint, string>();
        var byId = new Dictionary<uint, DirectoryEntry>();
        foreach (var e in cf.Entries) byId[e.Id] = e;
        BuildPaths(byId, 0, "", pathById);
        var bytesByPath = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var s in streams) bytesByPath[s.Path] = s.Data;
        var idOrderAll = new List<(string Path, byte[] Bytes)>();
        foreach (var e in cf.Entries.Where(x => x.ObjectType == CfbObjectType.Stream).OrderBy(x => x.Id))
        {
            string p = pathById.TryGetValue(e.Id, out var pp) ? pp : ("#" + e.Id);
            bytesByPath.TryGetValue(p, out var bb);
            idOrderAll.Add((p, bb ?? Array.Empty<byte>()));
        }
        var idOrderData = idOrderAll.Where(x => !x.Path.EndsWith("_crc", StringComparison.OrdinalIgnoreCase)).ToList();

        var keyFmts = new (string Name, Func<string, string> F)[]
        {
            ("path",  p => p),
            ("slash", p => "/" + p),
            ("bs",    p => p.Replace('/', '\\')),
            ("base",  p => p[(p.LastIndexOf('/') + 1)..]),
        };
        var sets = new (string Name, List<(string Path, byte[] Bytes)> Items)[]
        {
            ("data", data),
            ("all",  allStreams),
        };
        int[] tables = { 17, 7, 13, 31, 101, 1009 };
        foreach (var (setName, items) in sets)
        {
            var sortedItems = items.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var idItems = setName == "data" ? idOrderData : idOrderAll;
            var insertions = new (string Name, List<(string Path, byte[] Bytes)> Items)[]
            {
                ("list", items),
                ("sort", sortedItems),
                ("id", idItems),
            };
            foreach (var (insName, ins) in insertions)
            foreach (var kf in keyFmts)
            foreach (int ts in tables)
            {
                var ord = MfcOrder(ins, kf.F, ts);
                var sbV = new StringBuilder(); var sbKV = new StringBuilder(); var sbKC = new StringBuilder(); var sbKTV = new StringBuilder();
                using var msRaw = new MemoryStream();
                foreach (var (p, b) in ord)
                {
                    string v = Sha(b);
                    string k = kf.F(p);
                    sbV.Append(v);
                    sbKV.Append(k).Append(v);
                    sbKC.Append(k).Append(':').Append(v);
                    sbKTV.Append(k).Append('\t').Append(v);
                    msRaw.Write(SHA256.HashData(b));
                }
                Add($"mfc set={setName} ins={insName} key={kf.Name} ts={ts} V", Encoding.ASCII.GetBytes(sbV.ToString()));
                Add($"mfc set={setName} ins={insName} key={kf.Name} ts={ts} KV", Encoding.ASCII.GetBytes(sbKV.ToString()));
                Add($"mfc set={setName} ins={insName} key={kf.Name} ts={ts} K:V", Encoding.ASCII.GetBytes(sbKC.ToString()));
                Add($"mfc set={setName} ins={insName} key={kf.Name} ts={ts} K\tV", Encoding.ASCII.GetBytes(sbKTV.ToString()));
                Add($"mfc set={setName} ins={insName} key={kf.Name} ts={ts} raw", msRaw.ToArray());
            }
        }

        int matched = 0;
        foreach (var (name, bytes) in candidates)
        {
            string h = Sha(bytes);
            if (h == target) { Console.WriteLine($"MATCH: {name}  ({bytes.Length:N0} B)"); matched++; }
        }
        if (matched == 0) Console.WriteLine("no candidate matched.");
        Console.WriteLine($"tested {candidates.Count} candidates.");

        Console.WriteLine();
        Console.WriteLine("-- digest preview (first 12 hex) --");
        foreach (var (name, bytes) in candidates.Take(20))
            Console.WriteLine($"  {Sha(bytes)[..12]}  {bytes.Length,12:N0}  {name}");
        return matched > 0 ? 0 : 3;
    }

    // Emulates CMapStringToString: insert each item at the head of its hash bucket,
    // then iterate buckets 0..N-1, newest-first within each bucket.
    private static uint MfcHash(string key)
    {
        uint h = 0;
        foreach (char c in key) h = (h << 5) + h + (uint)c;
        return h;
    }

    private static void BuildPaths(Dictionary<uint, DirectoryEntry> byId, uint rootId, string prefix, Dictionary<uint, string> map)
    {
        if (!byId.TryGetValue(rootId, out var root)) return;
        var stack = new Stack<uint>();
        if (Valid(byId, root.Child)) stack.Push(root.Child);
        var seen = new HashSet<uint>();
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id) || !byId.TryGetValue(id, out var e)) continue;
            string p = prefix.Length == 0 ? e.Name : prefix + "/" + e.Name;
            if (e.ObjectType == CfbObjectType.Stream || e.IsStorage) map[id] = p;
            if (e.IsStorage) BuildPaths(byId, id, p, map);
            if (Valid(byId, e.RightSibling)) stack.Push(e.RightSibling);
            if (Valid(byId, e.LeftSibling)) stack.Push(e.LeftSibling);
        }
    }

    private static bool Valid(Dictionary<uint, DirectoryEntry> byId, uint id)
        => id != uint.MaxValue && id != uint.MaxValue - 1 && byId.ContainsKey(id);

    private static List<(string Path, byte[] Bytes)> MfcOrder(
        List<(string Path, byte[] Bytes)> items, Func<string, string> keyf, int tableSize)
    {
        var buckets = new List<(string Path, byte[] Bytes)>[tableSize];
        for (int i = 0; i < tableSize; i++) buckets[i] = new List<(string Path, byte[] Bytes)>();
        foreach (var it in items)
        {
            int bi = (int)(MfcHash(keyf(it.Path)) % (uint)tableSize);
            buckets[bi].Insert(0, it);
        }
        var result = new List<(string Path, byte[] Bytes)>();
        for (int i = 0; i < tableSize; i++) foreach (var it in buckets[i]) result.Add(it);
        return result;
    }

    private static byte[] Concat<T>(IList<T> items, Func<T, byte[]> sel)
    {
        using var ms = new MemoryStream();
        foreach (var it in items) ms.Write(sel(it));
        return ms.ToArray();
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
}
