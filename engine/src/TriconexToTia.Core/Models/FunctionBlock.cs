using TriconexToTia.Core.Enums;

namespace TriconexToTia.Core.Models;

public sealed class FunctionBlock : PlcBlock
{
    public override BlockType BlockType => BlockType.FB;
}
