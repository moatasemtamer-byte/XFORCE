using System.Text;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>Emits one .scl file per block (FB, FC and OB).</summary>
public sealed class SclGenerator : IGenerator
{
    public string Id => "scl";

    public string Description => "Structured Control Language block files";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        foreach (var block in context.Project.Blocks)
        {
            yield return GenerateBlock(block);
        }
    }

    private static GeneratedFile GenerateBlock(PlcBlock block)
    {
        var (openKeyword, closeKeyword) = block.BlockType switch
        {
            BlockType.FB => ("FUNCTION_BLOCK", "END_FUNCTION_BLOCK"),
            BlockType.FC => ("FUNCTION", "END_FUNCTION"),
            _ => ("ORGANIZATION_BLOCK", "END_ORGANIZATION_BLOCK"),
        };

        var builder = new StringBuilder();
        builder.Append(openKeyword).Append(" \"").Append(block.Name).Append('"');

        if (block is Function)
        {
            builder.Append(" : ").Append(block.MappedReturnType ?? "Void");
        }

        builder.AppendLine();
        builder.AppendLine("{ S7_Optimized_Access := 'TRUE' }");
        builder.AppendLine("VERSION : 0.1");
        builder.AppendLine();

        AppendSection(builder, "VAR_EXTERN", block, VariableDirection.External);
        AppendSection(builder, "VAR_INPUT", block, VariableDirection.Input);
        AppendSection(builder, "VAR_OUTPUT", block, VariableDirection.Output);
        AppendSection(builder, "VAR_IN_OUT", block, VariableDirection.InOut);

        var localKeyword = block.BlockType == BlockType.FB ? "VAR" : "VAR_TEMP";
        AppendSection(builder, localKeyword, block, VariableDirection.Local);

        builder.AppendLine("BEGIN");
        builder.Append("    // Converted from Triconex ").Append(block.SourceElementName)
            .Append(" \"").Append(block.Name).AppendLine("\"");
        builder.Append("    // Original language: ").AppendLine(block.Language.ToString());
        builder.AppendLine();

        var localNames = block.Variables
            .Where(v => v.Direction is VariableDirection.Input or VariableDirection.Output
                or VariableDirection.InOut or VariableDirection.Local)
            .Select(v => v.Name);

        if (string.IsNullOrWhiteSpace(block.Code))
        {
            builder.AppendLine("    // The source block had no code.");
        }
        else
        {
            var transformed = SclCodeTransformer.PrefixLocalVariables(block.Code.Trim(), localNames);
            builder.AppendLine(GenerationHelpers.Indent(transformed, 4));
        }

        builder.AppendLine();
        builder.AppendLine(closeKeyword);

        var path = $"Blocks/{GenerationHelpers.SanitizeFileName(block.Name)}.scl";
        return new GeneratedFile
        {
            RelativePath = path,
            Content = builder.ToString(),
            Kind = ArtifactKind.Block,
            EmitBom = true,
        };
    }

    private static void AppendSection(StringBuilder builder, string keyword, PlcBlock block, VariableDirection direction)
    {
        var variables = block.Variables.Where(v => v.Direction == direction).ToList();
        if (variables.Count == 0) return;

        builder.AppendLine(keyword);
        foreach (var variable in variables)
        {
            builder.Append("    ").Append(variable.Name).Append(" : ").Append(variable.EffectiveTypeName).Append(';');
            if (!string.IsNullOrWhiteSpace(variable.Comment))
            {
                builder.Append(" // ").Append(variable.Comment);
            }
            builder.AppendLine();
        }
        builder.AppendLine("END_VAR");
        builder.AppendLine();
    }
}
