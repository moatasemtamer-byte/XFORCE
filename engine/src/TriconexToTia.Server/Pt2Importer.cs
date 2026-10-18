using System.Diagnostics;
using System.Text;

namespace TriconexToTia.Server;

/// <summary>
/// Decompiles a TriStation project container (<c>.pt2</c>/<c>.pt2x</c>, an OLE/CFB compound file) into
/// the TriStation PLCopen XML the converter consumes, by running the bundled <c>pt2conv frompt2</c>
/// tool out-of-process. This lets an operator hand in a raw TriStation project instead of having to
/// export XML from TriStation first.
/// </summary>
public static class Pt2Importer
{
    public sealed record Result(bool Success, byte[]? Xml, string Report, string? Error);

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(
        double.TryParse(Environment.GetEnvironmentVariable("MSW_PT2CONV_TIMEOUT_MINUTES"), out var minutes) && minutes > 0
            ? minutes
            : 5);

    /// <summary>True when the bytes carry the OLE compound-file signature used by TriStation
    /// <c>.pt2</c>/<c>.pt2x</c> containers (as opposed to a ZIP or an XML stream).</summary>
    public static bool LooksLikePt2(byte[] bytes)
    {
        if (bytes.Length < 8) return false;
        // D0 CF 11 E0 A1 B1 1A E1
        return bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0
            && bytes[4] == 0xA1 && bytes[5] == 0xB1 && bytes[6] == 0x1A && bytes[7] == 0xE1;
    }

    public static async Task<Result> ConvertAsync(byte[] source, string sourceName, CancellationToken cancellationToken)
    {
        var executable = Pt2xBuilder.ResolveExecutable();
        if (executable is null)
        {
            return new Result(false, null, string.Empty,
                "Could not find pt2conv. Build it (dotnet build pt2-conv/src/Pt2Conv) or set MSW_PT2CONV_PATH to the executable.");
        }

        var work = Path.Combine(Path.GetTempPath(), "msw-pt2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var name = Path.GetFileNameWithoutExtension(sourceName) is { Length: > 0 } n ? n : "Converted";
        var inputPath = Path.Combine(work, Sanitize(sourceName));
        var outPath = Path.Combine(work, "tristation.xml");

        try
        {
            await File.WriteAllBytesAsync(inputPath, source, cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = work,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("frompt2");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outPath);
            startInfo.ArgumentList.Add("--name");
            startInfo.ArgumentList.Add(name);

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
                    $"frompt2 exited with code {process.ExitCode} and produced no XML.");
            }

            var xml = await File.ReadAllBytesAsync(outPath, cancellationToken);
            return new Result(true, xml, report.ToString(), null);
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

    private static string Sanitize(string name)
    {
        var basename = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(basename)) basename = "project.pt2x";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(basename.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
