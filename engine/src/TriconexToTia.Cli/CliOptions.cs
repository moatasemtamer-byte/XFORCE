namespace TriconexToTia.Cli;

/// <summary>Parsed command-line arguments.</summary>
public sealed class CliOptions
{
    public string? Input { get; private set; }

    public string? Output { get; private set; }

    public int TiaVersion { get; private set; } = 20;

    public string? PlcCpu { get; private set; }

    public bool ValidateOnly { get; private set; }

    public string? Mappings { get; private set; }

    public bool ShowHelp { get; private set; }

    public string? Error { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--input":
                    if (!TryTake(args, ref index, out var input)) options.Error = "--input requires a value.";
                    else options.Input = input;
                    break;
                case "--output":
                    if (!TryTake(args, ref index, out var output)) options.Error = "--output requires a value.";
                    else options.Output = output;
                    break;
                case "--tia-version":
                    if (!TryTake(args, ref index, out var version) || !int.TryParse(version, out var parsed))
                    {
                        options.Error = "--tia-version requires an integer (17-21).";
                    }
                    else
                    {
                        options.TiaVersion = parsed;
                    }
                    break;
                case "--plc-cpu":
                    if (!TryTake(args, ref index, out var cpu)) options.Error = "--plc-cpu requires a value.";
                    else options.PlcCpu = cpu;
                    break;
                case "--mappings":
                    if (!TryTake(args, ref index, out var mappings)) options.Error = "--mappings requires a value.";
                    else options.Mappings = mappings;
                    break;
                case "--validate-only":
                    options.ValidateOnly = true;
                    break;
                default:
                    options.Error = $"Unknown argument: {argument}";
                    break;
            }

            if (options.Error is not null) break;
        }

        if (!options.ShowHelp && options.Error is null && string.IsNullOrWhiteSpace(options.Input))
        {
            options.Error = "--input is required.";
        }

        if (!options.ShowHelp && options.Error is null && !options.ValidateOnly && string.IsNullOrWhiteSpace(options.Output))
        {
            options.Error = "--output is required unless --validate-only is used.";
        }

        return options;
    }

    private static bool TryTake(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }
        index++;
        value = args[index];
        return true;
    }

    public static string Usage =>
        """
        triconex-to-tia — converts a Triconex XML export into a Siemens TIA Portal import package.

        Usage:
          triconex-to-tia --input project.xml --output ./output [options]

        Options:
          --input <file>         Path to the Triconex XML file (required).
          --output <dir>         Output folder for the import package (required unless --validate-only).
          --tia-version <17-21>  TIA Portal version targeted by import.ps1 (default 20).
          --plc-cpu <order>      CPU order number for import.ps1 hardware configuration.
          --mappings <file>      Custom mappings.json (defaults to the embedded table).
          --validate-only        Validate the XML without generating any files.
          -h, --help             Show this help.

        Output package layout:
          Blocks/*.scl, UDTs/*.xml, Tags/*.xml, import.ps1, conversion_report.txt
        """;
}
