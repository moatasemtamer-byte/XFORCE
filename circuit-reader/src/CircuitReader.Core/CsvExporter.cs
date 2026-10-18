// CSV exporter — produces a UTF-8 BOM-prefixed .csv that Excel opens
// directly via double-click.  No extra NuGet dependencies (OpenXml /
// ClosedXML / EPPlus would all bloat the bundle by 5+ MB and require
// licence acceptance).  CSV gives the user the same data, instantly
// editable in Excel, and copy-paste-able into any spreadsheet.

using System.Globalization;
using System.Text;

namespace CircuitReader.Core;

public static class CsvExporter
{
    /// <summary>
    /// Write one row per rung to <paramref name="path"/>.  Columns:
    ///   Page, Rung, Kind, Output, Label, Equation, Note.
    /// The label/equation/note cells are properly escaped so Excel doesn't
    /// split on stray commas / quotes / newlines.
    /// </summary>
    public static void Write(IReadOnlyList<PageLadder> pages, string path)
    {
        // UTF-8 *with* BOM — Excel auto-detects the encoding from the BOM
        // and renders Unicode (the "—" / "≥" / Arabic etc.) correctly.
        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        using var sw = new StreamWriter(path, false, enc);
        sw.WriteLine("Page,Rung,Kind,Output,Label,Equation,Function,Note");
        foreach (var page in pages)
        {
            foreach (var r in page.Rungs)
            {
                var addr = r.Output?.Address ?? "";
                var label = page.AddressLabels.TryGetValue(addr, out var l)
                                ? l : r.Output?.Label ?? "";
                var kind = KindString(r.Output?.Kind);
                sw.Write(page.PageNumber.ToString(CultureInfo.InvariantCulture));
                sw.Write(',');
                sw.Write(r.Number.ToString(CultureInfo.InvariantCulture));
                sw.Write(',');
                sw.Write(Esc(kind));
                sw.Write(',');
                sw.Write(Esc(addr));
                sw.Write(',');
                sw.Write(Esc(label));
                sw.Write(',');
                sw.Write(Esc(r.BooleanEquation));
                sw.Write(',');
                sw.Write(Esc(r.FunctionEquation));
                sw.Write(',');
                sw.Write(Esc(r.Note ?? ""));
                sw.Write('\n');
            }
        }
    }

    private static string KindString(ElementKind? k) => k switch
    {
        ElementKind.Coil => "( )",
        ElementKind.Latch => "(L)",
        ElementKind.Unlatch => "(U)",
        ElementKind.Timer => "(T)",
        ElementKind.LetAssign => "LET",
        ElementKind.ClippedCoil => "[clip]",
        _ => "?",
    };

    /// <summary>
    /// Escape a value per RFC 4180 — quote when the value contains a
    /// comma, double-quote, CR or LF; double-up any embedded quotes.
    /// </summary>
    private static string Esc(string? v)
    {
        if (string.IsNullOrEmpty(v)) return string.Empty;
        bool needsQuotes = v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!needsQuotes) return v;
        var sb = new StringBuilder(v.Length + 2);
        sb.Append('"');
        foreach (var c in v)
        {
            if (c == '"') sb.Append("\"\"");
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }
}
