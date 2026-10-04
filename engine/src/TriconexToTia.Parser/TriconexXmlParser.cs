using System.Xml;
using System.Xml.Linq;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;

namespace TriconexToTia.Parser;

/// <summary>
/// Tolerant reader for a Triconex (TriStation) XML export.
///
/// The exact export schema is not fixed, so the parser matches element names
/// case-insensitively (ignoring '_' / '-') and recurses through unknown wrapper
/// elements. Anything it cannot place is captured as an
/// <see cref="UnrecognizedElement"/> rather than dropped.
/// </summary>
public sealed class TriconexXmlParser
{
    private const int SnippetLimit = 400;

    public PlcProject ParseFile(string path)
    {
        using var stream = File.OpenRead(path);
        var project = Parse(stream);
        project.SourceFile = Path.GetFileName(path);
        return project;
    }

    public PlcProject Parse(Stream stream)
    {
        var document = XDocument.Load(stream, LoadOptions.SetLineInfo);
        return Parse(document);
    }

    public PlcProject Parse(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        return Parse(document);
    }

    public PlcProject Parse(XDocument document)
    {
        var root = document.Root
            ?? throw new InvalidDataException("The XML document has no root element.");

        var project = new PlcProject
        {
            Name = root.Attr("name", "projectName", "ProjectName") ?? "TriconexProject",
        };

        Walk(root, project);
        return project;
    }

    private static readonly string[] FunctionBlockNames = { "FunctionBlock", "FB" };
    private static readonly string[] FunctionNames = { "Function", "FC" };
    private static readonly string[] ProgramNames = { "Program", "ProgramBlock", "OB", "POU", "ControlBlock" };
    private static readonly string[] DataTypeNames = { "DataType", "UDT", "StructType", "CustomType" };
    private static readonly string[] GlobalContainerNames = { "GlobalVariables", "Globals", "GlobalVarList", "GlobalVariableList" };
    private static readonly string[] IoPointNames = { "IOPoint", "IOSignal", "IOChannel", "Channel", "Point", "Signal", "Tag" };

    private static void Walk(XElement element, PlcProject project)
    {
        if (element.IsNamed(FunctionBlockNames))
        {
            project.Blocks.Add(ParseFunctionBlock(element));
            return;
        }

        if (element.IsNamed(FunctionNames))
        {
            project.Blocks.Add(ParseFunction(element));
            return;
        }

        if (element.IsNamed(ProgramNames))
        {
            project.Blocks.Add(ParseProgram(element));
            return;
        }

        if (element.IsNamed(DataTypeNames))
        {
            project.DataTypes.Add(ParseDataType(element));
            return;
        }

        if (element.IsNamed(GlobalContainerNames))
        {
            foreach (var child in element.Elements())
            {
                if (child.IsNamed("Variable", "Var", "GlobalVariable"))
                {
                    project.GlobalVariables.Add(ParseGlobalVariable(child));
                }
                else if (child.HasElements)
                {
                    Walk(child, project);
                }
            }
            return;
        }

        // A leaf I/O point. Containers named "IO"/"IOConfiguration" fall through
        // to the recursion branch below.
        if (!element.HasElements && element.IsNamed(IoPointNames))
        {
            project.IoPoints.Add(ParseIoPoint(element));
            return;
        }

        // A bare variable outside any block is treated as a project-global.
        if (!element.HasElements && element.IsNamed("Variable", "Var"))
        {
            project.GlobalVariables.Add(ParseGlobalVariable(element));
            return;
        }

        var children = element.Elements().ToList();
        if (children.Count > 0)
        {
            foreach (var child in children)
            {
                Walk(child, project);
            }
            return;
        }

        RecordUnrecognized(element, project);
    }

    private static FunctionBlock ParseFunctionBlock(XElement element) =>
        PopulateBlock(new FunctionBlock(), element);

    private static Function ParseFunction(XElement element) =>
        PopulateBlock(new Function(), element);

    private static Program ParseProgram(XElement element) =>
        PopulateBlock(new Program(), element);

    private static TBlock PopulateBlock<TBlock>(TBlock block, XElement element) where TBlock : PlcBlock
    {
        block.Name = element.Attr("name", "id", "blockName") ?? $"Block_{element.Line()}";
        block.SourceLine = element.Line();
        block.SourceElementName = element.Name.LocalName;
        block.Comment = element.Attr("comment", "description") ?? element.ChildValue("Comment", "Description");
        block.ReturnType = GetReturnType(element);

        // An explicit language attribute always wins: a <Code> element holding
        // text means ST, but it can also wrap a <FBD>/<LD> body, and an
        // attribute is the only way to tell those apart.
        var declared = LanguageFromAttribute(element.Attr("language", "lang"));
        if (declared == PlcLanguage.Unknown)
        {
            declared = null;
        }

        var codeElement = element.Elements()
            .FirstOrDefault(child => child.IsNamed("Code", "Body", "Statement", "Statements", "ST", "Implementation", "Source", "Logic"));

        if (codeElement is not null)
        {
            block.Code = codeElement.Value;
            block.Language = declared
                ?? (codeElement.HasElements && string.IsNullOrWhiteSpace(block.Code)
                    ? LanguageFromName(codeElement.Name.LocalName)
                    : PlcLanguage.ST);
        }
        else
        {
            block.Code = element.Elements()
                .Where(child => !child.HasElements && child.IsNamed("Text", "Content"))
                .Select(child => child.Value)
                .FirstOrDefault() ?? string.Empty;
            block.Language = declared ?? (block.Code.Length > 0 ? PlcLanguage.ST : PlcLanguage.Unknown);
        }

        ParseVariables(element, block);
        return block;
    }

