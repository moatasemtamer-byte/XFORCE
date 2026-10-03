using System.Text;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>Emits SimaticML tag-table XML for I/O points and global variables.</summary>
public sealed class TagTableXmlGenerator : IGenerator
{
    public string Id => "tags";

    public string Description => "Tag table XML files";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        var project = context.Project;

        if (project.IoPoints.Count > 0)
        {
            var builder = new StringBuilder();
            WriteHeader(builder, "Converted from Triconex I/O Configuration");
            builder.AppendLine("  <Tagtable Name=\"IOTags\">");
            foreach (var point in project.IoPoints)
            {
                WriteTag(builder, point.Name, point.EffectiveTypeName, point.LogicalAddress, point.Comment);
            }
            builder.AppendLine("  </Tagtable>");

            yield return new GeneratedFile
            {
                RelativePath = "Tags/IOTags.xml",
                Content = builder.ToString(),
                Kind = ArtifactKind.Tag,
                EmitBom = true,
            };
        }

        if (project.GlobalVariables.Count > 0)
        {
            var builder = new StringBuilder();
            WriteHeader(builder, "Converted from Triconex Global Variables");
            builder.AppendLine("  <Tagtable Name=\"GlobalTags\">");
            foreach (var variable in project.GlobalVariables)
            {
                WriteTag(builder, variable.Name, variable.EffectiveTypeName, variable.LogicalAddress, variable.Comment);
            }
            builder.AppendLine("  </Tagtable>");

            yield return new GeneratedFile
            {
                RelativePath = "Tags/GlobalTags.xml",
                Content = builder.ToString(),
                Kind = ArtifactKind.Tag,
                EmitBom = true,
            };
        }
    }

    private static void WriteHeader(StringBuilder builder, string comment)
    {
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.Append("<!-- ").Append(comment).AppendLine(" -->");
    }

    private static void WriteTag(StringBuilder builder, string name, string dataType, string? address, string? comment)
    {
        builder.Append("    <Tag Name=\"").Append(GenerationHelpers.EscapeXml(name))
            .Append("\" DataTypeName=\"").Append(GenerationHelpers.EscapeXml(dataType)).Append('"');

        if (!string.IsNullOrWhiteSpace(address))
        {
            builder.Append(" LogicalAddress=\"").Append(GenerationHelpers.EscapeXml(address)).Append('"');
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            builder.Append(" Comment=\"").Append(GenerationHelpers.EscapeXml(comment)).Append('"');
        }

        builder.AppendLine(" />");
    }
}
