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

    public bool AddressResolved { get; set; }

    public int SourceLine { get; set; } = -1;

    public string EffectiveTypeName => MappedTypeName ?? TypeName;
}
