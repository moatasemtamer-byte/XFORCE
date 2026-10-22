using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;
using TriconexToTia.Generators;
using TriconexToTia.Mapper;
using TriconexToTia.Parser;

namespace TriconexToTia.Engine;

/// <summary>
/// The five-stage pipeline: parse, validate, map, generate, report. Shared by
/// the command-line tool and the HTTP engine.
/// </summary>
public sealed class ConversionEngine
{
    public const string Version = "1.0.0";

    public ConversionResult Run(Stream xml, ConverterOptions options, string sourceFile = "")
    {
        PlcProject project;
        try
        {
            var parser = new PlcOpenXmlParser();
            project = parser.Parse(xml);
            if (!string.IsNullOrWhiteSpace(sourceFile))
            {
                project.SourceFile = sourceFile;
            }
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidDataException or FormatException)
        {
            return new ConversionResult
            {
                Success = false,
                FatalError = $"Failed to parse the source XML: {exception.Message}",
            };
        }

        // A TriStation EXTERN POU whose body declares a TYPE is a UDT, not a block (the San Martin
        // TR_POINT_INFO struct). It must become a data type so references to it resolve, and the
        // placeholder block must not be emitted.
        ExtractTypeDeclarations(project);

        var blockRenames = NormalizeBlockNames(project);

        Mappings mappings;
        try
        {
            mappings = MappingLoader.Load(options.MappingsPath);
        }
        catch (Exception exception)
        {
            return new ConversionResult
            {
                Success = false,
                Project = project,
                FatalError = $"Could not load mappings: {exception.Message}",
            };
        }

        var validation = new ProjectValidator(mappings).Validate(project);

        foreach (var rename in blockRenames)
        {
            validation.Add(ValidationIssue.Warning(
                $"Duplicate block name \"{rename.Original}\": Siemens block names are case-insensitive, "
                + $"so the block was renamed to \"{rename.Renamed}\" to keep both definitions.",
                rename.SourceLine,
                rename.Renamed));
        }

        if (validation.HasFatal)
        {
            return new ConversionResult
            {
                Success = false,
                Project = project,
                Validation = validation,
                FatalError = "Validation failed with fatal errors.",
                ValidateOnly = options.ValidateOnly,
            };
        }

        if (options.ValidateOnly)
        {
            return new ConversionResult
            {
                Success = true,
                Project = project,
                Validation = validation,
                ValidateOnly = true,
            };
        }

        var mappingResult = new MappingResult();
        Map(project, mappings, mappingResult, validation, blockRenames);

        var context = new GenerationContext
        {
            Project = project,
            Validation = validation,
            Mapping = mappingResult,
            Options = options,
        };

        IGenerator[] generators =
        {
            new FbdSimaticMlGenerator(),
            new SclGenerator(),
            new UdtSourceGenerator(),
            new TagTableXmlGenerator(),
            new GlobalDbGenerator(),
            new SupportBlockGenerator(),
            new ImportOrderGenerator(),
            new ImportScriptGenerator(),
        };

        foreach (var generator in generators)
        {
            foreach (var file in generator.Generate(context))
            {
                context.Generated.Add(file);
            }
        }

        // Manual actions raised while generating (FBD structural notes, declaration mismatches)
        // belong to the same report as the ones the mapping stage raised, so they are merged into
        // the mapping result before the report is built.
        foreach (var action in context.ManualActions)
        {
            mappingResult.AddManualAction(action);
        }

        // The report is generated last so it can list every file above.
        foreach (var file in new ReportGenerator().Generate(context))
        {
            context.Generated.Add(file);
        }

        // The machine-readable twin is generated after the SCL files so its known_issues
        // can be measured from the actual generated output.
        foreach (var file in new ReportJsonGenerator().Generate(context))
        {
            context.Generated.Add(file);
        }

        return new ConversionResult
        {
            Success = true,
            Project = project,
            Validation = validation,
            Mapping = mappingResult,
            Files = context.Generated,
        };
    }

