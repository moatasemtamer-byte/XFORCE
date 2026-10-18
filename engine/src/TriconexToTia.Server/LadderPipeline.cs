using System.Diagnostics;
using System.Text;

namespace TriconexToTia.Server;

/// <summary>
/// Runs the upstream chain that turns ladder evidence into the TriStation PLCopen XML the
/// rest of the engine already understands:
/// <c>PDF/LPT --[circuit-reader]--> equations --[BL Excel reader]--> XML</c>.
/// </summary>
/// <remarks>
/// Each stage is an out-of-process tool so the engine's own build graph stays clean, exactly
/// like <see cref="Pt2xBuilder"/>. Tools and their defaults are resolved from environment
/// variables so a host can override them without recompiling. Every stage runs in a fresh
/// temp workdir because BL writes sibling files next to its inputs.
///
/// circuit-reader emits the TRISTATION *functional-equation* listing (network # + logic). BL's
/// LPT reader wants the ASCII ladder-art printout instead, but its Excel reader accepts the
/// same equations, so the listing is staged into a "Functions" workbook and fed to BL with
/// <c>--xlsx</c>.
/// </remarks>
public static class LadderPipeline
{
    public sealed record Result(bool Success, byte[]? Xml, string Report, string? Error);

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(
        double.TryParse(Environment.GetEnvironmentVariable("MSW_LADDER_TIMEOUT_MINUTES"), out var minutes) && minutes > 0
            ? minutes
            : 30);

