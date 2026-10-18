using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;
using Xunit;

namespace TriconexToTia.Parser.Tests;

/// <summary>
/// Exercises the reader against <c>Fixtures/plcopen-sample.xml</c>, which mirrors the shape of a
/// real TriStation 1131 export (PLCopen tc6 body plus the ts: vendor extensions).
/// </summary>
public class PlcOpenXmlParserTests
{
    private const string FixtureResource = "TriconexToTia.Parser.Tests.Fixtures.plcopen-sample.xml";

    private static Stream OpenFixture() =>
        typeof(PlcOpenXmlParserTests).Assembly.GetManifestResourceStream(FixtureResource)
        ?? throw new InvalidOperationException($"Embedded fixture {FixtureResource} is missing.");

    private static PlcProject ParseFixture() => new PlcOpenXmlParser().Parse(OpenFixture());

    [Fact]
    public void Reads_file_header()
    {
        var header = ParseFixture().Header;

        Assert.Equal("TriStation 1131", header.ProductName);
        Assert.Equal("V5.8.0.153", header.ProductRelease);
        Assert.Equal("5.5.0.216", header.SchemaVersion);
        Assert.Equal("ConocoPhillips", header.CompanyName);
        Assert.Contains("CON8-38", header.ContentDescription);
    }

    [Fact]
    public void Project_name_comes_from_the_configuration()
    {
        Assert.Equal("MainConfiguration", ParseFixture().Name);
    }

    [Fact]
    public void Reads_programs_and_function_blocks_with_their_pou_type()
    {
        var project = ParseFixture();

        var program = Assert.Single(project.Programs);
        var functionBlock = Assert.Single(project.FunctionBlocks);

        Assert.Equal("MAIN", program.Name);
        Assert.Equal("STFB1", functionBlock.Name);
        Assert.Equal("1.19", functionBlock.Version);
    }

    [Fact]
    public void Reads_the_ts_application_type()
    {
        var project = ParseFixture();

        Assert.Equal("CONTROL", project.Programs.Single().ApplicationType);
        Assert.Equal("SAFETY and CONTROL", project.FunctionBlocks.Single().ApplicationType);
    }

    [Fact]
    public void Reads_interface_variables_with_direction_and_documentation()
    {
        var program = ParseFixture().Programs.Single();

        Assert.Equal(VariableDirection.Input, program.Variables.Single(v => v.Name == "IN_01").Direction);
        Assert.Equal("INPUT 01 TRIP", program.Variables.Single(v => v.Name == "IN_01").Documentation);
        Assert.Equal(VariableDirection.Output, program.Variables.Single(v => v.Name == "OUT_01").Direction);
        Assert.Equal(VariableDirection.InOut, program.Variables.Single(v => v.Name == "FEEDBACK").Direction);
        Assert.Equal("BOOL", program.Variables.Single(v => v.Name == "IN_01").TypeName);
    }

    [Fact]
    public void Reads_constant_group_from_local_vars()
    {
        var program = ParseFixture().Programs.Single();

        var constant = program.Variables.Single(v => v.Name == "SP_MAX");
        Assert.True(constant.Constant);
        Assert.Equal(VariableDirection.Local, constant.Direction);
        Assert.Equal("100.0", constant.InitialValue);
    }

    [Fact]
    public void Reads_derived_types_as_fb_instances_or_data_types()
    {
        var program = ParseFixture().Programs.Single();

        var trigger = program.Variables.Single(v => v.Name == "RT_01");
        Assert.True(trigger.IsDerivedType);
        Assert.Equal("R_TRIG", trigger.TypeName);

        var alarm = program.Variables.Single(v => v.Name == "LastAlarm");
        Assert.True(alarm.IsDerivedType);
        Assert.Equal("ALARM_REC", alarm.TypeName);
    }

    [Fact]
    public void Reads_st_body_verbatim()
    {
        var functionBlock = ParseFixture().FunctionBlocks.Single();

        Assert.Equal(PlcLanguage.ST, functionBlock.Language);
        Assert.StartsWith("FUNCTION_BLOCK STFB1", functionBlock.Code);
        Assert.Contains("OUT1 := IN4;", functionBlock.Code);
        Assert.EndsWith("END_FUNCTION_BLOCK", functionBlock.Code);
        // ST bodies carry their own declaration, so the block must not gain a duplicate interface.
        Assert.Empty(functionBlock.Variables);
        Assert.IsType<TextualLogic>(functionBlock.Logic);
    }

