using TriconexToTia.Core.Enums;
using System.Text;

namespace TriconexToTia.Core.Results;

/// <summary>A single file produced by a generator, held in memory until written.</summary>
public sealed class GeneratedFile
{
    public string RelativePath { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public ArtifactKind Kind { get; init; } = ArtifactKind.Block;

    /// <summary>
    /// Whether to emit a UTF-8 byte order mark. Siemens requires a BOM on the
    /// XML and SCL files it imports.
    /// </summary>
    public bool EmitBom { get; init; } = true;

    public byte[] ToBytes()
    {
        var body = Encoding.UTF8.GetBytes(Content);
        if (!EmitBom)
        {
            return body;
        }

        // Encoding.GetBytes never emits the preamble, so prepend it explicitly.
        var preamble = Encoding.UTF8.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }
}
