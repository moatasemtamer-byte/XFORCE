// XLSX exporter — writes a real .xlsx (OOXML) workbook by hand using only
// System.IO.Compression, so there is NO third-party spreadsheet dependency
// (OpenXML / ClosedXML / EPPlus would each add several MB and, in EPPlus's
// case, a commercial licence).  Strings are written as inline strings, which
// keeps the package to the five minimal parts every reader accepts.

using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace CircuitReader.Core;

public static class XlsxExporter
{
    /// <summary>A single cell — either text or a number.</summary>
    public readonly record struct Cell(string? Text, double? Number)
    {
        public static Cell S(string? t) => new(t, null);
        public static Cell N(double n) => new(null, n);
    }

    public sealed record Sheet(
        string Name,
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyList<Cell>> Rows);

    // -------- convenience: the standard one-sheet "Rungs" workbook ----------

    public static void Write(IReadOnlyList<PageLadder> pages, string path) =>
        Write(new[] { RungsSheet(pages) }, path);

    public static Sheet RungsSheet(IReadOnlyList<PageLadder> pages)
    {
        var headers = new[] { "Page", "Rung", "Kind", "Output", "Label", "Equation", "Function", "Note" };
        var rows = new List<IReadOnlyList<Cell>>();
        foreach (var page in pages)
        {
            foreach (var r in page.Rungs)
            {
                var addr = r.Output?.Address ?? "";
                var label = page.AddressLabels.TryGetValue(addr, out var l) ? l : r.Output?.Label ?? "";
                rows.Add(new[]
                {
                    Cell.N(page.PageNumber),
                    Cell.N(r.Number),
                    Cell.S(KindString(r.Output?.Kind)),
                    Cell.S(addr),
                    Cell.S(label),
                    Cell.S(r.BooleanEquation),
                    Cell.S(r.FunctionEquation),
                    Cell.S(r.Note ?? ""),
                });
            }
        }
        return new Sheet("Rungs", headers, rows);
    }

    // --------------------------- the writer ---------------------------------

    public static void Write(IReadOnlyList<Sheet> sheets, string path)
    {
        if (sheets.Count == 0)
            sheets = new[] { new Sheet("Sheet1", Array.Empty<string>(), Array.Empty<IReadOnlyList<Cell>>()) };

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        AddEntry(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
        AddEntry(zip, "_rels/.rels", RootRels());
        AddEntry(zip, "xl/workbook.xml", Workbook(sheets));
        AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
        for (int i = 0; i < sheets.Count; i++)
            AddEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static string ContentTypes(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        for (int i = 0; i < sheetCount; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string RootRels() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string Workbook(IReadOnlyList<Sheet> sheets)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
        sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        for (int i = 0; i < sheets.Count; i++)
            sb.Append($"<sheet name=\"{XmlAttr(SanitizeSheetName(sheets[i].Name))}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private static string WorkbookRels(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < sheetCount; i++)
            sb.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string SheetXml(Sheet sheet)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        int rowNum = 1;
        if (sheet.Headers.Count > 0)
        {
            sb.Append($"<row r=\"{rowNum}\">");
            for (int c = 0; c < sheet.Headers.Count; c++)
                AppendCell(sb, Ref(c, rowNum), Cell.S(sheet.Headers[c]));
            sb.Append("</row>");
            rowNum++;
        }

        foreach (var row in sheet.Rows)
        {
            sb.Append($"<row r=\"{rowNum}\">");
            for (int c = 0; c < row.Count; c++)
                AppendCell(sb, Ref(c, rowNum), row[c]);
            sb.Append("</row>");
            rowNum++;
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendCell(StringBuilder sb, string reference, Cell cell)
    {
        if (cell.Number is { } n)
        {
            sb.Append($"<c r=\"{reference}\"><v>{n.ToString(CultureInfo.InvariantCulture)}</v></c>");
        }
        else
        {
            var text = cell.Text ?? "";
            if (text.Length == 0) { sb.Append($"<c r=\"{reference}\"/>"); return; }
            sb.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlText(text)}</t></is></c>");
        }
    }

    private static string Ref(int col, int row) => ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);

    private static string ColumnName(int index)
    {
        var sb = new StringBuilder();
        index++;
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            index = (index - 1) / 26;
        }
        return sb.ToString();
    }

    private static string SanitizeSheetName(string name)
    {
        var cleaned = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        if (cleaned.Length == 0) cleaned = "Sheet";
        return cleaned.Length > 31 ? cleaned.Substring(0, 31) : cleaned;
    }

    private static string XmlText(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default:
                    // Strip control chars that are illegal in XML 1.0.
                    if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') sb.Append(' ');
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string XmlAttr(string s) => XmlText(s).Replace("\"", "&quot;");

    private static string KindString(ElementKind? k) => k switch
    {
        ElementKind.Coil => "( )",
        ElementKind.Latch => "(L)",
        ElementKind.Unlatch => "(U)",
        ElementKind.Timer => "(T)",
        ElementKind.Compare => "[CMP]",
        ElementKind.LetAssign => "LET",
        ElementKind.ClippedCoil => "[clip]",
        _ => "?",
    };
}
