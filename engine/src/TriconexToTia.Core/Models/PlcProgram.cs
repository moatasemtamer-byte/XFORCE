using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Models;

/// <summary>A Triconex PROGRAM becomes a Siemens organization block (OB).</summary>
public sealed class PlcProgram : PlcBlock
{
    public override BlockType BlockType => BlockType.OB;

    /// <summary>Suggested OB number, when the source naming implies one.</summary>
    public int? SuggestedObNumber { get; set; }
}
