using System.Text;
using System.Xml;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>
/// Emits one <c>SW.Types.PlcStruct</c> XML file per declared struct.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the earlier <c>.udt</c> SCL source path. That path was guessed: no <c>.udt</c> file
/// exists anywhere in the supplied datasets, so it had nothing to validate against. The XML form
/// does — 19 genuine TIA V16 <c>SW.Types.PlcStruct</c> exports from the CON100 workspace export.
/// </para>
/// <para>
/// Grammar taken from those 19 files:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>&lt;Document&gt;</c> &gt; <c>&lt;Engineering version="V16" /&gt;</c> &gt; <c>&lt;DocumentInfo&gt;</c>
/// &gt; <c>&lt;SW.Types.PlcStruct ID="0"&gt;</c>.
/// </description></item>
/// <item><description>
/// <c>AttributeList</c> child order is <c>Interface</c>, <c>IsFailsafeCompliant</c>, <c>Name</c>.
/// The <c>Interface</c> element wraps a <c>Sections</c> element that carries the
/// <c>.../SW/Interface/v4</c> namespace; <c>Section/@Name</c> is always <c>None</c>.
/// </description></item>
/// <item><description>
/// Member child order is <c>AttributeList</c>, <c>Comment</c>, <c>StartValue</c>, <c>Sections</c>.
/// This is the order observed in all 622 members across the 19 files.
/// </description></item>
/// <item><description>
/// A member referencing another UDT is written with the type name <em>quoted</em>
/// (<c>Datatype="&amp;quot;UT01000_St_SysRevision&amp;quot;"</c>) and the referenced struct is
/// inlined as a nested <c>Sections</c> block. Built-in types are never quoted.
/// </description></item>
/// <item><description>
/// A member comment is the compact form
/// <c>&lt;Comment&gt;&lt;MultiLanguageText Lang="en-US"&gt;</c>, which is <em>different</em> from the
/// object-level <c>MultilingualText</c>/<c>MultilingualTextItem</c> form used for titles and comments
/// on the type itself.
/// </description></item>
/// </list>
/// <para>
/// <b>Not covered by the supplied evidence:</b> array members. None of the 19 real UDTs contains a
/// single <c>Array[...]</c> datatype — the only parameterised types present are bounded strings such
/// as <c>String[7]</c>. Array members are still emitted below using TIA's documented
/// <c>Array[lo..hi] of X</c> spelling, but that spelling is unverified against real data.
/// </para>
/// </remarks>
public sealed class UdtSourceGenerator : IGenerator
{
    private const string PlcStructElement = "SW.Types.PlcStruct";

    private const string ns = TiaXmlDocument.InterfaceNamespace;

    public string Id => "udt";

