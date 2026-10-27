using System.IO.Compression;
using System.Reflection;

namespace Pt2Conv;

public static class EmbeddedBase
{
    private const string ResourceName = "Pt2Conv.Resources.base.pt2x.gz";
    private const string MarkerName = "base.pt2x";

    public static byte[] LoadPt2xBytes()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName);
        if (stream == null)
            throw new InvalidOperationException("Embedded base resource not found: " + ResourceName);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var mem = new MemoryStream();
        gzip.CopyTo(mem);
        return mem.ToArray();
    }

    public static string ExtractTo(string outDir)
    {
        var bytes = LoadPt2xBytes();
        var cf = new Cfb.CompoundFile(bytes);
        Directory.CreateDirectory(outDir);
        // Remove marker if present
        foreach (var s in cf.ListStreams())
        {
            string full = Path.Combine(outDir, s.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, s.Data);
        }
        return outDir;
    }

    public static string EnsureExtracted()
    {
        // The extracted base is cached under a content hash: replacing the embedded
        // resource (a new library/hardware scaffold) must not keep reusing the old tree.
        byte[] packed = LoadPackedBytes();
        using var sha = System.Security.Cryptography.SHA256.Create();
        string tag = Convert.ToHexString(sha.ComputeHash(packed))[..12].ToLowerInvariant();
        string dir = Path.Combine(Path.GetTempPath(), "pt2conv_base_" + tag);
        if (Directory.Exists(dir) && Directory.Exists(Path.Combine(dir, "Current")))
            return dir;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        return ExtractTo(dir);
    }

    private static byte[] LoadPackedBytes()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded base resource not found: " + ResourceName);
        using var mem = new MemoryStream();
        stream.CopyTo(mem);
        return mem.ToArray();
    }
}
