using System.Buffers.Binary;
using System.Text;

namespace Pt2Conv.Cfb;

/// <summary>A stream extracted from the container: its path and raw bytes.</summary>
public sealed record StreamContent(string Path, byte[] Data);

public enum CfbObjectType : byte
{
    Unknown = 0,
    Storage = 1,
    Stream = 2,
    Root = 5,
}

/// <summary>A parsed (or to-be-written) CFB directory entry.</summary>
public sealed class DirectoryEntry
{
    public uint Id;
    public string Name = "";
    public CfbObjectType ObjectType = CfbObjectType.Unknown;
    public byte Color;                     // 0 = red, 1 = black
    public uint LeftSibling = CfbConstants.NoStream;
    public uint RightSibling = CfbConstants.NoStream;
    public uint Child = CfbConstants.NoStream;
    public byte[] ClsId = new byte[16];
    public uint StateBits;
    public long CreationTime;
    public long ModifiedTime;
    public uint StartSector = CfbConstants.EndOfChain;
    public ulong StreamSize;

    public bool IsStorage => ObjectType == CfbObjectType.Storage;
    public bool IsRoot => ObjectType == CfbObjectType.Root;

    public override string ToString() => $"#{Id} {ObjectType} '{Name}' size={StreamSize} start=0x{StartSector:X8}";
}

/// <summary>An OLE/CFB compound file reader (TriStation .pt2/.pt2x).
/// Self-contained — no third-party OLE library.</summary>
public sealed class CompoundFile
{
    public int MinorVersion { get; private set; }
    public int MajorVersion { get; private set; }
    public int SectorSize { get; private set; } = 512;
    public int MiniSectorSize { get; private set; } = 64;
    public long MiniStreamCutoff { get; private set; } = 4096;
    public int FatSectorCount { get; private set; }
    public uint FirstDirSector { get; private set; }
    public uint FirstMiniFAT { get; private set; }
    public int MiniFatSectorCount { get; private set; }
    public int DirSectorCount { get; private set; }
    public long TotalFileSize { get; private set; }
    public string Path { get; }

    private readonly byte[] _data;
    private uint[] _fat = Array.Empty<uint>();
    private uint[] _miniFat = Array.Empty<uint>();
    private byte[] _miniStream = Array.Empty<byte>();

    public byte[] RawBytes => _data;
    public uint[] FatTable => _fat;
    public uint[] MiniFatTable => _miniFat;
    public byte[] MiniStreamBytes => _miniStream;
    public List<uint> DifatSectors { get; } = new();

    public List<DirectoryEntry> Entries { get; } = new();
    private DirectoryEntry?[] _bySlot = Array.Empty<DirectoryEntry?>();
    public int TotalSectorCount => _fat.Length;

    /// <summary>Returns the entry occupying directory <paramref name="slot"/> (null if empty/out of range).</summary>
    public DirectoryEntry? EntryBySlot(uint slot)
        => slot != CfbConstants.NoStream && (int)slot < _bySlot.Length ? _bySlot[(int)slot] : null;

    public static CompoundFile Open(string path) => new(path);

    public CompoundFile(string path)
    {
        Path = path;
        _data = File.ReadAllBytes(path);
        Read(_data);
    }

    public CompoundFile(byte[] data)
    {
        Path = "<memory>";
        _data = data;
        Read(data);
    }

