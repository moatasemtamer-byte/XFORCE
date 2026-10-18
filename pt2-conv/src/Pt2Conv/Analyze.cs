using System.Buffers.Binary;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// Structural + byte-level comparison of two CFB containers, with section attribution.
/// Used to test whether WB0239 is a container-level bug (Hypothesis A) or an
/// origin/authenticity check (Hypothesis B).
/// </summary>
public static class Analyze
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: analyze <fileA> <fileB>"); return 1; }
        var a = new CompoundFile(args[1]);
        var b = new CompoundFile(args[2]);
        Console.WriteLine($"A = {args[1]}  ({a.TotalFileSize:N0} B)");
        Console.WriteLine($"B = {args[2]}  ({b.TotalFileSize:N0} B)");
        Console.WriteLine();

        HeaderDiff(a, b);
        DirDiff(a, b);
        FatDiff(a, b);
        ByteRegionDiff(a, b);
        return 0;
    }

    // ---------------------------------------------------------------- header
    private static void HeaderDiff(CompoundFile a, CompoundFile b)
    {
        Console.WriteLine("== HEADER ==");
        var fa = HeaderFields(a.RawBytes);
        var fb = HeaderFields(b.RawBytes);
        foreach (var k in fa.Keys)
            Console.WriteLine($"  {k,-20} A={fa[k],-14} B={fb[k],-14} {(fa[k] == fb[k] ? "" : "  <-- DIFF")}");
        Console.WriteLine();
    }

    private static Dictionary<string, string> HeaderFields(byte[] d)
    {
        var m = new Dictionary<string, string>
        {
            ["majorVersion"] = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(26)).ToString(),
            ["byteOrder"] = "0x" + BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(28)).ToString("X4"),
            ["sectorShift"] = d[30].ToString(),
            ["miniSectorShift"] = d[32].ToString(),
            ["numDirSectors"] = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(40)).ToString(),
            ["numFatSectors"] = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(44)).ToString(),
            ["firstDirSector"] = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(48)).ToString(),
            ["transactionSig"] = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(52)).ToString(),
            ["miniStreamCutoff"] = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(56)).ToString(),
            ["firstMiniFat"] = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(60)).ToString(),
            ["numMiniFatSectors"] = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(64)).ToString(),
            ["firstDifat"] = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(68)).ToString(),
            ["numDifatSectors"] = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(72)).ToString(),
        };
        return m;
    }

    // ---------------------------------------------------------------- directory
    private static void DirDiff(CompoundFile a, CompoundFile b)
    {
        Console.WriteLine("== DIRECTORY ENTRIES (by path) ==");
        var ma = MapByPath(a);
        var mb = MapByPath(b);
        int shown = 0;
        foreach (var k in ma.Keys.Union(mb.Keys).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            ma.TryGetValue(k, out var ea);
            mb.TryGetValue(k, out var eb);
            string sa = ea == null ? "(absent)" : Desc(ea);
            string sb = eb == null ? "(absent)" : Desc(eb);
            if (sa != sb && shown++ < 40)
                Console.WriteLine($"  {k}\n      A {sa}\n      B {sb}");
        }
        Console.WriteLine($"  ({shown} differing; A={ma.Count} entries, B={mb.Count} entries)");
        Console.WriteLine();
    }

    private static string Desc(DirectoryEntry e)
        => $"type={e.ObjectType} color={e.Color} L={e.LeftSibling} R={e.RightSibling} C={e.Child} " +
           $"state=0x{e.StateBits:X8} start={e.StartSector} size={e.StreamSize} " +
           $"creat={e.CreationTime} mod={e.ModifiedTime}";

    private static Dictionary<string, DirectoryEntry> MapByPath(CompoundFile cf)
    {
        var map = new Dictionary<string, DirectoryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in cf.ListStreams())
        {
            string leaf = s.Path.Split('/').Last();
            var e = cf.Entries.FirstOrDefault(x => x.Name == leaf
                                                   && x.ObjectType == CfbObjectType.Stream
                                                   && x.StreamSize == (ulong)s.Data.Length);
            if (e != null) map[s.Path] = e;
        }
        return map;
    }

    // ---------------------------------------------------------------- FAT
    private static void FatDiff(CompoundFile a, CompoundFile b)
    {
        Console.WriteLine("== FAT / miniFAT ==");
        int na = a.FatSectorCount * 128, nb = b.FatSectorCount * 128;
        int n = Math.Min(na, nb);
        int diff = 0, shown = 0;
        for (uint s = 0; s < (uint)n; s++)
        {
            if (a.GetFat(s) == b.GetFat(s)) continue;
            diff++;
            if (shown++ < 25) Console.WriteLine($"  FAT[{s}] A={a.GetFat(s)} B={b.GetFat(s)}");
        }
        Console.WriteLine($"  FAT entries A={na} B={nb}; differing={diff}; " +
                          $"A free={CountFree(a, na)}, B free={CountFree(b, nb)}");
        Console.WriteLine($"  miniFAT sectors: A={a.MiniFatSectorCount} B={b.MiniFatSectorCount}");
        Console.WriteLine();
    }

    private static int CountFree(CompoundFile cf, int n)
    {
        int c = 0;
        for (uint s = 0; s < (uint)n; s++) if (cf.GetFat(s) == CfbConstants.FreeSect) c++;
        return c;
    }

    // ---------------------------------------------------------------- byte regions
    private static void ByteRegionDiff(CompoundFile a, CompoundFile b)
    {
        Console.WriteLine("== BYTE-LEVEL DIFF (region attribution) ==");
        byte[] da = a.RawBytes, db = b.RawBytes;
        var sa = SectorMap(a);
        var sb = SectorMap(b);
        int n = Math.Min(da.Length, db.Length);
        int i = 0;
        long totalDiff = 0;
        var regions = new List<(int start, int len, string ra, string rb)>();
        while (i < n)
        {
            if (da[i] == db[i]) { i++; continue; }
            int start = i;
            while (i < n && da[i] != db[i]) i++;
            totalDiff += i - start;
            regions.Add((start, i - start, Label(sa, start), Label(sb, start)));
        }
        if (da.Length != db.Length)
            regions.Add((n, Math.Abs(da.Length - db.Length), "(tail)", "(tail)"));
        int shown = 0;
        foreach (var reg in regions)
        {
            if (reg.len < 1 || shown++ >= 80) continue;
            Console.WriteLine($"  @0x{reg.start:X8} len={reg.len,-7} A[{reg.ra}]  B[{reg.rb}]");
        }
        Console.WriteLine($"  differing bytes={totalDiff:N0}; regions={regions.Count}; fileLen A={da.Length:N0} B={db.Length:N0}");
        Console.WriteLine();
    }

    private static Dictionary<long, string> SectorMap(CompoundFile cf)
    {
        var map = new Dictionary<long, string>();
        void Tag(uint sid, string label)
        {
            long off = 512L + (long)sid * 512;
            if (off < cf.RawBytes.Length) map[off] = label;
        }
        foreach (var sid in Chain(cf, cf.FirstDirSector)) Tag(sid, "DIR");
        foreach (var sid in cf.DifatSectors) Tag(sid, "FAT");
        if (cf.MiniFatSectorCount > 0)
            foreach (var sid in Chain(cf, cf.FirstMiniFAT)) Tag(sid, "MINIFAT");
        foreach (var e in cf.Entries)
        {
            if (e.ObjectType != CfbObjectType.Stream && e.ObjectType != CfbObjectType.Root) continue;
            if (e.StartSector == CfbConstants.EndOfChain || e.StreamSize == 0) continue;
            bool isRoot = e.ObjectType == CfbObjectType.Root;
            bool regular = isRoot || e.StreamSize >= (ulong)cf.MiniStreamCutoff;
            if (!regular) continue;
            string lbl = isRoot ? "MINISTREAM" : "STREAM:" + e.Name;
            foreach (var sid in Chain(cf, e.StartSector)) Tag(sid, lbl);
        }
        return map;
    }

    private static string Label(Dictionary<long, string> map, int off)
    {
        if (off < 512) return "HEADER";
        long aligned = 512L + ((off - 512) / 512) * 512;
        return map.TryGetValue(aligned, out var l) ? l : "FREE/SLACK";
    }

    private static List<uint> Chain(CompoundFile cf, uint start)
    {
        var list = new List<uint>();
        uint s = start;
        for (int i = 0; i < 100000; i++)
        {
            if (s >= 0xFFFFFFFA) break;
            list.Add(s);
            s = cf.GetFat(s);
            if (s == CfbConstants.EndOfChain) break;
        }
        return list;
    }
}
