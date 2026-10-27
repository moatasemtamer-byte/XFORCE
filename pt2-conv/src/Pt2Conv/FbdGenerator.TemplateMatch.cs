using System.Text;

namespace Pt2Conv;

public static partial class FbdGenerator
{
    /// <summary>
    /// True when a freshly generated FBD document carries the same content as the template
    /// stream (same sheets/items/geometry/pins/text), ignoring editor-only bookkeeping such
    /// as localId/connectId/network/snapEventId. In that case the caller can keep the
    /// template bytes, preserving the exact ids/network handles/order the editor stored.
    /// </summary>
    public static bool ContentMatchesTemplate(string generated, string template)
    {
        try
        {
            string a = ItemSignature(generated);
            string b = ItemSignature(template);
            if (a == b) return true;
            if (Environment.GetEnvironmentVariable("PT2_MATCHDBG") == "1")
            {
                var la = a.Split('\u0001');
                var lb = b.Split('\u0001');
                Console.Error.WriteLine($"[match] items gen={la.Length} tmpl={lb.Length}");
                int n = 0;
                for (int i = 0; i < Math.Min(la.Length, lb.Length) && n < 6; i++)
                {
                    if (la[i] == lb[i]) continue;
                    n++;
                    Console.Error.WriteLine("  GEN : " + la[i]);
                    Console.Error.WriteLine("  TMPL: " + lb[i]);
                }
            }
            return false;
        }
        catch { return false; }
    }

    private static string ItemSignature(string fbd)
    {
        var items = new List<string>();
        int i = 0;
        while (i < fbd.Length)
        {
            if (fbd.AsSpan(i).StartsWith("<Class ", StringComparison.Ordinal))
            {
                int te = fbd.IndexOf('>', i);
                if (te < 0) break;
                string head = fbd.Substring(i + 7, te - i - 7).TrimEnd('/').Trim();
                var attrs = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                             head, "([A-Za-z0-9_]+)=\"([^\"]*)\""))
                {
                    string k = m.Groups[1].Value;
                    if (k is "localId" or "connectId" or "outConnectId" or "inConnectId" or "network" or "snapEventId")
                        continue;
                    attrs[k] = m.Groups[2].Value;
                }
                var sb = new StringBuilder();
                sb.Append('<');
                foreach (var kv in attrs) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
                sb.Append('>');
                items.Add(sb.ToString());
                i = te + 1;
                continue;
            }
            if (fbd.AsSpan(i).StartsWith("<CData ", StringComparison.Ordinal))
            {
                int ce = fbd.IndexOf("</CData>", i, StringComparison.Ordinal);
                if (ce < 0) break;
                items.Add("T:" + fbd.Substring(i, ce - i));
                i = ce + 8;
                continue;
            }
            i++;
        }
        items.Sort(StringComparer.Ordinal);
        return string.Join("\u0001", items);
    }
}
