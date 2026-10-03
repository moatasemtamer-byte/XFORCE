using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;
using Xunit;

namespace TriconexToTia.Parser.Tests;

public class TriconexXmlParserTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project name="DemoProject">
          <DataTypes>
            <DataType name="PumpData" kind="STRUCT">
              <Member name="Speed" type="REAL" />
              <Member name="Running" type="BOOL" />
            </DataType>
          </DataTypes>
          <FunctionBlocks>
            <FunctionBlock name="PumpCtrl" language="ST">
              <Variables>
                <Variable name="Start" type="BOOL" direction="IN" />
                <Variable name="Running" type="BOOL" direction="OUT" />
                <Variable name="Fault" type="BOOL" direction="IN_OUT" />
                <Variable name="Timer1" type="TON" />
              </Variables>
              <Code><![CDATA[IF Start THEN Running := TRUE; END_IF;]]></Code>
            </FunctionBlock>
          </FunctionBlocks>
          <GlobalVariables>
            <Variable name="GlobalCounter" type="INT" />
          </GlobalVariables>
          <IOConfiguration>
            <IOPoint name="PumpStart" type="BOOL" direction="input" />
            <IOPoint name="PumpSpeed" type="REAL" direction="input" signal="analog" />
          </IOConfiguration>
        </Project>
        """;

    private static PlcProject Parse() => new TriconexXmlParser().Parse(Sample);

    [Fact]
    public void Parses_project_name_and_block_counts()
    {
        var project = Parse();

        Assert.Equal("DemoProject", project.Name);
        Assert.Single(project.FunctionBlocks);
        Assert.Equal("PumpCtrl", project.FunctionBlocks.Single().Name);
        Assert.Equal(PlcLanguage.ST, project.FunctionBlocks.Single().Language);
    }

    [Fact]
    public void Parses_variable_directions_and_code()
    {
        var block = Parse().FunctionBlocks.Single();

        Assert.Equal(4, block.Variables.Count);
        Assert.Equal(VariableDirection.Input, block.Variables.Single(v => v.Name == "Start").Direction);
        Assert.Equal(VariableDirection.Output, block.Variables.Single(v => v.Name == "Running").Direction);
        Assert.Equal(VariableDirection.InOut, block.Variables.Single(v => v.Name == "Fault").Direction);
        Assert.Equal(VariableDirection.Local, block.Variables.Single(v => v.Name == "Timer1").Direction);
        Assert.Contains("Running := TRUE", block.Code);
    }

    [Fact]
    public void Parses_data_types_members()
    {
        var type = Parse().DataTypes.Single();

        Assert.Equal("PumpData", type.Name);
        Assert.Equal(2, ((StructType)type).Members.Count);
        Assert.Equal("REAL", ((StructType)type).Members.Single(m => m.Name == "Speed").TypeName);
    }

    [Fact]
    public void Parses_global_variables_and_io_points()
    {
        var project = Parse();

        Assert.Single(project.GlobalVariables);
        Assert.Equal(2, project.IoPoints.Count);
        Assert.Equal(IoSignal.Digital, project.IoPoints.Single(p => p.Name == "PumpStart").Signal);
        Assert.Equal(IoSignal.Analog, project.IoPoints.Single(p => p.Name == "PumpSpeed").Signal);
        Assert.Equal(IoDirection.Input, project.IoPoints.Single(p => p.Name == "PumpStart").Direction);
    }

    [Fact]
    public void Captures_unrecognized_elements_instead_of_dropping_them()
    {
        const string xml = """
            <Project name="P">
              <MysteryThing>payload</MysteryThing>
            </Project>
            """;

        var project = new TriconexXmlParser().Parse(xml);

        Assert.Contains(project.UnrecognizedElements, e => e.ElementName == "MysteryThing");
    }

    [Fact]
    public void Rejects_malformed_xml()
    {
        Assert.Throws<System.Xml.XmlException>(() => new TriconexXmlParser().Parse("<Project>"));
    }
}
