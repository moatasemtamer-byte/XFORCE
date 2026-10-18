using System.Reflection;
using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;
using TriconexToTia.Generators;
using TriconexToTia.Mapper;
using Xunit;

namespace TriconexToTia.Engine.Tests;

/// <summary>
/// End-to-end pipeline tests. The input is PLCopen/tc6 as a TriStation export writes it, not an
/// invented schema.
/// </summary>
public class ConversionEngineTests
{
    private const string Tc6 = "http://www.plcopen.org/xml/tc6.xsd";
    private const string Ts = "http://www.triconex.com/controller/03/2008";

    /// <summary>A project with an FBD function block, an ST program and three memory tags.</summary>
    private static string Sample(string configurationName = "MainConfiguration") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <project xmlns="{Tc6}" xmlns:ts="{Ts}">
          <header>
            <content name="name">{configurationName}</content>
          </header>
          <types>
            <dataTypes />
            <pous>
            <pou name="PumpCtrl" pouType="functionBlock">
              <interface>
                <inputVars>
                  <variable name="Start">
                    <type><derived name="BOOL" /></type>
                  </variable>
                </inputVars>
                <outputVars>
                  <variable name="Running">
                    <type><derived name="BOOL" /></type>
                  </variable>
                </outputVars>
                <localVars>
                  <variable name="TON_1">
                    <type><derived name="TON" /></type>
                  </variable>
                  <variable name="TRUE">
                    <type><derived name="BOOL" /></type>
                  </variable>
                </localVars>
              </interface>
              <body>
                <FBD>
                  <block localId="1" typeName="R_TRIG">
                    <position x="20" y="20" ts:SheetNumber="1" />
                    <inputVariables>
                      <variable formalParameter="CLK">
                        <connectionPointIn>
                          <connection refLocalId="2" formalParameter="CLK" />
                        </connectionPointIn>
                      </variable>
                    </inputVariables>
                  </block>
                  <inVariable localId="2">
                    <position x="0" y="20" ts:SheetNumber="1" />
                    <expression>Start</expression>
                  </inVariable>
                  <inOutVariable localId="3">
                    <position x="60" y="20" ts:SheetNumber="1" />
                    <expression>Running</expression>
                    <connectionPointIn>
                      <connection refLocalId="1" formalParameter="Q" />
                    </connectionPointIn>
                  </inOutVariable>
                </FBD>
              </body>
            </pou>
            <pou name="MainProgram" pouType="program">
              <body>
                <ST><![CDATA[FUNCTION_BLOCK MainProgram VAR Start : BOOL; END_VAR BEGIN Start := TRUE; END_FUNCTION_BLOCK]]></ST>
              </body>
            </pou>
            </pous>
          </types>
          <instances>
            <configurations>
              <configuration name="{configurationName}">
                <resource name="Res1">
                  <task name="MainTask" interval="100" />
                  <globalVars>
                    <variable name="AIXXX" address="MIW.0009">
                      <type><derived name="DINT" /></type>
                      <ts:TAGNAME>
                        <ts:POINT_ASSIGNMENT PointType="MEMORY" AliasType="READWRITE_ALIASED" PhysicalAddress="MIW.0009" />
                      </ts:TAGNAME>
                    </variable>
                    <variable name="ALXXX" address="MBN.0010">
                      <type><derived name="BOOL" /></type>
                      <ts:TAGNAME>
                        <ts:POINT_ASSIGNMENT PointType="MEMORY" AliasType="UNSHADOWED" PhysicalAddress="MBN.0010" />
                      </ts:TAGNAME>
                    </variable>
                  </globalVars>
                </resource>
              </configuration>
            </configurations>
            <ts:CONTROLLER name="TRICON_EP" majorMinorVersion="11.2">
              <ts:COMMUNICATION>
                <ts:UDP_PORT>1502</ts:UDP_PORT>
              </ts:COMMUNICATION>
            </ts:CONTROLLER>
          </instances>
        </project>
        """;

    private static ConversionResult Run(string xml, ConverterOptions? options = null, string source = "sample.xml") =>
        new ConversionEngine().Run(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)),
            options ?? new ConverterOptions { TiaVersion = 20 },
            source);

    /// <summary>A one-block project whose FBD body, interface and globals are supplied by the test.</summary>
    private static string FbdSample(string interfaceXml, string fbdXml, string globalsXml = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <project xmlns="{Tc6}" xmlns:ts="{Ts}">
          <header><content name="name">MainConfiguration</content></header>
          <types>
            <dataTypes />
            <pous>
              <pou name="Seal" pouType="functionBlock">
                <interface>
        {interfaceXml}
                </interface>
                <body>
                  <FBD>
        {fbdXml}
                  </FBD>
                </body>
              </pou>
            </pous>
          </types>
          <instances>
            <configurations>
              <configuration name="MainConfiguration">
                <resource name="Res1">
                  <task name="MainTask" interval="100" />
                  <globalVars>
        {globalsXml}
                  </globalVars>
                </resource>
              </configuration>
            </configurations>
          </instances>
        </project>
        """;

    private static string Artifact(ConversionResult result, string path) =>
        result.Files.Single(file => file.RelativePath == path).Content;

