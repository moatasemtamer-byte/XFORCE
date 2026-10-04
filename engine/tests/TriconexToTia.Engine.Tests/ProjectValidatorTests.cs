using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;
using TriconexToTia.Mapper;
using TriconexToTia.Parser;
using Xunit;

namespace TriconexToTia.Engine.Tests;

/// <summary>Covers the severity rules from spec section 5, stage 2.</summary>
public class ProjectValidatorTests
{
    private static PlcProject Parse(string xml) => new TriconexXmlParser().Parse(xml);

    private static ValidationResult Validate(string xml) =>
        new ProjectValidator(MappingLoader.Load(null)).Validate(Parse(xml));

    [Fact]
    public void Empty_block_is_a_warning_not_fatal()
    {
        var result = Validate("""
            <Project name="P">
              <FunctionBlocks>
                <FunctionBlock name="Empty" language="ST" />
              </FunctionBlocks>
            </Project>
            """);

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("has no code"));
    }

    [Fact]
    public void Fbd_block_is_flagged_for_manual_verification()
    {
        const string xml = """
            <Project name="P">
              <FunctionBlocks>
                <FunctionBlock name="Fbd" language="FBD"><Code><![CDATA[a := a;]]></Code></FunctionBlock>
              </FunctionBlocks>
            </Project>
            """;

        // The language attribute must win over the presence of a <Code> element.
        Assert.Equal(PlcLanguage.FBD, Parse(xml).Blocks.Single().Language);

        var result = Validate(xml);

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("SCL conversion was attempted"));
    }

    [Fact]
    public void Duplicate_variable_in_one_block_is_fatal()
    {
        var result = Validate("""
            <Project name="P">
              <FunctionBlocks>
                <FunctionBlock name="B" language="ST">
                  <Variables>
                    <Variable name="X" type="BOOL" direction="IN" />
                    <Variable name="X" type="BOOL" direction="OUT" />
                  </Variables>
                  <Code><![CDATA[a := a;]]></Code>
                </FunctionBlock>
              </FunctionBlocks>
            </Project>
            """);

        Assert.True(result.HasFatal);
        Assert.Contains(result.Fatal, f => f.Message.Contains("Duplicate variable"));
    }

    [Fact]
    public void Unmapped_global_type_is_only_a_warning()
    {
        var result = Validate("""
            <Project name="P">
              <GlobalVariables>
                <Variable name="G" type="MYSTERY" />
              </GlobalVariables>
            </Project>
            """);

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("unmapped type"));
    }

    [Fact]
    public void Unrecognized_element_is_reported_as_info()
    {
        var result = Validate("""
            <Project name="P">
              <SomethingOdd>payload</SomethingOdd>
            </Project>
            """);

        Assert.False(result.HasFatal);
        Assert.Contains(result.Infos, i => i.Message.Contains("SomethingOdd"));
    }
}
