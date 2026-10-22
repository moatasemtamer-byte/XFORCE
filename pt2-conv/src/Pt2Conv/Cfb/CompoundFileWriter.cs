using System.Buffers.Binary;
using System.Text;

namespace Pt2Conv.Cfb;

/// <summary>A node in the directory tree we will serialize.</summary>
public sealed class CfbNode
{
    public string Name = "";
    public CfbObjectType Type;
    public byte[] Data = Array.Empty<byte>();
    public List<CfbNode> Children = new();
    public uint DirId = CfbConstants.NoStream;
    public uint StateBits;
    public uint StartSector = CfbConstants.EndOfChain;
    public ulong Size;

    public bool IsStorageOrRoot => Type is CfbObjectType.Storage or CfbObjectType.Root;
    public bool IsStreamWithData => Type == CfbObjectType.Stream && Data.Length > 0;
    public bool IsMini => IsStreamWithData && Data.Length < CfbConstants.MiniStreamCutoff;
}

/// <summary>
/// Writes a valid OLE/CFB document. Deterministic, self-contained.
/// Defaults to CFB v4 (4096-byte sectors) to match TriStation 5.8.0 exports;
/// pass <see cref="CfbConstants.SectorShift"/> to emit legacy v3 (512-byte sectors).
/// </summary>
public sealed class CompoundFileWriter
{
    private readonly List<CfbNode> _flat = new();
    private CfbNode _root = new();
    private long _miniStreamBytes;
    private int _numMiniSectors;
    private int _dataSectors;
    private int _dirSectors;
    private int _miniFatSectors;
    private int _fatSectors;
    private int _difatSectors;
    private int _totalSectors;
    private uint[] _fat = Array.Empty<uint>();

    private readonly ushort _sectorShift;
    private readonly int _sectorSize;
    private readonly int _entriesPerFatSector;
    private readonly int _entriesPerDifatSector;

    public CompoundFileWriter(ushort sectorShift = CfbConstants.V4SectorShift)
    {
        _sectorShift = sectorShift;
        _sectorSize = 1 << sectorShift;
        _entriesPerFatSector = _sectorSize / 4;
        _entriesPerDifatSector = _entriesPerFatSector - 1;
    }

    /// <summary>Byte offset of a data sector: sector N begins at (N+1)*SectorSize.</summary>
    private long SectorOffset(long sector) => (sector + 1) * _sectorSize;

    public static void Write(string outputPath, IEnumerable<(string Path, byte[] Data)> streams,
        ushort sectorShift = CfbConstants.V4SectorShift)
    {
        var root = BuildTree(streams);
        new CompoundFileWriter(sectorShift).WriteFile(outputPath, root);
    }

    public static CfbNode BuildTree(IEnumerable<(string Path, byte[] Data)> streams)
    {
        var root = new CfbNode { Name = "Root Entry", Type = CfbObjectType.Root };
        foreach (var (path, data) in streams)
        {
            var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var cur = root;
            for (int i = 0; i < segs.Length; i++)
            {
                bool last = i == segs.Length - 1;
                var child = cur.Children.FirstOrDefault(c =>
                    string.Equals(c.Name, segs[i], StringComparison.OrdinalIgnoreCase));
                if (child == null)
                {
                    child = new CfbNode
                    {
                        Name = segs[i],
                        Type = last ? CfbObjectType.Stream : CfbObjectType.Storage,
                    };
                    cur.Children.Add(child);
                }
                if (last)
                    child.Data = data;
                cur = child;
            }
        }
        return root;
    }

