using System.Text.Json;
using System.Text.Json.Serialization;

namespace TriconexToTia.Status;

/// <summary>
/// Per-object TIA import/compile result (schema from the GUI spec).
/// Written by <c>import.ps1</c> as <c>&lt;output&gt;\roundtrip.json</c>.
/// </summary>
public sealed class RoundTripJson
{
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
    [JsonPropertyName("tia_version")] public string? TiaVersion { get; set; }
    [JsonPropertyName("cpu_order_number")] public string? CpuOrderNumber { get; set; }
    [JsonPropertyName("project_path")] public string? ProjectPath { get; set; }
    [JsonPropertyName("objects")] public List<RoundTripObject> Objects { get; set; } = new();

    /// <summary>Optional top-level compile warning count. When absent, messages are counted.</summary>
    [JsonPropertyName("compile_warnings")] public int? CompileWarnings { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static RoundTripJson? TryReadFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<RoundTripJson>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class RoundTripObject
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("file")] public string? File { get; set; }
    [JsonPropertyName("imported")] public bool Imported { get; set; }
    [JsonPropertyName("compiled")] public bool Compiled { get; set; }
    [JsonPropertyName("messages")] public List<RoundTripMessage> Messages { get; set; } = new();

    /// <summary>Optional phase index (0-7) used by the dashboard phases panel.</summary>
    [JsonPropertyName("phase")] public int? Phase { get; set; }

    public bool HasError => Messages.Any(m => m.IsError);
    public bool HasWarning => Messages.Any(m => m.IsWarning);
}

public sealed class RoundTripMessage
{
    /// <summary>"Error" or "Warning" (matched case-insensitively).</summary>
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }

    /// <summary>Alias for <see cref="Description"/> used by some emitters.</summary>
    [JsonPropertyName("text")] public string? Text { get; set; }

    public string? Message => Description ?? Text;

    public bool IsError => string.Equals(State, "Error", StringComparison.OrdinalIgnoreCase);
    public bool IsWarning => string.Equals(State, "Warning", StringComparison.OrdinalIgnoreCase);
}