    [Fact]
    public void Reads_fbd_body_as_structured_logic()
    {
        var program = ParseFixture().Programs.Single();

        var logic = Assert.IsType<StructuredLogic>(program.Logic);
        Assert.Equal(PlcLanguage.FBD, program.Language);
        Assert.Equal(new[] { "FACESHEET", "DCS COMMAND FUNCTION BLOCK" }, logic.SheetTitles);
    }

    [Fact]
    public void Reads_fbd_items_with_positions_and_comments()
    {
        var items = ((StructuredLogic)ParseFixture().Programs.Single().Logic!).Items;

        var trigger = items.OfType<BlockCall>().Single(c => c.InstanceName == "RT_01");
        Assert.Equal("R_TRIG", trigger.TypeName);
        Assert.Equal(2, trigger.SheetNumber);
        Assert.Equal(80, trigger.X);

        // ts:SheetNumber is vendor-namespaced, and %DESCRIPTION is a macro token rather than text.
        var feedback = items.OfType<VariableReference>().Single(v => v.LocalId == "12");
        Assert.Equal("FEEDBACK", feedback.Expression);
        Assert.Equal(2, feedback.SheetNumber);
        Assert.Null(feedback.Annotation);

        var comment = Assert.Single(items.OfType<LogicComment>());
        Assert.Equal("FB05_DCS_CMD", comment.Text);
    }

    [Fact]
    public void Resolves_block_input_from_a_block_output()
    {
        var items = ((StructuredLogic)ParseFixture().Programs.Single().Logic!).Items;

        var orBlock = items.OfType<BlockCall>().Single(c => c.TypeName == "OR");
        var in1 = orBlock.Inputs.Single(a => a.FormalParameter == "IN1");

        var source = Assert.Single(in1.Sources);
        Assert.Equal("2", source.RefLocalId);
        Assert.Equal("Q", source.RefFormalParameter);
        Assert.Null(source.Expression);
        Assert.True(source.IsBlockOutput);
    }

    [Fact]
    public void Resolves_block_input_from_an_in_out_variable()
    {
        var items = ((StructuredLogic)ParseFixture().Programs.Single().Logic!).Items;

        var in2 = items.OfType<BlockCall>().Single(c => c.TypeName == "OR")
            .Inputs.Single(a => a.FormalParameter == "IN2");

        var source = Assert.Single(in2.Sources);
        Assert.Equal("FEEDBACK", source.Expression);
        Assert.Null(source.RefLocalId);
        Assert.False(source.IsBlockOutput);
    }

    [Fact]
    public void Keeps_in_variables_as_signal_sources()
    {
        var items = ((StructuredLogic)ParseFixture().Programs.Single().Logic!).Items;

        var references = items.OfType<VariableReference>().ToList();

        // IN_01 and FEEDBACK arrive as inVariables; FEEDBACK again as an unconnected inOutVariable.
        Assert.Equal(3, references.Count);
        Assert.Contains(references, r => r.Expression == "IN_01");
        Assert.Equal(2, references.Count(r => r.Expression == "FEEDBACK"));
    }

    [Fact]
    public void An_unconnected_in_out_variable_is_a_read_and_a_connected_one_is_a_write()
    {
        var items = ((StructuredLogic)ParseFixture().Programs.Single().Logic!).Items;

        // localId 12 has a connectionPointIn without a connection: it only feeds OR.IN2, so it is a read.
        Assert.Equal("12", items.OfType<VariableReference>().Single(v => v.LocalId == "12").LocalId);
        Assert.DoesNotContain(items.OfType<VariableAssignment>(), v => v.LocalId == "12");

        // localId 13 is driven by OR, so it is a write.
        var write = items.OfType<VariableAssignment>().Single(v => v.LocalId == "13");
        Assert.Equal("OUT_01", write.Target);
        Assert.Equal("3", write.RefLocalId);
    }

    [Fact]
    public void Assignment_records_the_block_that_drives_it()
    {
        var items = ((StructuredLogic)ParseFixture().Programs.Single().Logic!).Items;

        var assignment = items.OfType<VariableAssignment>().Single(v => v.Target == "OUT_01");

        Assert.Equal("3", assignment.RefLocalId);
        Assert.Equal("OR", assignment.RefFormalParameter);
    }

    [Fact]
    public void Collects_called_instances_and_library_calls()
    {
        var project = ParseFixture();

        Assert.Contains("RT_01", project.Programs.Single().CalledInstances);
        Assert.Equal(new[] { "OR", "R_TRIG" }, project.LibraryCalls);
    }

