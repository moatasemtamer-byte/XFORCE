namespace TriconexToTia.Core.Models;

public enum IoDirection
{
    Input,
    Output,
    Memory,
}

public enum IoSignal
{
    Digital,
    Analog,
}

/// <summary>A physical I/O point from the Triconex I/O configuration.</summary>
public sealed class IoPoint
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string? MappedTypeName { get; set; }

    public string EffectiveTypeName => MappedTypeName ?? TypeName;

    public IoDirection Direction { get; set; } = IoDirection.Input;

    public IoSignal Signal { get; set; } = IoSignal.Digital;

    public int? Rack { get; set; }

    public int? Slot { get; set; }

    public int? Channel { get; set; }

    /// <summary>Resolved Siemens logical address, e.g. "%I0.0".</summary>
    public string? LogicalAddress { get; set; }

    public bool AddressResolved { get; set; }

    public string? Comment { get; set; }

    public int SourceLine { get; set; } = -1;
}
