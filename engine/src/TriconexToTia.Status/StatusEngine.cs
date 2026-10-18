namespace TriconexToTia.Status;

/// <summary>
/// Maps evidence files on disk to status values. Pure, UI-free, and deterministic:
/// every status is derived from evidence, never hard-coded. Missing evidence is
/// always <see cref="EvidenceStatus.NotStarted"/>.
/// </summary>
public sealed class StatusEngine
{
    public const string ReportFileName = "report.json";
    public const string RoundTripFileName = "roundtrip.json";
    public const string TestsFileName = "tests.trx";

    /// <summary>Phase names 0-7, from the GUI spec dashboard.</summary>
    public static readonly IReadOnlyList<string> PhaseNames = new[]
    {
        "Infrastructure (UDTs + GlobalDB + Tags)",
        "Simplest ST block",
        "ST blocks with IEC library calls",
        "ST blocks with APPROXIMATE calls",
        "FBD blocks",
        "Helper FBs (UNPACK16/32)",
        "Library call mapping (Gap 7)",
        "Full project + batch UDT test"
    };

    private readonly string _reportFileName;
    private readonly string _roundTripFileName;
    private readonly string _testsFileName;

    public StatusEngine(
        string reportFileName = ReportFileName,
        string roundTripFileName = RoundTripFileName,
        string testsFileName = TestsFileName)
    {
        _reportFileName = reportFileName;
        _roundTripFileName = roundTripFileName;
        _testsFileName = testsFileName;
    }

    public ProjectStatus Compute(string outputDir, string statusDir)
    {
        var output = outputDir ?? string.Empty;
        var status = statusDir ?? string.Empty;

        var reportPath = Path.Combine(output, _reportFileName);
        var roundTripPath = Path.Combine(output, _roundTripFileName);
        var testsPath = Path.Combine(status, _testsFileName);

        var report = ReportJson.TryReadFile(reportPath);
        var roundTrip = RoundTripJson.TryReadFile(roundTripPath);
        var tests = TestRunSummary.TryReadFile(testsPath);

        var approximateNames = BuildApproximateNames(report);
        var roundTripNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Classify each roundtrip object once.
        var classified = new List<(RoundTripObject Object, EvidenceStatus Status)>();
        if (roundTrip is not null)
        {
            foreach (var o in roundTrip.Objects)
            {
                classified.Add((o, Classify(o, approximateNames)));
                if (!string.IsNullOrWhiteSpace(o.Name))
                {
                    roundTripNames.Add(o.Name!);
                }
            }
        }

        var items = new List<StatusItem>();
        foreach (var (o, s) in classified)
        {
            items.Add(new StatusItem(
                o.Kind ?? "Object",
                o.Name ?? o.File ?? "(unnamed)",
                s,
                roundTripPath,
                FirstMessage(o)));
        }

        if (report is not null)
        {
            foreach (var c in report.Converted)
            {
                if (string.IsNullOrWhiteSpace(c.Name) || roundTripNames.Contains(c.Name!)) continue;
                items.Add(new StatusItem(
                    c.Kind ?? "Converted", c.Name!, EvidenceStatus.UnitTestOnly, reportPath, c.Reason));
            }

            foreach (var a in report.Approximated)
            {
                if (string.IsNullOrWhiteSpace(a.Name) || roundTripNames.Contains(a.Name!)) continue;
                items.Add(new StatusItem(
                    a.Kind ?? "Approximated", a.Name!, EvidenceStatus.Unverified, reportPath, a.Reason));
            }
        }

        var (phaseStatuses, phases) = ComputePhases(classified);

        var evidenceFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(reportPath)) evidenceFiles["report"] = reportPath;
        if (File.Exists(roundTripPath)) evidenceFiles["roundtrip"] = roundTripPath;
        if (File.Exists(testsPath)) evidenceFiles["tests"] = testsPath;

