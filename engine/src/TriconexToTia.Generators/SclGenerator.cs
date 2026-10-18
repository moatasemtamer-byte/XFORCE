using System.Text;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>
/// Emits one .scl file per block (FB, FC and OB). An organization block cannot declare block
/// instances — TIA rejects an instance data type in an OB in any section — so when an OB needs one
/// its declarations and logic move into a generated wrapper function block and the OB keeps a
/// single call through the wrapper's instance DB. Those DBs are emitted as SimaticML
/// (<c>SW.Blocks.InstanceDB</c>) and imported as XML because neither an OF-DB external source nor
/// the Openness <c>CreateInstanceDB</c> API (ProDiag only in V16) can create an ordinary instance DB.
/// The import order is published separately by <see cref="ImportOrderGenerator"/>.
/// </summary>
public sealed class SclGenerator : IGenerator
{
    /// <summary>Siemens data types that are themselves blocks and therefore need an instance.</summary>
    private static readonly HashSet<string> InstanceDataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TON", "TOF", "TP", "R_TRIG", "F_TRIG", "CTU", "CTD", "CTUD",
    };

    public string Id => "scl";

    public string Description => "Structured Control Language block files";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        // Windows and Siemens both treat names case-insensitively, so "TEMPLATES" and "Templates"
        // would collide on disk and on import. Block names are reserved so a file name and a block
        // name are deduplicated independently.
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tiaVersion = context.Options.ClampTiaVersion();

        foreach (var block in context.Project.Blocks)
        {
            // In fbd/auto mode an FBD block the FBD generator rendered (that is, a block that did
            // NOT fall back to SCL) must not be emitted as SCL too; the two files would collide by
            // name on disk. Fallback blocks keep the SCL rendering, exactly like today's default.
            if (context.Options.FbdOutput != FbdOutputMode.Scl
                && block.Language == PlcLanguage.FBD
                && !context.FbdFallbackBlocks.Contains(block.Name))
            {
                continue;
            }

            var blockName = ReserveName(block.Name, usedNames);
            if (!string.Equals(blockName, block.Name, StringComparison.Ordinal))
            {
                context.AddManualAction(
                    $"Block \"{block.Name}\" was renamed to \"{blockName}\" because another block in the "
                    + "project has the same name ignoring case.");
            }

            var artifacts = GenerateBlock(block, context, blockName);

            if (artifacts.Skip) continue;

            yield return Unique(artifacts.Main, usedFiles);
            if (artifacts.Wrapper is null) continue;

            yield return Unique(artifacts.Wrapper, usedFiles);
            yield return RenderInstanceDbXml(artifacts.InstanceDbName!, artifacts.WrapperName!, tiaVersion);
        }
    }

    /// <summary>Reserves <paramref name="name"/> case-insensitively, appending a counter on a clash.</summary>
    private static string ReserveName(string name, HashSet<string> used)
    {
        if (used.Add(name)) return name;

        var suffix = 1;
        string candidate;
        do
        {
            candidate = name + "_" + suffix.ToString(GenerationHelpers.Culture);
            suffix++;
        }
        while (!used.Add(candidate));

        return candidate;
    }

    /// <summary>Gives a generated block file a name that is unique on disk and case-insensitively.</summary>
    private static GeneratedFile Unique(GeneratedFile file, HashSet<string> used)
    {
        var separator = file.RelativePath.LastIndexOf('/');
        var directory = file.RelativePath[..(separator + 1)];
        var fileName = file.RelativePath[(separator + 1)..];
        var stem = fileName.EndsWith(".scl", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".scl".Length]
            : fileName;

        var candidate = stem;
        var suffix = 1;
        while (!used.Add(candidate))
        {
            candidate = stem + "_" + suffix.ToString(GenerationHelpers.Culture);
            suffix++;
        }

        if (candidate == stem) return file;

        return new GeneratedFile
        {
            RelativePath = directory + candidate + ".scl",
            Content = file.Content,
            Kind = file.Kind,
            EmitBom = file.EmitBom,
        };
    }

    internal static GeneratedFile RenderInstanceDbXml(string name, string ofBlock, int tiaVersion)
    {
        // Verified against TIA V16: an instance DB imports as SimaticML with ProgrammingLanguage
        // "DB" and a MemoryLayout that carries ReadOnly="true". No <Number> is given so TIA assigns
        // the next free number; the block is referenced by name.
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<Document>");
        builder.Append("  <Engineering version=\"V").Append(tiaVersion.ToString(GenerationHelpers.Culture)).AppendLine("\" />");
        builder.AppendLine("  <SW.Blocks.InstanceDB ID=\"0\">");
        builder.AppendLine("    <AttributeList>");
        builder.AppendLine("      <Interface>");
        builder.AppendLine("        <Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v4\" />");
        builder.AppendLine("      </Interface>");
        builder.AppendLine("      <MemoryLayout ReadOnly=\"true\">Optimized</MemoryLayout>");
        builder.Append("      <Name>").Append(name).AppendLine("</Name>");
        builder.AppendLine("      <ProgrammingLanguage>DB</ProgrammingLanguage>");
        builder.Append("      <InstanceOfName>").Append(ofBlock).AppendLine("</InstanceOfName>");
        builder.AppendLine("    </AttributeList>");
        builder.AppendLine("    <ObjectList />");
        builder.AppendLine("  </SW.Blocks.InstanceDB>");
        builder.AppendLine("</Document>");

        return new GeneratedFile
        {
            RelativePath = $"Blocks/{GenerationHelpers.SanitizeFileName(name)}.xml",
            Content = builder.ToString(),
            Kind = ArtifactKind.Block,
            EmitBom = true,
        };
    }

    private static BlockArtifacts GenerateBlock(PlcBlock block, GenerationContext context, string blockName)
    {
        var body = new StringBuilder();
        var instances = new List<(string Name, string Type)>();
        var extraLocals = new List<(string Name, string Type)>();
        var holds = new List<(string Name, string Type)>();

        // Globals without a hardware address live in the generated data block and are referenced
        // through it. Those with an address stay in the tag table and keep their bare name.
        var dbGlobals = GlobalDbGenerator.DatabaseGlobals(context.Project)
            .Select(variable => variable.Name)
            .ToList();

        // The declared project-wide type of every global, so a diagram that wired a signal with a
        // different type can be coerced to the type the tag table actually declares.
        var globalTypes = context.Project.GlobalVariables
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().EffectiveTypeName,
                StringComparer.OrdinalIgnoreCase);

        var notes = new List<string>();

        if (block.Logic is StructuredLogic structured)
        {
            var emitter = new FbdSclEmitter(
                structured,
                block.Variables,
                dbGlobals,
                GlobalDbGenerator.ReferencePrefix,
                globalTypes);
            body.Append(emitter.Emit());
            instances.AddRange(emitter.InstanceDeclarations);
            holds.AddRange(emitter.HoldDeclarations);
            notes.AddRange(emitter.Notes);
        }
        else if (block.Logic is TextualLogic textual && !string.IsNullOrWhiteSpace(textual.Code))
        {
            // TriStation stores a complete block declaration in the CDATA section. Only the
            // statement part belongs inside the generated BEGIN/END wrapper.
            var source = textual.Code.Trim();
            var unwrapped = SclCodeTransformer.UnwrapBlock(source);
            var statements = unwrapped?.Statements ?? source;

            if (unwrapped is not null)
            {
                // The XML interface of a TriStation ST block is often empty: the declarations live
                // only in the CDATA text, so they are folded into the block before it is emitted.
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

                // A declaration the export left without a data type cannot be emitted, and the
                // statements that reference it cannot compile either. The block is left out of the
                // generated package so the rest of the project still imports cleanly; a human must
                // repair the type in the source and convert again.
                if (unwrapped.UnterminatedTypes.Count > 0)
                {
                    context.AddManualAction(
                        $"Block \"{block.Name}\" was left out because the source declares "
                        + string.Join(", ", unwrapped.UnterminatedTypes.Select(name => $"\"{name}\""))
                        + " without a data type; supply the type in the source and convert again.");
                    return new BlockArtifacts { Skip = true };
                }
            }

            // Locals that only exist in the source declaration are still block-local and need the
            // same '#' prefix as the ones the XML interface provided.
            var prefixed = block.Variables
                .Where(variable => variable.Direction is VariableDirection.Input or VariableDirection.Output
                    or VariableDirection.InOut or VariableDirection.Local)
                .Select(variable => variable.Name);
            body.Append(SclCodeTransformer.QualifyReferences(
                SclCodeTransformer.NormalizeOperators(statements),
                prefixed,
                dbGlobals,
                GlobalDbGenerator.ReferencePrefix));
        }

        if (body.Length == 0)
        {
            body.AppendLine("    // The source block had no code.");
        }

        foreach (var note in notes)
        {
            context.AddManualAction(note, "Review the converted FBD network in the generated SCL.");
        }

        var declared = block.Variables.Where(v => v.Direction == VariableDirection.Local).ToList();

        // An OB cannot hold block instances; move its logic into a wrapper FB and leave a call behind.
        var needsWrapper = block.BlockType == BlockType.OB
            && (instances.Count > 0
                || declared.Any(v => IsInstanceDataType(v.EffectiveTypeName, context))
                || extraLocals.Any(l => IsInstanceDataType(l.Type, context))
                || holds.Any(l => IsInstanceDataType(l.Type, context)));

        if (!needsWrapper)
        {
            return new BlockArtifacts
            {
                Main = ComposeBlock(
                    block,
                    context,
                    body.ToString(),
                    declared,
                    instances,
                    extraLocals,
                    holds,
                    blockName,
                    asFunctionBlock: block.BlockType == BlockType.FB,
                    includeSourceComment: true),
            };
        }

        var wrapperName = blockName + "_WRAP";
        var dbName = "DB_" + blockName;

        var wrapper = ComposeBlock(
            block,
            context,
            body.ToString(),
            declared,
            instances,
            extraLocals,
            holds,
            wrapperName,
            asFunctionBlock: true,
            includeSourceComment: true);

        // The OB keeps only the call through the wrapper's instance DB.
        var stub = new StringBuilder();
        stub.Append("// The logic is in the wrapper function block \"").Append(wrapperName).AppendLine("\".");
        stub.AppendLine("// An organization block cannot hold block instances, so the wrapper's state lives");
        stub.Append("// in the instance DB \"").Append(dbName).AppendLine("\".");
        stub.Append('"').Append(dbName).Append("\"();");

        var main = ComposeBlock(
            block,
            context,
            stub.ToString(),
            new List<Variable>(),
            new List<(string Name, string Type)>(),
            new List<(string Name, string Type)>(),
            new List<(string Name, string Type)>(),
            blockName,
            asFunctionBlock: false,
            includeSourceComment: false);

        return new BlockArtifacts
        {
            Main = main,
            Wrapper = wrapper,
            WrapperName = wrapperName,
            InstanceDbName = dbName,
        };
    }

    /// <summary>
    /// True when the type needs an instance, i.e. it is a Siemens block type, one of the generated
    /// support blocks, or a function block declared in this project. UDTs and elementary types are
    /// not instances and are allowed in an OB.
    /// </summary>
    private static bool IsInstanceDataType(string? typeName, GenerationContext context)
    {
        var name = Unquote(typeName);
        if (name.Length == 0) return false;
        if (InstanceDataTypes.Contains(name)) return true;
        if (name.StartsWith("Triconex_", StringComparison.OrdinalIgnoreCase)) return true;
        return context.Project.Blocks.Any(
            candidate => candidate.BlockType == BlockType.FB && SameTypeName(candidate.Name, name));
    }

    private static GeneratedFile ComposeBlock(
        PlcBlock block,
        GenerationContext context,
        string body,
        List<Variable> declared,
        List<(string Name, string Type)> instances,
        List<(string Name, string Type)> extraLocals,
        List<(string Name, string Type)> holds,
        string name,
        bool asFunctionBlock,
        bool includeSourceComment)
    {
        // A wrapper built from an OB is a function block, so the keyword follows asFunctionBlock
        // rather than the source block type.
        var (openKeyword, closeKeyword) = asFunctionBlock
            ? ("FUNCTION_BLOCK", "END_FUNCTION_BLOCK")
            : block.BlockType == BlockType.FC
                ? ("FUNCTION", "END_FUNCTION")
                : ("ORGANIZATION_BLOCK", "END_ORGANIZATION_BLOCK");

        var builder = new StringBuilder();
        builder.Append(openKeyword).Append(" \"").Append(name).Append('"');
        if (block.BlockType == BlockType.FC)
        {
            builder.Append(" : ").Append(block.MappedReturnType ?? "Void");
        }

        builder.AppendLine();
        builder.AppendLine("{ S7_Optimized_Access := 'TRUE' }");
        builder.AppendLine("VERSION : 0.1");
        builder.AppendLine();

        AppendSection(builder, "VAR_EXTERN", block, VariableDirection.External);
        AppendSection(builder, "VAR_INPUT", block, VariableDirection.Input);
        AppendSection(builder, "VAR_OUTPUT", block, VariableDirection.Output);
        AppendSection(builder, "VAR_INOUT", block, VariableDirection.InOut);

        // A function block holds its locals statically; an OB or FC local is a temporary. The
        // wrapper is a function block even though the block it was built from is an OB.
        var localKeyword = asFunctionBlock ? "VAR" : "VAR_TEMP";

        if (declared.Count > 0 || instances.Count > 0 || extraLocals.Count > 0 || holds.Count > 0)
        {
            builder.AppendLine(localKeyword);
            foreach (var variable in declared)
            {
                builder.Append("    ").Append(variable.Name).Append(" : ").Append(variable.EffectiveTypeName).Append(';');
                if (!string.IsNullOrWhiteSpace(variable.Comment))
                {
                    builder.Append(" // ").Append(variable.Comment);
                }

                builder.AppendLine();
            }

            foreach (var (extraName, extraType) in extraLocals)
            {
                builder.Append("    ").Append(extraName).Append(" : ").Append(extraType).AppendLine(";");
            }

            // A seal-in hold must be initialized so the output it feeds is defined on the first scan.
            foreach (var (holdName, holdType) in holds)
            {
                builder.Append("    ").Append(holdName).Append(" : ").Append(holdType);
                if (DefaultStartValue(holdType) is { } holdStart)
                {
                    builder.Append(" := ").Append(holdStart);
                }

                builder.AppendLine(";");
            }

            // The emitter reuses the instance name the source interface already declares, so most
            // instances arrive here already declared and must not be declared twice. A name that is
            // declared as a different type is a genuine mismatch and needs a human decision.
            var declaredByName = declared
                .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().EffectiveTypeName, StringComparer.OrdinalIgnoreCase);

            foreach (var (instanceName, instanceType) in instances.Distinct())
            {
                if (declaredByName.TryGetValue(instanceName, out var declaredType))
                {
                    if (!SameTypeName(declaredType, instanceType))
                    {
                        context.AddManualAction(
                            $"Block \"{block.Name}\": the local \"{instanceName}\" is declared as \"{declaredType}\" by the " +
                            $"source interface but the call needs a \"{instanceType}\". Check the generated declaration.");
                    }

                    continue;
                }

                builder.Append("    ").Append(instanceName).Append(" : ").Append(instanceType).AppendLine(";");
            }

            builder.AppendLine("END_VAR");
            builder.AppendLine();
        }

        builder.AppendLine("BEGIN");
        if (includeSourceComment)
        {
            builder.Append("    // Converted from Triconex ").Append(block.SourceElementName)
                .Append(" \"").Append(block.Name).AppendLine("\"");
            builder.Append("    // Original language: ").AppendLine(block.Language.ToString());
            builder.AppendLine();
        }

        builder.AppendLine(GenerationHelpers.Indent(body.TrimEnd(), 4));
        builder.AppendLine(closeKeyword);

        return new GeneratedFile
        {
            RelativePath = $"Blocks/{GenerationHelpers.SanitizeFileName(name)}.scl",
            Content = builder.ToString(),
            Kind = ArtifactKind.Block,
            EmitBom = true,
        };
    }

    /// <summary>
    /// Compares two type names ignoring the quoting that SCL needs around block type names, so a
    /// source local declared as "FB04_DI" matches an emitter instance of type FB04_DI.
    /// </summary>
    private static bool SameTypeName(string? left, string? right) =>
        string.Equals(Unquote(left), Unquote(right), StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string? value) =>
        (value ?? string.Empty).Trim().Trim('"').Trim();

    private static void AppendSection(StringBuilder builder, string keyword, PlcBlock block, VariableDirection direction)
    {
        var variables = block.Variables.Where(v => v.Direction == direction).ToList();
        if (variables.Count == 0) return;

        builder.AppendLine(keyword);
        // An FB output that is read before it is written (a seal-in or latch) makes the compiler
        // warn that it "might not be initialized". Declaring the type's default start value keeps
        // the runtime behaviour identical (it is the same value TIA would use) and silences it.
        var withStartValues = direction == VariableDirection.Output && block.BlockType != BlockType.FC;
        foreach (var variable in variables)
        {
            builder.Append("    ").Append(variable.Name).Append(" : ").Append(variable.EffectiveTypeName);
            if (withStartValues && DefaultStartValue(variable.EffectiveTypeName) is { } start)
            {
                builder.Append(" := ").Append(start);
            }

            builder.Append(';');
            if (!string.IsNullOrWhiteSpace(variable.Comment))
            {
                builder.Append(" // ").Append(variable.Comment);
            }
            builder.AppendLine();
        }
        builder.AppendLine("END_VAR");
        builder.AppendLine();
    }

    /// <summary>The explicit default start value for a scalar type, or null when none is emitted.</summary>
    private static string? DefaultStartValue(string? typeName) => Unquote(typeName) switch
    {
        "Bool" => "FALSE",
        "Byte" or "Word" or "DWord" or "SInt" or "USInt" or "Int" or "UInt" or "DInt" or "UDInt" => "0",
        "Real" or "LReal" => "0.0",
        "Time" => "T#0s",
        _ => null,
    };

    private sealed class BlockArtifacts
    {
        /// <summary>Set when the block cannot be generated at all and must be left out.</summary>
        public bool Skip { get; init; }

        public GeneratedFile Main { get; init; } = null!;

        public GeneratedFile? Wrapper { get; init; }

        public string? WrapperName { get; init; }

        public string? InstanceDbName { get; init; }
    }
}