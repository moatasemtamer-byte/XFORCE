using System.Text;
using System.Xml;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>
/// Emits SimaticML PLC tag-table XML (<c>SW.Tags.PlcTagTable</c>) for I/O points and addressed
/// global variables.
/// </summary>
/// <remarks>
/// <para>
/// Element names, <c>AttributeList</c> child order and the comment representation were taken from
/// six genuine TIA V16 exports (CON100 <c>Default tag table</c>, CON100 <c>PAL_SystemTags</c>, and
/// the four extracted lab projects), not from documentation or guesswork.
/// </para>
/// <para>
/// Three details matter and were wrong before:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The tag <c>AttributeList</c> children are emitted in the order
/// <c>DataTypeName, ExternalAccessible, ExternalVisible, ExternalWritable, LogicalAddress, Name</c>.
/// The previous code wrote <c>Name</c> first and omitted the two visibility booleans.
/// </description></item>
/// <item><description>
/// A tag comment must be an <c>ObjectList</c> &gt; <c>MultilingualText</c> item. All 83 real tags
/// carry one. The previous code emitted an XML comment, which TIA discards on import.
/// </description></item>
/// <item><description>
/// <c>DocumentInfo</c> is required. The previous code wrote a bare document with an XML comment in
/// place of provenance.
/// </description></item>
/// </list>
/// <para>
/// <b>Not covered by the supplied evidence:</b> a tag with no <c>LogicalAddress</c>. All 83 real
/// tags are addressed, so the address-less branch below is unverified.
/// </para>
/// </remarks>
public sealed class TagTableXmlGenerator : IGenerator
{
    private const string PlcTagTableElement = "SW.Tags.PlcTagTable";
    private const string PlcTagElement = "SW.Tags.PlcTag";

    public string Id => "tags";

    public string Description => "Tag table XML files";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        var project = context.Project;
        var engineeringVersion = $"V{context.Options.ClampTiaVersion()}";
        var created = context.Options.Timestamp ?? DateTimeOffset.UtcNow;

        if (project.IoPoints.Count > 0)
        {
            yield return new GeneratedFile
            {
                RelativePath = "Tags/IOTags.xml",
                Content = BuildDocument(
                    engineeringVersion,
                    created,
                    "IOTags",
                    project.IoPoints.Select(point => new TagEntry(
                        point.Name,
                        point.EffectiveTypeName,
                        point.LogicalAddress,
                        point.Comment))),
                Kind = ArtifactKind.Tag,
                EmitBom = true,
            };
        }

        // Only globals that carry a logical address can be represented in a tag table. An SCL data
        // block declaration cannot hold one, so address-less globals go to GlobalDbGenerator instead.
        // Emitting them in both places would declare the same name twice and fail to compile.
        var addressedGlobals = project.GlobalVariables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.LogicalAddress))
            .ToList();

        if (addressedGlobals.Count > 0)
        {
            yield return new GeneratedFile
            {
                RelativePath = "Tags/GlobalTags.xml",
                Content = BuildDocument(
                    engineeringVersion,
                    created,
                    "GlobalTags",
                    addressedGlobals.Select(variable => new TagEntry(
                        variable.Name,
                        variable.EffectiveTypeName,
                        variable.LogicalAddress,
                        variable.Comment))),
                Kind = ArtifactKind.Tag,
                EmitBom = true,
            };
        }
    }

    private static string BuildDocument(
        string engineeringVersion,
        DateTimeOffset created,
        string tableName,
        IEnumerable<TagEntry> tags)
    {
        using var buffer = new TiaXmlDocument.Utf8StringWriter();

        using (var writer = XmlWriter.Create(buffer, TiaXmlDocument.CreateSettings()))
        {
            TiaXmlDocument.WritePrologue(writer, engineeringVersion, created);

            writer.WriteStartElement(PlcTagTableElement);
            writer.WriteAttributeString("ID", "0");

            writer.WriteStartElement("AttributeList");
            writer.WriteElementString("Name", tableName);
            writer.WriteEndElement();

            writer.WriteStartElement("ObjectList");

            var ids = new TiaXmlDocument.IdAllocator();
            foreach (var tag in tags)
            {
                writer.WriteStartElement(PlcTagElement);
                writer.WriteAttributeString("ID", ids.Next());
                writer.WriteAttributeString("CompositionName", "Tags");

                writer.WriteStartElement("AttributeList");
                writer.WriteElementString("DataTypeName", tag.DataTypeName);

                // WithDefaults exports spell these three out; the lab projects omit them. We emit
                // them explicitly because the explicit form round-trips without relying on defaults.
                writer.WriteElementString("ExternalAccessible", "true");
                writer.WriteElementString("ExternalVisible", "true");
                writer.WriteElementString("ExternalWritable", "false");

                if (!string.IsNullOrWhiteSpace(tag.LogicalAddress))
                {
                    writer.WriteElementString("LogicalAddress", tag.LogicalAddress);
                }

                writer.WriteElementString("Name", tag.Name);
                writer.WriteEndElement();

                if (!string.IsNullOrWhiteSpace(tag.Comment))
                {
                    writer.WriteStartElement("ObjectList");
                    TiaXmlDocument.WriteObjectText(writer, ids, "Comment", tag.Comment);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();

            TiaXmlDocument.CloseDocument(writer);
        }

        return buffer.ToString();
    }

    private readonly record struct TagEntry(
        string Name,
        string DataTypeName,
        string? LogicalAddress,
        string? Comment);
}