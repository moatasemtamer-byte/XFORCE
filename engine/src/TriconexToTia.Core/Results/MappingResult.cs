using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Results;

public sealed class MappingResult
{
    public List<MappingRecord> Records { get; } = new();

    /// <summary>Library functions with no automatic equivalent, by block name.</summary>
    public List<ManualAction> ManualActions { get; } = new();

    public void Record(MappingRecord record) => Records.Add(record);

    public void AddManualAction(ManualAction action) => ManualActions.Add(action);

    public int Count(ConversionStatus status) => Records.Count(r => r.Status == status);
}

public sealed class ManualAction
{
    public string Title { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public int SourceLine { get; init; } = -1;
}
