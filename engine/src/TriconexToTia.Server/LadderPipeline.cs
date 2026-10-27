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
            var readerClock = Stopwatch.StartNew();
            var (exit, output) = await RunAsync(circuitReader, cancellationToken,
                "analyze", sourcePath, "--format", "tristation", "--out", stem);
            readerClock.Stop();
            report.AppendLine(output);
            report.AppendLine($"circuit-reader: {readerClock.Elapsed.TotalSeconds:F1}s");

            var listingPath = stem + ".tristation";
            if (exit != 0 || !File.Exists(listingPath))
            {
                return new Result(false, null, report.ToString(),
                    $"circuit-reader exited with code {exit} and produced no listing.");
            }

            var equations = ParseListing(await File.ReadAllLinesAsync(listingPath, cancellationToken), out int ignoredLines);
            if (equations.Count == 0)
            {
                return new Result(false, null, report.ToString(),
                    "circuit-reader produced an empty equation listing.");
            }
            report.AppendLine($"equations: {equations.Count} row(s)" +
                              (ignoredLines > 0 ? $", {ignoredLines} non-row line(s) ignored" : ""));

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
    private static List<(string Logic, string Network)> ParseListing(string[] lines, out int ignored)
    {
        var rows = new List<(string Logic, string Network)>();
        ignored = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0) { ignored++; continue; }
            var network = line.Substring(0, tab).Trim();
            var logic = line.Substring(tab + 1).Trim();
            if (logic.Length == 0) { ignored++; continue; }
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
    /// original listing. Rows are grouped by program name across the whole listing (a program
    /// whose networks resume later still stays one group), preserving first-appearance order.
    /// </summary>
    private static List<BlGroup> BuildGroups(
        List<(string Logic, string Network)> equations, byte[]? originalListing, out string note)
    {
        var single = new List<BlGroup> { new(null, equations) };
        if (originalListing is null)
        {
            note = "grouping: none (no listing); one program";
            return single;
        }

        var headers = LptProgramMap.Headers(originalListing);
        var networkToProgram = LptProgramMap.NetworkToProgram(originalListing);
        if (networkToProgram.Count == 0)
        {
            note = "grouping: none (the listing has no PROGRAM= tags); one program";
            return single;
        }

        // Network numbers are normally unique; when two programs both use e.g. "#1" a number-keyed
        // map would collide, so align by listing order instead (the exporter keeps both in order).
        List<string?>? sequence = null;
        string extra = "";
        if (headers.Select(h => h.Network).Distinct().Count() != headers.Count)
        {
            var candidate = LptProgramMap.ProgramSequence(originalListing);
            if (candidate.Count == equations.Count)
            {
                sequence = candidate;
                extra = " (duplicate network numbers: grouped by listing order)";
            }
            else
            {
                extra = $" (warning: {headers.Count} network header(s) vs {equations.Count} equation row(s); grouped by number)";
            }
        }

        var order = new List<string>();
        var buckets = new Dictionary<string, List<(string Logic, string Network)>>(StringComparer.Ordinal);
        string fallback = headers.Select(h => h.Program).FirstOrDefault(p => p is not null)
                          ?? networkToProgram.OrderBy(kv => kv.Key).Select(kv => kv.Value).First();
        string? current = null;
        int unmapped = 0;

        for (var index = 0; index < equations.Count; index++)
        {
            var row = equations[index];
            string? name = sequence is not null
                ? sequence[index]
                : int.TryParse(row.Network, out var network) && networkToProgram.TryGetValue(network, out var mapped)
                    ? mapped
                    : null;
            if (name is null)
            {
                unmapped++;
                name = current ?? fallback; // keep the network order: a row without a tag stays with the previous program
            }
            if (!buckets.TryGetValue(name, out var bucket))
            {
                bucket = new List<(string Logic, string Network)>();
                buckets[name] = bucket;
                order.Add(name);
            }
            bucket.Add(row);
            current = name;
        }

        if (order.Count <= 1)
        {
            string only = order.Count == 1 ? order[0] : fallback;
            note = $"grouping: one program ('{only}')" + extra;
            return new List<BlGroup> { new(only, equations) };
        }

        var groups = new List<BlGroup>();
        foreach (var name in order) groups.Add(new BlGroup(name, buckets[name]));
        note = $"grouping: {groups.Count} programs from PROGRAM= tags" +
               (unmapped > 0 ? $" ({unmapped} row(s) carried to the preceding program)" : "") + extra;
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
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            string baseName = SanitizePouName(group.Name ?? ("PROGRAM_" + (i + 1)));
            // Sanitisation can collapse two different labels ("A-B" and "A_B"); keep the POU
            // names unique so the merged project never carries a duplicate definition.
            string name = baseName;
            for (var suffix = 2; !used.Add(name); suffix++) name = baseName + "_" + suffix;
            if (!string.Equals(name, group.Name, StringComparison.Ordinal))
                report.AppendLine($"note: program '{group.Name}' imported as '{name}'");

            var wb = Path.Combine(work, $"functions_{i}.xlsx");
            BlFunctionsWorkbook.Write(group.Rows, wb);
            var xp = Path.Combine(work, $"ladder_{i}.xml");
            var groupClock = Stopwatch.StartNew();
            var bytes = await RunOneAsync(ladderCli, wb, xp, dptPath, templatePath, report, reportStage, cancellationToken);
            groupClock.Stop();
            if (bytes is null) return null;
            RequireSingleGeneratedProgram(bytes, name);
            xmls.Add(bytes);
            names.Add(name);
            report.AppendLine($"group {i + 1}/{groups.Count}: {name} ({group.Rows.Count} rows) in {groupClock.Elapsed.TotalSeconds:F1}s");
        }

        var merged = MergeProgramXml(xmls, names, report);
        report.AppendLine($"project: {CountPrograms(merged)} program POU(s)");
        return merged;
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

    // BL emits the whole project template plus the one program it just built — always named
    // "SeimensLogic" (the default in LadderTextOperations_safety.cs). To stitch several runs into
    // one project we keep the first run's XML as the base, rename its generated program, then for
    // each later run rename its generated program and append only that POU. Template POUs (FBs,
    // TEMPLATES, P01_FB) are identical across runs, so they are kept once. Every rename is applied
    // document-wide so task/resource references never keep pointing at the old name.
    private static byte[] MergeProgramXml(List<byte[]> xmls, List<string> names, StringBuilder report)
    {
        if (xmls.Count == 0) throw new InvalidOperationException("The ladder reader produced no XML.");

        var baseDoc = Load(xmls[0]);
        var generated = FindGeneratedProgram(baseDoc)
            ?? throw new InvalidOperationException("BL output has no generated 'SeimensLogic' program POU.");
        RenameProgram(baseDoc, generated, names[0]);

        var pous = baseDoc.SelectSingleNode("//*[local-name()='types']/*[local-name()='pous']")
            ?? throw new InvalidOperationException("BL output has no project/types/pous container.");

        // Keep the generated programs contiguous: insert each new one right after the previous,
        // so they sit together (and before the template's TEMPLATES/P01_FB programs).
        XmlNode insertAfter = generated;
        for (var i = 1; i < xmls.Count; i++)
        {
            var doc = Load(xmls[i]);
            var gen = FindGeneratedProgram(doc)
                ?? throw new InvalidOperationException($"BL output for program '{names[i]}' has no generated program POU.");
            RenameProgram(doc, gen, names[i]);
            var imported = baseDoc.ImportNode(gen, true);
            pous.InsertAfter(imported, insertAfter);
            insertAfter = imported;
        }

        var dropped = DeduplicatePous(baseDoc);
        if (dropped.Count > 0)
            report.AppendLine($"template: removed {dropped.Count} repeated POU definition(s): {string.Join(", ", dropped.Distinct())}");
        ValidateProject(baseDoc, names);
        return Save(baseDoc);
    }

    /// <summary>
    /// Removes repeated POU definitions that came in with the BL template (a TriStation export can
    /// carry the same function block twice). Two definitions of one name make the editor merge or
    /// duplicate the body, so the first one is kept. Returns the removed names.
    /// </summary>
    private static List<string> DeduplicatePous(XmlDocument doc)
    {
        var removed = new List<string>();
        var pous = doc.SelectSingleNode("//*[local-name()='types']/*[local-name()='pous']");
        if (pous is null) return removed;

        var seen = new Dictionary<string, XmlElement>(StringComparer.OrdinalIgnoreCase);
        foreach (XmlNode node in pous.SelectNodes("./*[local-name()='pou']")!.Cast<XmlNode>().ToList())
        {
            if (node is not XmlElement el) continue;
            string name = el.GetAttribute("name");
            if (name.Length == 0) continue;
            if (!seen.TryGetValue(name, out _))
            {
                seen[name] = el;
                continue;
            }
            pous.RemoveChild(node);
            removed.Add(name);
        }
        return removed;
    }

    private static XmlDocument Load(byte[] xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(Encoding.UTF8.GetString(StripBom(xml)));
        return doc;
    }

    private static byte[] Save(XmlDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Fails the run when BL produced anything other than exactly one non-empty program.</summary>
    private static void RequireSingleGeneratedProgram(byte[] xml, string groupName)
    {
        var doc = Load(xml);
        int found = 0;
        foreach (XmlNode node in doc.SelectNodes("//*[local-name()='pou' and @pouType='program']")!)
        {
            if (node is XmlElement el &&
                string.Equals(el.GetAttribute("name"), "SeimensLogic", StringComparison.Ordinal))
            {
                found++;
                if (el.SelectSingleNode(".//*[local-name()='body']") == null)
                    throw new InvalidOperationException($"BL produced an empty program for '{groupName}'.");
            }
        }
        if (found != 1)
            throw new InvalidOperationException(
                $"BL produced {found} generated program POU(s) for '{groupName}' (expected exactly 1).");
    }

    /// <summary>Post-conditions for the merged project: every requested program is present exactly
    /// once, and no POU name is duplicated.</summary>
    private static void ValidateProject(XmlDocument doc, IReadOnlyList<string> expected)
    {
        var names = new List<string>();
        foreach (XmlNode node in doc.SelectNodes("//*[local-name()='pous']/*[local-name()='pou']")!)
            if (node is XmlElement el) names.Add(el.GetAttribute("name"));

        var duplicates = names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException("Merged project has duplicate POU name(s): " + string.Join(", ", duplicates));

        foreach (var name in expected)
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Program '{name}' is missing from the merged project.");
        }
    }

    // The generated program is the POU BL names "SeimensLogic"; the template's own programs are
    // TEMPLATES (twice) and P01_FB. Match on the default name, and only among program POUs.
    private static XmlElement? FindGeneratedProgram(XmlDocument doc)
    {
        foreach (XmlNode node in doc.SelectNodes("//*[local-name()='pou' and @pouType='program']")!)
        {
            if (node is XmlElement el &&
                string.Equals(el.GetAttribute("name"), "SeimensLogic", StringComparison.Ordinal))
                return el;
        }
        return null;
    }

    // The POU name lives in the element's attribute and in the body's <name> element. Other
    // references (e.g. the resource's task->pou binding) also carry it; patch every mention of the
    // default name in the whole document so nothing keeps pointing at the old name.
    private static void RenameProgram(XmlDocument doc, XmlElement pou, string newName)
    {
        pou.SetAttribute("name", newName);
        foreach (XmlNode node in doc.SelectNodes(".//*[@name='SeimensLogic']")!)
        {
            if (node is XmlElement el) el.SetAttribute("name", newName);
        }
        foreach (XmlNode node in doc.SelectNodes(".//*[local-name()='name' and text()='SeimensLogic']")!)
        {
            node.InnerText = newName;
        }
    }

    private static byte[] StripBom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..] : bytes;

    private static int CountPrograms(byte[] xml)
    {
        try
        {
            return Load(xml).SelectNodes("//*[local-name()='pou' and @pouType='program']")!.Count;
        }
        catch { return -1; }
    }

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
