using TriconexToTia.Core.Results;
using Xunit;

namespace TriconexToTia.Engine.Tests;

public class ConversionEngineTests
{
    private const string Sample = """
        <Project name="DemoProject">
          <DataTypes>
            <DataType name="PumpData" kind="STRUCT">
              <Member name="Speed" type="REAL" />
            </DataType>
          </DataTypes>
          <FunctionBlocks>
            <FunctionBlock name="PumpCtrl" language="ST">
              <Variables>
                <Variable name="Start" type="BOOL" direction="IN" />
              </Variables>
              <Code><![CDATA[Start := Start;]]></Code>
            </FunctionBlock>
          </FunctionBlocks>
          <GlobalVariables>
            <Variable name="GlobalCounter" type="INT" />
          </GlobalVariables>
          <IOConfiguration>
            <IOPoint name="PumpStart" type="BOOL" direction="input" />
          </IOConfiguration>
        </Project>
        """;

    private static ConversionResult Run(string xml, ConverterOptions? options = null, string source = "sample.xml") =>
        new ConversionEngine().Run(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)),
            options ?? new ConverterOptions { TiaVersion = 20 },
            source);

    [Fact]
    public void Produces_a_complete_package()
    {
        var result = Run(Sample);

        Assert.True(result.Success, result.FatalError);
        Assert.Equal("DemoProject", result.Project.Name);
        Assert.Equal("sample.xml", result.Project.SourceFile);

        var paths = result.Files.Select(f => f.RelativePath).ToList();
        Assert.Contains("Blocks/PumpCtrl.scl", paths);
        Assert.Contains("UDTs/PumpData.xml", paths);
        Assert.Contains("Tags/IOTags.xml", paths);
        Assert.Contains("Tags/GlobalTags.xml", paths);
        Assert.Contains("import.ps1", paths);
        Assert.Contains("conversion_report.txt", paths);
    }

    [Fact]
    public void Generated_files_are_marked_for_utf8_bom()
    {
        var result = Run(Sample);

        var bytes = result.Files.Single(f => f.RelativePath == "Blocks/PumpCtrl.scl").ToBytes();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void Validate_only_generates_nothing()
    {
        var result = Run(Sample, new ConverterOptions { ValidateOnly = true });

        Assert.True(result.Success, result.FatalError);
        Assert.True(result.ValidateOnly);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void Undefined_variable_type_is_fatal()
    {
        const string xml = """
            <Project name="Bad">
              <FunctionBlocks>
                <FunctionBlock name="B" language="ST">
                  <Variables>
                    <Variable name="X" type="NOPE" direction="IN" />
                  </Variables>
                  <Code><![CDATA[X := X;]]></Code>
                </FunctionBlock>
              </FunctionBlocks>
            </Project>
            """;

        var result = Run(xml);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Fatal, i => i.Message.Contains("NOPE"));
    }

    [Fact]
    public void Duplicate_block_names_are_fatal()
    {
        const string xml = """
            <Project name="Dup">
              <FunctionBlocks>
                <FunctionBlock name="Same" language="ST"><Code><![CDATA[a := a;]]></Code></FunctionBlock>
                <FunctionBlock name="Same" language="ST"><Code><![CDATA[b := b;]]></Code></FunctionBlock>
              </FunctionBlocks>
            </Project>
            """;

        var result = Run(xml);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Fatal, i => i.Message.Contains("Duplicate block"));
    }

    [Fact]
    public void Malformed_xml_fails_gracefully()
    {
        var result = Run("<Project>");

        Assert.False(result.Success);
        Assert.NotNull(result.FatalError);
    }

    [Theory]
    [InlineData(10, 17)]
    [InlineData(17, 17)]
    [InlineData(20, 20)]
    [InlineData(25, 21)]
    public void Tia_version_is_clamped(int input, int expected)
    {
        var options = new ConverterOptions { TiaVersion = input };

        Assert.Equal(expected, options.ClampTiaVersion());
    }
}