    private static void Map(PlcProject project, Mappings mappings, MappingResult result, ValidationResult validation,
        IReadOnlyList<BlockRename> blockRenames)
    {
        foreach (var rename in blockRenames)
        {
            result.Record(new MappingRecord
            {
                Category = "BlockName",
                Source = rename.Original,
                Target = rename.Renamed,
                Status = ConversionStatus.Approximated,
                Note = "Siemens block names are case-insensitive; the block was renamed so both definitions can coexist.",
                SourceLine = rename.SourceLine,
            });
        }

        var typeMapper = new TypeMapper(mappings);
        var blockTypeMapper = new BlockTypeMapper(mappings);
        var libraryMapper = new LibraryFunctionMapper(mappings);
        var userTypes = project.DataTypes
            .Select(type => type.Name)
            .Concat(project.Blocks.Select(block => block.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A project that ships its own copy of a library function the mapping rule inlines (the San
        // Martin SCALE function) would emit a duplicate definition that TIA rejects, because the name
        // collides with the SCALE instruction. The calls are inlined by the rule, so the block is
        // dropped; the name stays in userTypes above so the call site still resolves to the rule.
        DropInlinedDuplicateBlocks(project, mappings);

        // The FBD pin type of every callee formal. A TriStation diagram can wire the same signal
        // with different types in different programs, so the type the pin expects is needed to
        // convert the wired value into the type the project-wide tag actually declares.
        var formalTypes = BuildFormalTypes(project);

        foreach (var block in project.Blocks)
        {
            var mappedType = blockTypeMapper.Map(block.SourceElementName);
            result.Record(new MappingRecord
            {
                Category = "BlockType",
                Source = block.SourceElementName,
                Target = blockTypeMapper.SiemensName(mappedType),
                Status = ConversionStatus.Converted,
                SourceLine = block.SourceLine,
            });

            // A TriStation ST block keeps its VAR declarations inside the CDATA body instead of the
            // XML interface, so those variables are absent from block.Variables at this point. Fold
            // them in now so their types are mapped with everything else; SclGenerator dedupes them
            // again when it emits, so the mapped type is what reaches Siemens.
            FoldTextualDeclarations(block);

            foreach (var variable in block.Variables)
            {
                MapType(typeMapper, userTypes, result, variable.TypeName, $"{block.Name}.{variable.Name}",
                    variable.SourceLine, mapped =>
                    {
                        variable.MappedTypeName = mapped.Mapped;
                        variable.TypeStatus = mapped.Status;
                        variable.MappingNote = mapped.Note;
                    });
            }

            if (block is Function function && !string.IsNullOrWhiteSpace(function.ReturnType))
            {
                MapType(typeMapper, userTypes, result, function.ReturnType, $"{block.Name} (return)",
                    block.SourceLine, mapped =>
                    {
                        block.MappedReturnType = mapped.Mapped;
                        block.ReturnTypeStatus = mapped.Status;
                    });
            }

            libraryMapper.Scan(block.Code, block.Name, block.SourceLine, result);

            // FBD calls arrive as BlockCall items, not as text, so the code scanner above never
            // sees them. Every call whose type is a TriStation library block gets the same rule
            // applied here: Siemens name, kind, parameter renames and conversion, plus a manual
            // action when the rule says there is no Siemens equivalent.
            if (block.Logic is StructuredLogic logic)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var call in logic.Items.OfType<BlockCall>())
                {
                    MapCall(call, block.Name, mappings, result, block.SourceLine, seen.Add(call.TypeName), userTypes);
                    ApplyFormalTypes(call, formalTypes);
                }
            }
            else if (block.Logic is TextualLogic textual && !string.IsNullOrWhiteSpace(textual.Code))
            {
                // A structured (FBD) call goes through MapCall above; a call written as ST text does
                // not, so a rule that replaces the whole call with an inline expression (e.g.
                // TIME_TO_SECS_REAL) has to be substituted in the text here. The block's own
                // definition is unaffected because it does not call itself.
                var rewritten = RewriteInlineCalls(textual.Code, mappings, projectNames: userTypes);
                if (!ReferenceEquals(rewritten, textual.Code) && rewritten != textual.Code)
                {
                    block.Code = rewritten;
                    block.Logic = new TextualLogic(textual.Language, rewritten);
                }
            }
        }

        foreach (var type in project.DataTypes)
        {
            foreach (var member in type.Members)
            {
                MapType(typeMapper, userTypes, result, member.TypeName, $"{type.Name}.{member.Name}",
                    member.SourceLine, mapped =>
                    {
                        member.MappedTypeName = mapped.Mapped;
                        member.TypeStatus = mapped.Status;
                        member.MappingNote = mapped.Note;
                    });
            }
        }

        foreach (var variable in project.GlobalVariables)
        {
            var sourceType = string.IsNullOrWhiteSpace(variable.TypeName) ? "BOOL" : variable.TypeName;
            MapType(typeMapper, userTypes, result, sourceType, variable.Name, variable.SourceLine, mapped =>
            {
                variable.MappedTypeName = mapped.Mapped;
            });
        }

        foreach (var point in project.IoPoints)
        {
            var sourceType = string.IsNullOrWhiteSpace(point.TypeName)
                ? (point.Signal == IoSignal.Analog ? "REAL" : "BOOL")
                : point.TypeName;
            MapType(typeMapper, userTypes, result, sourceType, point.Name, point.SourceLine, mapped =>
            {
                point.MappedTypeName = mapped.Mapped;
            });
        }

        new AddressMapper().Resolve(project, result, validation);
        new TriconexAddressTranslator().Apply(project, result, validation);
    }

    /// <summary>
    /// Siemens block names are case-insensitive, so a TriStation project that keeps both
    /// <c>TEMPLATES</c> and <c>Templates</c> cannot import both blocks; TIA silently replaces the
    /// first with the second. Each case-insensitive duplicate gets a deterministic <c>_1</c>,
    /// <c>_2</c>, ... suffix and the calls to it are rewritten, so no block is lost.
    /// <para>
    /// Only structured (FBD/LD) calls can be rewritten. A duplicate reachable from SCL text is left
    /// alone and still reported by the validator, because its textual reference cannot be patched
    /// safely. Programs (OBs) are never called, so they always rename.
    /// </para>
    /// </summary>
    /// <summary>
    /// Adds the declarations embedded in a TriStation text block's body to its variable list so that
    /// the type-mapping pass sees them. Blocks whose interface is already populated are untouched.
    /// </summary>
    private static void FoldTextualDeclarations(PlcBlock block)
    {
        if (block.Logic is not TextualLogic textual || string.IsNullOrWhiteSpace(textual.Code)) return;

        var unwrapped = SclCodeTransformer.UnwrapBlock(textual.Code);
        if (unwrapped is null || unwrapped.Declarations.Count == 0) return;

        var known = new HashSet<string>(
            block.Variables.Select(variable => variable.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var declaration in unwrapped.Declarations)
        {
            if (!known.Add(declaration.Name)) continue;
            block.Variables.Add(new Variable
            {
                Name = declaration.Name,
                TypeName = declaration.Type,
                Direction = declaration.Direction,
            });
        }
    }

    /// <summary>
    /// Moves a <c>TYPE ... STRUCT ... END_TYPE</c> declaration out of a TriStation POU body into the
    /// project's data types and removes the placeholder POU. The export sometimes ships a UDT as a
    /// function block whose whole body is the type declaration (TR_POINT_INFO); TIA rejects the
    /// block but needs the type.
    /// </summary>
    private static void ExtractTypeDeclarations(PlcProject project)
    {
        var typePattern = new Regex(
            @"\bTYPE\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?:STRUCT)?(?<body>.*?)\bEND_STRUCT\s*;?\s*(?:END_TYPE)?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var memberPattern = new Regex(
            @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        project.Blocks.RemoveAll(block =>
        {
            if (block.Logic is not TextualLogic textual || string.IsNullOrWhiteSpace(textual.Code))
            {
                return false;
            }

            var match = typePattern.Match(textual.Code);
            if (!match.Success) return false;

            var name = match.Groups["name"].Value;
            if (project.DataTypes.Any(type => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var structType = new StructType { Name = name, SourceLine = block.SourceLine };
            foreach (Match member in memberPattern.Matches(match.Groups["body"].Value))
            {
                structType.Members.Add(new Variable
                {
                    Name = member.Groups["name"].Value,
                    TypeName = member.Groups["type"].Value,
                    Direction = VariableDirection.Local,
                });
            }

            project.DataTypes.Add(structType);
            return true;
        });
    }

    private static List<BlockRename> NormalizeBlockNames(PlcProject project)
    {
        var renames = new List<BlockRename>();
        if (project.Blocks.Count < 2) return renames;

        var reserved = project.Blocks
            .Select(block => block.Name.Trim())
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var textualCode = project.Blocks
            .Select(block => block.Logic)
            .OfType<TextualLogic>()
            .Select(logic => logic.Code)
            .ToList();

        // A TriStation EXTERN POU is a stand-in for a library function. The export names every one
        // of them "EXTERN" and keeps the name the caller uses only in the code header
        // ("* FUNCTION:        ReportBadParam"), so suffixing them EXTERN_1..N leaves every SCL
        // call site referring to a function that does not exist. Rename each stand-in to the name
        // from its header instead.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preferred = new Dictionary<PlcBlock, string>();
        foreach (var block in project.Blocks)
        {
            if (!TryLibraryFunctionName(block, out var libraryName)) continue;
            if (reserved.Contains(libraryName) || !claimed.Add(libraryName)) continue;
            preferred[block] = libraryName;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in project.Blocks)
        {
            var name = block.Name.Trim();
            if (name.Length == 0) continue;

            if (preferred.TryGetValue(block, out var libraryName))
            {
                renames.Add(new BlockRename(name, libraryName, block.SourceLine));
                block.Name = libraryName;
                used.Add(libraryName);
                continue;
            }

            if (used.Add(name)) continue;

            if (textualCode.Any(code => ContainsWord(code, name)))
            {
                // Referenced from SCL text; renaming would break the reference, so leave it.
                continue;
            }

            var candidate = name;
            var suffix = 1;
            do
            {
                candidate = $"{name}_{suffix}";
                suffix++;
            } while (reserved.Contains(candidate) || !used.Add(candidate));

            renames.Add(new BlockRename(block.Name, candidate, block.SourceLine));
            block.Name = candidate;
        }

        if (renames.Count == 0) return renames;

        // Several stand-ins share the original name "EXTERN", so an original name that was renamed
        // more than once cannot be mapped back to a single target; those calls stay as they are.
        var lookup = renames
            .GroupBy(rename => rename.Original, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Renamed, StringComparer.Ordinal);

        foreach (var block in project.Blocks)
        {
            if (block.Logic is not StructuredLogic logic) continue;
            foreach (var call in logic.Items.OfType<BlockCall>())
            {
                if (lookup.TryGetValue(call.TypeName, out var renamed)) call.TypeName = renamed;
            }
        }

        return renames;
    }

    /// <summary>
    /// Reads the real library function name out of the doc header of an EXTERN stand-in, e.g.
    /// <c>* FUNCTION:        ReportBadParam</c>. Only accepted when the name is a plain identifier,
    /// so nothing is renamed to something that cannot be a Siemens block name.
    /// </summary>
    private static bool TryLibraryFunctionName(PlcBlock block, out string libraryName)
    {
        libraryName = string.Empty;
        if (string.IsNullOrWhiteSpace(block.Code)) return false;

        var match = LibraryFunctionHeader().Match(block.Code);
        if (!match.Success) return false;

        var name = match.Groups["name"].Value;
        libraryName = name;
        return true;
    }

    private static Regex LibraryFunctionHeader() => new(
        @"(?m)^\s*\*?\s*FUNCTION:\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static bool ContainsWord(string text, string word)
    {
        var index = 0;
        while (true)
        {
            index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            var beforeOk = index == 0 || !IsWordChar(text[index - 1]);
            var end = index + word.Length;
            var afterOk = end >= text.Length || !IsWordChar(text[end]);
            if (beforeOk && afterOk) return true;
            index += word.Length;
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private sealed record BlockRename(string Original, string Renamed, int SourceLine);

    private static void MapType(
        TypeMapper typeMapper,
        IReadOnlyCollection<string> userTypes,
        MappingResult result,
        string sourceType,
        string label,
        int sourceLine,
        Action<TypeMappingResult> apply)
    {
        var mapped = typeMapper.Map(sourceType, userTypes);
        apply(mapped);
        if (mapped.Status != ConversionStatus.Converted)
        {
            result.Record(new MappingRecord
            {
                Category = "Type",
                Source = $"{label} : {sourceType}",
                Target = mapped.Mapped,
                Status = mapped.Status,
                Note = mapped.Note,
                SourceLine = sourceLine,
            });
        }
    }

    private sealed record FormalTypes(
        IReadOnlyDictionary<string, string> Inputs,
        IReadOnlyDictionary<string, string> Outputs);

    /// <summary>
    /// Collects the input/output formal types of every callable block: the project's own FBs/FCs and
    /// the generated stand-in blocks, keyed by their TriStation type name.
    /// </summary>
    private static Dictionary<string, FormalTypes> BuildFormalTypes(PlcProject project)
    {
        var map = new Dictionary<string, FormalTypes>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in project.Blocks)
        {
            var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var variable in block.Variables)
            {
                switch (variable.Direction)
                {
                    case VariableDirection.Input:
                    case VariableDirection.InOut:
                        inputs[variable.Name] = variable.EffectiveTypeName;
                        break;
                    case VariableDirection.Output:
                        outputs[variable.Name] = variable.EffectiveTypeName;
                        break;
                }
            }

            map[block.Name] = new FormalTypes(inputs, outputs);
        }

        foreach (var name in new[] { "UNPACK16", "UNPACK32", "SR", "RS" })
        {
            if (SupportBlockGenerator.TryGetFormalTypes(name, out var inputs, out var outputs))
            {
                map[name] = new FormalTypes(inputs, outputs);
            }
        }

        return map;
    }

    /// <summary>
    /// Stamps each call argument with the type its pin declares, so the emitter can convert a wired
    /// signal whose declared type differs from the pin.
    /// </summary>
    private static void ApplyFormalTypes(BlockCall call, Dictionary<string, FormalTypes> formalTypes)
    {
        if (!formalTypes.TryGetValue(call.TypeName, out var types)) return;

        foreach (var argument in call.Inputs.Concat(call.InOuts))
        {
            if (argument.FormalParameter is null) continue;
            if (types.Inputs.TryGetValue(argument.FormalParameter, out var type)) argument.ExpectedType = type;
        }

        foreach (var output in call.Outputs)
        {
            if (output.FormalParameter is null) continue;
            if (types.Outputs.TryGetValue(output.FormalParameter, out var type)) output.ExpectedType = type;
        }
    }

    /// <summary>
    /// Removes a project function that re-declares a library function the mapping rule expands
    /// inline, when nothing refers to it as a block. The rule rewrites every FBD call, so the
    /// project copy is redundant and would otherwise collide with a Siemens instruction of the same
    /// name (SCALE). A block still named by SCL text is kept, because the text path cannot inline
    /// it and dropping it would break the reference.
    /// </summary>
    private static void DropInlinedDuplicateBlocks(PlcProject project, Mappings mappings)
    {
        var inlineNames = mappings.LibraryFunctions
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Inline))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (inlineNames.Count == 0) return;

        project.Blocks.RemoveAll(block =>
            block.BlockType == BlockType.FC
            && inlineNames.Contains(block.Name)
            && !project.Blocks.Any(other =>
                !ReferenceEquals(other, block)
                && other.Logic is TextualLogic otherText
                && ContainsWord(StripComments(otherText.Code), block.Name)));
    }

    /// <summary>
    /// Removes SCL comments (<c>(* *)</c> and <c>//</c>) so a name mentioned only in prose is not
    /// mistaken for a reference to a block.
    /// </summary>
    private static string StripComments(string code)
    {
        var stripped = Regex.Replace(code, @"\(\*.*?\*\)", " ", RegexOptions.Singleline);
        return Regex.Replace(stripped, @"//[^\r\n]*", " ");
    }

    /// <summary>
    /// Applies one library-function rule to a FBD call: the Siemens name the emitter must emit,
    /// whether the call is a stateless function (inlined) or an instance, the formal renames and
    /// value conversions the rule carries, and — for a rule with no Siemens equivalent — a manual
    /// action that survives into the report. <paramref name="record"/> is false for later calls of
    /// the same type in the same block, so the report lists each name once, as the text scanner
    /// does for structured text.
    /// </summary>
    private static void MapCall(
        BlockCall call,
        string blockName,
        Mappings mappings,
        MappingResult result,
        int sourceLine,
        bool record,
        IReadOnlyCollection<string> projectNames)
    {
        // A project that defines its own block under a library name (San Martin ships RS and SR as
        // real TriStation function blocks) calls that block, so the library rule must not rename
        // the call or its instance type to the stand-in. An inline function rule is the exception:
        // the project's SCALE function is the same function the rule expands, so its calls are
        // inlined and the project's duplicate definition is dropped rather than emitted.
        if (projectNames.Contains(call.TypeName))
        {
            if (!mappings.LibraryFunctions.TryGetValue(call.TypeName, out var named)
                || string.IsNullOrWhiteSpace(named.Inline))
            {
                return;
            }
        }

        if (!mappings.LibraryFunctions.TryGetValue(call.TypeName, out var rule)) return;

        var status = Mappings.ParseStatus(rule.Status);
        call.TypeStatus = status;
        call.MappingNote = rule.Note;
        call.IsFunction = string.Equals(rule.Kind, "function", StringComparison.OrdinalIgnoreCase);
        call.InlineExpression = rule.Inline;
        call.Operator = rule.Operator;
        call.ResultType = rule.ResultType;
        if (!string.IsNullOrWhiteSpace(rule.InputType))
        {
            foreach (var argument in call.Inputs.Concat(call.InOuts))
            {
                argument.ExpectedType ??= rule.InputType;
            }
        }

        // An empty Siemens name (PACK16 and friends are replaced by an inline expression, TR_PID
        // has no equivalent) leaves the call unmapped so the emitter falls back to the source name.
        if (!string.IsNullOrWhiteSpace(rule.Siemens))
        {
            call.MappedTypeName = rule.Siemens;
        }

        if (rule.Parameters is not null)
        {
            foreach (var (formal, parameter) in rule.Parameters)
            {
                foreach (var argument in call.Inputs.Concat(call.InOuts))
                {
                    if (!string.Equals(argument.FormalParameter, formal, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(parameter.Name))
                    {
                        argument.MappedFormalParameter = parameter.Name;
                    }

                    if (!string.IsNullOrWhiteSpace(parameter.Convert))
                    {
                        argument.ValueConversion = parameter.Convert;
                    }

                    if (!string.IsNullOrWhiteSpace(parameter.Type))
                    {
                        argument.ExpectedType = parameter.Type;
                    }
                }

                // Output formals are read by the consumers through SignalSource.RefFormalParameter,
                // so the rename lives on the call itself rather than on the output argument.
                foreach (var output in call.Outputs)
                {
                    if (!string.Equals(output.FormalParameter, formal, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(parameter.Name))
                    {
                        call.MappedOutputFormals[formal] = parameter.Name;
                    }

                    if (!string.IsNullOrWhiteSpace(parameter.Type))
                    {
                        output.ExpectedType = parameter.Type;
                    }
                }
            }
        }

        // The infix logic operators take Boolean operands, so a wired signal of another type is
        // converted into Bool at the call site.
        if (!string.IsNullOrWhiteSpace(call.Operator))
        {
            foreach (var argument in call.Inputs.Concat(call.InOuts))
            {
                argument.ExpectedType ??= "Bool";
            }
        }

        if (!record) return;

        result.Record(new MappingRecord
        {
            Category = "LibraryFunction",
            Source = call.TypeName,
            Target = rule.Siemens,
            Status = status,
            Note = rule.Note,
            SourceLine = sourceLine,
        });

        if (status == ConversionStatus.Manual)
        {
            result.AddManualAction(new ManualAction
            {
                Title = $"{call.TypeName} in \"{blockName}\"",
                Reason = rule.Note ?? "No Siemens equivalent.",
                Action = rule.Action ?? "Replace with the equivalent Siemens instruction and re-test.",
                SourceLine = sourceLine,
            });
        }
    }

    /// <summary>
    /// Replaces calls written as ST text with the inline expression of their library rule, e.g.
    /// <c>TIME_TO_SECS_REAL(#DELTAT)</c> becomes <c>DINT_TO_REAL(TIME_TO_DINT(#DELTAT)) / 1000.0</c>.
    /// The FBD path does this through the emitter; text the export already supplies as ST would
    /// otherwise keep a call Siemens does not know. Only single-argument inline rules are expanded,
    /// which is all the current rules with an inline expression but a placeholder-free variable list
    /// (the multi-argument PACK16/SCALE rules are only reached from FBD). A name the project defines
    /// as its own block is left untouched.
    /// </summary>
    private static string RewriteInlineCalls(
        string code,
        Mappings mappings,
        IReadOnlyCollection<string> projectNames)
    {
        if (string.IsNullOrEmpty(code)) return code;

        return InlineCallPattern().Replace(code, match =>
        {
            var name = match.Groups["name"].Value;
            if (projectNames.Contains(name)) return match.Value;
            if (!mappings.LibraryFunctions.TryGetValue(name, out var rule)) return match.Value;
            if (string.IsNullOrWhiteSpace(rule.Inline)) return match.Value;
            if (match.Groups["args"].Value.Contains('(', StringComparison.Ordinal)) return match.Value;

            var inline = rule.Inline!;
            var arguments = match.Groups["args"].Value.Split(',');
            for (var i = 0; i < arguments.Length; i++)
            {
                var placeholder = i == 0 ? "IN1" : "IN" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                inline = inline.Replace("{" + placeholder + "}", arguments[i].Trim(), StringComparison.Ordinal);
            }

            // A rule whose placeholders are not all positional (INPUT, MIN1, ...) is addressed by
            // name, which a text call does not carry; leave it for the FBD path.
            if (inline.Contains('{', StringComparison.Ordinal)) return match.Value;
            return inline;
        });
    }

    private static Regex InlineCallPattern() => new(
        @"(?<![\w.""#])(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>[^()]*)\)",
        RegexOptions.Compiled);
}

