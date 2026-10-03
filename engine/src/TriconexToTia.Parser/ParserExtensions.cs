using System.Xml;
using System.Xml.Linq;

namespace TriconexToTia.Parser;

/// <summary>Small helpers for tolerant, case-insensitive XML traversal.</summary>
internal static class ParserExtensions
{
    /// <summary>Element names are compared case-insensitively and ignoring '_' / '-'.</summary>
    public static bool IsNamed(this XElement element, params string[] names)
    {
        var local = Normalize(element.Name.LocalName);
        foreach (var name in names)
        {
            if (string.Equals(local, Normalize(name), StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public static string Normalize(string value) =>
        new string(value.Where(c => c != '_' && c != '-').ToArray()).ToLowerInvariant();

    public static string? Attr(this XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var target = Normalize(name);
            var attribute = element.Attributes()
                .FirstOrDefault(a => Normalize(a.Name.LocalName) == target);
            if (attribute is not null && !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return attribute.Value.Trim();
            }
        }
        return null;
    }

    public static string? ChildValue(this XElement element, params string[] names)
    {
        foreach (var child in element.Elements())
        {
            if (child.IsNamed(names) && !child.HasElements)
            {
                var value = child.Value.Trim();
                if (value.Length > 0) return value;
            }
        }
        return null;
    }

    public static XElement? Child(this XElement element, params string[] names) =>
        element.Elements().FirstOrDefault(child => child.IsNamed(names));

    public static int Line(this XObject node) =>
        node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : -1;

    public static string ParentPath(this XElement element)
    {
        var segments = new List<string>();
        for (var current = element.Parent; current is not null; current = current.Parent)
        {
            segments.Add(current.Name.LocalName);
        }
        segments.Reverse();
        return string.Join("/", segments);
    }

    public static bool IsDescendantOf(this XElement element, XElement ancestor, params string[] names)
    {
        for (var current = element.Parent; current is not null; current = current.Parent)
        {
            if (current == ancestor) return false;
            if (current.IsNamed(names)) return true;
        }
        return false;
    }
}
