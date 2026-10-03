using TriconexToTia.Core.Enums;

namespace TriconexToTia.Mapper;

/// <summary>Maps Triconex block kinds onto Siemens block kinds.</summary>
public sealed class BlockTypeMapper
{
    private readonly Mappings _mappings;

    public BlockTypeMapper(Mappings mappings) => _mappings = mappings;

    public BlockType Map(string sourceElementName)
    {
        if (_mappings.BlockTypes.TryGetValue(sourceElementName, out var value))
        {
            return value.ToUpperInvariant() switch
            {
                "FB" => BlockType.FB,
                "FC" => BlockType.FC,
                _ => BlockType.OB,
            };
        }

        return BlockType.FB;
    }

    public string SiemensName(BlockType blockType) => blockType switch
    {
        BlockType.OB => "OB",
        BlockType.FB => "FB",
        BlockType.FC => "FC",
        _ => "FB",
    };
}