    [Fact]
    public void Produces_a_complete_package()
    {
        var result = Run(Sample());

        Assert.True(result.Success, result.FatalError);
        Assert.Equal("MainConfiguration", result.Project.Name);
        Assert.Equal("sample.xml", result.Project.SourceFile);

        var paths = result.Files.Select(file => file.RelativePath).ToList();
        Assert.Contains("Blocks/PumpCtrl.scl", paths);
        Assert.Contains("Blocks/MainProgram.scl", paths);
        Assert.Contains("Tags/GlobalTags.xml", paths);
        Assert.Contains("import.ps1", paths);
        Assert.Contains("conversion_report.txt", paths);

        // No UDTs in this sample, so no UDT may be invented.
        Assert.DoesNotContain(paths, path => path.StartsWith("UDTs/", StringComparison.Ordinal));
    }

    [Fact]
    public void Fbd_body_is_emitted_as_ordered_scl()
    {
        var scl = Artifact(Run(Sample()), "Blocks/PumpCtrl.scl");

        Assert.Contains("FUNCTION_BLOCK \"PumpCtrl\"", scl);
        Assert.Contains("VAR_INPUT", scl);
        Assert.Contains("VAR_OUTPUT", scl);

        // The diagram declares an R_TRIG instance and wires its Q output to "Running".
        Assert.Contains("R_TRIG_1 : R_TRIG;", scl);
        Assert.Contains("#Running := R_TRIG_1.Q;", scl);

        // "TRUE" is an FBD literal, not a variable, so it must not become a declaration.
        Assert.DoesNotContain("TRUE : Bool;", scl);
    }

    [Fact]
    public void Self_read_output_parameter_is_routed_through_a_hold_local()
    {
        // Running is an output pin that the network also reads (a seal-in). TIA warns that such a
        // parameter might not be initialized, so the previous value goes through a generated local.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
                    <outputVars>
                      <variable name="Running"><type><derived name="BOOL" /></type></variable>
                    </outputVars>
            """,
            """
                    <block localId="1" typeName="OR">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN2"><connectionPointIn><connection refLocalId="3" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="10" ts:SheetNumber="1" /><expression>Start</expression></inVariable>
                    <inVariable localId="3"><position x="0" y="30" ts:SheetNumber="1" /><expression>Running</expression></inVariable>
                    <inOutVariable localId="4"><position x="60" y="20" ts:SheetNumber="1" /><expression>Running</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """);

        var scl = Artifact(Run(xml), "Blocks/Seal.scl");

        Assert.Contains("Running_HOLD : Bool := FALSE;", scl);
        Assert.Contains("#Running_HOLD :=", scl);
        Assert.Contains("#Running := #Running_HOLD;", scl);
    }

    [Fact]
    public void Comparison_result_is_converted_when_the_target_is_not_boolean()
    {
        // A comparison produces a Bool; the diagram stores it in a DINT work bit, so it is wrapped.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="a"><type><derived name="DINT" /></type></variable>
                      <variable name="b"><type><derived name="DINT" /></type></variable>
                    </inputVars>
            """,
            """
                    <block localId="1" typeName="LE">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN2"><connectionPointIn><connection refLocalId="3" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="10" ts:SheetNumber="1" /><expression>a</expression></inVariable>
                    <inVariable localId="3"><position x="0" y="30" ts:SheetNumber="1" /><expression>b</expression></inVariable>
                    <inOutVariable localId="4"><position x="60" y="20" ts:SheetNumber="1" /><expression>wDUMMY</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """,
            """
                    <variable name="wDUMMY"><type><derived name="DINT" /></type></variable>
            """);

        var scl = Artifact(Run(xml), "Blocks/Seal.scl");

