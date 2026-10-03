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
            var parser = new TriconexXmlParser();
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
        Map(project, mappings, mappingResult, validation);

        var context = new GenerationContext
        {
            Project = project,
            Validation = validation,
            Mapping = mappingResult,
            Options = options,
        };

        IGenerator[] generators =
        {
            new SclGenerator(),
            new UdtXmlGenerator(),
            new TagTableXmlGenerator(),
            new ImportScriptGenerator(),
        };

        foreach (var generator in generators)
        {
            foreach (var file in generator.Generate(context))
            {
                context.Generated.Add(file);
            }
        }

        // The report is generated last so it can list every file above.
        foreach (var file in new ReportGenerator().Generate(context))
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

    private static void Map(PlcProject project, Mappings mappings, MappingResult result, ValidationResult validation)
    {
        var typeMapper = new TypeMapper(mappings);
        var blockTypeMapper = new BlockTypeMapper(mappings);
        var libraryMapper = new LibraryFunctionMapper(mappings);
        var userTypes = project.DataTypes
            .Select(type => type.Name)
            .Concat(project.Blocks.Select(block => block.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
    }

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
}
