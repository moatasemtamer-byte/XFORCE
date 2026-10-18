using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// Validates the MS-CFB directory red-black tree: BST ordering by CFB name rules,
/// root black, no red-red parent/child, equal black-height on every path, and that
/// every referenced slot is a live entry with no aliasing.
/// </summary>
public static class RbTreeCheck
{
    private static CompoundFile _cf = null!;
    private static int _problems;
    private static int _visited;
    private static readonly Dictionary<uint, string> Owner = new();

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: rbtree <file>"); return 1; }
        _cf = new CompoundFile(args[1]);
        var root = _cf.Entries.FirstOrDefault(e => e.IsRoot);
        if (root == null) { Console.WriteLine("no root entry"); return 1; }

        if (IsValid(root.Child)) Walk(root.Child, "");

        int live = _cf.Entries.Count(e => !e.IsRoot && (e.IsStorage || e.ObjectType == CfbObjectType.Stream));
        Console.WriteLine($"referenced entries: {_visited}; live non-root entries: {live}");
        if (_visited != live)
        {
            Console.WriteLine($"  WARNING: {live - _visited} live entries unreachable");
            _problems++;
        }
        Console.WriteLine(_problems == 0 ? "RB-TREE: VALID" : $"RB-TREE: {_problems} PROBLEM(S)");
        return _problems == 0 ? 0 : 4;
    }

    private static int Walk(uint slot, string parentPath)
    {
        var e = _cf.EntryBySlot(slot)!;
        string path = string.IsNullOrEmpty(parentPath) ? e.Name : parentPath + "/" + e.Name;
        if (Owner.TryGetValue(slot, out var prev))
        {
            Console.WriteLine($"  ALIAS: slot {slot} ({path}) already owned by {prev}");
            _problems++;
            return 0;
        }
        Owner[slot] = path;
        _visited++;

        if (e.Color == 0 && _cf.EntryBySlot(e.LeftSibling)?.Color == 0)
        { Console.WriteLine($"  RED-RED left: {path}"); _problems++; }
        if (e.Color == 0 && _cf.EntryBySlot(e.RightSibling)?.Color == 0)
        { Console.WriteLine($"  RED-RED right: {path}"); _problems++; }

        bool hasL = IsValid(e.LeftSibling);
        bool hasR = IsValid(e.RightSibling);
        int bhL = hasL ? Walk(e.LeftSibling, parentPath) : 1;
        int bhR = hasR ? Walk(e.RightSibling, parentPath) : 1;

        if (hasL && Cmp(_cf.EntryBySlot(e.LeftSibling)!.Name, e.Name) >= 0)
        { Console.WriteLine($"  BST L: {_cf.EntryBySlot(e.LeftSibling)!.Name} !< {e.Name}"); _problems++; }
        if (hasR && Cmp(_cf.EntryBySlot(e.RightSibling)!.Name, e.Name) <= 0)
        { Console.WriteLine($"  BST R: {_cf.EntryBySlot(e.RightSibling)!.Name} !> {e.Name}"); _problems++; }

        if (bhL != bhR)
        {
            Console.WriteLine($"  BLACK-HEIGHT: {path} left={bhL} right={bhR}");
            _problems++;
        }

        if (e.IsStorage && IsValid(e.Child))
        {
            var child = _cf.EntryBySlot(e.Child)!;
            if (child.Color != 1)
            {
                Console.WriteLine($"  STORAGE CHILD NOT BLACK: {path}/{child.Name} color={child.Color}");
                _problems++;
            }
            Walk(e.Child, path);
        }
        return bhL + (e.Color == 1 ? 1 : 0);
    }

    private static bool IsValid(uint id)
        => id != CfbConstants.NoStream && id != CfbConstants.FreeSect
           && (int)id < _cf.Entries.Count && _cf.EntryBySlot(id) != null;

    private static int Cmp(string a, string b)
    {
        if (a.Length != b.Length) return a.Length - b.Length;
        return string.CompareOrdinal(a.ToUpperInvariant(), b.ToUpperInvariant());
    }
}
