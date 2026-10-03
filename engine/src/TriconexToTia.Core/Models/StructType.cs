namespace TriconexToTia.Core.Models;

/// <summary>A STRUCT declared in the source, emitted as a Siemens UDT.</summary>
public sealed class StructType : DataType
{
    public List<Variable> Members { get; } = new();
}
