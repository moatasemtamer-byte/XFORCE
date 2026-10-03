using TriconexToTia.Core.Results;
using TriconexToTia.Engine;
using Xunit;

namespace TriconexToTia.Generators.Tests;

public class GeneratorOutputTests
{
    private const string Sample = """
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
                <Variable name="Timer1" type="TON" />
              </Variables>
              <Code><![CDATA[IF Start THEN Timer1(IN := TRUE, PT := T#5S); Running := TRUE; ELSE Running := FALSE; END_IF;]]></Code>
            </FunctionBlock>
          </FunctionBlocks>
          <Functions>
            <Function name="CalcFlow" language="ST" returnType="REAL">
              <Variables>
                <Variable name="Area" type="REAL" direction="IN" />
                <Variable name="Velocity" type="REAL" direction="IN" />
                <Variable name="Temp" type="REAL" />
              </Variables>
              <Code><![CDATA[Temp := Area * Velocity; CalcFlow := Temp;]]></Code>
            </Function>
          </Functions>
          <Programs>
            <Program name="MainControl" language="ST">
              <Variables>
                <Variable name="Pump" type="PumpCtrl" />
              </Variables>
              <Code><![CDATA[Pump.Start := TRUE; Pump.Running := FALSE;]]></Code>
            </Program>
          </Programs>
          <GlobalVariables>
            <Variable name="GlobalCounter" type="INT" />
          </GlobalVariables>
          <IOConfiguration>
            <IOPoint name="PumpStart" type="BOOL" direction="input" />
            <IOPoint name="PumpRun" type="BOOL" direction="output" />
            <IOPoint name="PumpSpeed" type="REAL" direction="input" signal="analog" />
          </IOConfiguration>
        </Project>
        """;

    private static ConversionResult Run() =>
        new ConversionEngine().Run(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Sample)),
            new ConverterOptions { TiaVersion = 20 },
            "sample.xml");

    private static string File(ConversionResult result, string path) =>
        result.Files.Single(f => f.RelativePath == path).Content;

    [Fact]
    public void Scl_generator_emits_function_block_with_prefixed_body()
    {
        var scl = File(Run(), "Blocks/PumpCtrl.scl");

        Assert.Contains("FUNCTION_BLOCK \"PumpCtrl\"", scl);
        Assert.Contains("VAR_INPUT", scl);
        Assert.Contains("Start : Bool;", scl);
        Assert.Contains("#Start", scl);
        Assert.Contains("#Running", scl);
        Assert.Contains("END_FUNCTION_BLOCK", scl);
    }

    [Fact]
    public void Scl_generator_emits_function_keeping_return_name_unprefixed()
    {
        var scl = File(Run(), "Blocks/CalcFlow.scl");

        Assert.Contains("FUNCTION \"CalcFlow\" : Real", scl);
        Assert.Contains("#Temp := #Area * #Velocity;", scl);
        Assert.Contains("CalcFlow := #Temp;", scl);
        Assert.Contains("END_FUNCTION", scl);
    }

    [Fact]
    public void Scl_generator_emits_organization_block_with_instance_type()
    {
        var scl = File(Run(), "Blocks/MainControl.scl");

        Assert.Contains("ORGANIZATION_BLOCK \"MainControl\"", scl);
        Assert.Contains("Pump : \"PumpCtrl\";", scl);
        Assert.Contains("#Pump.Start := TRUE;", scl);
        Assert.Contains("END_ORGANIZATION_BLOCK", scl);
    }

    [Fact]
    public void Udt_generator_emits_simaticml_members()
    {
        var udt = File(Run(), "UDTs/PumpData.xml");

        Assert.Contains("Name=\"Speed\" Datatype=\"Real\"", udt);
        Assert.Contains("Name=\"Running\" Datatype=\"Bool\"", udt);
    }

    [Fact]
    public void Tag_generator_emits_io_and_global_tables()
    {
        var result = Run();

        var io = File(result, "Tags/IOTags.xml");
        Assert.Contains("Name=\"PumpStart\" DataTypeName=\"Bool\" LogicalAddress=\"%I0.0\"", io);
        Assert.Contains("Name=\"PumpRun\" DataTypeName=\"Bool\" LogicalAddress=\"%Q0.0\"", io);

        var globals = File(result, "Tags/GlobalTags.xml");
        Assert.Contains("Name=\"GlobalCounter\"", globals);
    }

    [Fact]
    public void Import_script_targets_configured_tia_version()
    {
        var script = File(Run(), "import.ps1");

        Assert.Contains("Siemens.Engineering", script);
        Assert.Contains("TIA Portal V20", script);
    }

    [Fact]
    public void Report_generator_contains_expected_sections()
    {
        var report = Run().Report;

        Assert.NotNull(report);
        Assert.Contains("CONVERSION REPORT", report);
        Assert.Contains("SUMMARY", report);
        Assert.Contains("FILES GENERATED", report);
        Assert.Contains("MANUAL ACTION REQUIRED", report);
    }
}
