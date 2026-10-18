using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Pt2Conv;

/// <summary>
/// Best-effort loading of PLCopen XML that may be truncated (missing closing tags).
/// Tries XDocument.Load first; on failure, repairs unclosed elements and reloads.
/// </summary>
public static class XmlRepair
{
    public static XDocument Load(string path)
    {
        var text = File.ReadAllText(path);
        try
        {
            return XDocument.Parse(text, LoadOptions.None);
        }
        catch (XmlException)
        {
            var repaired = Repair(text);
            try
            {
                return XDocument.Parse(repaired, LoadOptions.None);
            }
            catch (XmlException second)
            {
                throw new InvalidDataException(
                    $"XML file '{path}' is malformed even after repair: {second.Message}", second);
            }
        }
    }

    /// <summary>
    /// Closes unclosed elements and drops a dangling partial tag at end-of-file.
    /// </summary>
    private static bool StartsAt(string s, int idx, string token)
    {
        if (idx < 0 || idx + token.Length > s.Length) return false;
        return string.CompareOrdinal(s, idx, token, 0, token.Length) == 0;
    }

    public static string Repair(string text)
    {
        var stack = new List<string>();
        int i = 0, len = text.Length;
        bool truncated = false;
        while (i < len)
        {
            int lt = text.IndexOf('<', i);
            if (lt < 0) break;
            // comment / cdata / processing instruction / doctype
            if (StartsAt(text, lt, "<!--"))
            {
                int end = text.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                if (end < 0) { truncated = true; i = len; break; }
                i = end + 3;
                continue;
            }
            if (StartsAt(text, lt, "<![CDATA["))
            {
                int end = text.IndexOf("]]>", lt + 9, StringComparison.Ordinal);
                if (end < 0) { truncated = true; i = len; break; }
                i = end + 3;
                continue;
            }
            if (StartsAt(text, lt, "<?"))
            {
                int end = text.IndexOf("?>", lt + 2, StringComparison.Ordinal);
                if (end < 0) { truncated = true; i = len; break; }
                i = end + 2;
                continue;
            }
            if (StartsAt(text, lt, "<!"))
            {
                int end = text.IndexOf('>', lt + 2);
                if (end < 0) { truncated = true; i = len; break; }
                i = end + 1;
                continue;
            }

            int nextLt = text.IndexOf('<', lt + 1);
            int gt = text.IndexOf('>', lt);
            if (gt < 0 || (nextLt >= 0 && nextLt < gt))
            {
                // unterminated opening tag: no '>' at all, or the next tag starts first
                truncated = true;
                i = lt;
                break;
            }
            // extract name
            int nameStart = lt + 1;
            if (nameStart >= len) break;
            if (text[nameStart] == '/')
            {
                int closeStart = nameStart + 1;
                int nameEnd = closeStart;
                while (nameEnd < len && !char.IsWhiteSpace(text[nameEnd]) && text[nameEnd] != '>') nameEnd++;
                string closeName = text.Substring(closeStart, nameEnd - closeStart);
                // pop matching
                for (int k = stack.Count - 1; k >= 0; k--)
                {
                    if (stack[k] == closeName)
                    {
                        stack.RemoveRange(k, 1);
                        break;
                    }
                }
                i = gt + 1;
                continue;
            }

            // opening tag
            int nStart = nameStart;
            int nEnd = nStart;
            while (nEnd < len && nEnd < gt && !char.IsWhiteSpace(text[nEnd]) && text[nEnd] != '>' && text[nEnd] != '/') nEnd++;
            string name = text.Substring(nStart, nEnd - nStart);
            bool selfClosing = text.IndexOf("/>", lt, gt - lt + 1, StringComparison.Ordinal) >= 0;
            if (!string.IsNullOrEmpty(name) && !selfClosing)
                stack.Add(name);
            i = gt + 1;
        }

        var sb = new StringBuilder(text.Length + 128);
        sb.Append(text, 0, i);
        for (int k = stack.Count - 1; k >= 0; k--)
        {
            sb.Append("</").Append(stack[k]).Append('>');
        }
        return sb.ToString();
    }
}