    private void Read(byte[] data)
    {
        TotalFileSize = data.Length;
        if (data.Length < 512)
            throw new InvalidDataException("File too small to be a CFB document");
        for (int i = 0; i < 8; i++)
            if (data[i] != CfbConstants.Signature[i])
                throw new InvalidDataException("Missing CFB signature (not an OLE compound file)");

        MinorVersion = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(24));
        MajorVersion = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(26));
        if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28)) != 0xFFFE)
            throw new InvalidDataException("Unexpected byte order");
        SectorSize = 1 << data[30];
        MiniSectorSize = 1 << data[32];
        FatSectorCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(44));
        FirstDirSector = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(48));
        MiniStreamCutoff = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(56));
        FirstMiniFAT = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(60));
        MiniFatSectorCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(64));

        if (SectorSize is not (512 or 4096))
            throw new InvalidDataException($"Unsupported sector size {SectorSize}");
        if (FatSectorCount <= 0 || (long)FatSectorCount * (SectorSize / 4) < data.Length / SectorSize)
            throw new InvalidDataException("Invalid FAT sector count");

        // ---- rebuild FAT from DIFAT ----
        _fat = new uint[FatSectorCount * (SectorSize / 4)];
        {
            var difat = new List<uint>();
            int ptr = 76;
            for (int i = 0; i < 109 && ptr + 4 <= 512; i++, ptr += 4)
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(ptr));
                if (v != CfbConstants.FreeSect) difat.Add(v);
            }
            uint nextDifat = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(68));
            int difatCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(72));
            for (int d = 0; d < difatCount && IsOrdinary(nextDifat); d++)
            {
                long o = (long)nextDifat * SectorSize + SectorSize;
                int perSector = SectorSize / 4;
                for (int i = 0; i < perSector - 1; i++)
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)o + i * 4));
                    if (v != CfbConstants.FreeSect) difat.Add(v);
                }
                nextDifat = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)o + (perSector - 1) * 4));
            }
            int fatIdx = 0;
            foreach (uint s in difat)
            {
                DifatSectors.Add(s);
                long o = (long)s * SectorSize + SectorSize;
                if (o + SectorSize > data.Length) continue;
                for (int i = 0; i < SectorSize / 4 && fatIdx < _fat.Length; i++, fatIdx++)
                    _fat[fatIdx] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)o + i * 4));
            }
            if (fatIdx != _fat.Length)
                throw new InvalidDataException($"Incomplete FAT: expected {_fat.Length}, got {fatIdx}");
        }

        // ---- directory entries ----
        int[] dirChain = ChainToArray(FirstDirSector, _fat);
        DirSectorCount = dirChain.Length;
        var dirBytes = ReadChainSectors(dirChain);
        int entryCount = dirChain.Length * (SectorSize / 128);
        _bySlot = new DirectoryEntry?[entryCount];
        for (int id = 0; id < entryCount; id++)
        {
            int off = id * 128;
            if (off + 128 > dirBytes.Length) break;
            var e = ParseEntry(dirBytes.AsSpan(off));
            e.Id = (uint)id;
            _bySlot[id] = e;
            if (e.ObjectType != CfbObjectType.Unknown)
                Entries.Add(e);
        }
        if (Entries.Count == 0)
            throw new InvalidDataException("No directory entries");

        // ---- mini stream (contents of the root entry) ----
        DirectoryEntry root = Entries.First(e => e.IsRoot);
        if (root.StartSector != CfbConstants.EndOfChain && root.StreamSize > 0)
            _miniStream = ReadChain(root.StartSector, (long)root.StreamSize);

        // ---- mini FAT ----
        if (MiniFatSectorCount > 0)
        {
            var mf = ReadChain(FirstMiniFAT, (long)MiniFatSectorCount * SectorSize);
            _miniFat = new uint[mf.Length / 4];
            for (int i = 0; i < _miniFat.Length; i++)
                _miniFat[i] = BinaryPrimitives.ReadUInt32LittleEndian(mf.AsSpan(i * 4));
        }
    }

    private static bool IsOrdinary(uint v)
        => v < CfbConstants.FreeSect && v != CfbConstants.EndOfChain;

    private static DirectoryEntry ParseEntry(ReadOnlySpan<byte> b)
    {
        var e = new DirectoryEntry();
        int nameLen = b[64] | (b[65] << 8);
        if (nameLen >= 2 && nameLen <= 66)
            e.Name = Encoding.Unicode.GetString(b.Slice(0, nameLen - 2));
        e.ObjectType = (CfbObjectType)b[66];
        e.Color = b[67];
        e.LeftSibling = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(68));
        e.RightSibling = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(72));
        e.Child = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(76));
        b.Slice(80, 16).CopyTo(e.ClsId);
        e.StateBits = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(96));
        e.CreationTime = BinaryPrimitives.ReadInt64LittleEndian(b.Slice(100));
        e.ModifiedTime = BinaryPrimitives.ReadInt64LittleEndian(b.Slice(108));
        e.StartSector = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(116));
        e.StreamSize = BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(120));
        return e;
    }

    /// <summary>Returns the FAT word for a regular sector, or FreeSect if out of range.</summary>
    public uint GetFat(uint sector)
        => sector < (uint)_fat.Length ? _fat[sector] : CfbConstants.FreeSect;

    public bool IsSectorFree(uint sector)
        => GetFat(sector) == CfbConstants.FreeSect;

    private static int[] ChainToArray(uint start, uint[] fat)
    {
        var list = new List<int>();
        uint cur = start;
        int guard = fat.Length;
        var seen = new HashSet<int>();
        while (IsOrdinary(cur) && (int)cur < guard)
        {
            if (!seen.Add((int)cur))
                throw new InvalidDataException($"FAT cycle at sector {cur}");
            list.Add((int)cur);
            uint next = fat[cur];
            if (next == CfbConstants.EndOfChain)
                break;
            if (next == CfbConstants.FreeSect)
                throw new InvalidDataException($"FAT chain broken at sector {cur}");
            cur = next;
        }
        if (start != CfbConstants.EndOfChain && list.Count == 0 && (int)start < guard)
            list.Add((int)start);
        return list.ToArray();
    }

    private byte[] ReadChain(uint startSector, long expectedSize)
    {
        // If the chain belongs to the mini stream, sectors are *regular* sectors of the root chain.
        int[] chain = ChainToArray(startSector, _fat);
        long total = (long)chain.Length * SectorSize;
        if (total < expectedSize)
            throw new InvalidDataException($"Chain too short: have {total}, need {expectedSize}");
        var buf = new byte[expectedSize];
        for (int i = 0; i < chain.Length; i++)
        {
            long o = (long)chain[i] * SectorSize + SectorSize;
            int n = (int)Math.Min(SectorSize, expectedSize - (long)i * SectorSize);
            Array.Copy(_data, o, buf, i * SectorSize, n);
        }
        return buf;
    }

    private byte[] ReadChainSectors(int[] chain)
    {
        var buf = new byte[(long)chain.Length * SectorSize];
        for (int i = 0; i < chain.Length; i++)
        {
            long o = (long)chain[i] * SectorSize + SectorSize;
            Array.Copy(_data, o, buf, (long)i * SectorSize, SectorSize);
        }
        return buf;
    }

    /// <summary>Reads a stream's bytes given its directory entry. Handles mini vs regular.</summary>
    public byte[] ReadStream(DirectoryEntry e)
    {
        if (e.ObjectType != CfbObjectType.Stream)
            throw new InvalidDataException($"'{e.Name}' is not a stream");
        if (e.StreamSize == 0)
            return Array.Empty<byte>();
        if (e.StreamSize < (ulong)MiniStreamCutoff && _miniStream.Length > 0)
        {
            // mini stream: offset start sector within the mini stream, mini-sector chain via mini FAT
            var buf = new byte[e.StreamSize];
            long pos = 0;
            uint mini = e.StartSector;
            int guard = _miniFat.Length;
            while (pos < (long)e.StreamSize)
            {
                if (!((int)mini < guard))
                    throw new InvalidDataException($"Mini chain out of range for '{e.Name}'");
                long o = (long)mini * MiniSectorSize;
                int n = (int)Math.Min(MiniSectorSize, (long)e.StreamSize - pos);
                Array.Copy(_miniStream, o, buf, pos, n);
                pos += n;
                uint next = _miniFat[mini];
                if (pos >= (long)e.StreamSize) break;
                if (next == CfbConstants.EndOfChain)
                    throw new InvalidDataException($"Mini chain ended early for '{e.Name}'");
                if (next == CfbConstants.FreeSect || next >= (uint)guard)
                    throw new InvalidDataException($"Broken mini chain for '{e.Name}'");
                mini = next;
            }
            return buf;
        }
        var chain = ChainToArray(e.StartSector, _fat);
        long total = (long)chain.Length * SectorSize;
        if (total < (long)e.StreamSize)
            throw new InvalidDataException($"Regular chain too short for '{e.Name}'");
        var data = new byte[e.StreamSize];
        for (int i = 0; i < chain.Length; i++)
        {
            long o = (long)chain[i] * SectorSize + SectorSize;
            int n = (int)Math.Min(SectorSize, (long)e.StreamSize - (long)i * SectorSize);
            Array.Copy(_data, o, data, (long)i * SectorSize, n);
        }
        return data;
    }

    /// <summary>Reads a stream by '/' separated path beneath the root, e.g. "Current/Elements/DT".</summary>
    public byte[] ReadStream(string path)
    {
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<uint>();
        foreach (var e in Entries)
        {
            if (!e.IsRoot) continue;
            if (segs.Length == 0)
                return e.StreamSize == 0 ? Array.Empty<byte>() : _miniStream;
            return ReadChild(segs, 0, e, seen);
        }
        throw new InvalidDataException("No root entry");
    }

    private byte[] ReadChild(string[] segs, int depth, DirectoryEntry parent, HashSet<uint> seen)
    {
        uint id = parent.Child;
        int guard = Entries.Count;
        while (id != CfbConstants.NoStream && (int)id < guard)
        {
            if (!seen.Add(id)) break;
            var e = Entries[(int)id];
            if (string.Equals(e.Name, segs[depth], StringComparison.OrdinalIgnoreCase))
            {
                if (depth == segs.Length - 1)
                {
                    if (e.ObjectType != CfbObjectType.Stream)
                        throw new InvalidDataException($"'{string.Join('/',
                            segs.Take(depth + 1))}' is a storage, not a stream");
                    return ReadStream(e);
                }
                if (e.IsStorage)
                    return ReadChild(segs, depth + 1, e, seen);
                throw new InvalidDataException($"'{string.Join('/', segs.Take(depth + 1))}' is not a storage");
            }
            id = e.RightSibling;
        }
        throw new InvalidDataException($"Stream not found: '{string.Join('/', segs)}'");
    }

    /// <summary>Walks the storage tree and returns every stream as (path, bytes). Root name is skipped.</summary>
    public List<StreamContent> ListStreams()
    {
        var result = new List<StreamContent>();
        foreach (var root in Entries)
        {
            if (root.IsRoot)
            {
                Walk(result, root, "");
                break;
            }
        }
        return result;
    }

    /// <summary>In-order DFS over the red-black directory subtree rooted at <paramref name="parent"/>.
    /// Left/RightSibling are the RB tree children, so both must be descended.</summary>
    private void Walk(List<StreamContent> acc, DirectoryEntry parent, string prefix)
    {
        var stack = new Stack<uint>();
        if (IsValidId(parent.Child)) stack.Push(parent.Child);
        var seen = new HashSet<uint>();
        while (stack.Count > 0)
        {
            uint id = stack.Pop();
            if (!seen.Add(id)) continue;
            var e = _bySlot[(int)id]!;
            string path = string.IsNullOrEmpty(prefix) ? e.Name : prefix + "/" + e.Name;
            if (e.IsStorage)
            {
                Walk(acc, e, path);
            }
            else if (e.ObjectType == CfbObjectType.Stream)
            {
                acc.Add(new StreamContent(path, ReadStream(e)));
            }
            if (IsValidId(e.RightSibling)) stack.Push(e.RightSibling);
            if (IsValidId(e.LeftSibling)) stack.Push(e.LeftSibling);
        }
    }

    private bool IsValidId(uint id)
        => id != CfbConstants.NoStream && id != CfbConstants.FreeSect
           && (int)id < _bySlot.Length && (int)id >= 0 && _bySlot[(int)id] != null;
}