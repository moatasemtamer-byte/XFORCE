using System.Diagnostics;
using System.Text;
using System.Xml;
using TriconexToTia.Engine.Ladder;

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
    /// <paramref name="reportStage"/> (when given) receives a short stage label before each
    /// long-running tool runs, so an HTTP caller can show real progress.
    /// </summary>
    public static async Task<Result> BuildXmlAsync(
        Stream source,
        string filename,
        Stream? dpt,
        Stream? template,
        Action<string>? reportStage,
        CancellationToken cancellationToken,
        byte[]? originalListing = null)
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
            reportStage?.Invoke("circuit-reader");
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

            var ladderCli = ResolveTool("MSW_LADDERCLI_PATH", new[]
            {
                @"C:\XforceMsw\tools\BlLadderCli\bin\Release\net48\BlLadderCli.exe",
            });
            if (ladderCli is null)
            {
                return new Result(false, null, report.ToString(),
                    "Could not find BlLadderCli. Set MSW_LADDERCLI_PATH to the executable.");
            }

            // The categorized LPT carries `PROGRAM=<name>` on its network-header lines. When it
            // does, each consecutive run of networks under one name becomes its own TriStation
            // program: BL emits ONE program per run (named from a placeholder row), so we run BL
            // once per group and merge the resulting POUs. Without the tags the listing is a
            // single unnamed program and BL names it from its default.
            var groups = BuildGroups(equations, originalListing, out var groupingNote);
            report.AppendLine(groupingNote);

            var xml = await RunBlAsync(ladderCli, work, equations, groups, dptPath, templatePath, report, reportStage, cancellationToken);
            if (xml is null)
            {
                return new Result(false, null, report.ToString(),
                    "BlLadderCli exited non-zero or produced no XML.");
            }

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

    // A single BL invocation: one workbook (optionally carrying a program-naming placeholder row),
    // run once, one XML out.
    private sealed record BlGroup(string? Name, List<(string Logic, string Network)> Rows);

    /// <summary>
    /// Splits the equation rows into TriStation programs using the <c>PROGRAM=</c> tags in the
    /// original listing. Returns a single unnamed group when the listing is uncategorized.
    /// </summary>
    private static List<BlGroup> BuildGroups(
        List<(string Logic, string Network)> equations, byte[]? originalListing, out string note)
    {
        if (originalListing is null || !LptProgramMap.HasProgramTags(originalListing))
        {
            note = "grouping: none (listing carries no PROGRAM= tags); one program";
            return new List<BlGroup> { new(null, equations) };
        }

        var networkToProgram = LptProgramMap.NetworkToProgram(originalListing);
        var groups = new List<BlGroup>();
        string? current = null;
        List<(string Logic, string Network)> bucket = new();

        void Flush()
        {
            if (bucket.Count > 0) groups.Add(new BlGroup(current, bucket));
            bucket = new();
        }

        foreach (var row in equations)
        {
            string? name = null;
            if (int.TryParse(row.Network, out var n) && networkToProgram.TryGetValue(n, out var mapped))
                name = mapped;

            if (name is null)
            {
                // No program for this network: keep it with the run we are in.
                bucket.Add(row);
                continue;
            }

            if (!string.Equals(name, current, StringComparison.Ordinal))
            {
                Flush();
                current = name;
            }
            bucket.Add(row);
        }
        Flush();

        note = $"grouping: {groups.Count} program(s) from PROGRAM= tags";
        return groups;
    }

    private static async Task<byte[]?> RunBlAsync(
        string ladderCli,
        string work,
        List<(string Logic, string Network)> equations,
        List<BlGroup> groups,
        string? dptPath,
        string? templatePath,
        StringBuilder report,
        Action<string>? reportStage,
        CancellationToken cancellationToken)
    {
        // Fast path: no grouping. One workbook, one run (BL names the program itself).
        if (groups.Count == 1 && groups[0].Name is null)
        {
            var wb = Path.Combine(work, "functions.xlsx");
            BlFunctionsWorkbook.Write(equations, wb);
            report.AppendLine($"equations: {equations.Count}");
            return await RunOneAsync(ladderCli, wb, Path.Combine(work, "ladder.xml"), dptPath, templatePath, report, reportStage, cancellationToken);
        }

        var xmls = new List<byte[]>();
        var names = new List<string>();
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var name = SanitizePouName(group.Name ?? ("PROGRAM_" + (i + 1)));
            // The naming row: BL scans for a row whose text contains "Logic" and uses its first
            // `@`-separated field as the program name (LadderTextOperations_safety.cs).
            var rows = new List<(string Logic, string Network)>(group.Rows.Count + 1)
            {
                (name + "@Logic", "0"),
            };
            rows.AddRange(group.Rows);

            var wb = Path.Combine(work, $"functions_{i}.xlsx");
            BlFunctionsWorkbook.Write(rows, wb);
            var xp = Path.Combine(work, $"ladder_{i}.xml");
            var bytes = await RunOneAsync(ladderCli, wb, xp, dptPath, templatePath, report, reportStage, cancellationToken);
            if (bytes is null) return null;
            xmls.Add(bytes);
            names.Add(name);
            report.AppendLine($"group {i + 1}/{groups.Count}: {name} ({group.Rows.Count} rows)");
        }

        return MergeProgramXml(xmls, names);
    }

    private static async Task<byte[]?> RunOneAsync(
        string ladderCli,
        string workbookPath,
        string xmlPath,
        string? dptPath,
        string? templatePath,
        StringBuilder report,
        Action<string>? reportStage,
        CancellationToken cancellationToken)
    {
        var cliArgs = new List<string>();
        if (dptPath is not null) cliArgs.AddRange(new[] { "--dpt", dptPath });
        cliArgs.AddRange(new[] { "--xlsx", workbookPath, "--out", xmlPath });
        if (templatePath is not null && File.Exists(templatePath)) cliArgs.AddRange(new[] { "--template", templatePath });

        reportStage?.Invoke("ladder cli");
        var (cliExit, cliOutput) = await RunAsync(ladderCli, cancellationToken, cliArgs.ToArray());
        report.AppendLine(cliOutput);

        if (cliExit != 0 || !File.Exists(xmlPath)) return null;
        return await File.ReadAllBytesAsync(xmlPath, cancellationToken);
    }

    // BL emits the whole project template plus the one program it just built. To stitch several
    // runs into one project we keep the first run's XML as the base (template + its program) and
    // append the generated program POU from every later run. The generated POU is the one whose
    // name matches the placeholder we fed in; template POUs (FBs, the shared TEMPLATES/P01_FB)
    // are common to every run, so they are NOT copied again.
    private static byte[] MergeProgramXml(List<byte[]> xmls, List<string> names)
    {
        if (xmls.Count == 0) return Array.Empty<byte>();
        if (xmls.Count == 1) return xmls[0];

        var baseDoc = new XmlDocument { PreserveWhitespace = true };
        baseDoc.LoadXml(Encoding.UTF8.GetString(StripBom(xmls[0])));
        var root = baseDoc.DocumentElement!;

        for (var i = 1; i < xmls.Count; i++)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(Encoding.UTF8.GetString(StripBom(xmls[i])));
            foreach (XmlNode node in doc.DocumentElement!.SelectNodes("./*[local-name()='pou']")!)
            {
                var import = baseDoc.ImportNode(node, true);
                root.AppendChild(import);
            }
        }

        using var ms = new MemoryStream();
        baseDoc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] StripBom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..] : bytes;

    private static string SanitizePouName(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (cleaned.Length == 0 || char.IsDigit(cleaned[0])) cleaned = "P_" + cleaned;
        return cleaned;
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
