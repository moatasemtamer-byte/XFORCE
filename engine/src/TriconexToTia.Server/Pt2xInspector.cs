using System.Diagnostics;
using System.Text;

namespace TriconexToTia.Server;

/// <summary>
/// Opens a TriStation .pt2/.pt2x container for review by running the bundled
/// <c>pt2conv</c> command-line tool (<c>tojson</c>) and returning its JSON model
/// (POUs, interface variables, FBD sheets with geometry and wires, global tags)
/// that the web viewer renders in the browser.
/// </summary>
/// <remarks>
/// Invoked out-of-process, matching <see cref="Pt2xBuilder"/> and <see cref="Pt2Importer"/>:
/// the converter lives in the sibling <c>pt2-conv</c> project and is located by
/// <c>MSW_PT2CONV_PATH</c> or the repo's build output. Console output is the report.
/// </remarks>
public static class Pt2xInspector
{
    public sealed record Result(bool Success, string? Json, string Report, string? Error);

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(
        double.TryParse(Environment.GetEnvironmentVariable("MSW_PT2CONV_TIMEOUT_MINUTES"), out var minutes) && minutes > 0
            ? minutes
            : 5);

    public static async Task<Result> InspectAsync(byte[] project, string sourceName, CancellationToken cancellationToken)
    {
        var executable = Pt2xBuilder.ResolveExecutable();
        if (executable is null)
        {
            return new Result(false, null, string.Empty,
                "Could not find pt2conv. Build it (dotnet build pt2-conv/src/Pt2Conv) or set MSW_PT2CONV_PATH to the executable.");
        }

        var work = Path.Combine(Path.GetTempPath(), "msw-inspect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var inputPath = Path.Combine(work, "project.pt2x");
        var outPath = Path.Combine(work, "project.json");

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
            startInfo.ArgumentList.Add("tojson");
            startInfo.ArgumentList.Add(inputPath);
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
                    $"pt2conv exited with code {process.ExitCode} and produced no model.");
            }

            var json = await File.ReadAllTextAsync(outPath, cancellationToken);
            return new Result(true, json, report.ToString(), null);
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
