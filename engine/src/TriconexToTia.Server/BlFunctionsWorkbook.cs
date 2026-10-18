using System.IO.Compression;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TriconexToTia.Server;

/// <summary>
/// Writes the minimal OOXML workbook that BL's Excel ("Siemens") reader looks for: a single
/// sheet named <c>Functions</c> whose column A holds the full logic equation and column B the
/// network number. BL reads every sheet whose name contains "Function", skips row 1 and turns
/// each following row into <c>colA@colB@colC@colD</c> (see <c>TXTManager.ReadSiemensParser</c>).
/// </summary>
/// <remarks>
/// Built by hand with <see cref="System.IO.Compression"/> so the engine needs no spreadsheet
/// dependency. Strings are written through a shared-string table because the EPPlus build BL
/// ships with does not surface inline strings.
/// </remarks>
internal static class BlFunctionsWorkbook
{
    public static void Write(IEnumerable<(string Logic, string Network)> rows, string path)
    {
        var data = rows.ToList();

        var shared = new List<string>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        int Idx(string value)
        {
            if (index.TryGetValue(value, out var existing)) return existing;
            var id = shared.Count;
            index[value] = id;
            shared.Add(value);
            return id;
        }

        var logicHeader = Idx("Logic");
        var networkHeader = Idx("Network Number");

        var sheet = new StringBuilder();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sheet.Append("<dimension ref=\"A1:B").Append(data.Count + 1).Append("\"/><sheetData>");
        sheet.Append("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>").Append(logicHeader).Append("</v></c>")
             .Append("<c r=\"B1\" t=\"s\"><v>").Append(networkHeader).Append("</v></c></row>");
        for (var i = 0; i < data.Count; i++)
        {
            var r = i + 2;
            sheet.Append("<row r=\"").Append(r).Append("\">");
            sheet.Append("<c r=\"A").Append(r).Append("\" t=\"s\"><v>").Append(Idx(data[i].Logic)).Append("</v></c>");
            sheet.Append("<c r=\"B").Append(r).Append("\" t=\"s\"><v>").Append(Idx(data[i].Network)).Append("</v></c>");
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        var sst = new StringBuilder();
        sst.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sst.Append("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"")
           .Append(shared.Count).Append("\" uniqueCount=\"").Append(shared.Count).Append("\">");
        foreach (var value in shared)
            sst.Append("<si><t xml:space=\"preserve\">").Append(Escape(value)).Append("</t></si>");
        sst.Append("</sst>");

        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
            "</Types>");
        Add(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        Add(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Functions\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Add(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
            "</Relationships>");
        Add(zip, "xl/sharedStrings.xml", sst.ToString());
        Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') sb.Append(' ');
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
