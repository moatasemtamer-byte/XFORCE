using TriconexToTia.Core.Enums;
using TriconexToTia.Mapper;
using TriconexToTia.Parser;
using Xunit;

namespace TriconexToTia.Engine.Tests;

/// <summary>Covers the severity rules from spec section 5, stage 2, using PLCopen fixtures.</summary>
public class ProjectValidatorTests
{
    private static Core.Results.ValidationResult Validate(string pouBody, string interfaceXml = "")
    {
        var xml = $"""
            <project xmlns="http://www.plcopen.org/xml/tc6.xsd">
              <fileHeader productName="TriStation 1131" productRelease="V5.8.0.153" />
              <types><pous>
                {pouBody}
              </pous></types>
              {interfaceXml}
            </project>
            """;

        var project = new PlcOpenXmlParser().Parse(xml);
        return new ProjectValidator(MappingLoader.Load(null)).Validate(project);
    }

    private static string Pou(string name, string pouType, string body, string? interfaceXml = null) => $"""
        <pou name="{name}" version="1.0" pouType="{pouType}">
          {(interfaceXml is null ? string.Empty : $"<interface>{interfaceXml}</interface>")}
          <body>{body}</body>
          <documentation />
        </pou>
        """;

    [Fact]
    public void Empty_block_is_a_warning_not_fatal()
    {
        var result = Validate(Pou("Empty", "functionBlock", "<ST><![CDATA[]]></ST>"));

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("has no code"));
    }

    [Fact]
    public void Fbd_block_is_flagged_for_manual_verification()
    {
        var result = Validate(Pou("Fbd", "functionBlock",
            "<FBD><inVariable localId=\"1\"><expression>X</expression></inVariable></FBD>"));

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("connection graph"));
    }

    [Fact]
    public void Duplicate_variable_in_one_block_is_fatal()
    {
        var result = Validate(Pou("B", "functionBlock", "<ST><![CDATA[a := a;]]></ST>",
            """
            <inputVars>
              <variable name="X"><type><BOOL /></type></variable>
              <variable name="X"><type><BOOL /></type></variable>
            </inputVars>
            """));

        Assert.True(result.HasFatal);
        Assert.Contains(result.Fatal, f => f.Message.Contains("Duplicate variable"));
    }

    [Fact]
    public void Undefined_primitive_type_in_a_block_is_fatal()
    {
        var result = Validate(Pou("B", "functionBlock", "<ST><![CDATA[a := a;]]></ST>",
            """
            <inputVars><variable name="X"><type><MYSTERY /></type></variable></inputVars>
            """));

        Assert.True(result.HasFatal);
        Assert.Contains(result.Fatal, f => f.Message.Contains("undefined type \"MYSTERY\""));
    }

    [Fact]
    public void Derived_library_block_instance_is_a_manual_warning_not_fatal()
    {
        // TriStation library blocks are referenced by <derived> but are not part of the export. TR_PID
        // additionally has no Siemens equivalent, so it can only be reported: a fatal error here would
        // make every real project containing one unconvertible.
        var result = Validate(Pou("B", "functionBlock", "<ST><![CDATA[a := a;]]></ST>",
            """
            <localVars name="Statics">
              <variable name="PID_01"><type><derived name="TR_PID" /></type></variable>
            </localVars>
            """));

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("TR_PID") && w.Message.Contains("not defined"));
    }

    /// <summary>
    /// A library block that maps to a Siemens name will exist in the target project, so warning that
    /// it is "not defined in this export" would send the operator looking for a block they already
    /// have. Only the genuinely unmappable blocks may be reported.
    /// </summary>
    [Theory]
    [InlineData("R_TRIG")]
    [InlineData("F_TRIG")]
    [InlineData("UNPACK16")]
    [InlineData("UNPACK32")]
    public void Instance_of_a_mapped_library_block_is_a_known_type(string typeName)
    {
        var result = Validate(Pou("B", "functionBlock", "<ST><![CDATA[a := a;]]></ST>",
            $"""
            <localVars name="Statics">
              <variable name="Inst_01"><type><derived name="{typeName}" /></type></variable>
            </localVars>
            """));

        Assert.False(result.HasFatal);
        Assert.DoesNotContain(result.Warnings, w => w.Message.Contains(typeName) && w.Message.Contains("not defined"));
    }

    [Fact]
    public void Derived_reference_to_a_pou_in_the_same_project_is_known()
    {
        var result = Validate($"""
            {Pou("FB_A", "functionBlock", "<ST><![CDATA[a := a;]]></ST>")}
            {Pou("FB_B", "functionBlock", "<ST><![CDATA[a := a;]]></ST>",
                """
                <localVars name="Statics">
                  <variable name="Inst"><type><derived name="FB_A" /></type></variable>
                </localVars>
                """)}
            """);

        Assert.False(result.HasFatal);
        Assert.DoesNotContain(result.Warnings, w => w.Message.Contains("FB_A"));
    }

    [Fact]
    public void Unmapped_global_type_is_only_a_warning()
    {
        var xml = """
            <project xmlns="http://www.plcopen.org/xml/tc6.xsd">
              <fileHeader productName="TriStation 1131" />
              <instances><configurations><configuration name="Main">
                <resource name="Res1">
                  <globalVars>
                    <variable name="G"><type><MYSTERY /></type></variable>
                  </globalVars>
                </resource>
              </configuration></configurations></instances>
            </project>
            """;

        var result = new ProjectValidator(MappingLoader.Load(null))
            .Validate(new PlcOpenXmlParser().Parse(xml));

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("unmapped type"));
    }

    [Fact]
    public void Duplicate_block_name_is_a_warning_because_siemens_names_are_case_insensitive()
    {
        var result = Validate($"""
            {Pou("Dup", "functionBlock", "<ST><![CDATA[a := 1;]]></ST>")}
            {Pou("Dup", "program", "<ST><![CDATA[a := 1;]]></ST>")}
            """);

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("case-insensitively ambiguous"));
        Assert.Contains(result.Warnings, w => w.Message.Contains("Rename one block"));
    }

    [Fact]
    public void Fbd_items_producing_nothing_are_reported()
    {
        var result = Validate(Pou("Fbd", "functionBlock", """
            <FBD>
              <inVariable localId="1"><expression>X</expression></inVariable>
            </FBD>
            """));

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("nothing consumes"));
    }

    [Fact]
    public void Ladder_block_reports_that_it_has_no_scl_equivalent()
    {
        var result = Validate(Pou("Ladder", "functionBlock",
            "<ld><leftRail /></ld>"));

        Assert.False(result.HasFatal);
        Assert.Contains(result.Warnings, w => w.Message.Contains("no SCL equivalent"));
    }
}