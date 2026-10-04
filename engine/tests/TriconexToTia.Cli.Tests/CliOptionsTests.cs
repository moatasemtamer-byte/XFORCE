namespace TriconexToTia.Cli.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void Parse_WithInputAndOutput_Succeeds()
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "out" });

        Assert.Null(options.Error);
        Assert.Equal("a.xml", options.Input);
        Assert.Equal("out", options.Output);
        Assert.Equal(20, options.TiaVersion);
        Assert.False(options.ValidateOnly);
    }

    [Fact]
    public void Parse_WithoutInput_Fails()
    {
        Assert.Contains("--input is required", CliOptions.Parse(new[] { "--output", "out" }).Error);
    }

    [Fact]
    public void Parse_WithoutOutput_Fails()
    {
        Assert.Contains("--output is required", CliOptions.Parse(new[] { "--input", "a.xml" }).Error);
    }

    [Fact]
    public void Parse_ValidateOnly_DoesNotRequireOutput()
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--validate-only" });

        Assert.Null(options.Error);
        Assert.True(options.ValidateOnly);
    }

    [Fact]
    public void Parse_OptionWithoutValue_Fails()
    {
        Assert.Contains("--output requires a value", CliOptions.Parse(new[] { "--input", "a.xml", "--output" }).Error);
    }

    [Fact]
    public void Parse_UnknownArgument_Fails()
    {
        Assert.Contains("Unknown argument: --nope", CliOptions.Parse(new[] { "--nope" }).Error);
    }

    [Theory]
    [InlineData("17", 17)]
    [InlineData("20", 20)]
    [InlineData("21", 21)]
    public void Parse_TiaVersionInRange_IsAccepted(string text, int expected)
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--tia-version", text });

        Assert.Null(options.Error);
        Assert.Equal(expected, options.TiaVersion);
    }

    [Theory]
    [InlineData("16")]
    [InlineData("22")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Parse_TiaVersionOutOfRange_Fails(string text)
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--tia-version", text });

        Assert.Contains("must be between 17 and 21", options.Error);
    }

    [Fact]
    public void Parse_TiaVersionNotAnInteger_Fails()
    {
        Assert.Contains("--tia-version requires an integer", CliOptions.Parse(new[] { "--tia-version", "twenty" }).Error);
    }

    [Fact]
    public void Parse_PlcCpu_IsCaptured()
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--plc-cpu", "6ES7 151-8AB01-0AB0" });

        Assert.Null(options.Error);
        Assert.Equal("6ES7 151-8AB01-0AB0", options.PlcCpu);
    }

    [Fact]
    public void Parse_MappingsFileMissing_Fails()
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--mappings", "does-not-exist.json" });

        Assert.Contains("Mappings file not found", options.Error);
    }

    [Fact]
    public void Parse_MappingsFileExists_Succeeds()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "{}");
        try
        {
            var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--mappings", path });

            Assert.Null(options.Error);
            Assert.Equal(path, options.Mappings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Parse_Help_SkipsRequiredOptions(string flag)
    {
        var options = CliOptions.Parse(new[] { flag });

        Assert.True(options.ShowHelp);
        Assert.Null(options.Error);
    }

    [Theory]
    [InlineData("-v")]
    [InlineData("--version")]
    public void Parse_Version_SkipsRequiredOptions(string flag)
    {
        var options = CliOptions.Parse(new[] { flag });

        Assert.True(options.ShowVersion);
        Assert.Null(options.Error);
    }

    [Fact]
    public void Usage_DocumentsEveryOptionAndExitCode()
    {
        var usage = CliOptions.Usage;

        foreach (var option in new[] { "--input", "--output", "--tia-version", "--plc-cpu", "--mappings", "--validate-only", "--version", "--help" })
        {
            Assert.Contains(option, usage);
        }

        Assert.Contains("Exit codes:", usage);
    }
}
