using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// Container-level (directory / catalog) inspection for .pt2/.pt2x.
/// Unlike `extract`/`analyze` (which look at stream *contents*), this dumps the
/// raw CFB directory entries, their red-black tree links, colors, sector
/// allocation and header counters so two projects can be diffed structurally.
/// </summary>
public static class DirDump
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: dir <file.pt2x>");
            return 1;
        }
        var cf = new CompoundFile(args[1]);
        PrintHeader(args[1], cf);
        Console.WriteLine();
        PrintEntries(cf);
        return 0;
    }

    public static int RunDiff(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: dirdiff <a.pt2x> <b.pt2x>");
            return 1;
        }
        var ca = new CompoundFile(args[1]);
        var cb = new CompoundFile(args[2]);

        Console.WriteLine("=== header ===");
        Compare("major/minor", $"{ca.MajorVersion}/{ca.MinorVersion}", $"{cb.MajorVersion}/{cb.MinorVersion}");
        Compare("sector size", ca.SectorSize.ToString(), cb.SectorSize.ToString());
        Compare("dir sectors", ca.DirSectorCount.ToString(), cb.DirSectorCount.ToString());
        Compare("FAT sectors", ca.FatSectorCount.ToString(), cb.FatSectorCount.ToString());
        Compare("miniFAT sectors", ca.MiniFatSectorCount.ToString(), cb.MiniFatSectorCount.ToString());
        Console.WriteLine();
        Console.WriteLine("=== directory entries only in A ===");
        PrintOnly(ca, cb);
        Console.WriteLine();
        Console.WriteLine("=== directory entries only in B ===");
        PrintOnly(cb, ca);
        Console.WriteLine();
        Console.WriteLine("=== entries in both (by Id) that changed ===");
        PrintChanged(ca, cb);
        Console.WriteLine();
        Console.WriteLine("=== name multiset changed (count A -> B) ===");
        PrintNameCounts(ca, cb);
        return 0;
    }

    private static void PrintHeader(string file, CompoundFile cf)
    {
        var fi = new FileInfo(file);
        Console.WriteLine($"file           : {file}");
        Console.WriteLine($"size           : {fi.Length:N0} bytes");
        Console.WriteLine($"major/minor    : {cf.MajorVersion}/{cf.MinorVersion}");
        Console.WriteLine($"sector size    : {cf.SectorSize}  mini={cf.MiniSectorSize}  cutoff={cf.MiniStreamCutoff}");
        Console.WriteLine($"first dir sect : 0x{cf.FirstDirSector:X8}   dir sectors={cf.DirSectorCount}");
        Console.WriteLine($"FAT sectors    : {cf.FatSectorCount}   first miniFAT=0x{cf.FirstMiniFAT:X8}  miniFAT sectors={cf.MiniFatSectorCount}");
        Console.WriteLine($"DIFAT sectors  : {cf.DifatSectors.Count}");
    }

    private static void PrintEntries(CompoundFile cf)
    {
        Console.WriteLine($"{"Id",4}  {"Type",6} {"C",1}  {"Name",-32}  {"Left",10}  {"Right",10}  {"Child",10}  {"Start",10}  {"Size",12}  Modified");
        foreach (var e in cf.Entries.OrderBy(e => e.Id))
        {
            Console.WriteLine(
                $"{e.Id,4}  {Abbrev(e.ObjectType),6} {e.Color,1}  {Trunc(e.Name, 32),-32}  " +
                $"0x{e.LeftSibling:X8}  0x{e.RightSibling:X8}  0x{e.Child:X8}  " +
                $"0x{e.StartSector:X8}  {e.StreamSize,12}  {FmtTime(e.ModifiedTime)}");
        }
    }

    private static void PrintOnly(CompoundFile a, CompoundFile b)
    {
        var ids = new HashSet<uint>(b.Entries.Select(e => e.Id));
        foreach (var e in a.Entries.Where(e => !ids.Contains(e.Id)).OrderBy(e => e.Id))
            Console.WriteLine($"  #{e.Id,4} {Abbrev(e.ObjectType),6} '{e.Name}' size={e.StreamSize} start=0x{e.StartSector:X8}");
    }

    private static void PrintChanged(CompoundFile a, CompoundFile b)
    {
        var bById = b.Entries.ToDictionary(e => e.Id);
        foreach (var ea in a.Entries.OrderBy(e => e.Id))
        {
            if (!bById.TryGetValue(ea.Id, out var eb)) continue;
            var d = new List<string>();
            if (ea.Name != eb.Name) d.Add($"name '{ea.Name}'->'{eb.Name}'");
            if (ea.ObjectType != eb.ObjectType) d.Add($"type {ea.ObjectType}->{eb.ObjectType}");
            if (ea.Color != eb.Color) d.Add($"color {ea.Color}->{eb.Color}");
            if (ea.LeftSibling != eb.LeftSibling) d.Add($"left 0x{ea.LeftSibling:X8}->0x{eb.LeftSibling:X8}");
            if (ea.RightSibling != eb.RightSibling) d.Add($"right 0x{ea.RightSibling:X8}->0x{eb.RightSibling:X8}");
            if (ea.Child != eb.Child) d.Add($"child 0x{ea.Child:X8}->0x{eb.Child:X8}");
            if (ea.StartSector != eb.StartSector) d.Add($"start 0x{ea.StartSector:X8}->0x{eb.StartSector:X8}");
            if (ea.StreamSize != eb.StreamSize) d.Add($"size {ea.StreamSize}->{eb.StreamSize}");
            if (d.Count > 0)
                Console.WriteLine($"  #{ea.Id,4} '{ea.Name}': {string.Join(", ", d)}");
        }
    }

    private static void PrintNameCounts(CompoundFile a, CompoundFile b)
    {
        var ca = a.Entries.GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.Count());
        var cb = b.Entries.GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.Count());
        foreach (var n in ca.Keys.Union(cb.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            ca.TryGetValue(n, out int na);
            cb.TryGetValue(n, out int nb);
            if (na != nb) Console.WriteLine($"  {Trunc(n, 40),-40} {na} -> {nb}");
        }
    }

    private static void Compare(string label, string a, string b)
    {
        if (a != b) Console.WriteLine($"  {label}: {a} -> {b}");
    }

    private static string Abbrev(CfbObjectType t) => t switch
    {
        CfbObjectType.Storage => "Store",
        CfbObjectType.Stream => "Stream",
        CfbObjectType.Root => "Root",
        _ => "?",
    };

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "~";

    private static string FmtTime(long filetime)
    {
        try
        {
            if (filetime <= 0) return "-";
            return DateTime.FromFileTimeUtc(filetime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch { return "?"; }
    }
}
