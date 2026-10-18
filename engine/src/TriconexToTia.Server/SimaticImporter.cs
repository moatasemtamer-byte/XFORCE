using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace TriconexToTia.Server;

/// <summary>
/// Translates a Siemens SimaticML export (the output of TIA Portal Openness, i.e. an "AP" export:
/// a <c>.zip</c> of <c>Blocks/</c> + <c>Tags/</c> + <c>UDTs/</c>, or a single block <c>.xml</c>) into
/// the TriStation PLCopen XML that the PT2X builder consumes. This is the inverse of the engine's
/// Triconex-to-TIA direction and runs the bundled <c>pt2conv fromsimatic</c> tool out-of-process,
/// mirroring <see cref="Pt2xBuilder"/>.
/// </summary>
public static class SimaticImporter
{
    public sealed record Result(bool Success, byte[]? Xml, string Report, string? Error);

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(
        double.TryParse(Environment.GetEnvironmentVariable("MSW_PT2CONV_TIMEOUT_MINUTES"), out var minutes) && minutes > 0
            ? minutes
            : 5);

    /// <summary>True when the bytes look like a SimaticML export even if the name carries no hint.
    /// A single block <c>.xml</c> is sniffed by its head; a <c>.zip</c> is inspected entry-by-entry
    /// (a TIA Openness export lays Blocks/Tags/UDTs out as XML entries, whereas a project archive
    /// does not), so a plain TriStation XML or a TIA project archive is not mistaken for one.</summary>
    public static bool LooksLikeSimaticMl(byte[] bytes)
    {
        if (bytes.Length == 0) return false;

        if (IsZip(bytes))
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                var inspected = 0;
                foreach (var entry in archive.Entries)
                {
                    if (entry.Length == 0) continue;
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;

                    var looksLaidOut = name.Contains("Blocks/", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Tags/", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("UDTs/", StringComparison.OrdinalIgnoreCase);
                    if (EntryLooksLikeSimaticMl(entry)) return true;
                    if (looksLaidOut) return true;

                    if (++inspected >= 64) break;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        return HeadLooksLikeSimaticMl(bytes);
    }

    private static bool HeadLooksLikeSimaticMl(ReadOnlySpan<byte> bytes)
    {
        // A SimaticML root/namespace appears within the first few KB.
        var length = Math.Min(bytes.Length, 16384);
        if (length == 0) return false;
        var head = Encoding.UTF8.GetString(bytes[..length]);
        return head.Contains("SW.Blocks", StringComparison.Ordinal)
            || head.Contains("SW.Tags", StringComparison.Ordinal)
            || head.Contains("SW.Types", StringComparison.Ordinal)
            || head.Contains("automation/Openness", StringComparison.Ordinal);
    }

    private static bool EntryLooksLikeSimaticMl(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            var buffer = new byte[8192];
            var read = stream.Read(buffer, 0, buffer.Length);
            return read > 0 && HeadLooksLikeSimaticMl(buffer.AsSpan(0, read));
        }
        catch
        {
            return false;
        }
    }

    public static async Task<Result> ConvertAsync(byte[] source, string sourceName, CancellationToken cancellationToken)
    {
        var executable = Pt2xBuilder.ResolveExecutable();
        if (executable is null)
        {
            return new Result(false, null, string.Empty,
                "Could not find pt2conv. Build it (dotnet build pt2-conv/src/Pt2Conv) or set MSW_PT2CONV_PATH to the executable.");
        }

        var work = Path.Combine(Path.GetTempPath(), "msw-simatic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var inputDir = Path.Combine(work, "input");
        Directory.CreateDirectory(inputDir);
        var outPath = Path.Combine(work, "tristation.xml");
        var inputPath = inputDir;

        try
        {
            if (IsZip(source))
            {
                var zipPath = Path.Combine(work, "export.zip");
                await File.WriteAllBytesAsync(zipPath, source, cancellationToken);
                ZipFile.ExtractToDirectory(zipPath, inputDir, overwriteFiles: true);
            }
            else
            {
                inputPath = Path.Combine(inputDir, Sanitize(sourceName));
                await File.WriteAllBytesAsync(inputPath, source, cancellationToken);
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
            startInfo.ArgumentList.Add("fromsimatic");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outPath);
            startInfo.ArgumentList.Add("--name");
            startInfo.ArgumentList.Add(Path.GetFileNameWithoutExtension(sourceName) is { Length: > 0 } n ? n : "Converted");

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
                    $"fromsimatic exited with code {process.ExitCode} and produced no XML.");
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

    private static bool IsZip(byte[] bytes)
        => bytes.Length >= 2 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K';

    private static string Sanitize(string name)
    {
        var basename = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(basename)) basename = "export.xml";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(basename.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
