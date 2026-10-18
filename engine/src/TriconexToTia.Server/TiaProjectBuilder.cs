using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Server;

/// <summary>Outcome of turning a conversion package into a real TIA Portal project.</summary>
public sealed record TiaProjectBuildResult(
    bool Success,
    string ProjectName,
    byte[]? Zip,
    string Log,
    string? Error);

/// <summary>
/// Builds an openable TIA Portal project (.ap16) from a conversion package by driving the
/// package's own <c>import.ps1</c> through the Siemens Openness API.
///
/// TIA Portal cannot be fabricated by code: the only supported way to create a project is the
/// Openness API, which needs TIA Portal installed on this machine. The import step is slow
/// (minutes) and single-instance, so callers serialise it through <paramref name="gate"/>.
/// </summary>
public static class TiaProjectBuilder
{
    private static readonly TimeSpan ImportTimeout = TimeSpan.FromMinutes(45);

    public static async Task<TiaProjectBuildResult> BuildAsync(
        ConversionResult result,
        string tiaInstallPath,
        string? cpuOrderNumber,
        string? projectsDir,
        SemaphoreSlim gate,
        CancellationToken cancellationToken = default)
    {
        var name = Sanitize(string.IsNullOrWhiteSpace(result.Project.Name) ? "ConvertedProject" : result.Project.Name!);
        var root = Path.Combine(Path.GetTempPath(), "msw-tia-" + Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(root, "package");
        var projectsRoot = string.IsNullOrWhiteSpace(projectsDir)
            ? Path.Combine(root, "projects")
            : Path.Combine(projectsDir, "msw-" + Guid.NewGuid().ToString("N"));

        var log = new StringBuilder();

        try
        {
            Directory.CreateDirectory(packageDir);
            foreach (var file in result.Files)
            {
                var full = Path.Combine(packageDir, file.RelativePath);
                var folder = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                await File.WriteAllBytesAsync(full, file.ToBytes(), cancellationToken);
            }

            var script = Path.Combine(packageDir, "import.ps1");
            if (!File.Exists(script))
            {
                return new TiaProjectBuildResult(false, name, null, log.ToString(),
                    "The generated package does not contain import.ps1, so no TIA project can be built.");
            }

            // A CPU must be added or the import script saves an empty project and reports success.
            // The order number comes from the engine's environment, or from the value the converter
            // baked into the script from the request's PLC CPU option.
            var effectiveCpu = ResolveCpuOrderNumber(script, cpuOrderNumber);
            if (string.IsNullOrWhiteSpace(effectiveCpu))
            {
                return new TiaProjectBuildResult(false, name, null, log.ToString(),
                    "No CPU order number is available, so TIA would only create an empty project. "
                    + "Set MSW_TIA_CPU_ORDER_NUMBER on the engine host, or supply the CPU order number in the request.");
            }

            Directory.CreateDirectory(projectsRoot);
            var projectDir = Path.Combine(projectsRoot, name);
            if (Directory.Exists(projectDir)) Directory.Delete(projectDir, recursive: true);

            await gate.WaitAsync(cancellationToken);
            try
            {
                var exitCode = await RunImportAsync(script, packageDir, projectsRoot, name, tiaInstallPath, effectiveCpu, log, cancellationToken);
                if (exitCode is null)
                {
                    return new TiaProjectBuildResult(false, name, null, log.ToString(),
                        "The TIA import timed out.");
                }
                if (exitCode != 0)
                {
                    return new TiaProjectBuildResult(false, name, null, log.ToString(),
                        $"The TIA import failed (exit code {exitCode}). See the log for the Openness message.");
                }
            }
            finally
            {
                gate.Release();
            }

            if (!Directory.Exists(projectDir))
            {
                return new TiaProjectBuildResult(false, name, null, log.ToString(),
                    "The TIA import reported success but produced no project folder.");
            }

            var zip = ZipDirectory(projectDir, name);
            return new TiaProjectBuildResult(true, name, zip, log.ToString(), null);
        }
        catch (Exception exception)
        {
            return new TiaProjectBuildResult(false, name, null, log.ToString(), exception.Message);
        }
        finally
        {
            TryDelete(root);
            if (!string.IsNullOrWhiteSpace(projectsDir)) TryDelete(projectsRoot);
        }
    }

    private static async Task<int?> RunImportAsync(
        string script,
        string workingDirectory,
        string projectsRoot,
        string projectName,
        string tiaInstallPath,
        string? cpuOrderNumber,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in new[]
                 {
                     "-NoProfile",
                     "-NonInteractive",
                     "-ExecutionPolicy", "Bypass",
                     "-File", script,
                     "-ProjectName", projectName,
                     "-ProjectPath", projectsRoot,
                     "-TiaInstallPath", tiaInstallPath,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(cpuOrderNumber))
        {
            startInfo.ArgumentList.Add("-CpuOrderNumber");
            startInfo.ArgumentList.Add(cpuOrderNumber!);
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) lock (log) log.AppendLine(eventArgs.Data); };
        process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) lock (log) log.AppendLine(eventArgs.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ImportTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return null;
        }

        return process.ExitCode;
    }

    private static string? ResolveCpuOrderNumber(string scriptPath, string? overrideCpu)
    {
        if (!string.IsNullOrWhiteSpace(overrideCpu)) return overrideCpu!.Trim();

        try
        {
            var text = File.ReadAllText(scriptPath);
            var match = System.Text.RegularExpressions.Regex.Match(text, "\\$CpuOrderNumber\\s*=\\s*\"(?<v>[^\"]*)\"");
            if (match.Success)
            {
                var value = match.Groups["v"].Value.Trim();
                if (value.Length > 0) return value;
            }
        }
        catch
        {
            // Fall through: treated as "no CPU configured".
        }

        return null;
    }

    private static byte[] ZipDirectory(string directory, string rootName)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                var entry = archive.CreateEntry($"{rootName}/{relative}", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var source = File.OpenRead(file);
                source.CopyTo(entryStream);
            }
        }
        return memory.ToArray();
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "ConvertedProject" : cleaned;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort */ }
    }
}
