namespace Pt2Conv;

/// <summary>
/// <c>toreport</c> -- render the TriStation report suite (POU Variables, Instance Variable
/// Connections, Program Variable Listing, Tagnames Complete Listing, POU Cross-Reference,
/// Shared Libraries, Project Implementation) from a .pt2/.pt2x container to a PDF, using
/// the embedded Telerik <c>.trdp</c> templates. Entirely in-process: no TriStation install
/// or licence, and it runs on Linux.
/// </summary>
public static class ToReport
{
    /// <summary>Which halves of the print set a combined render emits.</summary>
    public enum Mode { Fbd, Reports, Both }

    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: toreport <project.pt2x> <out.pdf> [--name <projectName>] [--report <name>] [--mode fbd|reports|both]");
            Console.Error.WriteLine("  <project.pt2x>: a TriStation .pt2/.pt2x project container");
            Console.Error.WriteLine("  --name:   project name shown in the report header (default: file name)");
            Console.Error.WriteLine("  --report: render only one report whose title contains <name> (e.g. tagnames)");
            Console.Error.WriteLine("  --mode:   fbd = FBD sheets only, reports = report suite only, both = FBD then reports (default: reports)");
            Console.Error.WriteLine("  --dump-text <file>: write every string the renderer draws (debug/verification)");
            Console.Error.WriteLine("  --layout-dump <file>: write the page/row/cell/text geometry as TSV (debug/verification)");
            return 1;
        }

        string input = args[1], outPath = args[2];
        string? nameArg = null, reportArg = null, dumpArg = null, layoutArg = null;
        var mode = Mode.Reports;
        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name": if (i + 1 < args.Length) nameArg = args[++i]; break;
                case "--report": if (i + 1 < args.Length) reportArg = args[++i]; break;
                case "--dump-text": if (i + 1 < args.Length) dumpArg = args[++i]; break;
                case "--layout-dump": if (i + 1 < args.Length) layoutArg = args[++i]; break;
                case "--mode": if (i + 1 < args.Length && !TryParseMode(args[++i], out mode)) return UsageError($"unknown --mode '{args[i]}'"); break;
            }
        }

        var data = FromPt2.BuildReportData(input, nameArg);
        ReportRenderer.Trace = dumpArg != null;
        ReportRenderer.LayoutTrace = layoutArg != null;

        long bytes;
        string summary;
        if (mode == Mode.Both)
        {
            var doc = new PdfSharpCore.Pdf.PdfDocument();
            PdfGenerator.DrawInto(doc, FromPt2.BuildPdfModel(input, nameArg));
            ReportRenderer.RenderInto(doc, data, reportArg);
            doc.Save(outPath);
            PdfPostProcess.NormalizeFontSubsetTags(outPath);
            bytes = new FileInfo(outPath).Length;
            summary = $"FBD sheets + report suite ({doc.PageCount} pages)";
        }
        else if (mode == Mode.Fbd)
        {
            var project = FromPt2.BuildPdfModel(input, nameArg);
            PdfGenerator.Write(project, outPath);
            bytes = new FileInfo(outPath).Length;
            summary = $"{project.Pous.Count} POU(s), {project.Pous.Sum(p => p.Sheets.Count)} FBD sheet(s)";
        }
        else
        {
            ReportRenderer.WriteSuite(data, outPath, reportArg);
            bytes = new FileInfo(outPath).Length;
            summary = $"POU vars {data.PouReport.Count}, instance vars {data.InstVarReport.Count}, " +
                $"program vars {data.ProgramVariableListing.Count}, tags {data.TagnameCompleteListing.Count}, " +
                $"xref {data.PouXRefReport.Count}, libs {data.SharedLibReport.Count}";
        }

        if (dumpArg != null)
        {
            File.WriteAllLines(dumpArg, ReportRenderer.DumpText());
            Console.WriteLine($"Dumped {ReportRenderer.DumpText().Count} drawn strings to {dumpArg}");
        }

        if (layoutArg != null)
        {
            var layout = ReportRenderer.DumpLayout();
            File.WriteAllLines(layoutArg, layout);
            Console.WriteLine($"Dumped {layout.Count} layout entries to {layoutArg}");
        }

        Console.WriteLine($"Wrote {outPath} ({bytes:N0} bytes): {summary}");
        return 0;
    }

    private static bool TryParseMode(string value, out Mode mode)
    {
        switch (value.ToLowerInvariant())
        {
            case "fbd": mode = Mode.Fbd; return true;
            case "reports": mode = Mode.Reports; return true;
            case "both": mode = Mode.Both; return true;
            default: mode = Mode.Reports; return false;
        }
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