        Assert.Contains("BOOL_TO_DINT((#a <= #b))", scl);
    }

    [Fact]
    public void Pack_inputs_are_converted_from_integer_work_bits_to_boolean()
    {
        // PACK16's operands are Booleans; the diagram feeds them DINT work bits, so each is coerced.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
            """,
            """
                    <block localId="1" typeName="PACK16">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN01"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN02"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN03"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN04"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN05"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN06"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN07"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN08"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN09"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN10"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN11"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN12"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN13"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN14"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN15"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN16"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="10" ts:SheetNumber="1" /><expression>AIX</expression></inVariable>
                    <inOutVariable localId="3"><position x="60" y="20" ts:SheetNumber="1" /><expression>PACKED</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """,
            """
                      <variable name="AIX"><type><derived name="DINT" /></type></variable>
                      <variable name="PACKED"><type><derived name="DWORD" /></type></variable>
            """);

        var scl = Artifact(Run(xml), "Blocks/Seal.scl");

        Assert.Contains("BOOL_TO_DWORD(DINT_TO_BOOL(\"GlobalData\".AIX))", scl);
    }

    [Fact]
    public void Boolean_literal_is_converted_when_the_target_is_not_boolean()
    {
        // TRUE is a Bool literal; assigning it to an integer work bit needs an explicit conversion.
        var xml = FbdSample(
            """
                    <inputVars />
            """,
            """
                    <inOutVariable localId="1"><position x="60" y="20" ts:SheetNumber="1" /><expression>wDUMMY</expression><connectionPointIn><connection refLocalId="2" /></connectionPointIn></inOutVariable>
                    <inVariable localId="2"><position x="0" y="20" ts:SheetNumber="1" /><expression>TRUE</expression></inVariable>
            """,
            """
                      <variable name="wDUMMY"><type><derived name="DINT" /></type></variable>
            """);

        var scl = Artifact(Run(xml), "Blocks/Seal.scl");

        Assert.Contains("\"GlobalData\".wDUMMY := BOOL_TO_DINT(TRUE);", scl);
    }

    [Fact]
    public void St_body_is_carried_through_and_local_names_are_prefixed()
    {
        var scl = Artifact(Run(Sample()), "Blocks/MainProgram.scl");

        Assert.Contains("ORGANIZATION_BLOCK \"MainProgram\"", scl);
        Assert.Contains("END_ORGANIZATION_BLOCK", scl);
        Assert.Contains("#Start := TRUE;", scl);
    }

    [Fact]
    public void TriStation_addresses_are_translated_into_siemens_syntax()
    {
        var tags = Artifact(Run(Sample()), "Tags/GlobalTags.xml");

        // AIXXX: memory word 9, but a DInt is 32-bit -> %MD9, ALXXX: memory bit 10 -> %M1.2.
        Assert.Contains("<LogicalAddress>%MD9</LogicalAddress>", tags);
        Assert.Contains("<LogicalAddress>%M1.2</LogicalAddress>", tags);
    }

    [Fact]
    public void Generated_files_are_marked_for_utf8_bom()
    {
        var result = Run(Sample());

        var bytes = result.Files.Single(file => file.RelativePath == "Blocks/PumpCtrl.scl").ToBytes();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void Validate_only_generates_nothing()
    {
        var result = Run(Sample(), new ConverterOptions { ValidateOnly = true });

        Assert.True(result.Success, result.FatalError);
        Assert.True(result.ValidateOnly);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void Undefined_block_variable_type_is_fatal()
    {
        var xml = Sample().Replace(
            "<type><derived name=\"BOOL\" /></type>",
            "<type><NOPE /></type>",
            StringComparison.Ordinal);

        var result = Run(xml);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Fatal, issue => issue.Message.Contains("undefined type \"NOPE\""));
    }

    [Fact]
    public void Instance_of_a_block_missing_from_the_export_is_a_manual_action_not_a_failure()
    {
        // TriStation library blocks arrive as <derived name="R_TRIG"/>, and the Siemens equivalent
        // may well be missing, so this must not stop the conversion.
        var xml = Sample().Replace(
            "<type><derived name=\"BOOL\" /></type>",
            "<type><derived name=\"TMR_TRI\" /></type>",
            StringComparison.Ordinal);

        var result = Run(xml);

        Assert.True(result.Success, result.FatalError);
        Assert.Contains(result.Validation.Warnings, issue => issue.Message.Contains("TMR_TRI"));
    }

    [Fact]
    public void Unmapped_global_type_is_reported_without_stopping_the_conversion()
    {
        var xml = Sample().Replace(
            "<type><derived name=\"DINT\" /></type>",
            "<type><derived name=\"NOPE\" /></type>",
            StringComparison.Ordinal);

        var result = Run(xml);

        Assert.True(result.Success, result.FatalError);
        Assert.Contains(result.Validation.Warnings, issue => issue.Message.Contains("unmapped type \"NOPE\""));
    }

    [Fact]
    public void Duplicate_block_names_are_reported_as_a_warning_not_a_failure()
    {
        // Siemens block names are case-insensitive, so TEMPLATES and Templates collide there even
        // though TriStation accepts both.
        var xml = Sample().Replace(
            "name=\"MainProgram\" pouType=\"program\"",
            "name=\"PUMpCtrl\" pouType=\"program\"",
            StringComparison.Ordinal);

        var result = Run(xml);

        Assert.True(result.Success, result.FatalError);
        Assert.Contains(result.Validation.Warnings, issue => issue.Message.Contains("Duplicate block name"));
    }

    /// <summary>
    /// TR_PID has no Siemens equivalent (the note points at PID_Compact), so its call must survive
    /// as a documented manual action rather than being dropped or silently invented.
    /// </summary>
    [Fact]
    public void Block_call_without_a_siemens_equivalent_becomes_a_manual_action()
    {
        var xml = Sample().Replace("typeName=\"R_TRIG\"", "typeName=\"TR_PID\"", StringComparison.Ordinal);

        var result = Run(xml);

        Assert.True(result.Success, result.FatalError);
        Assert.Contains(result.Mapping.ManualActions, action => action.Title.Contains("TR_PID"));
        Assert.Contains("TODO manual mapping required", Artifact(result, "Blocks/PumpCtrl.scl"));
    }

    [Fact]
    public void Report_lists_files_and_manual_actions()
    {
        var result = Run(Sample());

        var report = Artifact(result, "conversion_report.txt");

        Assert.Contains("CONVERSION REPORT", report);
        Assert.Contains("SUMMARY", report);
        Assert.Contains("FILES GENERATED", report);
        Assert.Contains("Blocks/PumpCtrl.scl", report);
    }

    [Fact]
    public void Malformed_xml_fails_gracefully()
    {
        var result = Run("<project>");

        Assert.False(result.Success);
        Assert.NotNull(result.FatalError);
    }

    [Fact]
    public void A_non_plcopen_root_is_rejected_with_a_clear_message()
    {
        var result = Run("<Project name=\"Legacy\" />");

        Assert.False(result.Success);
        Assert.Contains("PLCopen", result.FatalError);
    }

    [Fact]
    public void The_real_customer_export_converts_end_to_end_when_present()
    {
        const string path = @"C:\Users\Moatasem\Downloads\CON8-38_FB.XML";
        if (!File.Exists(path)) return;

        using var stream = File.OpenRead(path);
        var result = new ConversionEngine().Run(
            stream,
            new ConverterOptions { TiaVersion = 20 },
            "CON8-38_FB.XML");

        Assert.True(result.Success, result.FatalError);
        Assert.Equal(26, result.Project.Blocks.Count);
        Assert.Equal(2381, result.Project.GlobalVariables.Count);

        // Every convertible block gets an SCL file, plus one stand-in block per unmapped library block
        // and one wrapper function block per organization block that needs a block instance. STFB5,
        // STFB6 and STFB7 are left out: their source declares a variable with no data type.
        var scl = result.Files.Where(file => file.RelativePath.EndsWith(".scl", StringComparison.Ordinal)).ToList();
        Assert.Equal(34, scl.Count);
        Assert.All(scl, file => Assert.Contains("END_", file.Content, StringComparison.Ordinal));
        Assert.DoesNotContain(scl, file => file.Content.Contains("<unspecified>", StringComparison.Ordinal));

        // Each wrapper has a matching instance DB emitted as SimaticML, and the import manifest
        // orders every generated source.
        var instanceDbs = result.Files
            .Where(file => file.RelativePath.StartsWith("Blocks/", StringComparison.Ordinal)
                && file.RelativePath.EndsWith(".xml", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(8, instanceDbs.Count);
        Assert.Contains(result.Files, file => file.RelativePath == "ImportOrder.json");

        // UNPACK16, UNPACK32 and SR are the only stand-ins this export needs.
        Assert.Contains(scl, file => file.RelativePath == "Library/Triconex_UNPACK16.scl");
        Assert.Contains(scl, file => file.RelativePath == "Library/Triconex_UNPACK32.scl");
        Assert.Contains(scl, file => file.RelativePath == "Library/Triconex_SR.scl");

        // Every manual action must be one of the known TriStation library calls, a structural note
        // the FBD emitter raised about the diagram itself, or a source declaration that has no data
        // type (an STFB where the XML interface is empty and the CDATA declaration is incomplete).
        var knownCalls = new[] { "PACK16", "PACK32", "SCALE", "TDD_I", "TP_I" };
        Assert.All(
            result.Mapping.ManualActions,
            action => Assert.True(
                knownCalls.Any(call => action.Title.StartsWith(call, StringComparison.Ordinal))
                || action.Title.Contains("sheet", StringComparison.OrdinalIgnoreCase)
                || action.Title.Contains("without a data type", StringComparison.OrdinalIgnoreCase)
                || action.Title.Contains("was renamed", StringComparison.OrdinalIgnoreCase),
                $"unexpected manual action: {action.Title}"));

        // The FBD emitter's structural notes must reach the report as well.
        Assert.Contains(result.Mapping.ManualActions, action => action.Title.Contains("sheet", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The TriStation 1131 Libraries Reference defines PACK16/PACK32/SCALE with a return value
    /// ("w := PACK16(...)") and UNPACK16/UNPACK32 as space saver instances
    /// ("VAR x : UNPACK16"). Classifying a block as a function breaks both the ordering and the
    /// emitted syntax, so the kind of every manually mapped library call is pinned here.
    /// </summary>
    [Theory]
    [InlineData("PACK16", true)]
    [InlineData("PACK32", true)]
    [InlineData("SCALE", true)]
    [InlineData("UNPACK16", false)]
    [InlineData("UNPACK32", false)]
    [InlineData("TDD_I", false)]
    [InlineData("TP_I", false)]
    public void Library_call_kind_matches_the_tristation_reference(string typeName, bool expectedIsFunction)
    {
        var mappings = MappingLoader.Load(null);

        Assert.True(mappings.LibraryFunctions.ContainsKey(typeName), $"no rule for {typeName}");

        var rule = mappings.LibraryFunctions[typeName];
        Assert.Equal(expectedIsFunction, string.Equals(rule.Kind, "function", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(rule.Note));
    }

    /// <summary>
    /// The functions whose TriStation semantics have an exact arithmetic form are generated from an
    /// <c>inline</c> expression instead of a Siemens name, so the diagram's own signals are
    /// substituted into the documented formula.
    /// </summary>
    [Theory]
    [InlineData("PACK16")]
    [InlineData("PACK32")]
    [InlineData("SCALE")]
    public void Arithmetic_library_function_is_mapped_to_an_inline_expression(string typeName)
    {
        var rule = MappingLoader.Load(null).LibraryFunctions[typeName];

        Assert.Equal(ConversionStatus.Approximated, Mappings.ParseStatus(rule.Status));
        Assert.True(IsFunctionKind(rule), "an inline expression can only replace a function");
        Assert.Null(rule.Siemens);
        Assert.False(string.IsNullOrWhiteSpace(rule.Inline), $"{typeName} needs an inline expression");

        // Every placeholder must name a formal the reference declares. Operands may repeat (SCALE
        // uses MIN1 twice), but a placeholder must never be an empty or malformed name.
        var formals = Regex.Matches(rule.Inline!, @"\{([A-Za-z_][A-Za-z0-9_]*)\}")
            .Select(match => match.Groups[1].Value)
            .ToList();
        Assert.NotEmpty(formals);
        Assert.All(formals, formal => Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", formal));
        Assert.DoesNotContain("<unspecified>", rule.Inline!);
    }

    /// <summary>
    /// The two timers map onto the Siemens timer of the same name; only the formal names and the
    /// millisecond-to-TIME conversion differ, which the parameter rules carry.
    /// </summary>
    [Theory]
    [InlineData("TDD_I", "TOF", "I1", "IN", "TGT", "PT")]
    [InlineData("TP_I", "TP", "IN", "IN", "PT", "PT")]
    public void Timer_library_block_maps_to_the_matching_siemens_timer(
        string typeName, string siemens, string inputFormal, string mappedInput, string timeFormal, string mappedTime)
    {
        var rule = MappingLoader.Load(null).LibraryFunctions[typeName];

        Assert.Equal(ConversionStatus.Approximated, Mappings.ParseStatus(rule.Status));
        Assert.Equal(siemens, rule.Siemens);
        Assert.False(IsFunctionKind(rule), "a timer keeps its state, so it must be an instance");

        Assert.NotNull(rule.Parameters);
        Assert.Equal(mappedInput, rule.Parameters![inputFormal].Name);
        Assert.Equal(mappedTime, rule.Parameters![timeFormal].Name);

        // The delay arrives as a DINT millisecond count and must reach PT as a TIME.
        Assert.Equal("DINT_TO_TIME", rule.Parameters[timeFormal].Convert);
    }

    /// <summary>
    /// UNPACK16/UNPACK32 have no Siemens equivalent, but a helper block can stand in for them
    /// because their formals (CI, INPUT, CO, NZ, Q01..Qnn) survive unchanged. The mapping therefore
    /// names a generated block instead of leaving the call site as a manual action.
    /// </summary>
    [Theory]
    [InlineData("UNPACK16")]
    [InlineData("UNPACK32")]
    public void Unpack_block_maps_to_a_generated_helper_block(string typeName)
    {
        var rule = MappingLoader.Load(null).LibraryFunctions[typeName];

        Assert.Equal(ConversionStatus.Approximated, Mappings.ParseStatus(rule.Status));
        Assert.Equal(SupportBlockGenerator.SupportPrefix + typeName, rule.Siemens);
        Assert.Null(rule.Inline);
        // A block, not a function: the outputs are read one at a time, so it cannot be inlined.
        Assert.False(IsFunctionKind(rule));
    }

    /// <summary>
    /// The helper block must unpack the bit its own output names, Q01 being the least significant
    /// one, and UNPACK16 must ignore the upper half of the DWORD.
    /// </summary>
    [Theory]
    [InlineData("UNPACK16", "Triconex_UNPACK16", "DWORD#16#0000FFFF", "DWORD#16#00000001", "DWORD#16#00008000", "Q16")]
    [InlineData("UNPACK32", "Triconex_UNPACK32", "DWORD#16#FFFFFFFF", "DWORD#16#00000001", "DWORD#16#80000000", "Q32")]
    public void Generated_helper_block_unpacks_one_bit_per_output(
        string sourceType,
        string generatedType,
        string mask,
        string lowBit,
        string topBit,
        string topOutput)
    {
        var result = Run(UnpackSample().Replace("UNPACK16", sourceType, StringComparison.Ordinal));

        Assert.True(result.Success, result.FatalError);
        var scl = Artifact(result, $"Library/{generatedType}.scl");
        Assert.Contains($"FUNCTION_BLOCK \"{generatedType}\"", scl);
        Assert.Contains($"#NZ := (#INPUT AND {mask}) <> 0;", scl);
        Assert.Contains($"#Q01 := (#INPUT AND {lowBit}) <> 0;", scl);
        Assert.Contains($"#{topOutput} := (#INPUT AND {topBit}) <> 0;", scl);
        Assert.Contains("END_FUNCTION_BLOCK", scl);

        // The call site must declare an instance of the generated block. A function
        // would have been inlined instead, with no declaration at all.
        var consumer = Artifact(result, "Blocks/UnpackCtrl.scl");
        Assert.Contains($"{sourceType} : {generatedType};", consumer);
    }

    private static bool IsFunctionKind(MappingRule rule) =>
        string.Equals(rule.Kind, "function", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A block call that reads another block's output must be emitted after it. This only holds when
    /// the producer is recognised as a block: FbdSclEmitter skips function producers when building
    /// the dependency order, and a function misclassified as a block would also be emitted without
    /// an instance declaration.
    /// </summary>
    [Fact]
    public void Unpack_block_is_ordered_before_its_consumer()
    {
        var result = Run(UnpackSample());

        Assert.True(result.Success, result.FatalError);

        var call = result.Project.Blocks
            .SelectMany(block => block.Logic is StructuredLogic logic ? logic.Items.OfType<BlockCall>() : Enumerable.Empty<BlockCall>())
            .Single(item => item.TypeName == "UNPACK16");

        Assert.False(call.IsFunction, "UNPACK16 is a TriStation function block, not a function");

        var scl = Artifact(result, "Blocks/UnpackCtrl.scl");

        // The instance is declared with the generated stand-in type. A function would have been
        // inlined instead, with no declaration at all. The instance keeps the TriStation name
        // because that no longer collides with its (renamed) type.
        Assert.Contains("UNPACK16 : Triconex_UNPACK16;", scl);
        Assert.Contains("UNPACK16(CI := TRUE, INPUT := #Word);", scl);

        var unpack = scl.IndexOf("UNPACK16(CI", StringComparison.Ordinal);
        var trig = scl.IndexOf("R_TRIG_3(", StringComparison.Ordinal);
        Assert.True(unpack >= 0 && trig >= 0, "both calls must be present");
        Assert.True(unpack < trig, "UNPACK16 must be called before the R_TRIG that reads its Q01 output");
    }

    /// <summary>
    /// One FBD containing an UNPACK16 whose Q01 output feeds an R_TRIG, so both the generated
    /// stand-in block and the call ordering are exercised. The block's type name is the only thing
    /// a test needs to vary.
    /// </summary>
    private static string UnpackSample() => $"""
            <?xml version="1.0" encoding="utf-8"?>
            <project xmlns="{Tc6}" xmlns:ts="{Ts}">
              <header><content name="name">MainConfiguration</content></header>
              <types>
                <dataTypes />
                <pous>
                  <pou name="UnpackCtrl" pouType="functionBlock">
                    <interface>
                      <inputVars>
                        <variable name="Word">
                          <type><derived name="DWORD" /></type>
                        </variable>
                      </inputVars>
                      <outputVars>
                        <variable name="Seen">
                          <type><derived name="BOOL" /></type>
                        </variable>
                      </outputVars>
                      <localVars />
                    </interface>
                    <body>
                      <FBD>
                        <block localId="1" typeName="UNPACK16">
                          <position x="20" y="20" ts:SheetNumber="1" />
                          <inputVariables>
                            <variable formalParameter="CI">
                              <connectionPointIn>
                                <connection refLocalId="5" formalParameter="CLK" />
                              </connectionPointIn>
                            </variable>
                            <variable formalParameter="INPUT">
                              <connectionPointIn>
                                <connection refLocalId="2" formalParameter="Word" />
                              </connectionPointIn>
                            </variable>
                          </inputVariables>
                          <outputVariables>
                            <variable formalParameter="Q01">
                              <connectionPointOut />
                            </variable>
                          </outputVariables>
                        </block>
                        <block localId="3" typeName="R_TRIG">
                          <position x="80" y="20" ts:SheetNumber="1" />
                          <inputVariables>
                            <variable formalParameter="CLK">
                              <connectionPointIn>
                                <connection refLocalId="1" formalParameter="Q01" />
                              </connectionPointIn>
                            </variable>
                          </inputVariables>
                          <outputVariables>
                            <variable formalParameter="Q">
                              <connectionPointOut />
                            </variable>
                          </outputVariables>
                        </block>
                        <inVariable localId="2">
                          <position x="0" y="20" ts:SheetNumber="1" />
                          <expression>Word</expression>
                        </inVariable>
                        <inVariable localId="5">
                          <position x="0" y="60" ts:SheetNumber="1" />
                          <expression>TRUE</expression>
                        </inVariable>
                        <inOutVariable localId="4">
                          <position x="140" y="20" ts:SheetNumber="1" />
                          <expression>Seen</expression>
                          <connectionPointIn>
                            <connection refLocalId="3" formalParameter="Q" />
                          </connectionPointIn>
                        </inOutVariable>
                      </FBD>
                    </body>
                  </pou>
                </pous>
              </types>
              <instances>
                <configurations>
                  <configuration name="MainConfiguration">
                    <resource name="Res1">
                      <task name="MainTask" interval="100" />
                      <globalVars />
                    </resource>
                  </configuration>
                </configurations>
                <ts:CONTROLLER name="TRICON_EP" majorMinorVersion="11.2">
                  <ts:COMMUNICATION><ts:UDP_PORT>1502</ts:UDP_PORT></ts:COMMUNICATION>
                </ts:CONTROLLER>
              </instances>
            </project>
            """;

    [Theory]
    [InlineData(10, 16)]
    [InlineData(16, 16)]
    [InlineData(17, 17)]
    [InlineData(20, 20)]
    [InlineData(25, 21)]
    public void Tia_version_is_clamped(int input, int expected)
    {
        var options = new ConverterOptions { TiaVersion = input };

        Assert.Equal(expected, options.ClampTiaVersion());
    }

    [Fact]
    public void Fbd_mode_emits_simaticml_instead_of_scl()
    {
        // The default SCL path only outputs Seal.scl; "fbd" must swap that for a SimaticML document
        // carrying an FlgNet body, and the SCL generator must not emit a second Seal file.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
                    <outputVars>
                      <variable name="Running"><type><derived name="BOOL" /></type></variable>
                    </outputVars>
            """,
            """
                    <block localId="1" typeName="OR">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN2"><connectionPointIn><connection refLocalId="3" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="10" ts:SheetNumber="1" /><expression>Start</expression></inVariable>
                    <inOutVariable localId="3"><position x="0" y="30" ts:SheetNumber="1" /><expression>Running</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """);

        var result = Run(xml, new ConverterOptions { TiaVersion = 20, FbdOutput = FbdOutputMode.Fbd });

        Assert.True(result.Success, result.FatalError);

        var paths = result.Files.Select(file => file.RelativePath).ToList();
        Assert.Contains("Blocks/Seal.xml", paths);
        Assert.DoesNotContain("Blocks/Seal.scl", paths);

        var block = Artifact(result, "Blocks/Seal.xml");
        Assert.Contains("<SW.Blocks.FB ID=\"0\">", block);
        Assert.Contains("<ProgrammingLanguage>FBD</ProgrammingLanguage>", block);
        // Boolean gates use TIA's single-letter, untyped box spellings (A/O), proven by live V16 import.
        Assert.Contains("<Part Name=\"O\"", block);
        Assert.Contains("FlgNet", block);
    }

    [Fact]
    public void Fbd_mode_emits_not_box_with_card_srctype_and_enable()
    {
        // Genuine V16 exports spell Boolean NOT as <Part Name="Not"> with Card=2 and SrcType Bool, and
        // wire its en pin. This locks the shape the live-import probe confirmed.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
                    <outputVars>
                      <variable name="Running"><type><derived name="BOOL" /></type></variable>
                    </outputVars>
            """,
            """
                    <block localId="1" typeName="NOT">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="20" ts:SheetNumber="1" /><expression>Start</expression></inVariable>
                    <inOutVariable localId="3"><position x="60" y="20" ts:SheetNumber="1" /><expression>Running</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """);

        var block = Artifact(
            Run(xml, new ConverterOptions { TiaVersion = 20, FbdOutput = FbdOutputMode.Fbd }),
            "Blocks/Seal.xml");

        Assert.Contains("<Part Name=\"Not\"", block);
        Assert.Contains("<TemplateValue Name=\"Card\" Type=\"Cardinality\">2</TemplateValue>", block);
        Assert.Contains("<TemplateValue Name=\"SrcType\" Type=\"Type\">Bool</TemplateValue>", block);
        Assert.Contains("Name=\"en\"", block);
    }

    [Fact]
    public void Fbd_mode_emits_move_box_with_card_automatic_srctype_and_enable()
    {
        // Genuine V16 exports spell MOVE as <Part Name="Move"> with Card=2, an AutomaticTyped SrcType and
        // a wired en pin.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
                    <outputVars>
                      <variable name="Running"><type><derived name="BOOL" /></type></variable>
                    </outputVars>
            """,
            """
                    <block localId="1" typeName="MOVE">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="20" ts:SheetNumber="1" /><expression>Start</expression></inVariable>
                    <inOutVariable localId="3"><position x="60" y="20" ts:SheetNumber="1" /><expression>Running</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """);

        var block = Artifact(
            Run(xml, new ConverterOptions { TiaVersion = 20, FbdOutput = FbdOutputMode.Fbd }),
            "Blocks/Seal.xml");

        Assert.Contains("<Part Name=\"Move\"", block);
        Assert.Contains("<TemplateValue Name=\"Card\" Type=\"Cardinality\">2</TemplateValue>", block);
        Assert.Contains("<AutomaticTyped Name=\"SrcType\" />", block);
        Assert.Contains("Name=\"en\"", block);
    }

    [Fact]
    public void Fbd_ga_dialect_emits_not_as_negated_single_input_gate()
    {
        // The V16.0 GA build rejects every Not part spelling, so the GA dialect renders NOT as an
        // equivalent single-input A gate whose in1 is inverted. This locks the shape the live-import
        // probe (A_NEG_C1) confirmed.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
                    <outputVars>
                      <variable name="Running"><type><derived name="BOOL" /></type></variable>
                    </outputVars>
            """,
            """
                    <block localId="1" typeName="NOT">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="20" ts:SheetNumber="1" /><expression>Start</expression></inVariable>
                    <inOutVariable localId="3"><position x="60" y="20" ts:SheetNumber="1" /><expression>Running</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """);

        var block = Artifact(
            Run(xml, new ConverterOptions
            {
                TiaVersion = 20,
                FbdOutput = FbdOutputMode.Fbd,
                FbdDialect = FbdBoxDialect.V16Ga,
            }),
            "Blocks/Seal.xml");

        Assert.Contains("<Part Name=\"A\"", block);
        Assert.Contains("<TemplateValue Name=\"Card\" Type=\"Cardinality\">1</TemplateValue>", block);
        Assert.Contains("<Negated Name=\"in1\" />", block);
        Assert.DoesNotContain("<Part Name=\"Not\"", block);
        Assert.DoesNotContain("AutomaticTyped", block);
        Assert.DoesNotContain("Name=\"en\"", block);
    }

    [Fact]
    public void Fbd_ga_dialect_dissolves_a_move_copy_into_its_assignment()
    {
        // The V16.0 GA build rejects a Move's AutomaticTyped SrcType, and a Move output cannot feed a
        // box pin, so a single-source MOVE is resolved to its input: the target operand is assigned
        // straight from the source (a Coil), with no Move part and no SrcType at all.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
                    <outputVars>
                      <variable name="Running"><type><derived name="BOOL" /></type></variable>
                    </outputVars>
            """,
            """
                    <block localId="1" typeName="MOVE">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="20" ts:SheetNumber="1" /><expression>Start</expression></inVariable>
                    <inOutVariable localId="3"><position x="60" y="20" ts:SheetNumber="1" /><expression>Running</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """);

        var block = Artifact(
            Run(xml, new ConverterOptions
            {
                TiaVersion = 20,
                FbdOutput = FbdOutputMode.Fbd,
                FbdDialect = FbdBoxDialect.V16Ga,
            }),
            "Blocks/Seal.xml");

        Assert.Contains("<Part Name=\"Coil\"", block);
        Assert.DoesNotContain("<Part Name=\"Move\"", block);
        Assert.DoesNotContain("AutomaticTyped", block);
    }

    [Fact]
    public void Fbd_mode_falls_back_to_scl_for_a_feedback_cycle()
    {
        // Two NOT boxes wired into each other form a cycle. TIA FBD cannot express a cyclic FlgNet, so
        // the block must stay SCL and the report must record the reason.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
                    <outputVars>
                      <variable name="Running"><type><derived name="BOOL" /></type></variable>
                    </outputVars>
            """,
            """
                    <block localId="1" typeName="NOT">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="3" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <block localId="3" typeName="NOT">
                      <position x="20" y="60" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="1" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inOutVariable localId="5"><position x="60" y="20" ts:SheetNumber="1" /><expression>Running</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """);

        var result = Run(xml, new ConverterOptions { TiaVersion = 20, FbdOutput = FbdOutputMode.Fbd });

        Assert.True(result.Success, result.FatalError);
        var paths = result.Files.Select(file => file.RelativePath).ToList();
        Assert.Contains("Blocks/Seal.scl", paths);
        Assert.DoesNotContain("Blocks/Seal.xml", paths);

        var report = Artifact(result, "conversion_report.txt");
        Assert.Contains("feedback cycle", report);
    }

    [Fact]
    public void Fbd_mode_falls_back_to_scl_for_unverified_boxes()
    {
        // ROL has no FBD shape verified against TIA V16, so the block must stay SCL and the report must
        // record the reason rather than emit a guessed box.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="A"><type><derived name="BOOL" /></type></variable>
                      <variable name="B"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
                    <outputVars>
                      <variable name="C"><type><derived name="BOOL" /></type></variable>
                    </outputVars>
            """,
            """
                    <block localId="1" typeName="ROL">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
                        <variable formalParameter="IN1"><connectionPointIn><connection refLocalId="2" /></connectionPointIn></variable>
                        <variable formalParameter="IN2"><connectionPointIn><connection refLocalId="3" /></connectionPointIn></variable>
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="10" ts:SheetNumber="1" /><expression>A</expression></inVariable>
                    <inVariable localId="3"><position x="0" y="30" ts:SheetNumber="1" /><expression>B</expression></inVariable>
                    <inOutVariable localId="4"><position x="60" y="20" ts:SheetNumber="1" /><expression>C</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """);

        var result = Run(xml, new ConverterOptions { TiaVersion = 20, FbdOutput = FbdOutputMode.Fbd });

        Assert.True(result.Success, result.FatalError);
        var paths = result.Files.Select(file => file.RelativePath).ToList();
        Assert.Contains("Blocks/Seal.scl", paths);
        Assert.DoesNotContain("Blocks/Seal.xml", paths);

        var report = Artifact(result, "conversion_report.txt");
        Assert.Contains("FBD FALLBACK TO SCL", report);
        Assert.Contains("no FBD form verified", report);
    }

    [Fact]
    public void Fbd_mode_emits_helper_blocks_for_inline_expansions()
    {
        // PACK16 has no box; the FBD renderer approximates it as the Triconex_PACK16 helper FC and
        // ships the FC source under Library/ so the call graph resolves.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
            """,
            Pack16Diagram(),
            """
                      <variable name="AIX"><type><derived name="DINT" /></type></variable>
                      <variable name="PACKED"><type><derived name="DWORD" /></type></variable>
            """);

        var result = Run(xml, new ConverterOptions { TiaVersion = 20, FbdOutput = FbdOutputMode.Fbd });

        Assert.True(result.Success, result.FatalError);

        var paths = result.Files.Select(file => file.RelativePath).ToList();
        Assert.Contains("Blocks/Seal.xml", paths);
        Assert.Contains("Library/Triconex_PACK16.scl", paths);
        Assert.DoesNotContain("Blocks/Seal.scl", paths);

        var block = Artifact(result, "Blocks/Seal.xml");
        Assert.Contains("Name=\"Triconex_PACK16\"", block);

        var helper = Artifact(result, "Library/Triconex_PACK16.scl");
        Assert.Contains("FUNCTION \"Triconex_PACK16\" : DWord", helper);
    }

    [Fact]
    public void Fbd_auto_mode_falls_back_to_scl_for_approximations()
    {
        // "auto" keeps a block in SCL when its FBD rendering needs an approximation, so the
        // approximation stays text-verifiable and no helper library is invented.
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
            """,
            Pack16Diagram(),
            """
                      <variable name="AIX"><type><derived name="DINT" /></type></variable>
                      <variable name="PACKED"><type><derived name="DWORD" /></type></variable>
            """);

        var result = Run(xml, new ConverterOptions { TiaVersion = 20, FbdOutput = FbdOutputMode.Auto });

        Assert.True(result.Success, result.FatalError);

        var paths = result.Files.Select(file => file.RelativePath).ToList();
        Assert.Contains("Blocks/Seal.scl", paths);
        Assert.DoesNotContain("Blocks/Seal.xml", paths);
        Assert.DoesNotContain("Library/Triconex_PACK16.scl", paths);
    }

    [Fact]
    public void Fbd_report_lists_approximations()
    {
        var xml = FbdSample(
            """
                    <inputVars>
                      <variable name="Start"><type><derived name="BOOL" /></type></variable>
                    </inputVars>
            """,
            Pack16Diagram(),
            """
                      <variable name="AIX"><type><derived name="DINT" /></type></variable>
                      <variable name="PACKED"><type><derived name="DWORD" /></type></variable>
            """);

        var report = Artifact(
            Run(xml, new ConverterOptions { TiaVersion = 20, FbdOutput = FbdOutputMode.Fbd }),
            "conversion_report.txt");

        Assert.Contains("FBD APPROXIMATIONS", report);
        Assert.Contains("Triconex_PACK16", report);
    }

    /// <summary>A PACK16 call fed by one DINT work bit on all sixteen inputs.</summary>
    private static string Pack16Diagram()
    {
        var inputs = new System.Text.StringBuilder();
        for (var bit = 1; bit <= 16; bit++)
        {
            inputs.Append("                    <variable formalParameter=\"IN")
                .Append(bit.ToString("D2", System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine("\"><connectionPointIn><connection refLocalId=\"2\" /></connectionPointIn></variable>");
        }

        return $"""
                    <block localId="1" typeName="PACK16">
                      <position x="20" y="20" ts:SheetNumber="1" />
                      <inputVariables>
            {inputs}
                      </inputVariables>
                    </block>
                    <inVariable localId="2"><position x="0" y="10" ts:SheetNumber="1" /><expression>AIX</expression></inVariable>
                    <inOutVariable localId="3"><position x="60" y="20" ts:SheetNumber="1" /><expression>PACKED</expression><connectionPointIn><connection refLocalId="1" /></connectionPointIn></inOutVariable>
            """;
    }
}




