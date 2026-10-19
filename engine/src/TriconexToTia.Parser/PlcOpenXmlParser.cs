using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;

namespace TriconexToTia.Parser;

/// <summary>
/// Reads a TriStation project export. TriStation writes PLCopen XML
/// (<c>http://www.plcopen.org/xml/tc6.xsd</c>) and puts every TriConEx specific
/// property in the <c>ts</c> namespace
/// (<c>http://www.triconex.com/controller/03/2008</c>), so both namespaces are read.
/// </summary>
/// <remarks>
/// The reader streams the document and only materialises one <c>pou</c> or
/// <c>configuration</c> at a time; exports of real projects reach tens of megabytes.
/// </remarks>
public sealed class PlcOpenXmlParser
{
    private static readonly XNamespace Tc6 = ProjectHeader.PlcOpenNamespace;
    private static readonly XNamespace Ts = ProjectHeader.VendorNamespace;

    /// <summary>Spellings that are IEC constants, not variables.</summary>
    private static readonly HashSet<string> LiteralNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRUE", "FALSE", "PI",
    };

    private PlcProject _project = new();

    /// <summary>
    /// Every global variable name already seen, so a point that the export lists in more than one
    /// place (the controller list and a POU interface, or a program that is emitted twice) is only
    /// declared once in the generated data block.
    /// </summary>
    private readonly HashSet<string> _globalNames = new(StringComparer.OrdinalIgnoreCase);

    public PlcProject ParseFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream, Path.GetFileName(path));
    }

    public PlcProject Parse(string xml) => Parse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

    public PlcProject Parse(Stream stream, string sourceFile = "")
    {
        _project = new PlcProject { SourceFile = sourceFile };
        _globalNames.Clear();

        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Ignore,
            CheckCharacters = false,
            CloseInput = false,
        };

        using var reader = XmlReader.Create(stream, settings);
        var path = new List<string>();

        // Advance past the XML declaration / comments to the root element.
        while (reader.Read() && reader.NodeType != XmlNodeType.Element)
        {
        }

        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "project")
        {
            throw new InvalidDataException(
                $"Expected a PLCopen <project> root element but found <{reader.LocalName}>. " +
                "Is this a TriStation XML export?");
        }

        path.Add("project");

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (path.Count > 0) path.RemoveAt(path.Count - 1);
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element) continue;

            var name = Qualify(reader);
            var parent = path.Count > 0 ? path[^1] : string.Empty;

            if (parent == "project" && reader.LocalName == "fileHeader")
            {
                ReadHeader(reader);
                continue;
            }

            if (path.Count >= 2 && path[0] == "project" && path[1] == "types")
            {
                // POUs live in <types><pous>, data types directly in <types>.
                if (reader.LocalName == "pou" && path.Count >= 3 && path[2] == "pous")
                {
                    ReadPou(XElement.Parse(reader.ReadOuterXml(), LoadOptions.SetLineInfo));
                    continue;
                }
                if (reader.LocalName == "dataTypes")
                {
                    ReadDataTypes(XElement.Parse(reader.ReadOuterXml()));
                    continue;
                }
            }

            if (path.Count == 2 && path[0] == "project" && path[1] == "instances" &&
                reader.LocalName == "configurations")
            {
                ReadConfigurations(XElement.Parse(reader.ReadOuterXml()));
                continue;
            }

            // TriStation writes LIBRARY in the default namespace, not the vendor one.
            if (reader.LocalName == "LIBRARY")
            {
                var library = reader.GetAttribute("Name");
                if (!string.IsNullOrWhiteSpace(library) && !_project.Libraries.Contains(library))
                {
                    _project.Libraries.Add(library);
                }
            }

            if (!reader.IsEmptyElement) path.Add(name);
        }

        Finalise();
        return _project;
    }

    private static string Qualify(XmlReader reader) =>
        reader.NamespaceURI == Ts.NamespaceName ? "ts:" + reader.LocalName : reader.LocalName;

    // ----------------------------------------------------------------- header

    private void ReadHeader(XmlReader reader)
    {
        var header = _project.Header;
        header.CompanyName = reader.GetAttribute("companyName");
        header.ProductName = reader.GetAttribute("productName");
        header.ProductVersion = reader.GetAttribute("productVersion");
        header.ProductRelease = reader.GetAttribute("productRelease");
        header.SchemaVersion = reader.GetAttribute("prjSchemaVersion");
        header.PreviousProductVersion = reader.GetAttribute("previousTSVersion");
        header.ContentDescription = reader.GetAttribute("contentDescription");

        if (DateTimeOffset.TryParse(reader.GetAttribute("creationDateTime"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var created))
        {
            header.CreatedAt = created;
        }

        if (!reader.IsEmptyElement) reader.Skip();
    }

    // ------------------------------------------------------------- data types

    private void ReadDataTypes(XElement root)
    {
        foreach (var dataType in root.Elements(Tc6 + "dataType"))
        {
            var name = dataType.Attr("name");
            if (name is null) continue;

            var structType = new StructType { Name = name, SourceLine = dataType.Line() };
            var members = dataType.Descendants(Tc6 + "struct").FirstOrDefault()
                           ?? dataType.Elements(Tc6 + "baseType").FirstOrDefault();

            if (members is not null)
            {
                foreach (var member in members.Elements(Tc6 + "variable"))
                {
                    var variable = ReadVariable(member, VariableDirection.Local, constant: false);
                    if (variable is not null) structType.Members.Add(variable);
                }
            }

            _project.DataTypes.Add(structType);
        }
    }

    // -------------------------------------------------------------------- pou

    private void ReadPou(XElement pou)
    {
        var name = pou.Attr("name");
        if (name is null)
        {
            _project.UnrecognizedElements.Add(new UnrecognizedElement
            {
                ElementName = "pou",
                ParentPath = "project/types/pou",
                SourceLine = pou.Line(),
                Snippet = Truncate(pou.ToString(SaveOptions.DisableFormatting)),
            });
            return;
        }

        var pouType = (pou.Attr("pouType") ?? "program").Trim();
        var version = pou.Attr("version");

        PlcBlock block = pouType.ToLowerInvariant() switch
        {
            "functionblock" => new FunctionBlock(),
            "function" => new Function(),
            _ => new Program(),
        };

        block.Name = name;
        block.Version = version;
        block.SourceLine = pou.Line();
        block.SourceElementName = $"pou[@pouType='{pouType}']";

        var pouExt = pou.Element(Ts + "POU_EXT");
        block.ApplicationType = pouExt?.Element(Ts + "ApplicationType")?.Value.Trim();

        ReadInterface(pou.Element(Tc6 + "interface"), block);

        // PLCopen declares the return type inside <interface>; some exporters put it on the POU.
        ReadReturnType(
            pou.Element(Tc6 + "interface")?.Element(Tc6 + "returnType") ?? pou.Element(Tc6 + "returnType"),
            block);

        block.Logic = ReadBody(pou.Element(Tc6 + "body"), block);

        _project.Blocks.Add(block);
    }

    /// <summary>
    /// Reads the &lt;returnType&gt; of a function. Without it the generated SCL would declare
    /// "FUNCTION ... : Void", which does not compile.
    /// </summary>
    private static void ReadReturnType(XElement? returnTypeElement, PlcBlock block)
    {
        if (returnTypeElement is null) return;

        var holder = new Variable();
        ReadType(returnTypeElement, holder);
        if (!string.IsNullOrWhiteSpace(holder.TypeName))
        {
            block.ReturnType = holder.TypeName;
        }
    }

    private void ReadInterface(XElement? interfaceElement, PlcBlock block)
    {
        if (interfaceElement is null) return;

        foreach (var group in interfaceElement.Elements())
        {
            // returnType is a type declaration, not a variable group.
            if (group.Name.LocalName.Equals("returnType", StringComparison.OrdinalIgnoreCase)) continue;

            var direction = group.Name.LocalName switch
            {
                "inputVars" => VariableDirection.Input,
                "outputVars" => VariableDirection.Output,
                "inOutVars" => VariableDirection.InOut,
                "externalVars" => VariableDirection.External,
                "globalVars" => VariableDirection.Global,
                _ => VariableDirection.Local,
            };

            if (direction == VariableDirection.Global)
            {
                // A POU interface <globalVars> block lists the program's own global memory. In
                // TriStation these points persist across scans (seal-in latches, step bits), so they
                // are neither tags nor block locals: they are materialised in the generated global
                // data block. Dropping them leaves every statement that references them undefined.
                ReadGlobalVariables(group);
                continue;
            }

            var constant = string.Equals(group.Attr("constant"), "true", StringComparison.OrdinalIgnoreCase);

            foreach (var variableElement in group.Elements(Tc6 + "variable"))
            {
                var name = variableElement.Attr("name");
                if (name is null) continue;

                // A TriStation FBD interface also lists the literals used by the diagram as pseudo
                // variables ("TRUE", "FALSE", "16#FF", "1"). They are constant values, not storage,
                // so they must not become declarations; the diagram keeps them as expressions.
                if (direction == VariableDirection.Local && IsLiteral(name))
                {
                    _project.UnrecognizedElements.Add(new UnrecognizedElement
                    {
                        ElementName = "variable",
                        ParentPath = $"project/types/pou[@name='{block.Name}']/interface/localVars",
                        SourceLine = variableElement.Line(),
                        Snippet = $"literal constant \"{name}\" (no declaration generated)",
                    });
                    continue;
                }

                var variable = ReadVariable(variableElement, direction, constant);
                if (variable is not null) block.Variables.Add(variable);
            }
        }
    }

    /// <summary>True for IEC literal spellings that TriStation stores as pseudo variables.</summary>
    private static bool IsLiteral(string name)
    {
        if (LiteralNames.Contains(name)) return true;
        if (name.StartsWith("16#", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("2#", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("8#", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static Variable? ReadVariable(XElement element, VariableDirection direction, bool constant)
    {
        var name = element.Attr("name");
        if (name is null) return null;

        var variable = new Variable
        {
            Name = name,
            Direction = direction,
            Constant = constant,
            SourceLine = element.Line(),
            Address = element.Attr("address") ?? element.Element(Tc6 + "address")?.Value.Trim() ?? element.Element(Ts + "address")?.Value.Trim(),
        };

        var typeElement = element.Element(Tc6 + "type");
        if (typeElement is not null)
        {
            ReadType(typeElement, variable);
        }

        var initialValue = element.Element(Tc6 + "initialValue")?.Element(Tc6 + "simpleValue")?.Attr("value");
        if (!string.IsNullOrWhiteSpace(initialValue)) variable.InitialValue = initialValue;

        var documentation = element.Element(Tc6 + "documentation")?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(documentation)) variable.Documentation = documentation;

        return variable;
    }

    /// <summary>
    /// Reads a <c>&lt;type&gt;</c> child: a primitive (BOOL, DINT, ...), a
    /// <c>&lt;derived name="..."/&gt;</c> reference to a function block or data type,
    /// or an array declaration.
    /// </summary>
    private static void ReadType(XElement typeElement, Variable variable)
    {
        var array = typeElement.Element(Tc6 + "array");
        var dimensions = array is null
            ? Array.Empty<XElement>()
            : array.Elements().Where(element => element.Name.LocalName == "dimension").ToArray();

        var inner = array?.Elements().FirstOrDefault(element => element.Name.LocalName != "dimension")
                    ?? typeElement.Elements().FirstOrDefault();
        if (inner is null) return;

        var derived = inner.Attribute("name");
        if (inner.Name.LocalName == "derived" && derived is not null)
        {
            variable.TypeName = derived.Value;
            variable.IsDerivedType = true;
        }
        else
        {
            variable.TypeName = inner.Name.LocalName;
        }

        if (dimensions.Length == 0) return;

        variable.IsArray = true;
        var bounds = dimensions
            .Select(dimension =>
            {
                var lower = dimension.Element(Tc6 + "lower")?.Value ?? dimension.Attr("lower") ?? string.Empty;
                var upper = dimension.Element(Tc6 + "upper")?.Value ?? dimension.Attr("upper") ?? string.Empty;
                return $"{lower}..{upper}";
            })
            .ToArray();
        variable.ArrayBounds = string.Join(", ", bounds);
    }

    // ------------------------------------------------------------------- body

    private LogicModel? ReadBody(XElement? body, PlcBlock block)
    {
        if (body is null) return null;

        foreach (var language in body.Elements())
        {
            switch (language.Name.LocalName)
            {
                case "ST":
                {
                    var code = language.Value.Trim();
                    // TriStation wraps the ST source in the IEC FUNCTION_BLOCK header and
                    // footer, so it can be used as-is once the block name matches.
                    block.Code = code;
                    block.Language = PlcLanguage.ST;
                    return new TextualLogic(PlcLanguage.ST, code);
                }
                case "FBD":
                    block.Language = PlcLanguage.FBD;
                    return ReadStructuredBody(language, block);
                case "ld":
                case "sfc":
                {
                    // Ladder and sequential logic is kept verbatim; SCL has no equivalent, so the
                    // generator emits the original XML as a sidecar file instead of rewriting it.
                    var languageKind = language.Name.LocalName == "ld" ? PlcLanguage.LD : PlcLanguage.SFC;
                    var code = language.ToString(SaveOptions.DisableFormatting);
                    block.Code = code;
                    block.Language = languageKind;
                    return new TextualLogic(languageKind, code);
                }
            }
        }

        return null;
    }

    private StructuredLogic ReadStructuredBody(XElement language, PlcBlock block)
    {
        var logic = new StructuredLogic(PlcLanguage.FBD);

        foreach (var sheet in language.Descendants(Ts + "SHEET"))
        {
            var title = sheet.Elements(Ts + "Macros")
                .FirstOrDefault(macro => string.Equals(macro.Attr("Name"), "SHEET_TITLE", StringComparison.OrdinalIgnoreCase))
                ?.Element(Ts + "Value")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(title)) logic.SheetTitles.Add(title);
        }

var items = new List<LogicItem>();
        var sources = new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var element in language.Descendants())
        {
            var localId = element.Attr("localId");
            if (localId is null) continue;
            if (!element.Name.Namespace.Equals(Tc6)) continue;

            LogicItem? item = element.Name.LocalName switch
            {
                "block" => ReadBlockCall(element, localId),
                "inVariable" => new VariableReference
                {
                    LocalId = localId,
                    Expression = element.Element(Tc6 + "expression")?.Value.Trim() ?? string.Empty,
                },
                "outVariable" => ReadAssignment(element, localId),
                // TriStation has no outVariable: an inOutVariable is written when something drives
                // it and only read otherwise.
                "inOutVariable" => HasIncomingConnection(element)
                    ? ReadAssignment(element, localId)
                    : new VariableReference
                    {
                        LocalId = localId,
                        Expression = element.Element(Tc6 + "expression")?.Value.Trim() ?? string.Empty,
                    },
                "comment" => ReadComment(element, localId),
                _ => null,
            };

            if (item is null) continue;

            var position = element.Element(Tc6 + "position");
            if (position is not null)
            {
                item.X = ParseInt(position.Attr("x"));
                item.Y = ParseInt(position.Attr("y"));
                // TriStation writes the sheet as a vendor-namespaced attribute.
                item.SheetNumber = ParseInt(position.Attr("SheetNumber"));
            }

            var annotation = element.Element(Ts + "FBDITEM_EXT")?.Element(Ts + "TEXT")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(annotation) && !annotation.StartsWith('%'))
            {
                item.Annotation = annotation;
            }

            // The diagram records the type it expects for this signal. The same name can carry a
            // different project-wide type, so keep the site type for the generator's coercion.
            var dataType = element.Element(Ts + "FBDITEM_EXT")?.Element(Ts + "DATATYPE")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(dataType)) item.SourceTypeName = dataType;

            // localId restarts on every sheet, so the sheet is part of the identity.
            if (!sources.TryAdd(item.SheetKey, element))
            {
                _project.UnrecognizedElements.Add(new UnrecognizedElement
                {
                    ElementName = element.Name.LocalName,
                    ParentPath = $"pou/{block.Name}/FBD/sheet {item.SheetNumber}",
                    SourceLine = element.Line(),
                    Snippet = $"localId \"{localId}\" appears more than once on sheet {item.SheetNumber}.",
                });
            }

            items.Add(item);
        }

        var bySheetKey = items
            .GroupBy(item => item.SheetKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        // TriStation reuses localIds across the sheets of one block, so a connection may point at an
        // item that only exists on another sheet.
        var byLocalId = items
            .GroupBy(item => item.LocalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var call in items.OfType<BlockCall>())
        {
            ResolveCall(call, sources[call.SheetKey], bySheetKey, byLocalId, logic);
        }

        foreach (var assignment in items.OfType<VariableAssignment>())
        {
            ResolveAssignment(assignment, sources[assignment.SheetKey], bySheetKey, byLocalId, logic);
        }

        logic.Items.AddRange(items);
        block.CalledInstances.AddRange(items.OfType<BlockCall>()
            .Select(call => call.InstanceName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal));

        return logic;
    }

    private static bool HasIncomingConnection(XElement element) =>
        element.Element(Tc6 + "connectionPointIn")?
            .Elements(Tc6 + "connection")
            .Any() ?? false;

    private static BlockCall ReadBlockCall(XElement element, string localId) => new()
    {
        LocalId = localId,
        TypeName = element.Attr("typeName") ?? string.Empty,
        InstanceName = element.Attr("instanceName"),
    };

    private static VariableAssignment ReadAssignment(XElement element, string localId) => new()
    {
        LocalId = localId,
        Target = element.Element(Tc6 + "expression")?.Value.Trim() ?? string.Empty,
    };

    private static LogicComment ReadComment(XElement element, string localId) => new()
    {
        LocalId = localId,
        Text = element.Element(Tc6 + "content")?.Value.Trim() ?? string.Empty,
    };

    /// <summary>
    /// Reads the formal parameters of a call and resolves every
    /// <c>&lt;connection refLocalId=... formalParameter=.../&gt;</c> into a signal source.
    /// A source that names another block becomes that block's output; anything else is the
    /// expression of the referenced variable item. Connections never cross sheets, so lookups use
    /// the sheet of the consuming item.
    /// </summary>
    private void ResolveCall(
        BlockCall call,
        XElement element,
        IReadOnlyDictionary<string, LogicItem> bySheetKey,
        IReadOnlyDictionary<string, LogicItem> byLocalId,
        StructuredLogic logic)
    {
        AddArguments(call.Inputs, element.Element(Tc6 + "inputVariables"), call.SheetNumber, bySheetKey, byLocalId, logic);
        AddArguments(call.Outputs, element.Element(Tc6 + "outputVariables"), call.SheetNumber, bySheetKey, byLocalId, logic);
        AddArguments(call.InOuts, element.Element(Tc6 + "inOutVariables"), call.SheetNumber, bySheetKey, byLocalId, logic);
    }

    private void AddArguments(
        List<CallArgument> target,
        XElement? container,
        int sheetNumber,
        IReadOnlyDictionary<string, LogicItem> bySheetKey,
        IReadOnlyDictionary<string, LogicItem> byLocalId,
        StructuredLogic logic)
    {
        if (container is null) return;

        foreach (var variable in container.Elements(Tc6 + "variable"))
        {
            var argument = new CallArgument { FormalParameter = variable.Attr("formalParameter") };

            // An input element drives the call through connectionPointIn; an output element carries
            // the consumer of the result in connectionPointOut.
            foreach (var connection in variable.Elements(Tc6 + "connectionPointIn")
                         .Elements(Tc6 + "connection")
                         .Concat(variable.Elements(Tc6 + "connectionPointOut")
                         .Elements(Tc6 + "connection")))
            {
                var source = ResolveSignal(
                    connection.Attr("refLocalId"),
                    connection.Attr("formalParameter"),
                    sheetNumber,
                    bySheetKey,
                    byLocalId,
                    logic);
                if (source is not null) argument.Sources.Add(source);
            }

            target.Add(argument);
        }
    }

    /// <summary>
    /// Resolves an assignment target. When the target is fed by another block the source
    /// becomes that block's output, otherwise the item stays a plain variable reference and
    /// the generator will treat it as a read.
    /// </summary>
    private void ResolveAssignment(
        VariableAssignment assignment,
        XElement element,
        IReadOnlyDictionary<string, LogicItem> bySheetKey,
        IReadOnlyDictionary<string, LogicItem> byLocalId,
        StructuredLogic logic)
    {
        foreach (var connection in element.Elements(Tc6 + "connectionPointIn")
                     .SelectMany(point => point.Elements(Tc6 + "connection")))
        {
            var refLocalId = connection.Attr("refLocalId");
            if (refLocalId is null) continue;

            var source = ResolveProducer(refLocalId, assignment.SheetNumber, bySheetKey, byLocalId, logic);
            if (source is null) continue;

            if (source is BlockCall)
            {
                assignment.RefLocalId = refLocalId;
                assignment.RefFormalParameter = connection.Attr("formalParameter");
                assignment.RefSheetNumber = source.SheetNumber;
                return;
            }

            // An outVariable driven by an inVariable copies that value; keep the expression so the
            // assignment can be emitted instead of being reported as dangling.
            var expression = source switch
            {
                VariableReference reference => reference.Expression,
                VariableAssignment other => other.Target,
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(expression))
            {
                assignment.Expression = expression;
                return;
            }
        }
    }

    /// <summary>
    /// Finds the item a connection points at. The sheet is preferred, but TriStation reuses localIds
    /// across sheets, so an item that would give no usable signal falls back to another sheet.
    /// </summary>
    private LogicItem? ResolveProducer(
        string refLocalId,
        int sheetNumber,
        IReadOnlyDictionary<string, LogicItem> bySheetKey,
        IReadOnlyDictionary<string, LogicItem> byLocalId,
        StructuredLogic logic)
    {
        var sameSheet = bySheetKey.GetValueOrDefault(Key(sheetNumber, refLocalId));
        if (IsUsableSource(sameSheet)) return sameSheet;

        var anySheet = byLocalId.GetValueOrDefault(refLocalId);
        if (IsUsableSource(anySheet))
        {
            logic.CrossSheetReferences.Add(
                $"sheet {sheetNumber} localId {refLocalId} resolved on sheet {anySheet!.SheetNumber}");
            return anySheet;
        }

        return sameSheet ?? anySheet;
    }

    private static bool IsUsableSource(LogicItem? item) => item switch
    {
        BlockCall => true,
        VariableReference reference => !string.IsNullOrWhiteSpace(reference.Expression),
        VariableAssignment assignment => !string.IsNullOrWhiteSpace(assignment.Target),
        _ => false,
    };

    private SignalSource? ResolveSignal(
        string? refLocalId,
        string? refFormalParameter,
        int sheetNumber,
        IReadOnlyDictionary<string, LogicItem> bySheetKey,
        IReadOnlyDictionary<string, LogicItem> byLocalId,
        StructuredLogic logic)
    {
        if (refLocalId is null) return null;

        var found = false;
        var item = ResolveProducer(refLocalId, sheetNumber, bySheetKey, byLocalId, logic);
        found = item is not null;

        if (found && item is not null)
        {
            if (item is BlockCall)
            {
                return SignalSource.FromBlockOutput(refLocalId, refFormalParameter, item.SheetNumber);
            }

            var expression = item switch
            {
                VariableReference reference => reference.Expression,
                VariableAssignment assignment => assignment.Target,
                _ => string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(expression))
            {
                return SignalSource.FromExpression(expression, item.SourceTypeName);
            }
        }

        // A localId we did not capture: keep the raw reference so nothing is silently lost.
        return SignalSource.FromExpression($"/* unresolved localId {refLocalId} on sheet {sheetNumber} */");
    }

    private static string Key(int sheetNumber, string localId) => $"{sheetNumber}:{localId}";

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static string Truncate(string xml, int maxLength = 2000) =>
        xml.Length <= maxLength ? xml : xml[..maxLength] + "...";

    // --------------------------------------------------------- configuration

    private void ReadConfigurations(XElement root)
    {
        // TriStation nests <LIBRARY_CONFIGURATION> inside the resource, so the libraries have to be
        // collected from this subtree rather than while streaming.
        foreach (var library in root.Descendants().Where(element => element.Name.LocalName == "LIBRARY"))
        {
            var name = library.Attr("Name");
            if (!string.IsNullOrWhiteSpace(name) && !_project.Libraries.Contains(name))
            {
                _project.Libraries.Add(name);
            }
        }

        foreach (var configuration in root.Elements(Tc6 + "configuration"))
        {
            ReadConfiguration(configuration);
        }

        // The controller sits next to the configuration, not inside it.
        var controller = root.Element(Ts + "CONTROLLER");
        if (controller is not null) ReadHardware(controller);
    }

    private void ReadConfiguration(XElement configuration)
    {
        var name = configuration.Attr("name");
        if (name is not null && _configurationNameFound) return;
        if (name is not null)
        {
            _project.Name = name;
            _configurationNameFound = true;
        }

        foreach (var child in configuration.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "resource":
                {
                    var task = child.Element(Tc6 + "task");
                    if (task is not null) _project.TaskIntervalMs = ParseInt(task.Attr("interval"));
                    ReadGlobalVariables(child.Element(Tc6 + "globalVars"));
                    break;
                }
                // TriStation writes the global variable list as a sibling of <resource>.
                case "globalVars":
                    ReadGlobalVariables(child);
                    break;
            }
        }
    }

    private bool _configurationNameFound;

    private void ReadGlobalVariables(XElement? globalVars)
    {
        if (globalVars is null) return;

        foreach (var element in globalVars.Elements(Tc6 + "variable"))
        {
            var variable = ReadVariable(element, VariableDirection.Global, constant: false);
            if (variable is null) continue;
            if (!_globalNames.Add(variable.Name)) continue;

            var metadata = ReadTagMetadata(element.Element(Ts + "TAGNAME"));

            var global = new GlobalVariable
            {
                Name = variable.Name,
                TypeName = variable.TypeName,
                InitialValue = variable.InitialValue,
                Comment = variable.Documentation,
                SourceLine = variable.SourceLine,
                Address = variable.Address,
                Metadata = metadata,
            };

            _project.GlobalVariables.Add(global);
        }
    }

    private static TagMetadata? ReadTagMetadata(XElement? tagName)
    {
        if (tagName is null) return null;

        var metadata = new TagMetadata();

        var declaration = tagName.Element(Ts + "TAG_DECLARATION");
        if (declaration is not null)
        {
            metadata.Application = declaration.Attr("Application");
            metadata.Group1 = declaration.Attr("Group1");
            metadata.Group2 = declaration.Attr("Group2");
            metadata.SharedForRead = string.Equals(declaration.Attr("SharedForRead"), "Yes", StringComparison.OrdinalIgnoreCase);
            metadata.EnableMultipleWrite = string.Equals(declaration.Attr("EnableMultipleWrite"), "Yes", StringComparison.OrdinalIgnoreCase);
        }

        var assignment = tagName.Element(Ts + "POINT_ASSIGNMENT");
        if (assignment is not null)
        {
            metadata.PointType = assignment.Attr("PointType");
            metadata.AliasType = assignment.Attr("AliasType");
            metadata.TagClass = assignment.Attr("TagClass");
            metadata.PhysicalAddress = assignment.Attr("PhysicalAddress");
            metadata.AliasCategory = assignment.Attr("AliasCategory");
            metadata.AliasNumber = ParseNullableInt(assignment.Attr("AliasNumber"));
            metadata.AliasNumberExt = ParseNullableInt(assignment.Attr("AliasNumberExt"));
            metadata.BinNumber = ParseNullableInt(assignment.Attr("BinNumber"));
            metadata.BinOffset = ParseNullableInt(assignment.Attr("BinOffset"));
        }

        var scaling = tagName.Element(Ts + "TAG_SCALING");
        if (scaling is not null)
        {
            metadata.MinSpan = ParseNullableDouble(scaling.Attr("MinSpan"));
            metadata.MaxSpan = ParseNullableDouble(scaling.Attr("MaxSpan"));
            metadata.DisplayPrecision = ParseNullableInt(scaling.Attr("DisplayPrecision"));
            metadata.DisableScaling = string.Equals(scaling.Attr("DisableScaling"), "Yes", StringComparison.OrdinalIgnoreCase);
        }

        var display = tagName.Element(Ts + "TAG_DISPLAY");
        if (display is not null)
        {
            metadata.TrueStateName = display.Attr("TrueStateName");
            metadata.FalseStateName = display.Attr("FalseStateName");
            metadata.TrueStateColor = display.Attr("TrueStateColor");
            metadata.FalseStateColor = display.Attr("FalseStateColor");
        }

        var location = tagName.Element(Ts + "TAG_LOCATION");
        if (location is not null)
        {
            metadata.AreaName = location.Attr("AreaName");
            metadata.Equipment = location.Attr("Equipment");
            metadata.SifName = location.Attr("SIFName");
        }

        var ioModule = tagName.Element(Ts + "IO_MODULE_PARAMS");
        if (ioModule is not null)
        {
            metadata.IoModuleType = ioModule.Attr("IoModuleType");
            metadata.PointOption = ioModule.Attr("PointOpt");

            var analogInput = ioModule.Element(Ts + "AI_PARAMS");
            if (analogInput is not null)
            {
                metadata.MinRawValue = ParseNullableDouble(analogInput.Attr("MinRawValue"));
                metadata.MaxRawValue = ParseNullableDouble(analogInput.Attr("MaxRawValue"));
                metadata.MinEngValue = ParseNullableDouble(analogInput.Attr("MinEngValue"));
                metadata.MaxEngValue = ParseNullableDouble(analogInput.Attr("MaxEngValue"));
                metadata.FieldFaultLowLimit = ParseNullableDouble(analogInput.Attr("FldFaultLoLimit"));
                metadata.FieldFaultHighLimit = ParseNullableDouble(analogInput.Attr("FldFaultHiLimit"));
                metadata.FilterType = analogInput.Attr("FilterType");
            }

            var digitalInput = ioModule.Element(Ts + "SDI_PARAMS");
            if (digitalInput is not null)
            {
                metadata.TriggerState = digitalInput.Attr("TriggerState");
                metadata.MinimumTriggerTime = ParseNullableInt(digitalInput.Attr("MinimumTriggerTime"));
            }
        }

        metadata.SoeBlockAssignment = tagName.Element(Ts + "SOE_BLOCKS_ASSIGNMENT")?.Value.Trim();

        return metadata.HasAny ? metadata : null;
    }

    private void ReadHardware(XElement controller)
    {
        var hardware = _project.Hardware;

        var ppd = controller.Element(Ts + "PPD_CONFIGURATION");
        if (ppd is not null)
        {
            hardware.ControllerName = ppd.Attr("Name");
            hardware.MajorVersion = ParseNullableInt(ppd.Attr("MajorVersion"));
            hardware.MinorVersion = ParseNullableInt(ppd.Attr("MinorVersion"));
            hardware.MainProcessorId = ParseNullableInt(ppd.Attr("MainProcessorId"));
        }

        var communication = ppd?.Element(Ts + "COMMUNICATION") ?? controller.Element(Ts + "COMMUNICATION") ?? controller;
        hardware.NodeName = Value(communication, Ts + "NODENAME");
        hardware.NodeNumber = ParseNullableInt(Value(communication, Ts + "NODENUMBER"));
        hardware.UdpPort = ParseNullableInt(Value(communication, Ts + "UDPPORT"));

        var connection = communication.Element(Ts + "CONNECTION_PARAMETERS");
        if (connection is not null)
        {
            hardware.IpAddress = Value(connection, Ts + "IP_ADDRESS");
            hardware.ConnectionType = Value(connection, Ts + "DEFAULT_CONNECTION_TYPE");
        }

        var allocation = ppd?.Element(Ts + "HARDWARE_ALLOCATION") ?? controller.Element(Ts + "HARDWARE_ALLOCATION");
        if (allocation is null) return;

        foreach (var chassisElement in allocation.Descendants()
                     .Where(e => e.Name == Ts + "MAIN_CHASSIS" || e.Name == Ts + "EXP_CHASSIS"))
        {
            var chassis = new ChassisInfo
            {
                IsMainChassis = chassisElement.Name == Ts + "MAIN_CHASSIS",
                Type = chassisElement.Attr("Type"),
                Description = chassisElement.Attr("Description"),
                ChassisNumber = ParseNullableInt(chassisElement.Attr("ChassisNumber") ?? chassisElement.Attr("Id")),
            };

            foreach (var module in chassisElement.Elements(Ts + "MODULE"))
            {
                var info = new ModuleInfo
                {
                    Id = ParseNullableInt(module.Attr("ID")),
                    SlotAddress = ParseNullableInt(Value(module, Ts + "SLOT_ADDRESS")),
                };

                // TriStation identifies modules by a numeric catalog id; the order number is not
                // stored in the export, so the mapping to a Siemens article stays a manual step.
                foreach (var attribute in module.Descendants(Ts + "HardwareModuleAttribute"))
                {
                    var attributeName = attribute.Attr("Name");
                    var value = attribute.Attr("Value");
                    if (attributeName is not null && value is not null) info.Attributes[attributeName] = value;
                }

                chassis.Modules.Add(info);
            }

            hardware.Chassis.Add(chassis);
        }
    }

    private static string? Value(XElement parent, XName name) => parent.Element(name)?.Value.Trim();

    // --------------------------------------------------------------- finalise

    private void Finalise()
    {
        var calls = _project.Blocks
            .SelectMany(block => block.Logic is StructuredLogic logic
                ? logic.Items.OfType<BlockCall>().Select(call => call.TypeName)
                : Enumerable.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        _project.LibraryCalls.AddRange(calls);

        if (string.IsNullOrWhiteSpace(_project.Name))
        {
            var description = _project.Header.ContentDescription;
            var firstLine = description?.Split('\n', '\r').FirstOrDefault()?.Trim();
            _project.Name = !string.IsNullOrWhiteSpace(firstLine)
                ? firstLine
                : Path.GetFileNameWithoutExtension(_project.SourceFile) is { Length: > 0 } stem
                    ? stem
                    : "TriconexProject";
        }
    }

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static double? ParseNullableDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
}
