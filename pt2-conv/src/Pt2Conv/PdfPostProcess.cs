using System.Text;
using System.Text.RegularExpressions;

namespace Pt2Conv;

/// <summary>
/// Makes generated PDFs reproducible. PDFsharp tags each embedded font subset with a random
/// six-letter prefix (e.g. <c>RCKWTS+Arial</c>), so two prints of the same project differ in
/// those bytes even though every page is identical. The tag is exactly six characters, so it
/// can be rewritten in place without touching any offset, length or stream.
/// </summary>
public static class PdfPostProcess
{
    public static void NormalizeFontSubsetTags(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text = Encoding.Latin1.GetString(bytes);
        var prefixes = Regex.Matches(text, @"/([A-Z]{6})\+(?=[A-Za-z])")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (prefixes.Count > 0)
            foreach (string prefix in prefixes)
                text = text.Replace("/" + prefix + "+", "/PT2FNT+", StringComparison.Ordinal);

        // The trailer carries a random document id; make it deterministic too (same length).
        const string id = "505432434F4E56000000000000000000";
        text = Regex.Replace(text,
            @"(/ID\s*\[\s*)<[0-9A-Fa-f]+>(\s*)<[0-9A-Fa-f]+>(\s*\])",
            m => m.Groups[1].Value + "<" + id + ">" + m.Groups[2].Value + "<" + id + ">" + m.Groups[3].Value);

        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(text));
    }
}