    private static string? GetReturnType(XElement element)
    {
        var attribute = element.Attr("returnType", "return", "resultType");
        if (attribute is not null) return attribute;

        var child = element.Elements().FirstOrDefault(e => e.IsNamed("ReturnType", "ResultType", "Result"));
        if (child is null) return null;

        return child.Attr("type", "name") ?? (child.HasElements ? null : child.Value.Trim());
    }

    private static void ParseVariables(XElement blockElement, PlcBlock block)
    {
        var variableElements = blockElement
            .DescendantsAndSelf()
            .Where(e => e != blockElement && e.IsNamed("Variable", "Var", "Parameter"))
            .Where(e => !e.IsDescendantOf(blockElement, "Code", "Body", "ST", "Implementation", "Logic"))
            .ToList();

        foreach (var element in variableElements)
        {
            var direction = ParseDirection(
                element.Attr("direction", "scope", "usage", "mode", "kind"),
                NearestGroupName(element, blockElement));
            block.Variables.Add(ParseVariable(element, direction));
        }
    }

    private static Variable ParseVariable(XElement element, VariableDirection direction)
    {
        var variable = new Variable
        {
            Name = element.Attr("name", "id") ?? $"Var_{element.Line()}",
            TypeName = ResolveTypeName(element),
            Direction = direction,
            SourceLine = element.Line(),
            Comment = element.Attr("comment", "description") ?? element.ChildValue("Comment", "Description"),
        };
        return variable;
    }

    private static StructType ParseDataType(XElement element)
    {
        var type = new StructType
        {
            Name = element.Attr("name", "id", "typeName") ?? $"Type_{element.Line()}",
            SourceLine = element.Line(),
            SourceElementName = element.Name.LocalName,
            Comment = element.Attr("comment", "description") ?? element.ChildValue("Comment", "Description"),
        };

        var members = element
            .Descendants()
            .Where(e => e.IsNamed("Member", "Field", "Variable", "Var", "Element"))
            .Where(e => !e.HasElements || e.Attr("name", "id") is not null)
            .ToList();

        foreach (var member in members)
        {
            type.Members.Add(new Variable
            {
                Name = member.Attr("name", "id") ?? $"Member_{member.Line()}",
                TypeName = ResolveTypeName(member),
                Direction = VariableDirection.Local,
                SourceLine = member.Line(),
                Comment = member.Attr("comment", "description") ?? member.ChildValue("Comment", "Description"),
            });
        }

        return type;
    }

    private static GlobalVariable ParseGlobalVariable(XElement element)
    {
        return new GlobalVariable
        {
            Name = element.Attr("name", "id") ?? $"Global_{element.Line()}",
            TypeName = ResolveTypeName(element),
            InitialValue = element.Attr("initial", "initialValue", "value") ?? element.ChildValue("InitialValue", "Initial", "Value"),
            Comment = element.Attr("comment", "description") ?? element.ChildValue("Comment", "Description"),
            LogicalAddress = element.Attr("address", "logicalAddress", "ioAddress", "modbus"),
            AddressResolved = element.Attr("address", "logicalAddress", "ioAddress") is not null,
            SourceLine = element.Line(),
        };
    }

    private static IoPoint ParseIoPoint(XElement element)
    {
        var point = new IoPoint
        {
            Name = element.Attr("name", "id", "tag") ?? $"IO_{element.Line()}",
            TypeName = ResolveTypeName(element),
            Direction = ParseIoDirection(element.Attr("direction", "io", "type", "mode")),
            Signal = ParseIoSignal(element.Attr("signal", "dataType", "type", "typeName")),
            Rack = ParseInt(element.Attr("rack", "chassis")),
            Slot = ParseInt(element.Attr("slot", "module")),
            Channel = ParseInt(element.Attr("channel", "point", "index")),
            Comment = element.Attr("comment", "description") ?? element.ChildValue("Comment", "Description"),
            SourceLine = element.Line(),
        };

        var address = element.Attr("address", "logicalAddress", "ioAddress", "modbus");
        if (address is not null)
        {
            point.LogicalAddress = address;
            point.AddressResolved = true;
        }

        return point;
    }

