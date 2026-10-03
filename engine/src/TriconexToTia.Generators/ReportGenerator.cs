using System.Text;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>Builds conversion_report.txt from the full pipeline result.</summary>
public sealed class ReportGenerator : IGenerator
{
    public const string ToolVersion = "1.0.0";

    public const string FileName = "conversion_report.txt";

    public string Id => "report";

    public string Description => "Human-readable conversion report";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        var project = context.Project;
        var validation = context.Validation;
        var mapping = context.Mapping;

        var approximations = mapping.Records.Where(r => r.Status == ConversionStatus.Approximated).ToList();
        var manualTypeRecords = mapping.Records.Where(r => r.Status == ConversionStatus.Manual).ToList();
        var failed = mapping.Records.Where(r => r.Status == ConversionStatus.Failed).ToList();
        var warnings = validation.Warnings.ToList();
        var unrecognized = project.UnrecognizedElements;
        var tagCount = project.IoPoints.Count + project.GlobalVariables.Count;

        var builder = new StringBuilder();
        var line = new string('=', 66);

        builder.AppendLine(line);
        builder.AppendLine("                    CONVERSION REPORT");
        builder.AppendLine(line);
        builder.Append("Source File   : ").AppendLine(
            string.IsNullOrWhiteSpace(project.SourceFile) ? "(unknown)" : project.SourceFile);
        builder.Append("Converted On  : ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.Append("Tool Version  : ").AppendLine(ToolVersion);
        builder.Append("Target        : Siemens TIA Portal V").AppendLine(context.Options.ClampTiaVersion().ToString());
        builder.Append("Project       : ").AppendLine(project.Name);
        builder.AppendLine(new string('-', 66));

        builder.AppendLine("SUMMARY");
        builder.Append("  Converted     : ").Append(project.Blocks.Count).Append(" blocks, ")
            .Append(project.DataTypes.Count).Append(" UDTs, ").Append(tagCount).AppendLine(" tags");
        builder.Append("  Approximated  : ").Append(approximations.Count).AppendLine(" items");
        builder.Append("  Manual needed : ").Append(mapping.ManualActions.Count + manualTypeRecords.Count).AppendLine(" items");
        builder.Append("  Failed        : ").Append(failed.Count).AppendLine(" items");
        builder.Append("  Unrecognized  : ").Append(unrecognized.Count).AppendLine(" XML element(s)");
        builder.AppendLine(new string('-', 66));

        builder.AppendLine("FILES GENERATED");
        var files = context.Generated.Where(f => f.RelativePath != FileName).ToList();
        if (files.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var file in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
            {
                builder.Append("  ").Append(file.RelativePath).Append("  (").Append(Describe(file)).AppendLine(")");
            }
        }
        builder.AppendLine(new string('-', 66));

        builder.AppendLine("APPROXIMATIONS (review before using)");
        if (approximations.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            for (var index = 0; index < approximations.Count; index++)
            {
                var record = approximations[index];
                builder.Append("  ").Append(index + 1).Append(". [").Append(record.Category).Append("] ")
                    .Append(record.Source).Append(" -> ").AppendLine(record.Target ?? "(none)");
                if (!string.IsNullOrWhiteSpace(record.Note)) builder.Append("     Reason: ").AppendLine(record.Note);
                if (record.SourceLine >= 0) builder.Append("     Source line: ").AppendLine(record.SourceLine.ToString());
            }
        }
        builder.AppendLine(new string('-', 66));

        builder.AppendLine("MANUAL ACTION REQUIRED");
        if (mapping.ManualActions.Count == 0 && manualTypeRecords.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            var counter = 0;
            foreach (var action in mapping.ManualActions)
            {
                counter++;
                builder.Append("  ").Append(counter).Append(". ").AppendLine(action.Title);
                builder.Append("     Reason: ").AppendLine(action.Reason);
                builder.Append("     Action: ").AppendLine(action.Action);
                if (action.SourceLine >= 0) builder.Append("     Source line: ").AppendLine(action.SourceLine.ToString());
            }
            foreach (var record in manualTypeRecords)
            {
                counter++;
                builder.Append("  ").Append(counter).Append(". [").Append(record.Category).Append("] ")
                    .AppendLine(record.Source);
                builder.Append("     Reason: ").AppendLine(record.Note ?? "No automatic mapping.");
                if (record.SourceLine >= 0) builder.Append("     Source line: ").AppendLine(record.SourceLine.ToString());
            }
        }
        builder.AppendLine(new string('-', 66));

        builder.AppendLine("UNRECOGNIZED XML ELEMENTS");
        if (unrecognized.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            for (var index = 0; index < unrecognized.Count; index++)
            {
                var element = unrecognized[index];
                builder.Append("  ").Append(index + 1).Append(". <").Append(element.ElementName).Append("> at line ")
                    .Append(element.SourceLine).Append(" (in ").Append(element.ParentPath).AppendLine(")");
                if (!string.IsNullOrWhiteSpace(element.Snippet))
                {
                    builder.Append("     ").AppendLine(element.Snippet);
                }
            }
        }
        builder.AppendLine(new string('-', 66));

        builder.AppendLine("VALIDATION WARNINGS");
        if (warnings.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            for (var index = 0; index < warnings.Count; index++)
            {
                var warning = warnings[index];
                builder.Append("  ").Append(index + 1).Append(". ").Append(warning.Message);
                if (warning.SourceLine >= 0) builder.Append(" (line ").Append(warning.SourceLine).Append(')');
                builder.AppendLine();
            }
        }

        builder.AppendLine(line);

        yield return new GeneratedFile
        {
            RelativePath = FileName,
            Content = builder.ToString(),
            Kind = ArtifactKind.Report,
            EmitBom = true,
        };
    }

    private static string Describe(GeneratedFile file)
    {
        var name = file.RelativePath;
        if (name.StartsWith("Blocks/", StringComparison.OrdinalIgnoreCase)) return "block" + Extension(name);
        if (name.StartsWith("UDTs/", StringComparison.OrdinalIgnoreCase)) return "UDT";
        if (name.StartsWith("Tags/", StringComparison.OrdinalIgnoreCase)) return "tag table";
        if (name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) return "import script";
        return file.Kind.ToString().ToLowerInvariant();
    }

    private static string Extension(string name)
    {
        var ext = Path.GetExtension(name);
        return ext.Length == 0 ? string.Empty : " " + ext;
    }
}
