using TriconexToTia.Core.Results;

namespace TriconexToTia.Core.Interfaces;

/// <summary>
/// A generator turns part of the mapped model into one or more output files.
/// Generators are independent and run in a fixed order.
/// </summary>
public interface IGenerator
{
    string Id { get; }

    string Description { get; }

    IEnumerable<GeneratedFile> Generate(GenerationContext context);
}
