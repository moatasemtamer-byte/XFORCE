using System.Text;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>Emits one SimaticML interface XML file per struct, as a UDT.</summary>
public sealed class UdtXmlGenerator : IGenerator
{
    private const string Namespace = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

    public string Id => "udt";

    public string Description => "User-defined data type XML files";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        foreach (var type in context.Project.DataTypes)
        {
            var builder = new StringBuilder();
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.Append("<!-- Converted from Triconex ")
                .Append(type.SourceElementName.Length > 0 ? type.SourceElementName : "STRUCT")
                .Append(" \"").Append(type.Name).AppendLine("\" -->");
            builder.Append("<Sections xmlns=\"").Append(Namespace).AppendLine("\">");
            builder.AppendLine("  <Section Name=\"None\">");

            foreach (var member in type.Members)
            {
                builder.Append("    <Member Name=\"").Append(GenerationHelpers.EscapeXml(member.Name))
                    .Append("\" Datatype=\"").Append(GenerationHelpers.EscapeXml(member.EffectiveTypeName))
                    .Append('"');

                if (!string.IsNullOrWhiteSpace(member.Comment))
                {
                    builder.Append(" Comment=\"").Append(GenerationHelpers.EscapeXml(member.Comment)).Append('"');
                }

                builder.AppendLine(" />");
            }

            builder.AppendLine("  </Section>");
            builder.AppendLine("</Sections>");

            yield return new GeneratedFile
            {
                RelativePath = $"UDTs/{GenerationHelpers.SanitizeFileName(type.Name)}.xml",
                Content = builder.ToString(),
                Kind = ArtifactKind.Udt,
                EmitBom = true,
            };
        }
    }

    internal static string UdtDatatypeName(string typeName) => $"\"{typeName}\"";
}
