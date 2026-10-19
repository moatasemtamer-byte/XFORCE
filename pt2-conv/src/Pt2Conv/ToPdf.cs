namespace Pt2Conv;

/// <summary>
/// <c>topdf</c> -- decompile a TriStation .pt2/.pt2x container and render its FBD
/// sheets to a PDF, entirely in-process (no TriStation installation or licence).
/// </summary>
public static class ToPdf
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: topdf <project.pt2x> <out.pdf> [--name <projectName>]");
            Console.Error.WriteLine("  <project.pt2x>: a TriStation .pt2/.pt2x project container");
            Console.Error.WriteLine("  --name: project name shown in the title block (default: file name)");
            return 1;
        }

        string input = args[1], outPath = args[2];
        string? nameArg = null;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--name" && i + 1 < args.Length) nameArg = args[++i];
        }

        var project = FromPt2.BuildPdfModel(input, nameArg);
        int sheets = project.Pous.Sum(p => p.Sheets.Count);
        PdfGenerator.Write(project, outPath);

        long bytes = new FileInfo(outPath).Length;
        Console.WriteLine(
            $"Wrote {outPath} ({bytes:N0} bytes): {project.Pous.Count} POU(s), {sheets} FBD sheet(s), {project.Tags.Count} tag(s)");
        return 0;
    }
}