    private void WriteFile(string outputPath, CfbNode root)
    {
        _root = root;
        Flatten(root);

        // ---- plan sizes (iterate until FAT/DIFAT counts stabilise) ----
        long miniBytes = 0;
        int miniSectors = 0;
        int dataSectors = 0;
        foreach (var node in _flat)
        {
            if (!node.IsStreamWithData) continue;
            if (node.IsMini) miniSectors += SectorCount(node.Data.Length, CfbConstants.MiniSectorSize);
            else dataSectors += SectorCount(node.Data.Length, _sectorSize);
        }
        miniBytes = (long)miniSectors * CfbConstants.MiniSectorSize;
        dataSectors += SectorCount(miniBytes, _sectorSize);

        int dirSectors = SectorCount(_flat.Count * 128, _sectorSize);
        int miniFatSectors = miniSectors == 0 ? 0 : SectorCount(miniSectors * 4, _sectorSize);

        int fat = 1, difat = 0;
        while (true)
        {
            int total = dataSectors + dirSectors + miniFatSectors + fat + difat;
            int f = SectorCount(total * 4, _sectorSize);
            int df = f > 109 ? SectorCount(f - 109, _entriesPerDifatSector) : 0;
            if (f == fat && df == difat) break;
            fat = f;
            difat = df;
        }

        _miniStreamBytes = miniBytes;
        _numMiniSectors = miniSectors;
        _dataSectors = dataSectors;
        _dirSectors = dirSectors;
        _miniFatSectors = miniFatSectors;
        _fatSectors = fat;
        _difatSectors = difat;
        _totalSectors = dataSectors + dirSectors + miniFatSectors + fat + difat;

        // ---- allocate chains ----
        _fat = new uint[_totalSectors];
        Array.Fill(_fat, CfbConstants.FreeSect);

        int nextSector = 0;
        var regular = new Dictionary<CfbNode, (uint Start, int Count)>();
        foreach (var node in _flat)
        {
            if (!node.IsStreamWithData || node.IsMini) continue;
            int n = SectorCount(node.Data.Length, _sectorSize);
            node.StartSector = (uint)nextSector;
            node.Size = (ulong)node.Data.Length;
            regular[node] = ((uint)nextSector, n);
            LinkChain(ref nextSector, n);
        }

        uint miniStart = CfbConstants.EndOfChain;
        if (miniBytes > 0)
        {
            miniStart = (uint)nextSector;
            LinkChain(ref nextSector, SectorCount(miniBytes, _sectorSize));
            _root.StartSector = miniStart;
            _root.Size = (ulong)miniBytes;
        }

        uint dirStart = (uint)nextSector;
        LinkChain(ref nextSector, dirSectors);

        uint miniFatStart = CfbConstants.EndOfChain;
        if (miniFatSectors > 0)
        {
            miniFatStart = (uint)nextSector;
            LinkChain(ref nextSector, miniFatSectors);
        }

        var fatStarts = new uint[_fatSectors];
        for (int i = 0; i < _fatSectors; i++)
        {
            fatStarts[i] = (uint)nextSector;
            _fat[nextSector] = CfbConstants.Fsect;
            nextSector++;
        }

        var difatStarts = new uint[_difatSectors];
        for (int i = 0; i < _difatSectors; i++)
        {
            difatStarts[i] = (uint)nextSector;
            _fat[nextSector] = CfbConstants.Difsect;
            nextSector++;
        }

        if (nextSector != _totalSectors)
            throw new InvalidDataException($"Sector accounting mismatch: {nextSector} != {_totalSectors}");

        // ---- build mini stream + mini FAT ----
        byte[] miniStream = new byte[_miniStreamBytes];
        uint[] miniFat = new uint[_numMiniSectors];
        Array.Fill(miniFat, CfbConstants.FreeSect);
        {
            int cursor = 0;
            foreach (var node in _flat)
            {
                if (!node.IsMini) continue;
                int n = SectorCount(node.Data.Length, CfbConstants.MiniSectorSize);
                node.StartSector = (uint)cursor;
                node.Size = (ulong)node.Data.Length;
                for (int i = 0; i < n; i++)
                {
                    int dst = (cursor + i) * CfbConstants.MiniSectorSize;
                    int len = Math.Min(CfbConstants.MiniSectorSize, node.Data.Length - i * CfbConstants.MiniSectorSize);
                    Array.Copy(node.Data, i * CfbConstants.MiniSectorSize, miniStream, dst, len);
                    miniFat[cursor + i] = (i == n - 1) ? CfbConstants.EndOfChain : (uint)(cursor + i + 1);
                }
                cursor += n;
            }
            if (cursor != _numMiniSectors)
                throw new InvalidDataException("Mini-sector accounting mismatch");
        }

        // ---- mini FAT bytes ----
        byte[] miniFatBytes = new byte[_miniFatSectors * _sectorSize];
        for (int i = 0; i < _numMiniSectors; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(miniFatBytes.AsSpan(i * 4), miniFat[i]);

        // ---- FAT bytes ----
        byte[] fatBytes = new byte[_fatSectors * _sectorSize];
        for (int i = 0; i < _fat.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(fatBytes.AsSpan(i * 4), _fat[i]);

        // ---- directory entries ----
        byte[] dirBytes = new byte[_dirSectors * _sectorSize];
        SerializeDirectories(dirBytes);

        // ---- assemble file ----
        // The 512-byte header occupies the first sector-sized block, so the data
        // sectors begin at offset _sectorSize (SectorCount==1 for the header).
        long dataStart = _sectorSize;
        long fileSize = dataStart + (long)_totalSectors * _sectorSize;
        var file = new byte[fileSize];
        Span<byte> h = file;
        CfbConstants.Signature.CopyTo(file, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(24), 0x003E);
        ushort majorVersion = _sectorSize >= CfbConstants.V4SectorSize ? (ushort)4 : (ushort)3;
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(26), majorVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(28), 0xFFFE);
        h[30] = (byte)_sectorShift;
        h[32] = 6;
        BinaryPrimitives.WriteInt32LittleEndian(h.Slice(40), majorVersion == 4 ? _dirSectors : 0);
        BinaryPrimitives.WriteInt32LittleEndian(h.Slice(44), _fatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(48), dirStart);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(56), 4096);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(60), miniFatStart);
        BinaryPrimitives.WriteInt32LittleEndian(h.Slice(64), _miniFatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(68), _difatSectors > 0 ? difatStarts[0] : CfbConstants.EndOfChain);
        BinaryPrimitives.WriteInt32LittleEndian(h.Slice(72), _difatSectors);

