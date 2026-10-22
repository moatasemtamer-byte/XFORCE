using System.Text;
using System.Text.RegularExpressions;

namespace TriconexToTia.Engine.Ladder;

/// <summary>
/// Reads the <c>PROGRAM=</c> grouping that a categorized TriStation ladder listing
/// (<c>.LPT</c>) carries on each network header line, e.g.
/// <c>Network #    23:        Size:   109 bytes PROGRAM=DIAGNOSTICS</c>.
/// </summary>
/// <remarks>
/// The grouping is a literal field in the listing, so when it is present the mapping is
/// exact — no inference is performed. A network without its own <c>PROGRAM=</c> belongs to
/// the most recent program seen above it. When the listing carries no <c>PROGRAM=</c> at all
/// (an uncategorized export) the result is empty and the caller keeps the existing
/// single-program behaviour.
/// </remarks>
public static class LptProgramMap
{
    // "Network # 23: Size: 109 bytes PROGRAM=DIAGNOSTICS" (spacing is column-aligned and
    // varies, so the whitespace is matched loosely).
    private static readonly Regex NetworkHeaderRe = new(
        @"Network\s*#\s*(?<n>\d+)\s*:\s*Size\s*:\s*(?<size>\d+)\s*bytes(?<tail>[^\r\n]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProgramTagRe = new(
        @"PROGRAM\s*=\s*(?<name>[A-Za-z0-9_\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Group(string Name, int FirstNetwork, int LastNetwork);

    public static bool HasProgramTags(byte[] bytes) =>
        ProgramTagRe.IsMatch(Decode(bytes));

    /// <summary>Consecutive network ranges, in order, each under one program name.</summary>
    public static List<Group> Groups(byte[] bytes)
    {
        var text = Decode(bytes).Replace("\r\n", "\n").Replace('\r', '\n');
        var groups = new List<Group>();
        string? current = null;
        int first = 0, last = 0;

        foreach (var line in text.Split('\n'))
        {
            var header = NetworkHeaderRe.Match(line);
            if (!header.Success) continue;
            int network = int.Parse(header.Groups["n"].Value);
            var tag = ProgramTagRe.Match(header.Groups["tail"].Value);
            if (!tag.Success) continue; // no label on this network: stays with current group
            var name = tag.Groups["name"].Value;

            if (current is null)
            {
                current = name;
                first = network;
                last = network;
            }
            else if (!name.Equals(current, StringComparison.Ordinal))
            {
                groups.Add(new Group(current, first, last));
                current = name;
                first = network;
                last = network;
            }
            else
            {
                last = network;
            }
        }

        if (current is not null) groups.Add(new Group(current, first, last));
        return groups;
    }

    /// <summary>Maps each network number to its program name (empty when uncategorized).</summary>
    public static Dictionary<int, string> NetworkToProgram(byte[] bytes)
    {
        var text = Decode(bytes).Replace("\r\n", "\n").Replace('\r', '\n');
        var map = new Dictionary<int, string>();
        string? current = null;
        foreach (var line in text.Split('\n'))
        {
            var header = NetworkHeaderRe.Match(line);
            if (!header.Success) continue;
            var tag = ProgramTagRe.Match(header.Groups["tail"].Value);
            if (tag.Success) current = tag.Groups["name"].Value;
            if (current is not null) map[int.Parse(header.Groups["n"].Value)] = current;
        }
        return map;
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return Encoding.ASCII.GetString(bytes); }
    }
}
