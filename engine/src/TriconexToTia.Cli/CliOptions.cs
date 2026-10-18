using TriconexToTia.Core.Enums;

namespace TriconexToTia.Cli;

/// <summary>Parsed command-line arguments.</summary>
public sealed class CliOptions
{
    public const int MinTiaVersion = 16;
    public const int MaxTiaVersion = 21;

    public string? Input { get; private set; }

    public string? Output { get; private set; }

    public int TiaVersion { get; private set; } = 20;

    public string? PlcCpu { get; private set; }

    public bool ValidateOnly { get; private set; }

    public string? Mappings { get; private set; }

    public FbdOutputMode FbdOutput { get; private set; } = FbdOutputMode.Scl;

    public FbdBoxDialect FbdDialect { get; private set; } = FbdBoxDialect.V16Update3;

    public bool ShowHelp { get; private set; }

    public bool ShowVersion { get; private set; }

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
                case "-v":
                case "--version":
                    options.ShowVersion = true;
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
                    if (!TryTake(args, ref index, out var version)) options.Error = "--tia-version requires a value.";
                    else if (!int.TryParse(version, out var parsed)) options.Error = "--tia-version requires an integer.";
                    else if (parsed is < MinTiaVersion or > MaxTiaVersion)
                        options.Error = $"--tia-version must be between {MinTiaVersion} and {MaxTiaVersion} (got {parsed}).";
                    else options.TiaVersion = parsed;
                    break;
                case "--cpu-order-number":
                case "--plc-cpu":
                    if (!TryTake(args, ref index, out var cpu)) options.Error = "--cpu-order-number requires a value.";
                    else options.PlcCpu = cpu;
                    break;
case "--mappings":
                    if (!TryTake(args, ref index, out var mappings)) options.Error = "--mappings requires a value.";
                    else options.Mappings = mappings;
                    break;
                case "--fbd-output":
                    if (!TryTake(args, ref index, out var fbd)) options.Error = "--fbd-output requires a value.";
                    else if (!Enum.TryParse(fbd, ignoreCase: true, out FbdOutputMode mode))
                        options.Error = "--fbd-output must be one of: scl, fbd, auto.";
                    else options.FbdOutput = mode;
                    break;
                case "--fbd-dialect":
                    if (!TryTake(args, ref index, out var dialect)) options.Error = "--fbd-dialect requires a value.";
                    else if (!TryParseDialect(dialect, out var parsedDialect))
                        options.Error = "--fbd-dialect must be one of: v16-update3, v16-ga.";
                    else options.FbdDialect = parsedDialect;
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

        var done = options.ShowHelp || options.ShowVersion;

        if (!done && options.Error is null && string.IsNullOrWhiteSpace(options.Input))
        {
            options.Error = "--input is required.";
        }

        if (!done && options.Error is null && !options.ValidateOnly && string.IsNullOrWhiteSpace(options.Output))
        {
            options.Error = "--output is required unless --validate-only is used.";
        }

        if (!done && options.Error is null && options.Mappings is not null && !File.Exists(options.Mappings))
        {
            options.Error = $"Mappings file not found: {options.Mappings}";
        }

        return options;
    }

    private static bool TryParseDialect(string value, out FbdBoxDialect dialect)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "v16-update3":
            case "v16u3":
            case "update3":
            case "u3":
                dialect = FbdBoxDialect.V16Update3;
                return true;
            case "v16-ga":
            case "v16ga":
            case "ga":
            case "v16.0":
                dialect = FbdBoxDialect.V16Ga;
                return true;
            default:
                dialect = default;
                return false;
        }
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
        triconex-to-tia - converts a Triconex XML export into a Siemens TIA Portal import package.

        Usage:
          triconex-to-tia --input project.xml --output ./output [options]

        Options:
          --input <file>          Path to the Triconex XML file (required).
          --output <dir>          Output folder for the import package (required unless --validate-only).
          --tia-version <16-21>   TIA Portal version targeted by import.ps1 (default 20).
          --cpu-order-number/--plc-cpu <order>       CPU order number for import.ps1 hardware configuration.
          --mappings <file>       Custom mappings.json (defaults to the embedded table).
          --fbd-output <mode>     How FBD-language blocks are emitted: scl (default, today's
                                  behaviour), fbd (SimaticML XML with per-block SCL fallback), or
                                  auto (like fbd, but blocks needing approximations stay SCL).
          --fbd-dialect <dial>    V16 box-template dialect for --fbd-output: v16-update3 (default,
                                  the genuine export spelling) or v16-ga (the base V16.0 build).
          --validate-only         Validate the XML without generating any files.
          -v, --version           Show the tool version.
          -h, --help              Show this help.

        Output package layout:
          Blocks/*.scl, Globals/GlobalData.db, UDTs/*.xml, Tags/*.xml, import.ps1, conversion_report.txt

        Exit codes:
          0  Success.
          1  Conversion failed (input XML invalid, or an unexpected error occurred).
          2  Bad command line usage.
        """;
}




