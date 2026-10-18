namespace TriconexToTia.Status;

/// <summary>
/// The full state of the project, computed purely from evidence files on disk.
/// Every value here is derived; nothing is hard-coded.
/// </summary>
public sealed class ProjectStatus
{
    public EvidenceStatus BuildStatus { get; init; } = EvidenceStatus.NotStarted;
    public EvidenceStatus ConversionStatus { get; init; } = EvidenceStatus.NotStarted;

    /// <summary>Status per phase 0-7 (see the GUI spec phase list).</summary>
    public IReadOnlyList<EvidenceStatus> PhaseStatuses { get; init; } = Array.Empty<EvidenceStatus>();

    /// <summary>Per-phase detail (name, counts) for the dashboard.</summary>
    public IReadOnlyList<PhaseStatus> Phases { get; init; } = Array.Empty<PhaseStatus>();

    /// <summary>Items currently <see cref="EvidenceStatus.Unverified"/> (footer count).</summary>
    public int UnverifiedCount { get; init; }

    /// <summary>Items currently <see cref="EvidenceStatus.UnitTestOnly"/> (footer count).</summary>
    public int UnitTestOnlyCount { get; init; }

    /// <summary>Compile warnings from the last TIA run in roundtrip.json.</summary>
    public int CompileWarnings { get; init; }

    /// <summary>Open known issues from report.json.</summary>
    public int OpenKnownIssues { get; init; }

    public string? EvidenceTimestamp { get; init; }
    public string? EvidencePath { get; init; }

    /// <summary>Per-item classification used to compute the counts.</summary>
    public IReadOnlyList<StatusItem> Items { get; init; } = Array.Empty<StatusItem>();

    /// <summary>Raw parsed evidence, exposed for screens that need detail.</summary>
    public ReportJson? Report { get; init; }
    public RoundTripJson? RoundTrip { get; init; }
    public TestRunSummary? Tests { get; init; }

    /// <summary>Paths consulted, so the GUI can offer "open the evidence file".</summary>
    public IReadOnlyDictionary<string, string> EvidenceFiles { get; init; } =
        new Dictionary<string, string>();
}

public sealed record StatusItem(
    string Kind,
    string Name,
    EvidenceStatus Status,
    string? EvidencePath,
    string? Detail);

public sealed record PhaseStatus(
    int Phase,
    string Name,
    EvidenceStatus Status,
    int VerifiedCount,
    int TotalCount);