    private static void RecordUnrecognized(XElement element, PlcProject project)
    {
        project.UnrecognizedElements.Add(new UnrecognizedElement
        {
            ElementName = element.Name.LocalName,
            ParentPath = element.ParentPath(),
            SourceLine = element.Line(),
            Snippet = Truncate(element.ToString(), SnippetLimit),
        });
    }

    private static string Truncate(string value, int limit) =>
        value.Length <= limit ? value : value[..limit] + "...";

    private static string ResolveTypeName(XElement element)
    {
        var attribute = element.Attr("type", "dataType", "dataTypeName", "typeName");
        if (attribute is not null)
        {
            var arrayLower = element.Attr("arrayLower", "lowerBound");
            var arrayUpper = element.Attr("arrayUpper", "upperBound");
            if (arrayLower is not null && arrayUpper is not null)
            {
                return $"ARRAY[{arrayLower}..{arrayUpper}] OF {attribute}";
            }
            return attribute;
        }

        var typeChild = element.Elements().FirstOrDefault(e => e.IsNamed("Type", "DataType", "DataTypeName"));
        if (typeChild is not null)
        {
            var name = typeChild.Attr("name", "type", "typeName") ?? typeChild.Value.Trim();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        return "ANY";
    }

    private static string? NearestGroupName(XElement variable, XElement blockElement)
    {
        for (var current = variable.Parent; current is not null && current != blockElement; current = current.Parent)
        {
            var name = current.Name.LocalName;
            if (DirectionFromGroupName(name) is not null) return name;
        }
        return null;
    }

    private static VariableDirection ParseDirection(string? raw, string? groupName)
    {
        if (raw is not null)
        {
            var value = ParserExtensions.Normalize(raw);
            switch (value)
            {
                case "in":
                case "input":
                case "varinput":
                case "read":
                    return VariableDirection.Input;
                case "out":
                case "output":
                case "varoutput":
                case "write":
                    return VariableDirection.Output;
                case "inout":
                case "varinout":
                case "inputoutput":
                case "readwrite":
                    return VariableDirection.InOut;
                case "external":
                case "global":
                    return VariableDirection.External;
                case "var":
                case "local":
                case "temp":
                case "internal":
                    return VariableDirection.Local;
            }
        }

        return DirectionFromGroupName(groupName) ?? VariableDirection.Local;
    }

    private static VariableDirection? DirectionFromGroupName(string? groupName)
    {
        if (groupName is null) return null;
        var value = ParserExtensions.Normalize(groupName);

        if (value is "input" or "inputs" or "varinput" or "variablesinput" or "in") return VariableDirection.Input;
        if (value is "output" or "outputs" or "varoutput" or "variablesoutput" or "out") return VariableDirection.Output;
        if (value is "inout" or "inouts" or "varinout" or "variablesinout" or "inoutvars") return VariableDirection.InOut;
        if (value is "local" or "locals" or "temp" or "temps" or "temporary" or "variable" or "variables" or "var")
            return VariableDirection.Local;
        if (value is "external" or "globals" or "globalvariables") return VariableDirection.External;

        return null;
    }

    private static IoDirection ParseIoDirection(string? raw)
    {
        if (raw is null) return IoDirection.Memory;
        var value = ParserExtensions.Normalize(raw);
        if (value.Contains("out", StringComparison.Ordinal)) return IoDirection.Output;
        if (value.Contains("in", StringComparison.Ordinal)) return IoDirection.Input;
        return IoDirection.Memory;
    }

    private static IoSignal ParseIoSignal(string? raw)
    {
        if (raw is null) return IoSignal.Digital;
        var value = ParserExtensions.Normalize(raw);
        if (value.Contains("real", StringComparison.Ordinal) ||
            value.Contains("int", StringComparison.Ordinal) ||
            value.Contains("word", StringComparison.Ordinal) ||
            value.Contains("analog", StringComparison.Ordinal) ||
            value.Contains("ai", StringComparison.Ordinal) ||
            value.Contains("ao", StringComparison.Ordinal))
        {
            return IoSignal.Analog;
        }
        return IoSignal.Digital;
    }

    private static int? ParseInt(string? raw) =>
        int.TryParse(raw, out var value) ? value : null;

    private static PlcLanguage? LanguageFromAttribute(string? raw)
    {
        if (raw is null) return null;
        return LanguageFromName(raw);
    }

    private static PlcLanguage LanguageFromName(string raw)
    {
        var value = ParserExtensions.Normalize(raw);
        return value switch
        {
            "st" or "scl" or "structuredtext" => PlcLanguage.ST,
            "fbd" or "functionblockdiagram" => PlcLanguage.FBD,
            "ld" or "ladder" or "ladderdiagram" or "lad" => PlcLanguage.LD,
            "sfc" or "sequentialfunctionchart" or "grafcet" => PlcLanguage.SFC,
            _ => PlcLanguage.Unknown,
        };
    }
}