    /// <summary>
    /// Turns an uploaded PDF or LPT into TriStation PLCopen XML, returning the bytes.
    /// </summary>
    public static async Task<Result> BuildXmlAsync(
        Stream source,
        string filename,
        Stream? dpt,
        Stream? template,
        CancellationToken cancellationToken)
    {
        var report = new StringBuilder();

        var work = Path.Combine(Path.GetTempPath(), "msw-ladder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            // Stage every input into the workdir: BL writes sibling files next to whatever
            // paths it is handed, so nothing is read straight out of the upload stream.
            var stagedDpt = await StageFileAsync(dpt, Path.Combine(work, "input.dpt"), cancellationToken);
            var stagedTemplate = await StageFileAsync(template, Path.Combine(work, "template.xml"), cancellationToken);
            var templatePath = stagedTemplate ?? ResolveDefaultPath("MSW_LADDER_TEMPLATE_PATH");

            // The DPT is the TriStation tag database. It is OPTIONAL: the equation reader infers
            // operand types from the listing itself, so the logic builds with or without it. A DPT
            // that was supplied but is empty or the wrong format is reported up front; if none is
            // supplied the configured default (MSW_DPT_PATH) is used when it is itself valid,
            // otherwise the run is logic-only.
            string? dptPath = null;
            if (stagedDpt is not null)
            {
                if (DescribeDptProblem(stagedDpt) is { } uploadedDptProblem)
                {
                    return new Result(false, null, report.ToString(), uploadedDptProblem);
                }
                dptPath = stagedDpt;
            }
            else if (ResolveDefaultPath("MSW_DPT_PATH") is { } configuredDpt &&
                     File.Exists(configuredDpt) && DescribeDptProblem(configuredDpt) is null)
            {
                dptPath = configuredDpt;
            }

            var circuitReader = ResolveTool("MSW_CIRCUITREADER_PATH", new[]
            {
                @"C:\Users\Moatasem\circuit-reader\csharp\src\CircuitReader.Cli\bin\Release\net7.0\circuit-reader.exe",
            });
            if (circuitReader is null)
            {
                return new Result(false, null, report.ToString(),
                    "Could not find circuit-reader. Set MSW_CIRCUITREADER_PATH to the executable.");
            }

            // circuit-reader's analyze stage accepts both a ladder PDF and a raw ASCII listing,
            // so every input goes through it and comes out as a TRISTATION equation listing.
            var sourcePath = Path.Combine(work, Path.GetFileName(filename));
            await using (var input = File.Create(sourcePath))
            {
                await source.CopyToAsync(input, cancellationToken);
            }

            var stem = Path.Combine(work, "listing");
            var (exit, output) = await RunAsync(circuitReader, cancellationToken,
                "analyze", sourcePath, "--format", "tristation", "--out", stem);
            report.AppendLine(output);

            var listingPath = stem + ".tristation";
            if (exit != 0 || !File.Exists(listingPath))
            {
                return new Result(false, null, report.ToString(),
                    $"circuit-reader exited with code {exit} and produced no listing.");
            }

            var equations = ParseListing(await File.ReadAllLinesAsync(listingPath, cancellationToken));
            if (equations.Count == 0)
            {
                return new Result(false, null, report.ToString(),
                    "circuit-reader produced an empty equation listing.");
            }

            var workbookPath = Path.Combine(work, "functions.xlsx");
            BlFunctionsWorkbook.Write(equations, workbookPath);
            report.AppendLine($"equations: {equations.Count}");

            var ladderCli = ResolveTool("MSW_LADDERCLI_PATH", new[]
            {
                @"C:\XforceMsw\tools\BlLadderCli\bin\Release\net48\BlLadderCli.exe",
            });
            if (ladderCli is null)
            {
                return new Result(false, null, report.ToString(),
                    "Could not find BlLadderCli. Set MSW_LADDERCLI_PATH to the executable.");
            }

            var xmlPath = Path.Combine(work, "ladder.xml");
            var cliArgs = new List<string>();
            if (dptPath is not null) cliArgs.AddRange(new[] { "--dpt", dptPath });
            cliArgs.AddRange(new[] { "--xlsx", workbookPath, "--out", xmlPath });
            if (templatePath is not null && File.Exists(templatePath)) cliArgs.AddRange(new[] { "--template", templatePath });

            var (cliExit, cliOutput) = await RunAsync(ladderCli, cancellationToken, cliArgs.ToArray());
            report.AppendLine(cliOutput);

            if (cliExit != 0 || !File.Exists(xmlPath))
            {
                return new Result(false, null, report.ToString(),
                    $"BlLadderCli exited with code {cliExit} and produced no XML.");
            }

            // Read the XML back before the workdir is torn down.
            var xml = await File.ReadAllBytesAsync(xmlPath, cancellationToken);
            return new Result(true, xml, report.ToString(), null);
        }
        catch (Exception exception)
        {
            return new Result(false, null, report.ToString(), exception.Message);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* best effort */ }
        }
    }

    // The TRISTATION functional listing is tab-separated: "<network #>\t<logic>" with a
    // "Networks #\tLogic" header.  Emit it as (logic, network) pairs for the workbook.
    private static List<(string Logic, string Network)> ParseListing(string[] lines)
    {
        var rows = new List<(string Logic, string Network)>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            var network = line.Substring(0, tab).Trim();
            var logic = line.Substring(tab + 1).Trim();
            if (logic.Length == 0) continue;
            if (string.Equals(network, "Networks #", StringComparison.OrdinalIgnoreCase)) continue; // header
            rows.Add((logic, network));
        }
        return rows;
    }

    // A TRISTATION DPT export always carries a tag table (its header names the dictionary and
    // "TRISTATION"). BL dereferences that table without a null check, so an empty or wrong-file
    // upload shows up as a bare NullReferenceException. Catch it here and say what is actually
    // wrong: the uploaded DPT has no dictionary.
    private static string? DescribeDptProblem(string path)
    {
        var info = new FileInfo(path);
        if (info.Length < 64)
        {
            return $"The DPT dictionary '{info.Name}' is empty or too small ({info.Length} bytes). " +
                   "Upload the Triconex DPT export that lists the program's tags.";
        }

        string head;
        using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            var buffer = new char[4096];
            var read = reader.Read(buffer, 0, buffer.Length);
            head = new string(buffer, 0, read);
        }

        if (head.IndexOf("Dictionary", StringComparison.OrdinalIgnoreCase) < 0 &&
            head.IndexOf("TRISTATION", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return $"The DPT dictionary '{info.Name}' does not look like a Triconex DPT export " +
                   "(no tag dictionary header was found).";
        }

        return null;
    }

    private static async Task<string?> StageFileAsync(Stream? stream, string destination, CancellationToken cancellationToken)
    {
        if (stream is null) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var file = File.Create(destination);
        await stream.CopyToAsync(file, cancellationToken);
        return destination;
    }

    private static string? ResolveDefaultPath(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ResolveTool(string variable, string[] fallbacks)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        return fallbacks.FirstOrDefault(File.Exists);
    }

    private static async Task<(int Exit, string Output)> RunAsync(string executable, CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) output.AppendLine(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) output.AppendLine(args.Data); };

        if (!process.Start()) return (-1, "Failed to start " + executable + ".");

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
            return (-1, output.ToString() + Environment.NewLine + "Timed out.");
        }

        return (process.ExitCode, output.ToString());
    }
}
