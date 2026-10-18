namespace TriconexToTia.Core.Models;

/// <summary>
/// TriStation tag properties carried in the <c>ts:TAGNAME</c> extension. These are the
/// safety-instrumentation attributes that have no equivalent in a Siemens tag table, so
/// they are preserved as tag comments rather than silently dropped.
/// </summary>
public sealed class TagMetadata
{
    /// <summary>ts:TAG_DECLARATION/@Application — "Safety" or "Control".</summary>
    public string? Application { get; set; }

    public string? Group1 { get; set; }

    public string? Group2 { get; set; }

    public bool SharedForRead { get; set; }

    public bool EnableMultipleWrite { get; set; }

    /// <summary>ts:POINT_ASSIGNMENT/@PointType — e.g. MEMORY.</summary>
    public string? PointType { get; set; }

    /// <summary>ts:POINT_ASSIGNMENT/@AliasType — UNALIASED / READ_ALIASED / READWRITE_ALIASED.</summary>
    public string? AliasType { get; set; }

    public string? TagClass { get; set; }

    /// <summary>Triconex physical address exactly as exported, e.g. "MBR.0053".</summary>
    public string? PhysicalAddress { get; set; }

    public string? AliasCategory { get; set; }

    public int? AliasNumber { get; set; }

    public int? AliasNumberExt { get; set; }

    public int? BinNumber { get; set; }

    public int? BinOffset { get; set; }

    // ---- scaling / display ----
    public double? MinSpan { get; set; }

    public double? MaxSpan { get; set; }

    public int? DisplayPrecision { get; set; }

    public bool DisableScaling { get; set; }

    public string? TrueStateName { get; set; }

    public string? FalseStateName { get; set; }

    public string? TrueStateColor { get; set; }

    public string? FalseStateColor { get; set; }

    // ---- location ----
    public string? AreaName { get; set; }

    public string? Equipment { get; set; }

    public string? SifName { get; set; }

    // ---- I/O module parameters ----
    public string? IoModuleType { get; set; }

    public string? PointOption { get; set; }

    public double? MinRawValue { get; set; }

    public double? MaxRawValue { get; set; }

    public double? MinEngValue { get; set; }

    public double? MaxEngValue { get; set; }

    public double? FieldFaultLowLimit { get; set; }

    public double? FieldFaultHighLimit { get; set; }

    public string? FilterType { get; set; }

    public string? TriggerState { get; set; }

    public int? MinimumTriggerTime { get; set; }

    /// <summary>ts:SOE_BLOCKS_ASSIGNMENT — comma separated sequence-of-events block ids.</summary>
    public string? SoeBlockAssignment { get; set; }

    public bool HasAny =>
        Application is not null || PhysicalAddress is not null || MinRawValue is not null ||
        TrueStateName is not null || AreaName is not null || Equipment is not null ||
        SifName is not null || SoeBlockAssignment is not null || Group1 is not null;
}

/// <summary>Controller identity and hardware allocation from the ts: extensions.</summary>
public sealed class HardwareInfo
{
    /// <summary>ts:PPD_CONFIGURATION/@Name, e.g. "TRICON_EP".</summary>
    public string? ControllerName { get; set; }

    public int? MajorVersion { get; set; }

    public int? MinorVersion { get; set; }

    public int? MainProcessorId { get; set; }

    public string? NodeName { get; set; }

    public int? NodeNumber { get; set; }

    public string? IpAddress { get; set; }

    public int? UdpPort { get; set; }

    public string? ConnectionType { get; set; }

    public List<ChassisInfo> Chassis { get; } = new();

    public bool HasAny => ControllerName is not null || Chassis.Count > 0 || NodeName is not null;
}

/// <summary>One chassis and its modules.</summary>
public sealed class ChassisInfo
{
    /// <summary>True for ts:MAIN_CHASSIS, false for ts:EXP_CHASSIS.</summary>
    public bool IsMainChassis { get; set; }

    public string? Type { get; set; }

    public string? Description { get; set; }

    public int? ChassisNumber { get; set; }

    public List<ModuleInfo> Modules { get; } = new();
}

/// <summary>One module in a slot.</summary>
public sealed class ModuleInfo
{
    /// <summary>
    /// Numeric TriStation catalog id. The export does not contain the Siemens article number,
    /// so this id must be resolved against the TriStation catalog before import.
    /// </summary>
    public int? Id { get; set; }

    /// <summary>ts:SLOT_ADDRESS — the chassis slot the module occupies.</summary>
    public int? SlotAddress { get; set; }

    /// <summary>ts:HardwareModuleAttribute values, including the point counts.</summary>
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int? InputDiscretes => Count("InputDiscretes");

    public int? OutputDiscretes => Count("OutputDiscretes");

    public int? InputIntegers => Count("InputIntegers");

    public int? OutputIntegers => Count("OutputIntegers");

    private int? Count(string name) =>
        Attributes.TryGetValue(name, out var value) && int.TryParse(value, out var result) ? result : null;
}