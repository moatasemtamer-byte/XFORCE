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
}
