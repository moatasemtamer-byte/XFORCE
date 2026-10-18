using System.Xml.Linq;

namespace TriconexToTia.Engine.Tests.Synthesizer;

public static class PlcOpenXmlSynthesizer
{
    private static readonly XNamespace Tc6 = "http://www.plcopen.org/xml/tc6_0200";

    public static string CreateMinimal()
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Tc6 + "project",
                new XAttribute("xmlns", "http://www.plcopen.org/xml/tc6_0200"),
                new XElement(Tc6 + "instances")));
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    public static string CreateWithBlock(string blockName, string bodySt = "x := 1;")
    {
        var st = new XElement(Tc6 + "ST", bodySt);
        var body = new XElement(Tc6 + "body", st);
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Tc6 + "project",
                new XAttribute("xmlns", "http://www.plcopen.org/xml/tc6_0200"),
                new XElement(Tc6 + "types",
                    new XElement(Tc6 + "pous",
                        new XElement(Tc6 + "pou",
                            new XAttribute("name", blockName),
                            new XAttribute("pouType", "functionBlock"),
                            new XElement(Tc6 + "interface"),
                            body)))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }
}
