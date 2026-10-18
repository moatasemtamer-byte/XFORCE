using System;
using System.Xml.Linq;
using TriconexToTia.Core.Results;
using TriconexToTia.Engine;
using Xunit;

namespace TriconexToTia.Generators.Tests;

/// <summary>
/// Generator tests over a PLCopen/tc6 project. TriStation carries its points in
/// <c>globalVars</c>, where a <c>ts:TAGNAME</c> block marks the hardware ones.
/// Globals that carry a Siemens logical address become <c>Tags/GlobalTags.xml</c>,
/// because an SCL data block declaration cannot hold an address; the address-free
/// ones become members of <c>Globals/GlobalData.db</c>, because a global variable
/// without a hardware address is not a PLC tag.
/// </summary>
public class GeneratorOutputTests
{
    private const string Tc6 = "http://www.plcopen.org/xml/tc6.xsd";
    private const string Ts = "http://www.triconex.com/controller/03/2008";

    private static string Sample(string pouExtra = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <project xmlns="{Tc6}" xmlns:ts="{Ts}">
          <types>
            <dataTypes>
              <dataType name="PumpData">
                <baseType>
                  <struct>
                    <variable name="Speed"><type><REAL /></type></variable>
                    <variable name="Running"><type><BOOL /></type></variable>
                  </struct>
                </baseType>
              </dataType>
            </dataTypes>
            <pous>
              <pou name="PumpCtrl" pouType="functionBlock">
                <interface>
                  <inputVars>
                    <variable name="Start"><type><BOOL /></type></variable>
                  </inputVars>
                  <outputVars>
                    <variable name="Running"><type><BOOL /></type></variable>
                  </outputVars>
                  <localVars>
                    <variable name="Timer1"><type><derived name="TON" /></type></variable>
                  </localVars>
                </interface>
                <body>
                  <ST><![CDATA[IF Start THEN Timer1(IN := TRUE, PT := T#5S); Running := TRUE; ELSE Running := FALSE; END_IF;]]></ST>
                </body>
              </pou>
              <pou name="CalcFlow" pouType="function">
                <interface>
                  <inputVars>
                    <variable name="Area"><type><REAL /></type></variable>
                    <variable name="Velocity"><type><REAL /></type></variable>
                  </inputVars>
                  <inOutVars>
                    <variable name="Temp"><type><REAL /></type></variable>
                  </inOutVars>
                  <returnType><REAL /></returnType>
                </interface>
                <body>
                  <ST><![CDATA[Temp := Area * Velocity; CalcFlow := Temp;]]></ST>
                </body>
              </pou>
              <pou name="MainControl" pouType="program">
                <interface>
                  <localVars>
                    <variable name="Pump"><type><derived name="PumpCtrl" /></type></variable>
                  </localVars>
                </interface>
                <body>
                  <ST><![CDATA[Pump.Start := TRUE; Pump.Running := FALSE;]]></ST>
                </body>
              </pou>
              {pouExtra}
            </pous>
          </types>
          <instances>
            <configurations>
              <configuration name="MainConfiguration">
                <resource name="Res1">
                  <task name="MainTask" interval="100" />
                  <globalVars>
                    <variable name="GlobalCounter"><type><INT /></type></variable>
                    <variable name="AI_RAW_FIELD">
                      <type><DINT /></type>
                      <documentation><xhtml:p xmlns:xhtml="http://www.w3.org/1999/xhtml">Field analog input</xhtml:p></documentation>
                      <ts:TAGNAME>
                        <ts:POINT_ASSIGNMENT PointType="MEMORY" AliasType="UNSHADOWED" PhysicalAddress="MIN.0007" />
                      </ts:TAGNAME>
                    </variable>
                  </globalVars>
                </resource>
              </configuration>
            </configurations>
          </instances>
        </project>
        """;

    private static ConversionResult Run(int tiaVersion = 20, string? xml = null) =>
        new ConversionEngine().Run(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml ?? Sample())),
            new ConverterOptions { TiaVersion = tiaVersion },
            "sample.xml");

    private static string Artifact(ConversionResult result, string path) =>
        result.Files.Single(file => file.RelativePath == path).Content;

    [Fact]
    public void Scl_generator_emits_function_block_with_prefixed_body()
    {
        var scl = Artifact(Run(), "Blocks/PumpCtrl.scl");

        Assert.Contains("FUNCTION_BLOCK \"PumpCtrl\"", scl);
        Assert.Contains("VAR_INPUT", scl);
        Assert.Contains("Start : Bool;", scl);
        Assert.Contains("VAR_OUTPUT", scl);
        Assert.Contains("#Start", scl);
        Assert.Contains("#Running", scl);
        Assert.Contains("Timer1 : TON;", scl);
        Assert.Contains("END_FUNCTION_BLOCK", scl);

        // The generated wrapper provides the block, so the body must not repeat it.
        Assert.DoesNotContain("FUNCTION_BLOCK PumpCtrl", scl);
    }

    [Fact]
    public void Scl_generator_emits_function_keeping_return_name_unprefixed()
    {
        var scl = Artifact(Run(), "Blocks/CalcFlow.scl");

        Assert.Contains("FUNCTION \"CalcFlow\" : Real", scl);
        Assert.Contains("#Temp := #Area * #Velocity;", scl);
        Assert.Contains("CalcFlow := #Temp;", scl);
        Assert.Contains("END_FUNCTION", scl);
    }

    [Fact]
    public void Scl_generator_emits_organization_block_with_instance_type()
    {
        var result = Run();

        // An OB cannot hold block instances, so its logic moves into a wrapper function block and
        // the OB keeps a single call through the wrapper's instance DB.
        var main = Artifact(result, "Blocks/MainControl.scl");

        Assert.Contains("ORGANIZATION_BLOCK \"MainControl\"", main);
        Assert.Contains("\"DB_MainControl\"();", main);
        Assert.Contains("END_ORGANIZATION_BLOCK", main);
        Assert.DoesNotContain("Pump : \"PumpCtrl\";", main);

        var wrapper = Artifact(result, "Blocks/MainControl_WRAP.scl");

        Assert.Contains("FUNCTION_BLOCK \"MainControl_WRAP\"", wrapper);
        Assert.Contains("Pump : \"PumpCtrl\";", wrapper);
        Assert.Contains("#Pump.Start := TRUE;", wrapper);
        Assert.Contains("END_FUNCTION_BLOCK", wrapper);
    }

    [Fact]
    public void Scl_generator_emits_externals_and_does_not_prefix_them()
    {
        var xml = Sample("""
            <pou name="Ext" pouType="functionBlock">
              <interface>
                <externalVars>
                  <variable name="Shared"><type><INT /></type></variable>
                </externalVars>
                <localVars>
                  <variable name="Local"><type><BOOL /></type></variable>
                </localVars>
              </interface>
              <body><ST><![CDATA[Local := Shared > 0;]]></ST></body>
            </pou>
            """);

        var scl = Artifact(Run(xml: xml), "Blocks/Ext.scl");

        Assert.Contains("VAR_EXTERN", scl);
        Assert.Contains("Shared : Int;", scl);
        Assert.Contains("#Local := Shared > 0;", scl);
        Assert.DoesNotContain("#Shared", scl);
    }

    [Fact]
    public void Scl_generator_repairs_spaced_comparison_operators()
    {
        // TriStation writes "< >" (and "> =", "< =") with a space; Siemens SCL reads those as two
        // separate operators and rejects the block.
        var xml = Sample("""
            <pou name="Cmp" pouType="functionBlock">
              <interface />
              <body><ST><![CDATA[FUNCTION_BLOCK Cmp
            VAR_INPUT A : BOOL; B : BOOL; END_VAR
            VAR R : BOOL; END_VAR
            R := A < > B;
            END_FUNCTION_BLOCK]]></ST></body>
            </pou>
            """);

        var scl = Artifact(Run(xml: xml), "Blocks/Cmp.scl");

        Assert.Contains("#R := #A <> #B;", scl);
        Assert.DoesNotContain("< >", scl);
    }

    [Fact]
    public void Scl_generator_leaves_out_a_block_whose_declaration_has_no_type()
    {
        // A variable the export left without a data type cannot be declared, and the statements that
        // use it cannot compile, so the block is left out of the package instead of breaking the
        // whole import. A manual action records why.
        var xml = Sample("""
            <pou name="Bad" pouType="functionBlock">
              <interface />
              <body><ST><![CDATA[FUNCTION_BLOCK Bad
            VAR_INPUT
            IN1 : BOOL ;
            IN2:
            END_VAR
            IN1 := IN2;
            END_FUNCTION_BLOCK]]></ST></body>
            </pou>
            """);

        var result = Run(xml: xml);

        Assert.DoesNotContain(result.Files, file => file.RelativePath == "Blocks/Bad.scl");
        Assert.Contains(
            result.Mapping.ManualActions,
            action => action.Title.Contains("left out", StringComparison.OrdinalIgnoreCase)
                && action.Title.Contains("IN2"));
    }

    [Fact]
    public void Scl_generator_reuses_the_project_name_for_case_insensitive_collisions()
    {
        var xml = Sample("""
            <pou name="PUMPCTRL" pouType="program">
              <body><ST><![CDATA[]]></ST></body>
            </pou>
            """);

        var result = Run(xml: xml);

        var paths = result.Files.Select(file => file.RelativePath).ToArray();
        Assert.Contains("Blocks/PumpCtrl.scl", paths);
        Assert.Contains("Blocks/PUMPCTRL_1.scl", paths);
    }

    [Fact]
    public void Udt_generator_emits_plcstruct_xml_matching_real_v16_exports()
    {
        var udt = Artifact(Run(tiaVersion: 16), "UDTs/PumpData.xml");

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", udt);
        Assert.Contains("<Engineering version=\"V16\" />", udt);
        Assert.Contains("<ExportSetting>WithDefaults</ExportSetting>", udt);
        Assert.Contains("<SW.Types.PlcStruct ID=\"0\">", udt);

        // Sections carries the Interface namespace and Section/@Name is always "None".
        Assert.Contains(
            "<Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v4\">",
            udt);
        Assert.Contains("<Section Name=\"None\">", udt);

        // AttributeList child order, as observed in all 19 real UDTs.
        var attributeList = udt.IndexOf("<IsFailsafeCompliant>", StringComparison.Ordinal);
        var interfaceAt = udt.IndexOf("<Interface>", StringComparison.Ordinal);
        var nameAt = udt.IndexOf("<Name>PumpData</Name>", StringComparison.Ordinal);
        Assert.True(interfaceAt < attributeList, "Interface must precede IsFailsafeCompliant");
        Assert.True(attributeList < nameAt, "IsFailsafeCompliant must precede Name");

        // AttributeList is emitted independently of Comment; the defaults here are the 403-member
        // majority case in the real exports (accessible/visible true, writable/setpoint false).
        Assert.Contains("<Member Name=\"Speed\" Datatype=\"Real\">", udt);
        Assert.Contains("<Member Name=\"Running\" Datatype=\"Bool\">", udt);
        Assert.Contains(
            "<BooleanAttribute Name=\"ExternalAccessible\" SystemDefined=\"true\">true</BooleanAttribute>",
            udt);
        Assert.Contains(
            "<BooleanAttribute Name=\"ExternalWritable\" SystemDefined=\"true\">false</BooleanAttribute>",
            udt);
        // Built-in types are never quoted; only declared structs are.
        Assert.DoesNotContain("Datatype=\"&quot;Real&quot;\"", udt);
        Assert.DoesNotContain("Datatype=\"&quot;Bool&quot;\"", udt);
    }

    [Fact]
    public void Udt_generator_quotes_referenced_structs_and_inlines_them()
    {
        // A second declared type that PumpData embeds, so the reference is not self-referential.
        var xml = Sample().Replace(
            "<dataType name=\"PumpData\">",
            "<dataType name=\"SensorData\"><baseType><struct>" +
            "<variable name=\"Reading\"><type><INT /></type></variable>" +
            "</struct></baseType></dataType>" +
            "<dataType name=\"PumpData\">",
            StringComparison.Ordinal).Replace(
            "<variable name=\"Running\"><type><BOOL /></type></variable>",
            "<variable name=\"Running\"><type><BOOL /></type></variable>" +
            "<variable name=\"Sensor\"><type><derived name=\"SensorData\" /></type></variable>",
            StringComparison.Ordinal);

        var result = Run(tiaVersion: 16, xml: xml);
        var udt = Artifact(result, "UDTs/PumpData.xml");

        // A reference to a declared struct is quoted, and its definition is inlined underneath it.
        Assert.Contains("<Member Name=\"Sensor\" Datatype=\"&quot;SensorData&quot;\">", udt);
        Assert.Contains("<Member Name=\"Reading\" Datatype=\"Int\">", udt);
        // A built-in type is never quoted.
        Assert.DoesNotContain("Datatype=\"&quot;Real&quot;\"", udt);
        Assert.DoesNotContain("Datatype=\"&quot;Int&quot;\"", udt);
    }

    [Fact]
    public void Tag_table_emits_the_verified_attribute_order_and_multilingual_comment()
    {
        var tags = Artifact(Run(tiaVersion: 16), "Tags/GlobalTags.xml");

        Assert.Contains("<SW.Tags.PlcTagTable ID=\"0\">", tags);
        Assert.Contains("<ExportSetting>WithDefaults</ExportSetting>", tags);

        // Order proven against all 83 real tags: DataTypeName, ExternalAccessible, ExternalVisible,
        // ExternalWritable, LogicalAddress, Name.
        var dataType = tags.IndexOf("<DataTypeName>", StringComparison.Ordinal);
        var accessible = tags.IndexOf("<ExternalAccessible>", StringComparison.Ordinal);
        var visible = tags.IndexOf("<ExternalVisible>", StringComparison.Ordinal);
        var writable = tags.IndexOf("<ExternalWritable>", StringComparison.Ordinal);
        var address = tags.IndexOf("<LogicalAddress>", StringComparison.Ordinal);
        var name = tags.IndexOf("<Name>AI_RAW_FIELD</Name>", StringComparison.Ordinal);

        Assert.True(dataType < accessible, "DataTypeName must precede ExternalAccessible");
        Assert.True(accessible < visible, "ExternalAccessible must precede ExternalVisible");
        Assert.True(visible < writable, "ExternalVisible must precede ExternalWritable");
        Assert.True(writable < address, "ExternalWritable must precede LogicalAddress");
        Assert.True(address < name, "LogicalAddress must precede Name");

        // Comments must be object-level multilingual text; an XML comment is discarded by TIA.
        Assert.Contains("<MultilingualText", tags);
        Assert.Contains("CompositionName=\"Comment\"", tags);
        Assert.Contains("<Culture>en-US</Culture>", tags);
        Assert.DoesNotContain("<!--", tags);
    }

    [Fact]
    public void Tag_generator_emits_addressed_points_with_translated_addresses()
    {
        var tags = Artifact(Run(), "Tags/GlobalTags.xml");

        // AI_RAW_FIELD carries a TriStation POINT_ASSIGNMENT; GlobalCounter does not.
        Assert.Contains("<Name>AI_RAW_FIELD</Name>", tags);
        Assert.Contains("<DataTypeName>DInt</DataTypeName>", tags);

        // MIN.0007 is memory word 7, but AI_RAW_FIELD is a DInt (32-bit), so the address
        // widens to a double word (%MD7); %MW7 would not match the data type in TIA.
        Assert.Contains("<LogicalAddress>%MD7</LogicalAddress>", tags);
    }

    [Fact]
    public void Global_db_carries_the_address_free_globals()
    {
        var globals = Artifact(Run(), "Globals/GlobalData.db");

        Assert.Contains("DATA_BLOCK \"GlobalData\"", globals);
        Assert.Contains("GlobalCounter : Int;", globals);
        Assert.Contains("END_DATA_BLOCK", globals);

        // Addressed points belong in the tag table, not in the global DB.
        Assert.DoesNotContain("AI_RAW_FIELD", globals);
    }

    [Fact]
    public void Global_db_members_are_not_given_placeholder_addresses()
    {
        // A global DB is not addressed, so the mapper must not invent a %MW address.
        var tags = Artifact(Run(), "Tags/GlobalTags.xml");

        Assert.DoesNotContain("GlobalCounter", tags);
        Assert.DoesNotContain("%MW100", tags);
    }

    [Fact]
    public void Addressed_and_address_free_globals_land_in_different_artifacts()
    {
        var paths = Run().Files.Select(file => file.RelativePath).ToArray();

        Assert.Contains("Globals/GlobalData.db", paths);
        Assert.Contains("Tags/GlobalTags.xml", paths);

        // The I/O table only carries physical I/O points, and this fixture declares none.
        Assert.DoesNotContain("Tags/IOTags.xml", paths);
    }

    [Fact]
    public void Tag_output_is_well_formed_xml_with_single_root()
    {
        var document = XDocument.Parse(Artifact(Run(), "Tags/GlobalTags.xml"));

        Assert.Equal("Document", document.Root!.Name.LocalName);
        // Real exports carry DocumentInfo between Engineering and the type element.
        Assert.Equal(
            new[] { "Engineering", "DocumentInfo", "SW.Tags.PlcTagTable" },
            document.Root.Elements().Select(e => e.Name.LocalName).ToArray());
    }

    [Fact]
    public void Tag_output_uses_plc_tag_composition_structure()
    {
        var document = XDocument.Parse(Artifact(Run(), "Tags/GlobalTags.xml"));

        Assert.Equal("V20", document.Root!.Element("Engineering")!.Attribute("version")!.Value);

        var table = document.Root.Elements("SW.Tags.PlcTagTable").Single();
        Assert.Equal("GlobalTags", table.Element("AttributeList")!.Element("Name")!.Value);

        var tag = table.Element("ObjectList")!.Elements("SW.Tags.PlcTag").First();
        Assert.Equal("Tags", tag.Attribute("CompositionName")!.Value);

        // Order proven across all 83 real tags.
        var attributes = tag.Element("AttributeList")!.Elements().Select(e => e.Name.LocalName).ToArray();
        Assert.Equal(
            new[] { "DataTypeName", "ExternalAccessible", "ExternalVisible", "ExternalWritable", "LogicalAddress", "Name" },
            attributes);
        Assert.Equal("true", tag.Element("AttributeList")!.Element("ExternalAccessible")!.Value);
    }

    [Fact]
    public void Global_db_declares_a_static_var_block()
    {
        var db = Artifact(Run(), "Globals/GlobalData.db");

        Assert.Contains("{ S7_Optimized_Access := 'TRUE' }", db);
        Assert.Contains("VERSION : 0.1", db);
        Assert.Contains("VAR", db);
        Assert.Contains("END_VAR", db);
    }

    [Fact]
    public void Tag_output_tracks_configured_tia_version()
    {
        var document = XDocument.Parse(Artifact(Run(18), "Tags/GlobalTags.xml"));

        Assert.Equal("V18", document.Root!.Element("Engineering")!.Attribute("version")!.Value);
    }

    [Fact]
    public void Import_script_targets_configured_tia_version()
    {
        var script = Artifact(Run(), "import.ps1");

        Assert.Contains("Siemens.Engineering", script);
        Assert.Contains("TIA Portal V20", script);
    }

    [Fact]
    public void Import_script_orders_library_udts_tags_globals_blocks()
    {
        var script = Artifact(Run(), "import.ps1");

        // A block cannot instantiate a function block that does not exist yet, so the
        // stand-ins in Library/ must land before Blocks/. Everything Blocks/ references
        // (UDTs, tags, the global DB) must land before it too.
        var library = script.IndexOf("$libraryDir = Join-Path $ScriptDir", StringComparison.Ordinal);
        var udts = script.IndexOf("$udtDir = Join-Path $ScriptDir", StringComparison.Ordinal);
        var tags = script.IndexOf("$tagDir = Join-Path $ScriptDir", StringComparison.Ordinal);
        var globals = script.IndexOf("$globalDir = Join-Path $ScriptDir", StringComparison.Ordinal);
        var blocks = script.IndexOf("$blockDir = Join-Path $ScriptDir", StringComparison.Ordinal);

        Assert.True(library > 0, "Library section missing from import script");
        Assert.True(udts > library, "UDTs must be imported after Library");
        Assert.True(tags > udts, "Tags must be imported after UDTs");
        Assert.True(globals > tags, "GlobalDB must be imported after Tags");
        Assert.True(blocks > globals, "Blocks must be imported after GlobalDB");
    }

    [Fact]
    public void Import_script_only_references_openness_types_that_exist()
    {
        var script = Artifact(Run(), "import.ps1");

        // SW.ISoftwareContainer and Compiler.CompilerService do not exist in the V16 API.
        // Both were generated into import.ps1 and would have failed at runtime.
        Assert.DoesNotContain("SW.ISoftwareContainer", script);
        Assert.DoesNotContain("CompilerService", script);
        Assert.Contains("HW.Features.SoftwareContainer", script);
        Assert.Contains("Compiler.ICompilable", script);
    }

    [Fact]
    public void Import_script_does_not_use_powershell7_generic_method_syntax()
    {
        var script = Artifact(Run(), "import.ps1");

        // $obj.Method[T]() parses in PowerShell 7 but not in 5.1, which is what #Requires asks for.
        Assert.DoesNotContain(".GetService[", script);
        Assert.Contains("MakeGenericMethod", script);
    }

    [Fact]
    public void Report_generator_contains_expected_sections()
    {
        var report = Run().Report;

        Assert.NotNull(report);
        Assert.Contains("CONVERSION REPORT", report);
        Assert.Contains("SUMMARY", report);
        Assert.Contains("FILES GENERATED", report);
    }

    private static string TouchingGlobal(string declaration, string body) => Sample($"""
        <pou name="TouchCounter" pouType="functionBlock">
          <interface>
            {declaration}
          </interface>
          <body>
            <ST><![CDATA[{body}]]></ST>
          </body>
        </pou>
        """);

    [Fact]
    public void Address_free_globals_are_referenced_through_the_data_block()
    {
        var scl = Artifact(
            Run(xml: TouchingGlobal(string.Empty, "GlobalCounter := GlobalCounter - 1;")),
            "Blocks/TouchCounter.scl");

        Assert.Contains("\"GlobalData\".GlobalCounter", scl);
        Assert.DoesNotContain("#GlobalCounter", scl);
    }

    [Fact]
    public void An_addressed_global_keeps_its_bare_name_in_block_code()
    {
        // AI_RAW_FIELD carries MIN.0007, so it stays in the tag table and is referenced
        // directly rather than through the data block.
        var scl = Artifact(
            Run(xml: TouchingGlobal(string.Empty, "AI_RAW_FIELD := 0;")),
            "Blocks/TouchCounter.scl");

        Assert.Contains("AI_RAW_FIELD := 0;", scl);
        Assert.DoesNotContain("\"GlobalData\".AI_RAW_FIELD", scl);
    }

    [Fact]
    public void A_local_declaration_shadows_the_data_block_member()
    {
        var scl = Artifact(
            Run(xml: TouchingGlobal(
                "<localVars><variable name=\"GlobalCounter\"><type><INT /></type></variable></localVars>",
                "GlobalCounter := GlobalCounter - 1;")),
            "Blocks/TouchCounter.scl");

        Assert.Contains("#GlobalCounter := #GlobalCounter - 1;", scl);
        Assert.DoesNotContain("\"GlobalData\".GlobalCounter", scl);
    }
}