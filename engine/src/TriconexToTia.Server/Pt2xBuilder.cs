using System.Diagnostics;
using System.Text;

namespace TriconexToTia.Server;

/// <summary>
/// Builds a TriStation PT2X container from a TriStation PLCopen XML export by
/// running the bundled <c>pt2conv</c> command-line tool (<c>buildxml</c>).
/// </summary>
/// <remarks>
/// The tool is invoked out-of-process rather than linked in-process so the engine's
/// build graph stays self-contained (the converter lives in the sibling
/// <c>pt2-conv</c> project). The executable is located by
/// <c>MSW_PT2CONV_PATH</c>, or discovered at the usual build output under the repo
/// root. Console output is captured and returned as the build report.
/// </remarks>
public static class Pt2xBuilder
{
    public sealed record Result(bool Success, byte[]? Bytes, string Report, string? Error);

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(
        double.TryParse(Environment.GetEnvironmentVariable("MSW_PT2CONV_TIMEOUT_MINUTES"), out var minutes) && minutes > 0
            ? minutes
            : 5);

    public static async Task<Result> BuildAsync(Stream xml, string sourceName, CancellationToken cancellationToken)
    {
        var executable = ResolveExecutable();
        if (executable is null)
        {
            return new Result(false, null, string.Empty,
                "Could not find pt2conv. Build it (dotnet build pt2-conv/src/Pt2Conv) or set MSW_PT2CONV_PATH to the executable.");
        }

        var work = Path.Combine(Path.GetTempPath(), "msw-pt2x-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var xmlPath = Path.Combine(work, "project.xml");
        var outPath = Path.Combine(work, "project.pt2x");

        try
        {
            await using (var file = File.Create(xmlPath))
            {
                await xml.CopyToAsync(file, cancellationToken);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = work,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("buildxml");
            startInfo.ArgumentList.Add(xmlPath);
            startInfo.ArgumentList.Add(outPath);

            using var process = new Process { StartInfo = startInfo };
            var report = new StringBuilder();
            process.OutputDataReceived += (_, args) => { if (args.Data is not null) report.AppendLine(args.Data); };
            process.ErrorDataReceived += (_, args) => { if (args.Data is not null) report.AppendLine(args.Data); };

            if (!process.Start())
            {
                return new Result(false, null, string.Empty, "Failed to start pt2conv.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new Result(false, null, report.ToString(), "pt2conv timed out.");
            }

            if (process.ExitCode != 0 || !File.Exists(outPath))
            {
                return new Result(false, null, report.ToString(),
                    $"pt2conv exited with code {process.ExitCode} and produced no container.");
            }

            var bytes = await File.ReadAllBytesAsync(outPath, cancellationToken);
            return new Result(true, bytes, report.ToString(), null);
        }
        catch (Exception exception)
        {
            return new Result(false, null, string.Empty, exception.Message);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* best effort */ }
        }
    }

    /// <summary>Finds the pt2conv executable: explicit env var first, then the repo's build output.</summary>
    internal static string? ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("MSW_PT2CONV_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        foreach (var root in CandidateRoots())
        {
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                var candidate = Path.Combine(root, "pt2-conv", "src", "Pt2Conv", "bin", configuration, "net7.0", "pt2conv.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    /// <summary>Walks up from the running assembly to the repo root (the folder holding both engine/ and pt2-conv/).</summary>
    private static IEnumerable<string> CandidateRoots()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var engine = Path.Combine(directory.FullName, "engine");
            var converter = Path.Combine(directory.FullName, "pt2-conv");
            if (Directory.Exists(engine) && Directory.Exists(converter))
            {
                yield return directory.FullName;
                yield break;
            }
            directory = directory.Parent;
        }
    }
}