    public string Description => "User-defined data type (UDT) XML files";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        var declaredTypes = new Dictionary<string, StructType>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in context.Project.DataTypes.OfType<StructType>())
        {
            declaredTypes[type.Name] = type;
        }

        var engineeringVersion = $"V{context.Options.ClampTiaVersion()}";
        var created = context.Options.Timestamp ?? DateTimeOffset.UtcNow;

        foreach (var type in context.Project.DataTypes.OfType<StructType>())
        {
            yield return new GeneratedFile
            {
                RelativePath = $"UDTs/{GenerationHelpers.SanitizeFileName(type.Name)}.xml",
                Content = Render(type, declaredTypes, engineeringVersion, created),
                Kind = ArtifactKind.Udt,
                EmitBom = true,
            };
        }
    }

    private static string Render(
        StructType type,
        Dictionary<string, StructType> declaredTypes,
        string engineeringVersion,
        DateTimeOffset created)
    {
        using var buffer = new TiaXmlDocument.Utf8StringWriter();

        using (var writer = XmlWriter.Create(buffer, TiaXmlDocument.CreateSettings()))
        {
            TiaXmlDocument.WritePrologue(writer, engineeringVersion, created);

            writer.WriteStartElement(PlcStructElement);
            writer.WriteAttributeString("ID", "0");

            writer.WriteStartElement("AttributeList");

            writer.WriteStartElement("Interface");

            // The namespace is declared on Sections and inherited by the whole member subtree.
            // Writing it as a literal xmlns attribute instead makes XmlWriter reject the document.
            writer.WriteStartElement("Sections", ns);

            writer.WriteStartElement("Section", ns);
            writer.WriteAttributeString("Name", "None");

            foreach (var member in type.Members)
            {
                WriteMember(writer, member, declaredTypes, ns);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteElementString("IsFailsafeCompliant", "false");
            writer.WriteElementString("Name", type.Name);
            writer.WriteEndElement();

            var ids = new TiaXmlDocument.IdAllocator();
            if (!string.IsNullOrWhiteSpace(type.Comment))
            {
                writer.WriteStartElement("ObjectList");
                TiaXmlDocument.WriteObjectText(writer, ids, "Comment", type.Comment);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();

            TiaXmlDocument.CloseDocument(writer);
        }

        return buffer.ToString();
    }

    private static void WriteMember(
        XmlWriter writer,
        Variable member,
        Dictionary<string, StructType> declaredTypes,
        string ns,
        HashSet<string>? inlinePath = null)
    {
        writer.WriteStartElement("Member", ns);
        writer.WriteAttributeString("Name", member.Name);
        writer.WriteAttributeString("Datatype", DatatypeAttribute(member, declaredTypes));

        // AttributeList and Comment are independent in the real exports: 8 members carry a Comment
        // with no AttributeList, and 52 carry an AttributeList with no Comment. TIA omits
        // AttributeList for members left at their defaults, so emitting the system-defined defaults
        // explicitly is equivalent to omitting it.
        TiaXmlDocument.WriteMemberBooleanAttributes(writer, ns);
        TiaXmlDocument.WriteMemberComment(writer, ns, member.Comment);

        if (!string.IsNullOrWhiteSpace(member.InitialValue))
        {
            writer.WriteStartElement("StartValue", ns);
            writer.WriteString(SingleLine(member.InitialValue));
            writer.WriteEndElement();
        }

        // TIA inlines the definition of a referenced struct rather than storing a bare reference.
        if (declaredTypes.TryGetValue(ReferencedTypeName(member), out var nested) && !ReferenceEquals(nested, member))
        {
            WriteInlineSections(writer, nested, declaredTypes, ns, inlinePath);
        }

        writer.WriteEndElement();
    }

    private static void WriteInlineSections(
        XmlWriter writer,
        StructType type,
        Dictionary<string, StructType> declaredTypes,
        string ns,
        HashSet<string>? inlinePath = null)
    {
        // A struct that embeds itself, directly or through a chain, would recurse forever. TIA
        // expresses such a link with a pointer type instead of inlining it, so stop at the cycle
        // and leave the reference quoted.
        inlinePath ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!inlinePath.Add(type.Name)) return;

        writer.WriteStartElement("Sections", ns);
        writer.WriteStartElement("Section", ns);
        writer.WriteAttributeString("Name", "None");

        foreach (var member in type.Members)
        {
            WriteMember(writer, member, declaredTypes, ns, inlinePath);
        }

        writer.WriteEndElement();
        writer.WriteEndElement();

        inlinePath.Remove(type.Name);
    }

    /// <summary>
    /// Builds the <c>Datatype</c> attribute. A declared struct is quoted; a built-in type is not.
    /// Array bounds use TIA's <c>Array[lo..hi] of X</c> spelling.
    /// </summary>
    private static string DatatypeAttribute(Variable member, Dictionary<string, StructType> declaredTypes)
    {
        var typeName = ReferencedTypeName(member);
        var isDeclaredStruct = declaredTypes.ContainsKey(typeName);
        var element = isDeclaredStruct ? $"\"{typeName}\"" : typeName;

        if (!member.IsArray) return element;

        var bounds = member.ArrayBounds?.Trim() ?? string.Empty;
        return bounds.Length == 0
            ? $"Array of {element}"
            : $"Array[{bounds}] of {element}";
    }

    /// <summary>
    /// The type name to look up in <c>declaredTypes</c>, with the SCL quoting the type mapper adds
    /// to user-defined types removed.
    /// </summary>
    /// <remarks>
    /// <c>TypeMapper</c> returns <c>"Name"</c> (quotes included) for a user-defined type because
    /// SCL needs the quotes. Those literal quotes are not part of the type name, so looking them up
    /// verbatim misses the dictionary and silently drops the inlined nested <c>Sections</c> block.
    /// </remarks>
    private static string ReferencedTypeName(Variable member)
    {
        var name = member.EffectiveTypeName.Trim();
        return name.Length >= 2 && name[0] == '"' && name[^1] == '"'
            ? name[1..^1].Trim()
            : name;
    }

    /// <summary>Collapses a value onto one line so it cannot break out of its element.</summary>
    private static string SingleLine(string text) =>
        text.Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
}