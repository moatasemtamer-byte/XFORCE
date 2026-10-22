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
        var contents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in context.Generated)
        {
            var path = file.RelativePath.Replace('\\', '/');
            contents[path] = file.Content ?? string.Empty;

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
            .GroupBy(entry => entry.Phase)
            .OrderBy(group => group.Key)
            .SelectMany(group => SortPhase(group.ToList(), contents))
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

    /// <summary>
    /// Orders one phase. Alphabetical order is only a tie-breaker: a source that instantiates
    /// another block of the same phase (an instance declaration such as <c>AOUT_2 : AOUT;</c>)
    /// has to come after that block, otherwise TIA rejects the type as unknown. This happens for
    /// real, e.g. <c>AI_COND</c> precedes <c>AOUT</c> alphabetically but needs it to exist first.
    /// The same holds for a call in the body: <c>AOUT</c> calls <c>ReportBadParam</c>, so the
    /// stand-in has to be imported before it.
    /// </summary>
    private static List<Entry> SortPhase(List<Entry> phase, IReadOnlyDictionary<string, string> contents)
    {
        if (phase.Count <= 1) return phase;

        var pathByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in phase)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(entry.Path);
            if (!string.IsNullOrEmpty(name) && !pathByName.ContainsKey(name))
            {
                pathByName[name] = entry.Path;
            }
        }

        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in phase)
        {
            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var self = System.IO.Path.GetFileNameWithoutExtension(entry.Path);
            contents.TryGetValue(entry.Path, out var text);

            foreach (Match match in InstanceDeclaration().Matches(text ?? string.Empty))
            {
                var type = match.Groups["type"].Value;
                if (!pathByName.TryGetValue(type, out var target)) continue;
                if (string.Equals(target, entry.Path, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(type, self, StringComparison.OrdinalIgnoreCase)) continue;
                missing.Add(target);
            }

            // A block can be needed by a name that only ever appears in the body: the FBD/LD
            // emitter lowers a call to a stand-in (ReportBadParam, PACK16, _GetTimer) into a plain
            // SCL call, so an instance declaration says nothing about it. Without this, a block is
            // imported before the function it calls and TIA reports "Tag X not defined".
            foreach (Match match in CallSite().Matches(text ?? string.Empty))
            {
                var name = match.Groups["name"].Value;
                if (!pathByName.TryGetValue(name, out var target)) continue;
                if (string.Equals(target, entry.Path, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, self, StringComparison.OrdinalIgnoreCase)) continue;
                missing.Add(target);
            }

            dependencies[entry.Path] = missing;
        }

        var ordered = new List<Entry>(phase.Count);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = new List<Entry>(phase);

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(entry => dependencies[entry.Path].All(dependency =>
                    done.Contains(dependency) || !dependencies.ContainsKey(dependency)))
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Path, StringComparer.Ordinal)
                .ToList();

            if (ready.Count == 0)
            {
                // A dependency cycle can never be satisfied; fall back to the stable alphabetical
                // order so the manifest is still deterministic and the report flags the block.
                ordered.AddRange(remaining.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Path, StringComparer.Ordinal));
                break;
            }

            foreach (var entry in ready)
            {
                ordered.Add(entry);
                done.Add(entry.Path);
                remaining.Remove(entry);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Matches a declaration that can give a block instance, e.g. <c>AOUT_2 : AOUT;</c>. A type the
    /// emitter resolved to a user-defined block arrives quoted (<c>RS_01 : "RS";</c>), so the quotes
    /// are optional here; without them the sort misses the dependency and TIA rejects the instance
    /// as an unknown data type.
    /// </summary>
    private static Regex InstanceDeclaration() => new(
        @"(?m)^\s*[A-Za-z_][A-Za-z0-9_]*\s*:\s*""?(?<type>[A-Za-z_][A-Za-z0-9_]*)""?\s*;",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches a call site such as <c>ReportBadParam(0)</c>. Only names that are also a block of
    /// this phase count as a dependency, so prose in the source comments and SCL system functions
    /// such as <c>REAL_TO_DINT</c> are ignored.
    /// </summary>
    private static Regex CallSite() => new(
        @"(?<![\w.""'])(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);

    private static bool EndsWith(string path, string suffix) =>
        path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private readonly record struct Entry(int Phase, string Path, string Kind);
}