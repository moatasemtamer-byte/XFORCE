using System.Collections.Concurrent;

namespace TriconexToTia.Server;

/// <summary>
/// In-memory progress for in-flight conversions. The browser generates a random id, sends it
/// with the upload as the <c>progressId</c> form field, and polls <c>/api/progress/{id}</c>
/// while the synchronous <c>/convert</c> request is still running. Entries expire quickly;
/// nothing here is durable and nothing here may ever fail a conversion.
/// </summary>
public static class ConversionProgress
{
    private sealed record Entry(string Stage, DateTimeOffset UpdatedAt, bool Done, string? Error);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>Moves a conversion to a new stage. A no-op when the client sent no id.</summary>
    public static void Report(string? id, string stage, string? error = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        Entries[id] = new Entry(stage, DateTimeOffset.UtcNow, Done: false, error);
        Prune();
    }

    /// <summary>Marks a conversion finished (successfully or not). The entry lingers so a poll
    /// that races the response still sees the terminal state.</summary>
    public static void Complete(string? id, string? error = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        Entries[id] = new Entry("done", DateTimeOffset.UtcNow, Done: true, error);
    }

    public static object? Get(string id)
    {
        if (!Entries.TryGetValue(id, out var entry)) return null;
        return new
        {
            id,
            stage = entry.Stage,
            done = entry.Done,
            error = entry.Error,
            updatedAt = entry.UpdatedAt,
        };
    }

    private static void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow - Lifetime;
        foreach (var pair in Entries)
        {
            if (pair.Value.UpdatedAt < cutoff) Entries.TryRemove(pair.Key, out _);
        }
    }
}
