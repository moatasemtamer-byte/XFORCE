using TriconexToTia.Core.Enums;

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
    [InlineData("16", 16)]
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
    [InlineData("22")]
    [InlineData("15")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Parse_TiaVersionOutOfRange_Fails(string text)
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--tia-version", text });

        Assert.Contains("must be between 16 and 21", options.Error);
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

        foreach (var option in new[] { "--input", "--output", "--tia-version", "--plc-cpu", "--mappings", "--fbd-output", "--fbd-dialect", "--validate-only", "--version", "--help" })
        {
            Assert.Contains(option, usage);
        }

        Assert.Contains("Exit codes:", usage);
    }

    [Fact]
    public void Parse_FbdOutput_DefaultsToScl()
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o" });

        Assert.Null(options.Error);
        Assert.Equal(FbdOutputMode.Scl, options.FbdOutput);
    }

    [Theory]
    [InlineData("fbd", FbdOutputMode.Fbd)]
    [InlineData("auto", FbdOutputMode.Auto)]
    [InlineData("FBD", FbdOutputMode.Fbd)]
    [InlineData("Scl", FbdOutputMode.Scl)]
    public void Parse_FbdOutput_IsCaptured(string text, FbdOutputMode expected)
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--fbd-output", text });

        Assert.Null(options.Error);
        Assert.Equal(expected, options.FbdOutput);
    }

    [Fact]
    public void Parse_FbdOutput_Invalid_Fails()
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--fbd-output", "graph" });

        Assert.Contains("--fbd-output must be one of: scl, fbd, auto.", options.Error);
    }

    [Fact]
    public void Parse_FbdDialect_DefaultsToUpdate3()
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o" });

        Assert.Equal(FbdBoxDialect.V16Update3, options.FbdDialect);
    }

    [Theory]
    [InlineData("v16-ga", FbdBoxDialect.V16Ga)]
    [InlineData("V16-GA", FbdBoxDialect.V16Ga)]
    [InlineData("ga", FbdBoxDialect.V16Ga)]
    [InlineData("v16-update3", FbdBoxDialect.V16Update3)]
    [InlineData("update3", FbdBoxDialect.V16Update3)]
    public void Parse_FbdDialect_IsCaptured(string text, FbdBoxDialect expected)
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--fbd-dialect", text });

        Assert.Null(options.Error);
        Assert.Equal(expected, options.FbdDialect);
    }

    [Fact]
    public void Parse_FbdDialect_Invalid_Fails()
    {
        var options = CliOptions.Parse(new[] { "--input", "a.xml", "--output", "o", "--fbd-dialect", "v18" });

        Assert.Contains("--fbd-dialect must be one of: v16-update3, v16-ga.", options.Error);
    }
}
