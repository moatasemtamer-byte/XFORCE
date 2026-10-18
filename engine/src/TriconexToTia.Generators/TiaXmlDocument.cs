using System.Text;
using System.Xml;

namespace TriconexToTia.Generators;

/// <summary>
/// Writes the <c>Document</c> prologue that every genuine TIA Portal V16 SimaticML export carries.
/// </summary>
/// <remarks>
/// <para>
/// Shape verified against 64 real exports (19 <c>SW.Types.PlcStruct</c>, 34 CON100 block files,
/// 9 lab block files, 6 <c>SW.Tags.PlcTagTable</c> files). All 64 declare
/// <c>&lt;Engineering version="V16" /&gt;</c>, so the version written here is asserted by the
/// caller rather than discovered.
/// </para>
/// <para>
/// <c>DocumentInfo</c> is not cosmetic: it is where <c>ExportSetting</c> lives, and the two
/// observed settings change what the body contains. With <c>WithDefaults</c> (CON100) each tag
/// spells out <c>ExternalAccessible</c>/<c>ExternalVisible</c>/<c>ExternalWritable</c>; with
/// <c>None</c> (the lab projects) those elements are omitted and the defaults are implied.
/// We emit <c>WithDefaults</c> because the explicit form is what we can round-trip losslessly.
/// </para>
/// <para>
/// Encoding is UTF-8 <em>with</em> BOM and the declaration reads <c>encoding="utf-8"</c> in lower
/// case, matching all 64 samples.
/// </para>
/// </remarks>
internal static class TiaXmlDocument
{
    /// <summary>Namespace declared on the UDT <c>Sections</c> element.</summary>
    public const string InterfaceNamespace = "http://www.siemens.com/automation/Openness/SW/Interface/v4";

    /// <summary>Namespace declared by graphical (LAD/FBD) network sources.</summary>
    public const string FlgNetNamespace =
        "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4";

    /// <summary>Culture used by every real comment and title in the supplied exports.</summary>
    public const string Culture = "en-US";

    public static XmlWriterSettings CreateSettings() => new()
    {
        Indent = true,
        IndentChars = "  ",
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };

    /// <summary>
    /// A <see cref="StringWriter"/> that reports UTF-8.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlWriter"/> derives the encoding named in the XML declaration from the
    /// <see cref="TextWriter"/> it is given. A plain <see cref="StringWriter"/> reports UTF-16, so
    /// writing into a <see cref="StringBuilder"/> yields <c>encoding="utf-16"</c> — which none of the
    /// 64 real exports declares. All of them declare lower-case <c>utf-8</c> and carry a BOM.
    /// </remarks>
    public sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    /// <summary>
    /// Writes <c>&lt;Document&gt;</c>, <c>&lt;Engineering version=... /&gt;</c> and
    /// <c>&lt;DocumentInfo&gt;</c>. The caller writes the body element then calls
    /// <see cref="CloseDocument"/>.
    /// </summary>
    public static void WritePrologue(XmlWriter writer, string engineeringVersion, DateTimeOffset created)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Document");

        // Real files use a self-closing element with a space before the slash.
        writer.WriteStartElement("Engineering");
        writer.WriteAttributeString("version", engineeringVersion);
        writer.WriteEndElement();

        writer.WriteStartElement("DocumentInfo");
        writer.WriteElementString("Created", created.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteElementString("ExportSetting", "WithDefaults");

        writer.WriteStartElement("InstalledProducts");
        WriteProduct(writer, "Totally Integrated Automation Portal", "V16 Update 3", product: true);
        WriteProduct(writer, "TIA Portal Openness", "V16 Update 3", product: false);
        WriteProduct(writer, "STEP 7 Professional", "V16 Update 3", product: true);
        writer.WriteEndElement();

        writer.WriteEndElement();
    }

