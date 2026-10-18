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
                    MapCall(call, block.Name, mappings, result, block.SourceLine, seen.Add(call.TypeName));
                    ApplyFormalTypes(call, formalTypes);
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

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in project.Blocks)
        {
            var name = block.Name.Trim();
            if (name.Length == 0) continue;
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

        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rename in renames) lookup[rename.Original] = rename.Renamed;

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
        bool record)
    {
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
}

