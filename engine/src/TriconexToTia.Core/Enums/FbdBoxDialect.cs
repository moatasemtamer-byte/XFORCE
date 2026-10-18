namespace TriconexToTia.Core.Enums;

/// <summary>
/// Which TIA Portal V16 FBD box-template dialect the generator targets. The FlgNet document spine is
/// identical for both; only a handful of box spellings differ between the V16.0 GA Openness build and
/// the V16 Update 3 build that produced the genuine exports.
/// <list type="bullet">
/// <item><c>V16Update3</c> matches the genuine V16 Update 3 exports: <c>Not</c> is a Card +
/// SrcType(Bool) box and <c>Move</c> is a Card + AutomaticTyped box.</item>
/// <item><c>V16Ga</c> matches the V16.0 GA build, which rejects those two spellings: <c>Not</c> is
/// emitted as a negated single-input <c>A</c> gate and <c>Move</c> as a Card + <c>en</c> box whose
/// Card is its output count.</item>
/// </list>
/// </summary>
public enum FbdBoxDialect
{
    /// <summary>TIA Portal V16 Update 3 (the dialect of the genuine CON8_38 exports).</summary>
    V16Update3 = 0,

    /// <summary>TIA Portal V16.0 GA (the base V16 release).</summary>
    V16Ga = 1,
}
