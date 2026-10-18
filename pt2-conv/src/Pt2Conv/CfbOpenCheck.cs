using System.Runtime.InteropServices;

namespace Pt2Conv;

/// <summary>
/// Independent validation of a CFB container using Windows' own OLE32 implementation.
/// Returns 0 (S_OK) for a well-formed compound file, 0x800300FA (STG_E_DOCFILECORRUPT)
/// or similar on a malformed one. This catches structural errors (bad FAT special
/// values, red-black tree violations, broken chains) that our own reader tolerates.
/// </summary>
public static class CfbOpenCheck
{
    private const uint StgmShareExclusive = 0x10;

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int StgOpenStorage(string name, IntPtr prio, uint mode, IntPtr excl, uint res, out IntPtr outStg);

    public static int Check(string path)
    {
        IntPtr stg;
        int hr = StgOpenStorage(path, IntPtr.Zero, StgmShareExclusive, IntPtr.Zero, 0, out stg);
        if (stg != IntPtr.Zero) Marshal.Release(stg);
        return hr;
    }
}
