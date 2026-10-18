using CircuitReader.Core;

namespace CircuitReader.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) { Usage(); return 2; }
            return args[0] switch
            {
                "analyze" => Analyze(args),
                "dump-text" => DumpText(args),
                "dump-words" => DumpWords(args),
                "tri" => Tri(args),
                "-h" or "--help" or "help" => Usage(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            circuit-reader — PLC ladder-logic / circuit PDF analyzer

            usage:
                  circuit-reader analyze <file> [--out <stem>] [--format <list>]
                  circuit-reader dump-text <file> [page]

                <file> may be a ladder-logic PDF or a raw ASCII program listing
                (e.g. a Triconex TRISTATION ".LPT"/".txt" printout).

                analyze options:
                  --out <stem>      output path stem (default: next to the input, "<name>.report")
                  --format <list>   comma-separated: md,csv,json,html,xlsx,tristation  or  all  (default: md)
                                    (tristation = TRISTATION functional listing, Triconex only)

                examples:
                  circuit-reader analyze north.pdf --format all
                  circuit-reader analyze UNIT40_1.LPT --format md,xlsx
                  circuit-reader analyze plant.pdf --out C:\out\plant --format md,xlsx
            """);
        return 0;
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"unknown command: {cmd}");
        Usage();
        return 2;
    }

    private static int Analyze(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("analyze: missing <pdf>"); return 2; }
        string pdf = args[1];
        if (!File.Exists(pdf)) { Console.Error.WriteLine($"analyze: file not found: {pdf}"); return 2; }

        string? outStem = null;
        var formats = new List<string> { "md" };
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                    if (++i >= args.Length) { Console.Error.WriteLine("--out needs a value"); return 2; }
                    outStem = args[i];
                    break;
                case "--format":
                    if (++i >= args.Length) { Console.Error.WriteLine("--format needs a value"); return 2; }
                    formats = ParseFormats(args[i]);
                    break;
                default:
                    Console.Error.WriteLine($"analyze: unexpected argument '{args[i]}'");
                    return 2;
            }
        }

        outStem ??= Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(pdf)) ?? ".",
            Path.GetFileNameWithoutExtension(pdf) + ".report");

        var pdfName = Path.GetFileName(pdf);
        Console.WriteLine($"Analyzing {pdfName} …");
        var result = DocumentAnalyzer.Analyze(pdf);
        var xref = XrefIndex.Build(result.Pages, result.Dialect);

        Console.WriteLine($"  dialect : {DocumentAnalyzer.DialectName(result.Dialect)}");
        Console.WriteLine($"  pages   : {result.PageCount}");
        Console.WriteLine($"  rungs   : {result.RungCount}");
        Console.WriteLine($"  addrs   : {xref.ByAddress.Count} (state-vars {xref.StateVariables.Count()}, " +
                          $"dangling {xref.DanglingReads.Count()}, multi-writer {xref.MultipleWriters.Count()})");

        foreach (var fmt in formats)
        {
            string path = outStem + "." + fmt;
            switch (fmt)
            {
                case "md":
                    File.WriteAllText(path, ReportWriter.ToMarkdown(pdfName, result.Pages, result.Dialect, xref));
                    break;
                case "csv":
                    CsvExporter.Write(result.Pages, path);
                    break;
                case "json":
                    JsonExporter.Write(pdfName, result.Dialect, result.Pages, path, xref);
                    break;
                case "html":
                    HtmlExporter.Write(pdfName, result.Dialect, result.Pages, path, xref);
                    break;
                case "xlsx":
                    XlsxExporter.Write(new[] { XlsxExporter.RungsSheet(result.Pages), XrefReport.CrossRefSheet(xref) }, path);
                    break;
                case "tristation":
                    File.WriteAllText(path, TristationExporter.Render(result.Pages));
                    break;
                default:
                    Console.Error.WriteLine($"  (skipped unknown format '{fmt}')");
                    continue;
            }
            Console.WriteLine($"  wrote   : {path}");
        }
        return 0;
    }

    private static List<string> ParseFormats(string spec)
    {
        if (spec.Equals("all", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "md", "csv", "json", "html", "xlsx" };
        return spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Select(s => s.ToLowerInvariant())
                   .Distinct()
                   .ToList();
    }

    private static int DumpText(string[] args)
    {
        string pdf = args[1];
        int page = args.Length > 2 ? int.Parse(args[2]) : 0;
        foreach (var p in DocumentAnalyzer.ReadSource(pdf))
        {
            if (page != 0 && p.PageNumber != page) continue;
            Console.WriteLine($"##### PAGE {p.PageNumber} #####");
            foreach (var line in p.TextLines)
                Console.WriteLine(line);
            if (page != 0 && p.PageNumber == page) break;
        }
        return 0;
    }

    private static int DumpWords(string[] args)
    {
        string pdf = args[1];
        int page = args.Length > 2 ? int.Parse(args[2]) : 1;
        foreach (var p in PdfReader.ReadPages(pdf))
        {
            if (p.PageNumber != page) continue;
            Console.WriteLine($"##### PAGE {p.PageNumber}  W={p.Width:F0} H={p.Height:F0} #####");
            foreach (var w in p.Words.OrderBy(w => Math.Round(w.Y / 4)).ThenBy(w => w.X))
                Console.WriteLine($"{w.X,7:F1} {w.Y,7:F1}  {w.Text}");
            break;
        }
        return 0;
    }

    private static int Tri(string[] args)
    {
        string pdf = args[1];
        var wanted = new HashSet<int>();
        for (int i = 2; i < args.Length; i++) wanted.Add(int.Parse(args[i]));

        var pages = DocumentAnalyzer.ReadSource(pdf).Select(p => ((IReadOnlyList<string>)p.TextLines, p.PageNumber));
        var doc = TriconexParser.Parse(pages);
        Console.WriteLine($"networks={doc.NetworkCount} rungs={doc.RungCount} labels={doc.AddressLabels.Count}");
        foreach (var net in doc.Networks)
        {
            if (wanted.Count > 0 && !wanted.Contains(net.Number)) continue;
            Console.WriteLine($"\n=== Network {net.Number} (page {net.Page}, {net.Kind}, {net.SizeBytes} bytes) ===");
            if (net.Comment.Length > 0) Console.WriteLine($"  ; {net.Comment}");
            foreach (var r in net.Rungs)
            {
                Console.WriteLine($"  BOOL: {r.BooleanEquation}");
                Console.WriteLine($"  FUNC: {r.FunctionEquation}");
                if (r.Note is not null) Console.WriteLine($"  note: {r.Note}");
            }
        }
        return 0;
    }
}
