using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Models;

public sealed class Function : PlcBlock
{
    public override BlockType BlockType => BlockType.FC;
}