    [Fact]
    public void Reads_data_type_members()
    {
        var dataType = ParseFixture().DataTypes.Single();

        Assert.Equal("ALARM_REC", dataType.Name);
        var members = Assert.IsType<StructType>(dataType).Members;
        Assert.Equal(2, members.Count);
        Assert.Equal("DINT", members.Single(m => m.Name == "Code").TypeName);
        Assert.Equal("BOOL", members.Single(m => m.Name == "Active").TypeName);
    }

    [Fact]
    public void Reads_task_interval()
    {
        Assert.Equal(100, ParseFixture().TaskIntervalMs);
    }

    [Fact]
    public void Reads_libraries_from_the_default_namespace()
    {
        Assert.Equal(new[] { "STDSHEET", "STDLIB", "TX1LIB", "TCXLIB" }, ParseFixture().Libraries);
    }

    [Fact]
    public void Reads_global_variables()
    {
        var globals = ParseFixture().GlobalVariables;

        Assert.Equal(3, globals.Count);
        var plain = globals.Single(g => g.Name == "TAG1");
        Assert.Equal("BOOL", plain.TypeName);
        Assert.False(plain.IsTag);
    }

    [Fact]
    public void Reads_tag_metadata_for_aliased_memory_points()
    {
        var tag = ParseFixture().GlobalVariables.Single(g => g.Name == "AIXXX");

        Assert.True(tag.IsTag);
        Assert.Equal("Safety", tag.Metadata!.Application);
        Assert.Equal("H2S_LEL2", tag.Metadata.Group1);
        Assert.Equal("MEMORY", tag.Metadata.PointType);
        Assert.Equal("READWRITE_ALIASED", tag.Metadata.AliasType);
        Assert.Equal("MIW.0009", tag.Metadata.PhysicalAddress);
        // The parser keeps the TriStation address untouched; the Siemens address is derived later
        // by TriconexAddressTranslator.
        Assert.Null(tag.LogicalAddress);
        Assert.Equal(40259, tag.Metadata.AliasNumber);
        Assert.Equal(415009, tag.Metadata.AliasNumberExt);
        Assert.Equal(11, tag.Metadata.BinNumber);
        Assert.Equal(8, tag.Metadata.BinOffset);
        Assert.Equal("ACTIVATED", tag.Metadata.TrueStateName);
        Assert.Equal(4.000086784362793d, tag.Metadata.MinEngValue!.Value, 6);
        Assert.Equal(20.000087738037109d, tag.Metadata.MaxEngValue!.Value, 6);
        Assert.Equal(21.6d, tag.Metadata.FieldFaultHighLimit!.Value, 4);
        Assert.Equal("0", tag.Metadata.SoeBlockAssignment);
    }

    [Fact]
    public void Reads_unaliased_memory_tag()
    {
        var tag = ParseFixture().GlobalVariables.Single(g => g.Name == "ALXXX");

        Assert.Equal("MBN.0242", tag.Metadata!.PhysicalAddress);
        Assert.Equal("UNALIASED", tag.Metadata.AliasType);
        Assert.Equal("Control", tag.Metadata.Application);
        Assert.Equal(13, tag.Metadata.BinNumber);
        Assert.Equal(241, tag.Metadata.BinOffset);
    }

    [Fact]
    public void Reads_controller_and_chassis_configuration()
    {
        var hardware = ParseFixture().Hardware;

        Assert.Equal("TRICON_EP", hardware.ControllerName);
        Assert.Equal(11, hardware.MajorVersion);
        Assert.Equal(2, hardware.MinorVersion);
        Assert.Equal(241, hardware.MainProcessorId);
        Assert.Equal("TRINODE008", hardware.NodeName);
        Assert.Equal(8, hardware.NodeNumber);
        Assert.Equal("173.10.2.8", hardware.IpAddress);
        Assert.Equal(1502, hardware.UdpPort);
        Assert.Equal("Network", hardware.ConnectionType);
    }

