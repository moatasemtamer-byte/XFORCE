using System.Security;
using System.Text;

namespace TriconexToTia.Generators;

internal static class GenerationHelpers
{
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unnamed";
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }
        return builder.ToString().Trim();
    }

    public static string EscapeXml(string? value) =>
        SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    public static string Indent(string code, int spaces)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;
        var pad = new string(' ', spaces);
        var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            builder.Append(line.Length == 0 ? string.Empty : pad + line);
            if (index < lines.Length - 1) builder.Append('\n');
        }
        return builder.ToString();
    }

    public static string StripPlaceholderQuotes(string mapped) =>
        mapped.Length >= 2 && mapped.StartsWith('"') && mapped.EndsWith('"')
            ? mapped[1..^1]
            : mapped;
}
