namespace SessionContinue;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Hidden headless mode used for automated verification of the same service
        // classes the GUI drives: --cli list|nexttitle|clone …
        if (args.Length >= 2 && args[0] == "--cli")
        {
            return RunCli(args);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunCli(string[] args)
    {
        using var cli = new OcCli();
        var service = new CloneService(cli);
        try
        {
            switch (args[1])
            {
                case "list":
                {
                    if (args.Length < 3) { Console.Error.WriteLine("usage: --cli list <projectDir>"); return 2; }
                    var rows = service.ListAsync(args[2]).GetAwaiter().GetResult();
                    foreach (var r in rows)
                        Console.WriteLine($"{r.Id}\t{r.Updated}\t{r.Title}");
                    return 0;
                }
                case "nexttitle":
                {
                    if (args.Length < 3) { Console.Error.WriteLine("usage: --cli nexttitle <current> [existing…]"); return 2; }
                    var existing = args.Skip(3).ToArray();
                    Console.WriteLine(CloneService.NextTitle(args[2], existing));
                    return 0;
                }
                case "clone":
                {
                    if (args.Length < 4) { Console.Error.WriteLine("usage: --cli clone <projectDir> <sessionId> [newTitle]"); return 2; }
                    var projectDir = args[2];
                    var rows = service.ListAsync(projectDir).GetAwaiter().GetResult();
                    var source = rows.FirstOrDefault(r => r.Id == args[3])
                                 ?? throw new InvalidOperationException("session not found: " + args[3]);
                    var title = args.Length >= 5 ? args[4] : null;
                    var result = service.CloneAsync(projectDir, source, title, rows.Select(r => r.Title).ToList(),
                        status => Console.Error.WriteLine(status)).GetAwaiter().GetResult();
                    Console.WriteLine("newid=" + result.NewId);
                    Console.WriteLine("title=" + result.NewTitle);
                    Console.WriteLine("export=" + result.ExportPath);
                    return 0;
                }
                default:
                    Console.Error.WriteLine("unknown --cli command: " + args[1]);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