    [Fact]
    public void Reads_chassis_modules_with_slot_address()
    {
        var chassis = ParseFixture().Hardware.Chassis;

        Assert.Equal(2, chassis.Count);

        var main = chassis[0];
        Assert.True(main.IsMainChassis);
        Assert.Equal("EP_MAIN", main.Type);
        Assert.Equal(2, main.Modules.Count);
        Assert.Equal(62, main.Modules[0].Id);
        Assert.Equal(2, main.Modules[0].SlotAddress);
        Assert.Equal(32, main.Modules[0].InputDiscretes);
        Assert.Equal(32, main.Modules[0].OutputDiscretes);

        var expansion = chassis[1];
        Assert.False(expansion.IsMainChassis);
        Assert.Equal("LD_EXP", expansion.Type);
        Assert.Equal(2, expansion.ChassisNumber);
        Assert.Equal(17, Assert.Single(expansion.Modules).Id);
        Assert.Equal(4, expansion.Modules[0].SlotAddress);
    }

    [Fact]
    public void Rejects_a_document_that_is_not_plcopen()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => new PlcOpenXmlParser().Parse("<Project name=\"P\"><Mystery /></Project>"));

        Assert.Contains("PLCopen", exception.Message);
    }

    [Fact]
    public void Rejects_malformed_xml()
    {
        Assert.Throws<System.Xml.XmlException>(() => new PlcOpenXmlParser().Parse("<project>"));
    }

    [Fact]
    public void Captures_a_pou_without_a_name_instead_of_dropping_it()
    {
        const string xml = """
            <project xmlns="http://www.plcopen.org/xml/tc6.xsd">
              <fileHeader productName="TriStation 1131" />
              <types><pous><pou pouType="program"><body /></pou></pous></types>
            </project>
            """;

        var project = new PlcOpenXmlParser().Parse(xml);

        Assert.Empty(project.Blocks);
        var unrecognised = Assert.Single(project.UnrecognizedElements);
        Assert.Equal("pou", unrecognised.ElementName);
        Assert.Equal("project/types/pou", unrecognised.ParentPath);
    }

/// <summary>
    /// Full acceptance run against the 27 MB customer export when it is available locally.
    /// </summary>
    [Fact]
    public void Parses_the_real_triStation_export_when_present()
    {
        const string path = @"C:\Users\Moatasem\Downloads\CON8-38_FB.XML";
        if (!File.Exists(path)) return;

        var project = new PlcOpenXmlParser().ParseFile(path);

        Assert.Equal("MainConfiguration", project.Name);
        Assert.Equal(10, project.Programs.Count());
        Assert.Equal(16, project.FunctionBlocks.Count());
        Assert.Equal(26, project.Blocks.Count);
        Assert.Equal(2381, project.GlobalVariables.Count);
        Assert.All(project.GlobalVariables.Where(g => g.IsTag), tag => Assert.NotNull(tag.Metadata!.PhysicalAddress));

        Assert.Contains("R_TRIG", project.LibraryCalls);
        Assert.Contains("TX1LIB", project.Libraries);
        Assert.Equal(100, project.TaskIntervalMs);

        Assert.Equal("TRICON_EP", project.Hardware.ControllerName);
        Assert.Equal("TRINODE008", project.Hardware.NodeName);
        Assert.Equal("173.10.2.8", project.Hardware.IpAddress);
        Assert.Equal(4, project.Hardware.Chassis.Count);

        // Every FBD connection must resolve to a block output or a variable expression.
        var structured = project.Blocks.Where(b => b.Logic is StructuredLogic).ToList();
        Assert.NotEmpty(structured);

        var unresolved = structured
            .SelectMany(block => ((StructuredLogic)block.Logic!).Items)
            .OfType<BlockCall>()
            .SelectMany(call => call.Inputs.Concat(call.InOuts))
            .SelectMany(argument => argument.Sources)
            .Where(source => source.Expression is not null && source.Expression.StartsWith("/* unresolved"))
            .ToList();

        Assert.Empty(unresolved);

        // TriStation restarts localId numbering per sheet, so items must survive that grouping.
        Assert.All(structured, block =>
        {
            var logic = (StructuredLogic)block.Logic!;
            Assert.Equal(logic.Items.Count, logic.Items.Select(item => item.SheetKey).Distinct().Count());
        });

        // Most calls must have at least one wired input; a few legitimately have none (for example a timer
// whose PT/ET outputs are used but whose inputs come from the static instance).
var calls = structured
            .SelectMany(block => ((StructuredLogic)block.Logic!).Items)
            .OfType<BlockCall>()
            .Where(call => call.Inputs.Count > 0)
            .ToList();

        Assert.NotEmpty(calls);
        var wired = calls.Count(call => call.Inputs.Any(input => input.Sources.Count > 0));
        Assert.True(wired > calls.Count / 2, $"only {wired} of {calls.Count} calls have a wired input");
    }
}