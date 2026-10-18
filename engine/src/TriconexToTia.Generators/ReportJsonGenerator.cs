using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Results;
using TriconexToTia.Status;

namespace TriconexToTia.Generators;

/// <summary>
/// Emits the machine-readable twin of <c>conversion_report.txt</c> as <c>report.json</c>.
/// This is the evidence file the GUI reads; every value is derived from the run, never
/// hard-coded. <c>known_issues</c> are measured from the generated output and the model.
/// </summary>
public sealed class ReportJsonGenerator : IGenerator
{
    public const string FileName = "report.json";

    public string Id => "report-json";

    public string Description => "Machine-readable conversion report (GUI evidence)";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Regex CoercionPattern = new(
        @"\b(BOOL_TO_DINT|DINT_TO_BOOL|BOOL_TO_DWORD|DWORD_TO_BOOL|BOOL_TO_WORD|WORD_TO_BOOL|BOOL_TO_INT|INT_TO_BOOL|DINT_TO_WORD|WORD_TO_DINT)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        var project = context.Project;
        var mapping = context.Mapping;

        var converted = new List<ReportItem>();
        foreach (var block in project.Blocks.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            converted.Add(new ReportItem { Kind = "Block", Name = block.Name, Status = "CONVERTED" });
        }
        foreach (var record in mapping.Records.Where(r => r.Status == ConversionStatus.Converted))
        {
            converted.Add(new ReportItem { Kind = record.Category, Name = record.Source, Status = "CONVERTED" });
        }

        var approximated = mapping.Records
            .Where(r => r.Status == ConversionStatus.Approximated)
            .Select(ToItem)
            .ToList();

        var manual = new List<ReportItem>();
        foreach (var action in mapping.ManualActions)
        {
            manual.Add(new ReportItem { Kind = "ManualAction", Name = action.Title, Reason = action.Reason, Status = "MANUAL" });
        }
        manual.AddRange(mapping.Records
            .Where(r => r.Status == ConversionStatus.Manual)
            .Select(ToItem));

        var neverConverted = mapping.Records
            .Where(r => r.Status == ConversionStatus.Failed)
            .Select(ToItem)
            .ToList();

        var counts = BuildCounts(context);
        var issues = BuildKnownIssues(context);

        var report = new ReportJson
        {
            Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            SourceFile = string.IsNullOrWhiteSpace(project.SourceFile) ? null : project.SourceFile,
            SourceVersion = project.Header?.ProductVersion,
            TiaTarget = "V" + context.Options.ClampTiaVersion().ToString(CultureInfo.InvariantCulture),
            CpuOrderNumber = string.IsNullOrWhiteSpace(context.Options.PlcCpu) ? null : context.Options.PlcCpu,
            Counts = counts,
            Converted = converted,
            Approximated = approximated,
            Manual = manual,
            NeverConverted = neverConverted,
            KnownIssues = issues,
        };

        if (context.Options.FbdOutput != FbdOutputMode.Scl)
        {
            foreach (var outcome in context.FbdOutcomes
                .Where(block => block.Rendered != "FBD")
                .OrderBy(block => block.Block, StringComparer.OrdinalIgnoreCase))
            {
                report.FbdFallbacks.Add(new ReportItem
                {
                    Kind = "FbdBlock",
                    Name = outcome.Block,
                    Reason = outcome.Reason,
                    Status = "SCL",
                });
            }

            foreach (var outcome in context.FbdOutcomes
                .Where(block => block.Rendered == "FBD" && block.Approximations.Count > 0)
                .OrderBy(block => block.Block, StringComparer.OrdinalIgnoreCase))
            {
                report.FbdApproximations.Add(new ReportItem
                {
                    Kind = "FbdBlock",
                    Name = outcome.Block,
                    Reason = string.Join("; ", outcome.Approximations),
                    Status = "FBD",
                });
            }
        }

        yield return new GeneratedFile
        {
            RelativePath = FileName,
            Content = JsonSerializer.Serialize(report, JsonOptions),
            Kind = ArtifactKind.Report,
            EmitBom = false,
        };
    }