        // DIFAT: header entries + DIFAT sectors
        for (int i = 0; i < 109; i++)
        {
            uint v = i < _fatSectors ? fatStarts[i] : CfbConstants.FreeSect;
            BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(76 + i * 4), v);
        }
        if (_difatSectors > 0)
        {
            int pos = 109;
            for (int s = 0; s < _difatSectors; s++)
            {
                long o = SectorOffset(difatStarts[s]);
                var sec = file.AsSpan((int)o, _sectorSize);
                int written = 0;
                while (written < _entriesPerDifatSector && pos < _fatSectors)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(sec.Slice(written * 4), fatStarts[pos]);
                    written++;
                    pos++;
                }
                for (int i = written; i < _entriesPerDifatSector; i++)
                    BinaryPrimitives.WriteUInt32LittleEndian(sec.Slice(i * 4), CfbConstants.FreeSect);
                uint next = (s == _difatSectors - 1) ? CfbConstants.EndOfChain : difatStarts[s + 1];
                BinaryPrimitives.WriteUInt32LittleEndian(sec.Slice(_entriesPerDifatSector * 4), next);
            }
        }

        CopyChainToFile(file, regular);
        if (miniBytes > 0)
            CopyToFile(file, miniStream, miniStart, (int)miniBytes);
        CopyToFile(file, dirBytes, dirStart, dirBytes.Length);
        CopyToFile(file, miniFatBytes, miniFatStart, miniFatBytes.Length);
        CopyToFile(file, fatBytes, fatStarts[0], fatBytes.Length);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(outputPath, file);
    }

    private void SerializeDirectories(byte[] dirBytes)
    {
        for (int i = 0; i < _flat.Count; i++)
            _flat[i].DirId = (uint)i;

        var left = new int[_flat.Count];
        var right = new int[_flat.Count];
        var color = new byte[_flat.Count];
        var childRoot = new uint[_flat.Count];
        Array.Fill(left, -1);
        Array.Fill(right, -1);
        Array.Fill(childRoot, CfbConstants.NoStream);

        BuildParentIndex(out var parentOf);

        // Each storage/root owns a red-black BST over its children, keyed by CFB name order.
        foreach (var node in _flat)
        {
            if (!node.IsStorageOrRoot || node.Children.Count == 0) continue;
            var sorted = node.Children
                .OrderBy(c => c.Name, CfbNameComparer.Instance)
                .ToList();
            var tree = new RbTree(sorted.Select(c => (int)c.DirId).ToArray());
            foreach (var (dirId, lnk) in tree.Result)
            {
                left[dirId] = lnk.Left;
                right[dirId] = lnk.Right;
                color[dirId] = lnk.Red ? (byte)0 : (byte)1;
            }
            childRoot[(int)node.DirId] = (uint)tree.RootDirId;
        }

        for (int i = 0; i < _flat.Count; i++)
        {
            var node = _flat[i];
            Span<byte> e = dirBytes.AsSpan(i * 128);
            byte[] name = Encoding.Unicode.GetBytes(node.Name);
            Array.Copy(name, 0, dirBytes, i * 128, Math.Min(name.Length, 62));
            BinaryPrimitives.WriteUInt16LittleEndian(e.Slice(64), (ushort)(name.Length + 2));
            e[66] = (byte)node.Type;
            e[67] = color[i];   // 0 = red, 1 = black
            BinaryPrimitives.WriteUInt32LittleEndian(e.Slice(68), left[i] < 0 ? CfbConstants.NoStream : (uint)left[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(e.Slice(72), right[i] < 0 ? CfbConstants.NoStream : (uint)right[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(e.Slice(76), node.IsStorageOrRoot ? childRoot[i] : CfbConstants.NoStream);
            BinaryPrimitives.WriteUInt32LittleEndian(e.Slice(96), node.StateBits);
            BinaryPrimitives.WriteInt64LittleEndian(e.Slice(100), 0);
            BinaryPrimitives.WriteInt64LittleEndian(e.Slice(108), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(e.Slice(116), node.StartSector);
            BinaryPrimitives.WriteUInt64LittleEndian(e.Slice(120), node.Size);
        }
    }

    private void BuildParentIndex(out Dictionary<CfbNode, CfbNode> parentOf)
    {
        parentOf = new Dictionary<CfbNode, CfbNode>();
        foreach (var n in _flat)
            foreach (var c in n.Children)
                parentOf[c] = n;
    }

    /// <summary>
    /// Classic CLRS red-black tree over the DirId keys, keyed by a supplied rank (CFB name order).
    /// Produces per-node left/right child DirIds (or -1) and red/black colours.
    /// </summary>
    private sealed class RbTree
    {
        private readonly int[] _key;      // rank -> dirId
        private readonly int[] _parent;
        private readonly int[] _left;
        private readonly int[] _right;
        private readonly bool[] _red;

        public int RootDirId { get; private set; } = -1;
        public Dictionary<int, (int Left, int Right, bool Red)> Result { get; } = new();

        public RbTree(int[] dirIdsByRank)
        {
            int n = dirIdsByRank.Length;
            _key = dirIdsByRank;
            _parent = new int[n];
            _left = new int[n];
            _right = new int[n];
            _red = new bool[n];
            Array.Fill(_parent, -1);
            Array.Fill(_left, -1);
            Array.Fill(_right, -1);

            for (int r = 0; r < n; r++) Insert(r);
            _red[RootDirId] = false;
            for (int r = 0; r < n; r++)
                Result[_key[r]] = (
                    _left[r] < 0 ? -1 : _key[_left[r]],
                    _right[r] < 0 ? -1 : _key[_right[r]],
                    _red[r]);
            RootDirId = _key[RootDirId];
        }

        private void Insert(int z)
        {
            int y = -1, x = RootDirId;
            while (x != -1)
            {
                y = x;
                x = z < x ? _left[x] : _right[x];
            }
            _parent[z] = y;
            if (y == -1) RootDirId = z;
            else if (z < y) _left[y] = z;
            else _right[y] = z;
            _red[z] = true;
            InsertFixup(z);
        }

        private void InsertFixup(int z)
        {
            while (_parent[z] != -1 && _red[_parent[z]])
            {
                int p = _parent[z], g = _parent[p];
                if (p == _left[g])
                {
                    int u = _right[g];
                    if (u != -1 && _red[u]) { _red[p] = false; _red[u] = false; _red[g] = true; z = g; }
                    else
                    {
                        if (z == _right[p]) { z = p; LeftRotate(z); p = _parent[z]; g = _parent[p]; }
                        _red[p] = false; _red[g] = true; RightRotate(g);
                    }
                }
                else
                {
                    int u = _left[g];
                    if (u != -1 && _red[u]) { _red[p] = false; _red[u] = false; _red[g] = true; z = g; }
                    else
                    {
                        if (z == _left[p]) { z = p; RightRotate(z); p = _parent[z]; g = _parent[p]; }
                        _red[p] = false; _red[g] = true; LeftRotate(g);
                    }
                }
            }
            _red[RootDirId] = false;
        }

        private void LeftRotate(int x)
        {
            int y = _right[x];
            _right[x] = _left[y];
            if (_left[y] != -1) _parent[_left[y]] = x;
            _parent[y] = _parent[x];
            if (_parent[x] == -1) RootDirId = y;
            else if (x == _left[_parent[x]]) _left[_parent[x]] = y;
            else _right[_parent[x]] = y;
            _left[y] = x;
            _parent[x] = y;
        }

        private void RightRotate(int x)
        {
            int y = _left[x];
            _left[x] = _right[y];
            if (_right[y] != -1) _parent[_right[y]] = x;
            _parent[y] = _parent[x];
            if (_parent[x] == -1) RootDirId = y;
            else if (x == _right[_parent[x]]) _right[_parent[x]] = y;
            else _left[_parent[x]] = y;
            _right[y] = x;
            _parent[x] = y;
        }
    }

    private void Flatten(CfbNode node)
    {
        _flat.Add(node);
        foreach (var c in node.Children)
            Flatten(c);
    }

    private CfbNode FindParent(CfbNode node)
    {
        foreach (var n in _flat)
            if (n.Children.Contains(node))
                return n;
        throw new InvalidDataException($"Node '{node.Name}' has no parent");
    }

    private static int SectorCount(long bytes, int unit)
        => (int)((bytes + unit - 1) / unit);

    private void LinkChain(ref int cursor, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var next = (i == count - 1) ? CfbConstants.EndOfChain : (uint)(cursor + i + 1);
            _fat[cursor + i] = next;
        }
        cursor += count;
    }

    private void CopyChainToFile(byte[] file, Dictionary<CfbNode, (uint Start, int Count)> chains)
    {
        foreach (var kv in chains)
        {
            var node = kv.Key;
            var (start, count) = kv.Value;
            for (int i = 0; i < count; i++)
            {
                long dst = SectorOffset(start + i);
                int len = Math.Min(_sectorSize, node.Data.Length - i * _sectorSize);
                Array.Copy(node.Data, i * _sectorSize, file, dst, len);
            }
        }
    }

    private void CopyToFile(byte[] file, byte[] src, uint startSector, int totalBytes)
    {
        if (totalBytes <= 0 || startSector == CfbConstants.EndOfChain) return;
        int n = (totalBytes + _sectorSize - 1) / _sectorSize;
        for (int i = 0; i < n; i++)
        {
            long dst = SectorOffset(startSector + i);
            int len = Math.Min(_sectorSize, totalBytes - i * _sectorSize);
            Array.Copy(src, i * _sectorSize, file, dst, len);
        }
    }

    /// <summary>MS-CFB directory ordering: shorter names first, then case-insensitive ordinal (uppercased).</summary>
    private sealed class CfbNameComparer : IComparer<string>
    {
        public static readonly CfbNameComparer Instance = new();
        public int Compare(string? a, string? b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;
            if (a.Length != b.Length) return a.Length - b.Length;
            return string.CompareOrdinal(a.ToUpperInvariant(), b.ToUpperInvariant());
        }
    }
}
