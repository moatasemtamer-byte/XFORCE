using TriconexToTia.Core.Results;
using TriconexToTia.Engine;

namespace TriconexToTia.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var options = CliOptions.Parse(args);

        if (options.ShowVersion)
        {
            Console.WriteLine($"triconex-to-tia {ConversionEngine.Version}");
            return 0;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(CliOptions.Usage);
            return 0;
        }

        if (options.Error is not null)
        {
            Console.Error.WriteLine(options.Error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptions.Usage);
            return 2;
        }

        var input = options.Input!;
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"Input file not found: {input}");
            return 2;
        }

        var converterOptions = new ConverterOptions
        {
            TiaVersion = options.TiaVersion,
            PlcCpu = options.PlcCpu,
            MappingsPath = options.Mappings,
            ValidateOnly = options.ValidateOnly,
            FbdOutput = options.FbdOutput,
            FbdDialect = options.FbdDialect,
        };

        ConversionResult result;
        try
        {
            using var stream = File.OpenRead(input);
            result = new ConversionEngine().Run(stream, converterOptions, Path.GetFileName(input));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unexpected error: {exception.Message}");
            return 1;
        }

        if (!result.Success)
        {
            Console.Error.WriteLine($"Conversion failed: {result.FatalError}");
            foreach (var issue in result.Validation.Fatal)
            {
                Console.Error.WriteLine($"  FATAL (line {issue.SourceLine}): {issue.Message}");
            }
            return 1;
        }

        if (options.ValidateOnly)
        {
            PrintValidation(result);
            return 0;
        }

        var output = options.Output!;
        WarnIfNotEmpty(output);
        Directory.CreateDirectory(output);

        var written = 0;
        foreach (var file in result.Files)
        {
            var relative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(output, relative);
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(target, file.ToBytes());
            written++;
        }

        Console.WriteLine($"Converted \"{result.Project.Name}\" from {Path.GetFileName(input)}");
        Console.WriteLine($"  Blocks   : {result.Project.Blocks.Count}");
        Console.WriteLine($"  UDTs     : {result.Project.DataTypes.Count}");
        Console.WriteLine($"  Tags     : {result.Project.IoPoints.Count + result.Project.GlobalVariables.Count}");
        Console.WriteLine($"  Warnings : {result.Validation.Warnings.Count()}");
        Console.WriteLine($"  Manual   : {result.Mapping.ManualActions.Count}");
        Console.WriteLine($"  Files    : {written} written to {Path.GetFullPath(output)}");
        Console.WriteLine($"  Report   : {Path.Combine(Path.GetFullPath(output), "conversion_report.txt")}");

        if (result.Mapping.ManualActions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{result.Mapping.ManualActions.Count} item(s) need manual action in TIA Portal; see the report for details.");
        }

        return 0;
    }

    private static void WarnIfNotEmpty(string output)
    {
        if (!Directory.Exists(output)) return;

        try
        {
            if (Directory.EnumerateFileSystemEntries(output, "*", SearchOption.AllDirectories).Any())
            {
                Console.Error.WriteLine($"Warning: output directory is not empty; same-named files will be overwritten ({Path.GetFullPath(output)}).");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: could not inspect output directory: {exception.Message}");
        }
    }

    private static void PrintValidation(ConversionResult result)
    {
        Console.WriteLine($"Validation of \"{result.Project.Name}\" succeeded.");
        Console.WriteLine($"  Blocks   : {result.Project.Blocks.Count}");
        Console.WriteLine($"  UDTs     : {result.Project.DataTypes.Count}");
        Console.WriteLine($"  Tags     : {result.Project.IoPoints.Count + result.Project.GlobalVariables.Count}");

        foreach (var warning in result.Validation.Warnings)
        {
            Console.WriteLine($"  WARNING (line {warning.SourceLine}): {warning.Message}");
        }

        foreach (var info in result.Validation.Infos)
        {
            Console.WriteLine($"  INFO (line {info.SourceLine}): {info.Message}");
        }
    }
}
