using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Models;

/// <summary>Logic of a block: either verbatim text (ST/LD/SFC) or a resolved FBD graph.</summary>
public abstract class LogicModel
{
    public abstract PlcLanguage Language { get; }
}

/// <summary>Structured Text / ladder / sequential bodies carried through verbatim.</summary>
public sealed class TextualLogic : LogicModel
{
    public TextualLogic(PlcLanguage language, string code)
    {
        Language = language;
        Code = code;
    }

    public override PlcLanguage Language { get; }

    public string Code { get; }
}

/// <summary>
/// An FBD/LD body flattened into items plus fully resolved connections. The parser
/// resolves every <c>connection refLocalId</c> to the item that drives it, so the
/// generator never has to reason about geometry.
/// </summary>
public sealed class StructuredLogic : LogicModel
{
    public StructuredLogic(PlcLanguage language)
    {
        Language = language;
    }

    public override PlcLanguage Language { get; }

    public List<LogicItem> Items { get; } = new();

    /// <summary>Sheet title / macro values carried in <c>ts:Macros</c>, when present.</summary>
    public List<string> SheetTitles { get; } = new();

    /// <summary>
    /// Connections whose <c>refLocalId</c> is not present on the same sheet. TriStation reuses a
    /// localId across the sheets of one block, so these were resolved by localId alone and are
    /// reported because the export cannot confirm the match.
    /// </summary>
    public List<string> CrossSheetReferences { get; } = new();
}