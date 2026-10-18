namespace TriconexToTia.Status;

/// <summary>
/// The four evidence levels (plus failure) defined by the GUI evidence model.
/// The GUI must use exactly these names; they are computed from evidence files
/// on disk and are never hard-coded.
/// </summary>
public enum EvidenceStatus
{
    /// <summary>Imported AND compiled in TIA with a recorded result (green).</summary>
    VerifiedInTia,

    /// <summary>Passes our tests, never seen by TIA (amber).</summary>
    UnitTestOnly,

    /// <summary>Output exists, semantics or format not confirmed (orange).</summary>
    Unverified,

    /// <summary>No evidence exists (grey).</summary>
    NotStarted,

    /// <summary>Evidence exists and shows a failure (red).</summary>
    Failed
}