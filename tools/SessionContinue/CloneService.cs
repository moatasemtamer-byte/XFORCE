using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SessionContinue;

/// <summary>One row from `opencode session list --format json`.</summary>
public sealed record SessionRow(string Id, string Title, long Updated, long Created);

/// <summary>Outcome of a successful clone.</summary>
public sealed record CloneResult(string NewId, string NewTitle, string ExportPath);

/// <summary>
/// The continuation logic: list a project's sessions, export the chosen one to JSON,
/// rewrite it under a fresh id and a numbered title ("Name 2", "Name 3", ...), and
/// import it back so OpenCode shows a session that is the old one, continued.
/// </summary>
public sealed class CloneService
{
    private static readonly Regex TrailingNumber = new(@"^(.*\S)\s(\d+)$", RegexOptions.Compiled);

    private readonly OcCli _cli;

    public CloneService(OcCli cli) => _cli = cli;

    public static string ExportDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SessionContinue", "exports");

    /// <summary>Sessions of the project rooted at <paramref name="projectDir"/>, newest first.</summary>
    public async Task<List<SessionRow>> ListAsync(string projectDir)
    {
        var result = await _cli.RunAsync(projectDir, new[] { "session", "list", "--format", "json", "-n", "500" })
            .ConfigureAwait(false);
        if (!result.Ok)
            throw new InvalidOperationException(
                "opencode session list failed:\n" + (result.StdErr + result.StdOut).Trim());

        var rows = new List<SessionRow>();
        var array = JsonNode.Parse(result.StdOut)?.AsArray() ?? throw new InvalidOperationException("unreadable session list");
        foreach (var node in array)
        {
            if (node is not JsonObject o) continue;
            rows.Add(new SessionRow(
                o["id"]?.GetValue<string>() ?? "",
                o["title"]?.GetValue<string>() ?? "(untitled)",
                o["updated"]?.GetValue<long>() ?? 0,
                o["created"]?.GetValue<long>() ?? 0));
        }

        return rows.OrderByDescending(r => r.Updated).ToList();
    }

    /// <summary>The next free continuation title: "X" -> "X 2" -> "X 3", skipping taken numbers.</summary>
    public static string NextTitle(string currentTitle, IEnumerable<string> existingTitles)
    {
        var taken = new HashSet<string>(
            existingTitles.Select(t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);

        string baseTitle;
        int last;
        var match = TrailingNumber.Match(currentTitle.Trim());
        if (match.Success)
        {
            baseTitle = match.Groups[1].Value.TrimEnd();
            last = int.Parse(match.Groups[2].Value);
        }
        else
        {
            baseTitle = currentTitle.Trim();
            last = 1;
        }

        if (baseTitle.Length == 0) baseTitle = currentTitle.Trim();
        var next = last + 1;
        while (taken.Contains(baseTitle + " " + next)) next++;
        return baseTitle + " " + next;
    }

    /// <summary>Exports the session, rewrites id/title, imports the clone, returns the result.</summary>
    public async Task<CloneResult> CloneAsync(
        string projectDir,
        SessionRow source,
        string? newTitleOverride,
        IReadOnlyList<string> allTitles,
        Action<string>? progress = null)
    {
        progress?.Invoke("exporting session…");
        var exported = await _cli.RunAsync(
                projectDir,
                new[] { "session", "export", source.Id },
                TimeSpan.FromMinutes(15))
            .ConfigureAwait(false);
        if (!exported.Ok || string.IsNullOrWhiteSpace(exported.StdOut))
            throw new InvalidOperationException(
                "opencode session export failed:\n" + (exported.StdErr + exported.StdOut).Trim());

        progress?.Invoke("rewriting export…");
        var doc = JsonNode.Parse(exported.StdOut)?.AsObject()
                  ?? throw new InvalidOperationException("export is not a JSON object");
        var info = doc["info"]?.AsObject() ?? throw new InvalidOperationException("export has no info object");

        var newId = NewSessionId();
        var newTitle = string.IsNullOrWhiteSpace(newTitleOverride)
            ? NextTitle(source.Title, allTitles)
            : newTitleOverride.Trim();
        if (newTitle.Length == 0) newTitle = NextTitle(source.Title, allTitles);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        info["id"] = newId;
        info["title"] = newTitle;
        if (info["cost"] is not null) info["cost"] = 0;
        if (info["time"] is JsonObject time)
        {
            foreach (var key in new[] { "created", "updated", "idle", "viewed" })
                if (time[key] is not null) time[key] = now;
        }

        progress?.Invoke("saving export file…");
        Directory.CreateDirectory(ExportDirectory);
        var exportPath = Path.Combine(
            ExportDirectory,
            Sanitize(newTitle) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
        await File.WriteAllTextAsync(exportPath, doc.ToJsonString(), new System.Text.UTF8Encoding(false))
            .ConfigureAwait(false);

        progress?.Invoke("importing clone…");
        var imported = await _cli.RunAsync(
                projectDir,
                new[] { "session", "import", exportPath, "--directory", projectDir },
                TimeSpan.FromMinutes(15))
            .ConfigureAwait(false);
        if (!imported.Ok)
            throw new InvalidOperationException(
                "opencode session import failed:\n" + (imported.StdErr + imported.StdOut).Trim());

        progress?.Invoke("done");
        return new CloneResult(newId, newTitle, exportPath);
    }

    /// <summary>26 alphanumeric chars after "ses_", matching OpenCode's own id shape.</summary>
    private static string NewSessionId()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = RandomNumberGenerator.GetBytes(26);
        var chars = new char[26];
        for (var i = 0; i < 26; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return "ses_" + new string(chars);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "session" : (cleaned.Length > 60 ? cleaned.Substring(0, 60) : cleaned);
    }
}
