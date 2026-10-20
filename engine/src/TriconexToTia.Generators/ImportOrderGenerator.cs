using System.Text;
using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>
/// Publishes <c>ImportOrder.json</c>: every block source the other generators produced, listed in
/// the order TIA must import them. A source can only reference a block that already exists, so the
/// order is: library (support) FBs, the global data block, user FBs and FCs, wrapper FBs, the
/// instance-DB SimaticML files, and finally the organization blocks. UDTs, tags and the report are
/// not listed because the harness imports those in their own phases.
/// </summary>
public sealed class ImportOrderGenerator : IGenerator
{
    public string Id => "import-order";

    public string Description => "Ordered block import manifest";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        var entries = new List<Entry>();

        foreach (var file in context.Generated)
        {
            var path = file.RelativePath.Replace('\\', '/');

            if (path.StartsWith("Library/", StringComparison.OrdinalIgnoreCase) && EndsWith(path, ".scl"))
            {
                entries.Add(new Entry(0, path, "scl"));
            }
            else if (path.StartsWith("Globals/", StringComparison.OrdinalIgnoreCase) && EndsWith(path, ".db"))
            {
                entries.Add(new Entry(1, path, "scl"));
            }
            else if (path.StartsWith("Blocks/", StringComparison.OrdinalIgnoreCase) && EndsWith(path, ".xml"))
            {
                // SimaticML block documents carry their element in the content: a user FB/FC must
                // exist before the instance DBs and OBs that reference it, an FBD organization block
                // goes last like its SCL twin, and an instance DB sits between its FB and the OBs.
                var xml = file.Content ?? string.Empty;
                if (xml.Contains("<SW.Blocks.FB", StringComparison.Ordinal)
                    || xml.Contains("<SW.Blocks.FC", StringComparison.Ordinal))
                {
                    entries.Add(new Entry(2, path, "xml"));
                }
                else if (xml.Contains("<SW.Blocks.OB", StringComparison.Ordinal))
                {
                    entries.Add(new Entry(5, path, "xml"));
                }
                else
                {
                    entries.Add(new Entry(4, path, "xml"));
                }
            }
            else if (path.StartsWith("Blocks/", StringComparison.OrdinalIgnoreCase) && EndsWith(path, "_WRAP.scl"))
            {
                entries.Add(new Entry(3, path, "scl"));
            }
            else if (path.StartsWith("Blocks/", StringComparison.OrdinalIgnoreCase) && EndsWith(path, ".scl"))
            {
                // An organization block is imported after the wrappers and their instance DBs;
                // every other block source here is a user FB/FC and comes before them.
                var phase = (file.Content ?? string.Empty).Contains("ORGANIZATION_BLOCK", StringComparison.Ordinal) ? 5 : 2;
                entries.Add(new Entry(phase, path, "scl"));
            }
        }

        if (entries.Count == 0) yield break;

        var ordered = entries
            .OrderBy(entry => entry.Phase)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("  \"order\": [");
        for (var index = 0; index < ordered.Count; index++)
        {
            var entry = ordered[index];
            builder.Append("    { \"path\": \"").Append(entry.Path)
                .Append("\", \"kind\": \"").Append(entry.Kind).Append('"')
                .Append(index + 1 < ordered.Count ? " }," : " }")
                .AppendLine();
        }

        builder.AppendLine("  ]");
        builder.AppendLine("}");

        yield return new GeneratedFile
        {
            RelativePath = "ImportOrder.json",
            Content = builder.ToString(),
            Kind = ArtifactKind.Package,
            EmitBom = false,
        };
    }

    private static bool EndsWith(string path, string suffix) =>
        path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private readonly record struct Entry(int Phase, string Path, string Kind);
}