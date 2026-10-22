namespace Pt2Conv.Cfb;

public static class CfbConstants
{
    public const int HeaderSize = 512;
    public const int SectorShift = 9;
    public const int SectorSize = 1 << SectorShift;          // 512
    public const int V4SectorShift = 12;
    public const int V4SectorSize = 1 << V4SectorShift;      // 4096
    public const int MiniSectorShift = 6;
    public const int MiniSectorSize = 1 << MiniSectorShift;  // 64
    public const int MiniStreamCutoff = 4096;

    public const uint FreeSect = 0xFFFFFFFF;  // [MS-CFB] FREESECT
    public const uint EndOfChain = 0xFFFFFFFE;// MS-CFB ENDOFCHAIN
    public const uint Fsect = 0xFFFFFFFD;     // MS-CFB FATSECT
    public const uint Difsect = 0xFFFFFFFC;   // MS-CFB DIFSECT

    public const uint NoStream = 0xFFFFFFFF; // sibling/child "none"

    public const int MaxNameLenBytes = 64;   // 32 UTF-16 units + terminator

    public static readonly byte[] Signature =
    {
        0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1
    };
}