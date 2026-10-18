namespace TriconexToTia.Core.Models;

/// <summary>A project-scope variable declared outside any block.</summary>
public sealed class GlobalVariable
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string? MappedTypeName { get; set; }

    public string? InitialValue { get; set; }

    public string? Comment { get; set; }

    /// <summary>Explicit Siemens address if the source supplied one.</summary>
    public string? LogicalAddress { get; set; }

    /// <summary>Raw <c>address</c> attribute as written in the source, kept for reporting.</summary>
    public string? Address { get; set; }

    public bool AddressResolved { get; set; }

    public int SourceLine { get; set; } = -1;

    public string EffectiveTypeName => MappedTypeName ?? TypeName;

    /// <summary>ts:TAGNAME properties (safety attributes, scaling, address details).</summary>
    public TagMetadata? Metadata { get; set; }

    /// <summary>True when the variable carries a ts:TAGNAME block, i.e. is a real tag.</summary>
    public bool IsTag => Metadata is not null;
}