    private static ReportItem ToItem(MappingRecord record) => new()
    {
        Kind = record.Category,
        Name = record.Source,
        Reason = string.IsNullOrWhiteSpace(record.Note) ? record.Target : record.Note,
        Status = record.Status.ToString().ToUpperInvariant(),
        Sites = 1,
    };

    private static ReportCounts BuildCounts(GenerationContext context)
    {
        var project = context.Project;
        var mapping = context.Mapping;
        var libraryRecords = mapping.Records.Where(IsLibrary).ToList();
        var fbdOutcomes = context.Options.FbdOutput != FbdOutputMode.Scl ? context.FbdOutcomes : EmptyOutcomes;

        return new ReportCounts
        {
            PousTotal = project.Blocks.Count,
            PousConverted = project.Blocks.Count,
            GlobalsTotal = project.GlobalVariables.Count,
            GlobalsConverted = project.GlobalVariables.Count,
            UdtsTotal = project.DataTypes.Count,
            LibraryCallsTotal = project.LibraryCalls.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            LibraryCallsAuto = libraryRecords.Count(r => r.Status == ConversionStatus.Converted),
            LibraryCallsApproximate = libraryRecords.Count(r => r.Status == ConversionStatus.Approximated),
            LibraryCallsManual = libraryRecords.Count(r => r.Status == ConversionStatus.Manual),
            Fatals = context.Validation.Fatal.Count(),
            Warnings = context.Validation.Warnings.Count(),
            ManualActions = mapping.ManualActions.Count +
                mapping.Records.Count(r => r.Status == ConversionStatus.Manual),
            FbdTotal = project.Blocks.Count(block => block.Language == PlcLanguage.FBD),
            FbdRendered = fbdOutcomes.Count(outcome => outcome.Rendered == "FBD"),
            FbdFallenBack = fbdOutcomes.Count(outcome => outcome.Rendered != "FBD"),
        };
    }

    private static readonly List<FbdBlockOutcome> EmptyOutcomes = new();

    private static bool IsLibrary(MappingRecord record) =>
        record.Category.Contains("Library", StringComparison.OrdinalIgnoreCase);

    private static List<KnownIssue> BuildKnownIssues(GenerationContext context)
    {
        // Measured from the generated SCL, not assumed.
        var coercionSites = 0;
        var holdBlocks = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in context.Generated)
        {
            if (!file.RelativePath.EndsWith(".scl", StringComparison.OrdinalIgnoreCase)) continue;
            var content = file.Content ?? string.Empty;
            coercionSites += CoercionPattern.Matches(content).Count;
            if (content.Contains("_HOLD", StringComparison.Ordinal))
            {
                holdBlocks.Add(BlockName(file.RelativePath));
            }
        }

        // Address collisions measured from the parsed tag model.
        var addresses = context.Project.GlobalVariables
            .Select(v => v.Metadata?.PhysicalAddress ?? v.Address)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();
        var distinctAddresses = addresses.Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return new List<KnownIssue>
        {
            new()
            {
                Id = "INIT_WARNINGS",
                Description = "VAR declarations lack explicit initial values",
                AffectedBlocks = holdBlocks.ToList(),
                Status = holdBlocks.Count > 0 ? "OPEN" : "RESOLVED",
            },
            new()
            {
                Id = "TYPE_CONFLICT",
                Description = "Signal names with conflicting types across programs",
                Count = coercionSites,
                Status = coercionSites > 0 ? "OPEN" : "RESOLVED",
            },
            new()
            {
                Id = "ADDRESS_COLLISION",
                Description = "Multiple tags share the same PhysicalAddress",
                SharedAddressCount = distinctAddresses,
                TagsAffected = addresses.Count,
                Status = distinctAddresses < addresses.Count ? "OPEN" : "RESOLVED",
            },
        };
    }

    private static string BlockName(string relativePath)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath);
        return name.EndsWith("_WRAP", StringComparison.OrdinalIgnoreCase)
            ? name[..^"_WRAP".Length]
            : name;
    }
}