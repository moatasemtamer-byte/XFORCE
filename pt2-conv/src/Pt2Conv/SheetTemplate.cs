using System.Text;
using System.Xml.Linq;

namespace Pt2Conv;

/// <summary>
/// One drawable primitive of a TriStation sheet template: a border box or a text run.
/// Coordinates are template units with the origin at the sheet's top-left corner and y
/// pointing down -- the same space the FBD content is exported in (1 unit = 1/10 of a
/// TriStation internal unit).
/// </summary>
public sealed class TemplateElem
{
    public double X, Y, W, H;
    /// <summary>Draw the border rectangle (a boxed cell of the title block).</summary>
    public bool Box;
    /// <summary>Literal text; may still contain %MACRO placeholders.</summary>
    public string Text = "";
    /// <summary>The template's text size (TriStation points, not template units).</summary>
    public double Size = 10;
    /// <summary>Left | Center | Right.</summary>
    public string Align = "Left";
    public bool ExpandMacros;
}

/// <summary>
/// A TriStation sheet template, as stored in Current/Elements/SheetTemplate*/*.BKG.
///
/// A template holds two background sheets: background 0 carries the full title block,
/// background 1 the compact one (~60 % of the full block). Both are anchored at the
/// bottom-right corner of the drawing area, so the sheet itself is the template's
/// extent plus the 30 unit frame band that surrounds the drawing area.
/// </summary>
public sealed class SheetTemplate
{
    public string Name = "";
    public string Size = "D";
    public int HZones = 16;
    public int VZones = 8;
    /// <summary>The sheet size in template units (the frame's outer edge).</summary>
    public double SheetW;
    public double SheetH;
    /// <summary>The frame band between the sheet's edge and the drawing area.</summary>
    public double Margin = 30;
    /// <summary>One entry per background sheet, in template order.</summary>
    public List<List<TemplateElem>> Backgrounds = new();

    public bool Portrait => SheetH > SheetW;

    public static SheetTemplate? Parse(byte[] data, string name)
    {
        try
        {
            return Parse(XDocument.Parse(Encoding.UTF8.GetString(data)), name);
        }
        catch
        {
            return null;
        }
    }

    public static SheetTemplate? Parse(XDocument doc, string name)
    {
        var root = doc.Root;
        if (root == null) return null;

        var t = new SheetTemplate
        {
            Name = name,
            Size = (string?)root.Attribute("drawingSize") ?? "D",
            HZones = Int(root, "hZoneCount", 16),
            VZones = Int(root, "vZoneCount", 8),
        };

        var sheets = root.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Collection" && (string?)e.Attribute("Name") == "sheets");
        if (sheets == null) return null;

        foreach (var sheet in sheets.Elements().Where(e => e.Name.LocalName == "Class"))
        {
            var displayables = sheet.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Collection" && (string?)e.Attribute("Name") == "displayables");
            var list = new List<TemplateElem>();
            if (displayables != null)
            {
                foreach (var d in displayables.Elements().Where(e => e.Name.LocalName == "Class"))
                    ParseElem(d, 0, 0, list);
            }
            t.Backgrounds.Add(list);
        }

        // The title block sits flush with the drawing area's bottom-right corner; the
        // sheet adds the frame band around it. So the sheet is the largest extent of
        // the template plus that band.
        double maxX = 0, maxY = 0;
        foreach (var bg in t.Backgrounds)
        {
            foreach (var e in bg)
            {
                if (e.X + e.W > maxX) maxX = e.X + e.W;
                if (e.Y + e.H > maxY) maxY = e.Y + e.H;
            }
        }
        t.SheetW = maxX + t.Margin;
        t.SheetH = maxY + t.Margin;
        return t;
    }

    /// <summary>
    /// Flattens one displayable into primitives. A displayable that captures a nested
    /// sheet (a group, e.g. the title block) contributes its own border box plus the
    /// contents of the group, whose coordinates are relative to the group's origin.
    /// </summary>
    private static void ParseElem(XElement el, double ox, double oy, List<TemplateElem> outElems, bool parentExpand = false)
    {
        double x = Num(el, "posX") + ox;
        double y = Num(el, "posY") + oy;
        double w = Num(el, "width");
        double h = Num(el, "height");
        string border = Str(el, "border", "None");
        string align = Str(el, "textAlignment", "Left");
        double size = Num(el, "textSize", 10);
        // The macro flag of the group wins: a title block that expands its macros
        // expands the macros of every text inside it.
        bool expand = parentExpand
            || Str(el, "expandMacros", "False").Equals("True", StringComparison.OrdinalIgnoreCase);
        string text = Text(el);

        if (border != "None")
            outElems.Add(new TemplateElem { X = x, Y = y, W = w, H = h, Box = true, Size = size, Align = align });
        if (text.Length > 0)
            outElems.Add(new TemplateElem { X = x, Y = y, W = w, H = h, Text = text, Size = size, Align = align, ExpandMacros = expand });

        var group = el.Elements()
            .FirstOrDefault(c => c.Name.LocalName == "Class" && (string?)c.Attribute("Embed") == "captured");
        if (group == null) return;

        var nested = group.Descendants()
            .FirstOrDefault(c => c.Name.LocalName == "Collection" && (string?)c.Attribute("Name") == "displayables");
        if (nested == null) return;
        foreach (var child in nested.Elements().Where(e => e.Name.LocalName == "Class"))
            ParseElem(child, x, y, outElems, expand);
    }

    private static string Text(XElement el)
    {
        var cdata = el.Elements().FirstOrDefault(e => e.Name.LocalName == "CData" && (string?)e.Attribute("Name") == "text");
        if (cdata == null) return "";
        return cdata.Value.Replace("\r\n", "\n").Trim('\n');
    }

    private static double Num(XElement el, string name, double fallback = 0)
    {
        var a = el.Attribute(name);
        return a != null && double.TryParse(a.Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }

    private static int Int(XElement el, string name, int fallback)
    {
        var a = el.Attribute(name);
        return a != null && int.TryParse(a.Value, out int v) ? v : fallback;
    }

    private static string Str(XElement el, string name, string fallback)
    {
        return (string?)el.Attribute(name) ?? fallback;
    }
}
