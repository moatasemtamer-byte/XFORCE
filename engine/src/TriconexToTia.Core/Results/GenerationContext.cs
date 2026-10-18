using TriconexToTia.Core.Models;

namespace TriconexToTia.Core.Results;

/// <summary>Everything a generator needs, plus the running output list.</summary>
public sealed class GenerationContext
{
    public PlcProject Project { get; init; } = new();

    public ValidationResult Validation { get; init; } = new();

    public MappingResult Mapping { get; init; } = new();

    public ConverterOptions Options { get; init; } = new();

    public List<GeneratedFile> Generated { get; } = new();

    /// <summary>
    /// Blocks the FBD generator kept as SCL. <see cref="TriconexToTia.Generators.SclGenerator"/>
    /// still emits those; a block on this list must not be skipped twice.
    /// </summary>
    public HashSet<string> FbdFallbackBlocks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Emission outcome of every FBD-language block, for the report.</summary>
    public List<FbdBlockOutcome> FbdOutcomes { get; } = new();

    private readonly List<ManualAction> _manualActions = new();

    /// <summary>
    /// Manual actions raised by generators (for example an FBD call that has no Siemens
    /// equivalent). They are merged into the final report.
    /// </summary>
    public IReadOnlyList<ManualAction> ManualActions => _manualActions;

    public void AddManualAction(string title, string reason = "", string action = "", int sourceLine = 0)
    {
        _manualActions.Add(new ManualAction
        {
            Title = title,
            Reason = reason,
            Action = action,
            SourceLine = sourceLine,
        });
    }
}
