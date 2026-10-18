namespace TriconexToTia.Core.Enums;

/// <summary>
/// How FBD-language blocks are emitted.
/// <list type="bullet">
/// <item><c>Scl</c> keeps today's behaviour: every block, including FBD ones, becomes SCL.</item>
/// <item><c>Fbd</c> renders each FBD network as SimaticML XML (<c>SW.Blocks.*</c> with an FlgNet
/// body) and falls back to SCL per block when a network cannot be expressed faithfully.</item>
/// <item><c>Auto</c> behaves like <c>Fbd</c>, but a block whose FBD rendering needs an approximated
/// construct (an inserted conversion or a helper block) stays in SCL instead.</item>
/// </list>
/// </summary>
public enum FbdOutputMode
{
    Scl = 0,
    Fbd = 1,
    Auto = 2,
}