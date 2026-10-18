using System.Text.Json;
using System.Text.Json.Serialization;

namespace TriconexToTia.Status;

/// <summary>
/// Machine-readable twin of <c>conversion_report.txt</c> (schema from the GUI spec).
/// Written by the engine as <c>&lt;output&gt;\report.json</c>.
/// </summary>
public sealed class ReportJson
{
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
    [JsonPropertyName("source_file")] public string? SourceFile { get; set; }
    [JsonPropertyName("source_version")] public string? SourceVersion { get; set; }
    [JsonPropertyName("tia_target")] public string? TiaTarget { get; set; }
    [JsonPropertyName("cpu_order_number")] public string? CpuOrderNumber { get; set; }
    [JsonPropertyName("counts")] public ReportCounts Counts { get; set; } = new();
    [JsonPropertyName("converted")] public List<ReportItem> Converted { get; set; } = new();
    [JsonPropertyName("approximated")] public List<ReportItem> Approximated { get; set; } = new();
    [JsonPropertyName("manual")] public List<ReportItem> Manual { get; set; } = new();
    [JsonPropertyName("never_converted")] public List<ReportItem> NeverConverted { get; set; } = new();
    [JsonPropertyName("fbd_fallbacks")] public List<ReportItem> FbdFallbacks { get; set; } = new();
    [JsonPropertyName("fbd_approximations")] public List<ReportItem> FbdApproximations { get; set; } = new();
    [JsonPropertyName("known_issues")] public List<KnownIssue> KnownIssues { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ReportJson? TryReadFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ReportJson>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class ReportCounts
{
    [JsonPropertyName("pous_total")] public int PousTotal { get; set; }
    [JsonPropertyName("pous_converted")] public int PousConverted { get; set; }
    [JsonPropertyName("globals_total")] public int GlobalsTotal { get; set; }
    [JsonPropertyName("globals_converted")] public int GlobalsConverted { get; set; }
    [JsonPropertyName("udts_total")] public int UdtsTotal { get; set; }
    [JsonPropertyName("library_calls_total")] public int LibraryCallsTotal { get; set; }
    [JsonPropertyName("library_calls_auto")] public int LibraryCallsAuto { get; set; }
    [JsonPropertyName("library_calls_approximate")] public int LibraryCallsApproximate { get; set; }
    [JsonPropertyName("library_calls_manual")] public int LibraryCallsManual { get; set; }
    [JsonPropertyName("fatals")] public int Fatals { get; set; }
    [JsonPropertyName("warnings")] public int Warnings { get; set; }
    [JsonPropertyName("manual_actions")] public int ManualActions { get; set; }
    [JsonPropertyName("fbd_total")] public int FbdTotal { get; set; }
    [JsonPropertyName("fbd_rendered")] public int FbdRendered { get; set; }
    [JsonPropertyName("fbd_fallen_back")] public int FbdFallenBack { get; set; }
}

public sealed class ReportItem
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("block")] public string? Block { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("sites")] public int Sites { get; set; }
}

public sealed class KnownIssue
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("affected_blocks")] public List<string> AffectedBlocks { get; set; } = new();
    [JsonPropertyName("count")] public int? Count { get; set; }
    [JsonPropertyName("shared_address_count")] public int? SharedAddressCount { get; set; }
    [JsonPropertyName("tags_affected")] public int? TagsAffected { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }

    public bool IsOpen => string.Equals(Status, "OPEN", StringComparison.OrdinalIgnoreCase);
}