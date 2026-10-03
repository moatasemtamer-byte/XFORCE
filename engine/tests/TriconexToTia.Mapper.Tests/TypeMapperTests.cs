using TriconexToTia.Core.Enums;
using Xunit;

namespace TriconexToTia.Mapper.Tests;

public class TypeMapperTests
{
    private static readonly Mappings Mappings = MappingLoader.Load(null);
    private static readonly string[] NoUserTypes = Array.Empty<string>();

    [Theory]
    [InlineData("BOOL", "Bool")]
    [InlineData("bool", "Bool")]
    [InlineData("INT", "Int")]
    [InlineData("DINT", "DInt")]
    [InlineData("REAL", "Real")]
    [InlineData("LREAL", "LReal")]
    [InlineData("WORD", "Word")]
    [InlineData("TON", "TON")]
    [InlineData("TIME", "Time")]
    public void Maps_primitive_types_exactly(string source, string expected)
    {
        var result = new TypeMapper(Mappings).Map(source, NoUserTypes);

        Assert.Equal(expected, result.Mapped);
        Assert.Equal(ConversionStatus.Converted, result.Status);
    }

    [Fact]
    public void Marks_approximated_types()
    {
        var result = new TypeMapper(Mappings).Map("TIME_OF_DAY", NoUserTypes);

        Assert.Equal("TOD", result.Mapped);
        Assert.Equal(ConversionStatus.Approximated, result.Status);
    }

    [Fact]
    public void Maps_array_element_type()
    {
        var result = new TypeMapper(Mappings).Map("ARRAY[1..8] OF BOOL", NoUserTypes);

        Assert.Equal("Array[1..8] of Bool", result.Mapped);
    }

    [Fact]
    public void Preserves_string_length_and_defaults_bare_string()
    {
        var mapper = new TypeMapper(Mappings);

        Assert.Equal("String[16]", mapper.Map("STRING[16]", NoUserTypes).Mapped);
        Assert.Equal("String[254]", mapper.Map("STRING", NoUserTypes).Mapped);
    }

    [Fact]
    public void Quotes_user_defined_types()
    {
        var result = new TypeMapper(Mappings).Map("PumpData", new[] { "PumpData" });

        Assert.Equal("\"PumpData\"", result.Mapped);
        Assert.True(result.IsUserDefined);
    }

    [Fact]
    public void Unknown_types_are_manual()
    {
        var result = new TypeMapper(Mappings).Map("MYSTERY_TYPE", NoUserTypes);

        Assert.Equal(ConversionStatus.Manual, result.Status);
    }

    [Fact]
    public void Block_type_mapper_maps_kinds()
    {
        var mapper = new BlockTypeMapper(Mappings);

        Assert.Equal(BlockType.FC, mapper.Map("FUNCTION"));
        Assert.Equal(BlockType.FB, mapper.Map("FUNCTION_BLOCK"));
        Assert.Equal(BlockType.OB, mapper.Map("PROGRAM"));
    }

    [Fact]
    public void Library_scanner_flags_manual_tr_pid()
    {
        var result = new Core.Results.MappingResult();

        new LibraryFunctionMapper(Mappings).Scan("x := TR_PID(a, b);", "Blk", 1, result);

        Assert.Single(result.ManualActions);
        Assert.Equal(ConversionStatus.Manual, result.Records.Single(r => r.Source == "TR_PID").Status);
    }

    [Fact]
    public void Library_scanner_flags_unknown_tr_prefix()
    {
        var result = new Core.Results.MappingResult();

        new LibraryFunctionMapper(Mappings).Scan("x := TR_ZZZ(a);", "Blk", 1, result);

        Assert.Single(result.ManualActions);
    }

    [Fact]
    public void Library_scanner_accepts_exact_functions()
    {
        var result = new Core.Results.MappingResult();

        new LibraryFunctionMapper(Mappings).Scan("x := ABS(a);", "Blk", 1, result);

        Assert.Empty(result.ManualActions);
        Assert.Equal(ConversionStatus.Converted, result.Records.Single(r => r.Source == "ABS").Status);
    }
}
