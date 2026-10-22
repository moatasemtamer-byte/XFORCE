using System.Diagnostics;
using System.Text;

namespace TriconexToTia.Server;

/// <summary>
/// Renders a TriStation .pt2/.pt2x container to a printable PDF by running the
/// bundled <c>pt2conv</c> command-line tool. The converter replays the same FBD sheet
/// geometry the editor draws (frame, title block, blocks, terminals, wires, comments)
/// and/or reproduces the report suite from the embedded Telerik <c>.trdp</c> templates.
/// Both are pure managed drawing with no TriStation, licence or Windows dependency, so
/// they run on the engine host (Linux on Railway).
/// </summary>
/// <remarks>
/// Invoked out-of-process, matching <see cref="Pt2xInspector"/> and <see cref="Pt2xBuilder"/>:
/// the converter lives in the sibling <c>pt2-conv</c> project and is located by
/// <c>MSW_PT2CONV_PATH</c> or the repo's build output. Console output is the report.
/// </remarks>
public static class Pt2xPdf
{
    public sealed record Result(bool Success, byte[]? Pdf, string Report, string? Error);

    /// <summary>Which parts of the TriStation print set to render.</summary>
    public enum Mode { Fbd, Reports, Both }

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(
        double.TryParse(Environment.GetEnvironmentVariable("MSW_PT2CONV_TIMEOUT_MINUTES"), out var minutes) && minutes > 0
            ? minutes
            : 5);

    /// <summary>Parses a requested mode name; anything unrecognised falls back to <see cref="Mode.Fbd"/>.</summary>
    public static Mode ParseMode(string? value) => value?.ToLowerInvariant() switch
    {
        "reports" or "report" => Mode.Reports,
        "both" or "all" or "full" => Mode.Both,
        _ => Mode.Fbd,
    };

    public static async Task<Result> RenderAsync(byte[] project, string sourceName, string? projectName, CancellationToken cancellationToken)
        => await RenderAsync(project, sourceName, projectName, Mode.Fbd, cancellationToken);

    public static async Task<Result> RenderAsync(byte[] project, string sourceName, string? projectName, Mode mode, CancellationToken cancellationToken)
    {
        var executable = Pt2xBuilder.ResolveExecutable();
        if (executable is null)
        {
            return new Result(false, null, string.Empty,
                "Could not find pt2conv. Build it (dotnet build pt2-conv/src/Pt2Conv) or set MSW_PT2CONV_PATH to the executable.");
        }

        var work = Path.Combine(Path.GetTempPath(), "msw-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var inputPath = Path.Combine(work, "project.pt2x");
        var outPath = Path.Combine(work, "project.pdf");

        try
        {
            await File.WriteAllBytesAsync(inputPath, project, cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = work,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (mode == Mode.Fbd)
            {
                startInfo.ArgumentList.Add("topdf");
            }
            else
            {
                startInfo.ArgumentList.Add("toreport");
            }
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outPath);
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                startInfo.ArgumentList.Add("--name");
                startInfo.ArgumentList.Add(projectName);
            }
            if (mode != Mode.Fbd)
            {
                startInfo.ArgumentList.Add("--mode");
                startInfo.ArgumentList.Add(mode == Mode.Both ? "both" : "reports");
            }

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
                    $"pt2conv exited with code {process.ExitCode} and produced no PDF.");
            }

            var pdf = await File.ReadAllBytesAsync(outPath, cancellationToken);
            return new Result(true, pdf, report.ToString(), null);
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
}