        return new ProjectStatus
        {
            BuildStatus = ComputeBuildStatus(tests),
            ConversionStatus = ComputeConversionStatus(report, items),
            PhaseStatuses = phaseStatuses,
            Phases = phases,
            UnverifiedCount = items.Count(i => i.Status == EvidenceStatus.Unverified),
            UnitTestOnlyCount = items.Count(i => i.Status == EvidenceStatus.UnitTestOnly),
            CompileWarnings = ComputeCompileWarnings(roundTrip),
            OpenKnownIssues = report?.KnownIssues.Count(i => i.IsOpen) ?? 0,
            EvidenceTimestamp = roundTrip?.Timestamp ?? report?.Timestamp,
            EvidencePath = File.Exists(roundTripPath)
                ? roundTripPath
                : File.Exists(reportPath) ? reportPath : null,
            Items = items,
            Report = report,
            RoundTrip = roundTrip,
            Tests = tests,
            EvidenceFiles = evidenceFiles
        };
    }

    private static EvidenceStatus ComputeBuildStatus(TestRunSummary? tests)
    {
        if (tests is null) return EvidenceStatus.NotStarted;
        if (!tests.AllPassed) return EvidenceStatus.Failed;
        return EvidenceStatus.UnitTestOnly;
    }

    private static EvidenceStatus ComputeConversionStatus(ReportJson? report, IReadOnlyList<StatusItem> items)
    {
        if (report is null) return EvidenceStatus.NotStarted;
        if (report.Counts.Fatals > 0) return EvidenceStatus.Failed;
        if (items.Any(i => i.Status == EvidenceStatus.Failed)) return EvidenceStatus.Failed;
        if (items.Any(i => i.Status == EvidenceStatus.VerifiedInTia)) return EvidenceStatus.VerifiedInTia;
        return EvidenceStatus.Unverified;
    }

    private static EvidenceStatus Classify(RoundTripObject o, HashSet<string> approximateNames)
    {
        if (!o.Imported) return EvidenceStatus.Failed;
        if (o.HasError) return EvidenceStatus.Failed;
        if (!o.Compiled) return EvidenceStatus.Unverified;

        // Approximate or inferred items are capped at UNVERIFIED even if TIA compiles them.
        if (!string.IsNullOrWhiteSpace(o.Name) && approximateNames.Contains(o.Name!))
        {
            return EvidenceStatus.Unverified;
        }

        return EvidenceStatus.VerifiedInTia;
    }

    private static HashSet<string> BuildApproximateNames(ReportJson? report) => report is null
        ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        : report.Approximated
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static int ComputeCompileWarnings(RoundTripJson? roundtrip)
    {
        if (roundtrip is null) return 0;
        if (roundtrip.CompileWarnings is int n) return n;
        return roundtrip.Objects.Sum(o => o.Messages.Count(m => m.IsWarning));
    }

    private static string? FirstMessage(RoundTripObject o)
    {
        var m = o.Messages.FirstOrDefault();
        return m is null ? null : $"[{m.State}] {m.Message}".Trim();
    }

    private static (IReadOnlyList<EvidenceStatus>, IReadOnlyList<PhaseStatus>) ComputePhases(
        IReadOnlyList<(RoundTripObject Object, EvidenceStatus Status)> classified)
    {
        var statuses = new List<EvidenceStatus>();
        var details = new List<PhaseStatus>();

        for (var p = 0; p < PhaseNames.Count; p++)
        {
            var objects = classified.Where(c => c.Object.Phase == p).ToList();
            if (objects.Count == 0)
            {
                statuses.Add(EvidenceStatus.NotStarted);
                details.Add(new PhaseStatus(p, PhaseNames[p], EvidenceStatus.NotStarted, 0, 0));
                continue;
            }

            var verified = objects.Count(c => c.Status == EvidenceStatus.VerifiedInTia);
            EvidenceStatus status = objects.Any(c => c.Status == EvidenceStatus.Failed)
                ? EvidenceStatus.Failed
                : verified == objects.Count
                    ? EvidenceStatus.VerifiedInTia
                    : EvidenceStatus.Unverified;

            statuses.Add(status);
            details.Add(new PhaseStatus(p, PhaseNames[p], status, verified, objects.Count));
        }

        return (statuses, details);
    }
}