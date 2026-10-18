using System.Text.Json;

namespace TriconexToTia.Server;

/// <summary>
/// A durable, append-only log of conversions kept next to the evidence reports in
/// <c>MSW_OUTPUT_DIR/history.jsonl</c>. One JSON object per line; each line is written as soon
/// as a conversion settles (success or failure) so a crash cannot erase the record. The web
/// dashboard reads it through <c>/api/history</c> to show what the engine has converted.
/// Retention is enforced on write: entries older than <c>MSW_HISTORY_RETENTION_DAYS</c>
/// (default 14) and files older than <c>MSW_EVIDENCE_RETENTION_DAYS</c> are pruned.
/// All operations are best-effort: history must never fail a conversion.
/// </summary>
public static class ConversionHistory
{
    public const string FileName = "history.jsonl";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly object WriteGate = new();

    /// <summary>Appends one settled conversion. Silently does nothing when no directory is
    /// configured or the write fails.</summary>
    public static void Record(
        string? directory,
        string? id,
        string sourceName,
        string sourceKind,
        string target,
        bool success,
        long durationMs,
        int? artifactCount,
        string? error)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;

        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileName);
            var line = JsonSerializer.Serialize(new
            {
                id,
                at = DateTimeOffset.UtcNow,
                sourceName,
                sourceKind,
                target,
                success,
                durationMs,
                artifactCount,
                error,
            }, Json);

            lock (WriteGate)
            {
                File.AppendAllText(path, line + Environment.NewLine, new System.Text.UTF8Encoding(false));
            }

            PruneHistory(path);
            PruneEvidence(directory);
        }
        catch
        {
            // History is a convenience; a full or read-only volume must not break anything.
        }
    }

    /// <summary>Reads the newest <paramref name="limit"/> entries, newest first.</summary>
    public static object? Read(string directory, int limit)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new { entries = Array.Empty<object>(), directory };

        try
        {
            var entries = new List<object>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    entries.Add(document.RootElement.Clone());
                }
                catch
                {
                    // A torn last line from a crash is expected; skip it.
                }
            }

            entries.Reverse();
            return new { entries = entries.Take(Math.Clamp(limit, 1, 500)).ToArray(), directory };
        }
        catch
        {
            return new { entries = Array.Empty<object>(), directory };
        }
    }

    // Keeps history.jsonl from growing without bound: rewrite it in place with only the
    // retained tail once it exceeds twice the retained line count.
    private static void PruneHistory(string path)
    {
        var days = ReadPositiveInt("MSW_HISTORY_RETENTION_DAYS", 14);
        var keepFrom = DateTimeOffset.UtcNow - TimeSpan.FromDays(days);

        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 512 * 1024) return;

        var kept = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("at", out var at) &&
                    at.TryGetDateTimeOffset(out var moment) &&
                    moment >= keepFrom)
                {
                    kept.Add(line);
                }
            }
            catch
            {
                // Drop unreadable lines while we are already rewriting.
            }
        }

        lock (WriteGate)
        {
            File.WriteAllLines(path, kept, new System.Text.UTF8Encoding(false));
        }
    }

    // Deletes stale evidence files (the per-run reports) so the volume cannot fill up.
    // history.jsonl is managed by PruneHistory and is never touched here.
    private static void PruneEvidence(string directory)
    {
        var days = ReadPositiveInt("MSW_EVIDENCE_RETENTION_DAYS", 14);
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(days);

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetFileName(file), FileName, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff) info.Delete();
            }
            catch
            {
                // Best effort.
            }
        }

        var statusDir = Path.Combine(directory, "status");
        if (!Directory.Exists(statusDir)) return;
        foreach (var file in Directory.EnumerateFiles(statusDir))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff) info.Delete();
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static int ReadPositiveInt(string variable, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(variable);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
