using System.Text;
using System.Text.RegularExpressions;

namespace TriconexToTia.Engine.Ladder;

/// <summary>
/// Reads the <c>PROGRAM=</c> grouping that a categorized TriStation ladder listing
/// (<c>.LPT</c>) carries on each network header line, e.g.
/// <c>Network #    23:        Size:   109 bytes PROGRAM=DIAGNOSTICS</c>.
/// </summary>
/// <remarks>
/// The grouping is a literal field in the listing, so when it is present the mapping is exact.
/// A network without its own <c>PROGRAM=</c> belongs to the most recent program seen above it.
/// When the listing carries no <c>PROGRAM=</c> at all (an uncategorized export) the result is
/// empty and the caller keeps the single-program behaviour.
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

    /// <summary>One network header in listing order. <paramref name="Program"/> is the name on the
    /// header itself (null when the header carries no tag).</summary>
    public sealed record Header(int Network, string? Program);

    /// <summary>Network headers in the order they appear, with the tags they carry.</summary>
    public static List<Header> Headers(byte[] bytes)
    {
        var text = Decode(bytes).Replace("\r\n", "\n").Replace('\r', '\n');
        var headers = new List<Header>();
        foreach (var line in text.Split('\n'))
        {
            var header = NetworkHeaderRe.Match(line);
            if (!header.Success) continue;
            if (!int.TryParse(header.Groups["n"].Value, out var network)) continue;
            var tag = ProgramTagRe.Match(header.Groups["tail"].Value);
            headers.Add(new Header(network, tag.Success ? tag.Groups["name"].Value : null));
        }
        return headers;
    }

    /// <summary>Maps each network number to its program name (empty when uncategorized).</summary>
    public static Dictionary<int, string> NetworkToProgram(byte[] bytes)
    {
        var map = new Dictionary<int, string>();
        string? current = null;
        foreach (var header in Headers(bytes))
        {
            if (header.Program is not null) current = header.Program;
            if (current is not null) map[header.Network] = current;
        }
        return map;
    }

    /// <summary>Program name for every header in listing order, carrying the previous tag forward.
    /// Used when network numbers repeat across programs, where a number-keyed map would collide.</summary>
    public static List<string?> ProgramSequence(byte[] bytes)
    {
        var result = new List<string?>();
        string? current = null;
        foreach (var header in Headers(bytes))
        {
            if (header.Program is not null) current = header.Program;
            result.Add(current);
        }
        return result;
    }

    /// <summary>
    /// Decodes a listing without corrupting non-ASCII bytes: BOM first, then strict UTF-8, then
    /// Latin-1 (a byte-preserving fallback). <see cref="Encoding.UTF8"/> alone never throws, so a
    /// Windows-1252 listing would silently turn every accented byte into U+FFFD.
    /// </summary>
    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