    public static void CloseDocument(XmlWriter writer)
    {
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteProduct(XmlWriter writer, string name, string version, bool product)
    {
        writer.WriteStartElement(product ? "Product" : "OptionPackage");
        writer.WriteElementString("DisplayName", name);
        writer.WriteElementString("DisplayVersion", version);
        writer.WriteEndElement();
    }

    /// <summary>
    /// Allocates the next <c>ID</c> for an <c>ObjectList</c> child. Real exports number the
    /// container 0 and then hand out consecutive integers across every nested item, including the
    /// multilingual-text wrappers, so IDs are drawn from one counter per document.
    /// </summary>
    public sealed class IdAllocator
    {
        private int next;

        public IdAllocator(int start = 1) => next = start;

        public string Next() => (next++).ToString(GenerationHelpers.Culture);
    }

    /// <summary>
    /// Writes the object-level comment/title form used by tag tables and UDT object lists:
    /// <c>MultilingualText</c> &gt; <c>MultilingualTextItem</c> &gt; <c>Culture</c>/<c>Text</c>.
    /// </summary>
    /// <remarks>
    /// This is deliberately different from the compact member form
    /// (<c>&lt;Comment&gt;&lt;MultiLanguageText Lang=".."&gt;</c>) that UDT <em>members</em> use. Both
    /// shapes appear in the real exports and they are not interchangeable.
    /// </remarks>
    public static void WriteObjectText(
        XmlWriter writer,
        IdAllocator ids,
        string compositionName,
        string text,
        string? title = null)
    {
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(title)) return;

        if (!string.IsNullOrWhiteSpace(text))
        {
            WriteMultilingualText(writer, ids, "Comment", text);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            WriteMultilingualText(writer, ids, "Title", title);
        }
    }

    private static void WriteMultilingualText(XmlWriter writer, IdAllocator ids, string compositionName, string text)
    {
        writer.WriteStartElement("MultilingualText");
        writer.WriteAttributeString("ID", ids.Next());
        writer.WriteAttributeString("CompositionName", compositionName);
        writer.WriteStartElement("ObjectList");

        writer.WriteStartElement("MultilingualTextItem");
        writer.WriteAttributeString("ID", ids.Next());
        writer.WriteAttributeString("CompositionName", "Items");
        writer.WriteStartElement("AttributeList");
        writer.WriteElementString("Culture", Culture);
        writer.WriteElementString("Text", text);
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    /// <summary>
    /// Writes a UDT member comment in the compact form: <c>&lt;Comment&gt;&lt;MultiLanguageText
    /// Lang="en-US"&gt;</c>. Attribute order and casing match the real exports exactly.
    /// </summary>
    public static void WriteMemberComment(XmlWriter writer, string ns, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        writer.WriteStartElement("Comment", ns);
        writer.WriteStartElement("MultiLanguageText", ns);
        writer.WriteAttributeString("Lang", Culture);
        writer.WriteString(text);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    /// <summary>
    /// Writes the four system-defined boolean attributes every real UDT member carries, in the
    /// order and casing the exports use.
    /// </summary>
    public static void WriteMemberBooleanAttributes(
        XmlWriter writer,
        string ns,
        bool externalAccessible = true,
        bool externalVisible = true,
        bool externalWritable = false,
        bool setPoint = false)
    {
        writer.WriteStartElement("AttributeList", ns);
        WriteBoolean(writer, ns, "ExternalAccessible", externalAccessible);
        WriteBoolean(writer, ns, "ExternalVisible", externalVisible);
        WriteBoolean(writer, ns, "ExternalWritable", externalWritable);
        WriteBoolean(writer, ns, "SetPoint", setPoint);
        writer.WriteEndElement();
    }

    private static void WriteBoolean(XmlWriter writer, string ns, string name, bool value)
    {
        writer.WriteStartElement("BooleanAttribute", ns);
        writer.WriteAttributeString("Name", name);
        writer.WriteAttributeString("SystemDefined", "true");
        writer.WriteString(value ? "true" : "false");
        writer.WriteEndElement();
    }
}