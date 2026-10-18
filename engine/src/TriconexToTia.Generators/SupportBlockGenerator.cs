using System.Text;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>
/// Emits SCL definitions for the Siemens blocks that stand in for TriStation library blocks with
/// no direct equivalent. UNPACK16 and UNPACK32 unpack one DWORD into a separate Boolean output per
/// bit, which a diagram can only express as an instance with sixteen or thirty-two outputs, so the
/// substitution is a generated function block rather than an inline expression.
///
/// The generated block keeps TriStation's formal names (CI, INPUT, CO, NZ, Q01..Qnn), so the call
/// sites need no parameter rewriting.
/// </summary>
public sealed class SupportBlockGenerator : IGenerator
{
    /// <summary>
    /// Prefix that marks a mapping rule's Siemens name as a block this generator has to provide.
    /// A rule whose Siemens name carries it is never expected to exist in the Siemens library.
    /// </summary>
    public const string SupportPrefix = "Triconex_";

    /// <summary>Number of unpacked bits for each TriStation block this generator can stand in for.</summary>
    private static readonly Dictionary<string, int> Widths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UNPACK16"] = 16,
        ["UNPACK32"] = 32,
    };

    /// <summary>
    /// TriStation flip-flops. TIA has no SR/RS data type, so they become generated function blocks
    /// with TriStation's formal names (S1, R, Q1, Q2).
    /// </summary>
    private static readonly HashSet<string> FlipFlops = new(StringComparer.OrdinalIgnoreCase)
    {
        "SR",
        "RS",
    };

    public string Id => "support-blocks";

    public string Description => "Siemens stand-in blocks for TriStation library blocks";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        foreach (var name in UsedSupportBlocks(context.Project).OrderBy(name => name, StringComparer.Ordinal))
        {
            yield return new GeneratedFile
            {
                RelativePath = $"Library/{SupportPrefix}{name}.scl",
                Content = Render(name),
                Kind = ArtifactKind.Block,
                EmitBom = true,
            };
        }
    }

    /// <summary>
    /// The TriStation blocks whose stand-in is needed: calls that resolved to a generated support
    /// block, and declarations whose mapped type did. Only the ones a project actually uses are
    /// emitted, so an export that never unpacks anything gets no Library folder at all.
    /// </summary>
    public static IReadOnlyCollection<string> UsedSupportBlocks(PlcProject project)
    {
        var used = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in project.Blocks)
        {
            if (block.Logic is StructuredLogic logic)
            {
                foreach (var call in logic.Items.OfType<BlockCall>())
                {
                    var mapped = call.MappedTypeName;
                    if (mapped is null || !mapped.StartsWith(SupportPrefix, StringComparison.Ordinal)) continue;
                    if (IsProvided(call.TypeName)) used.Add(call.TypeName);
                }
            }

            foreach (var variable in block.Variables)
            {
                AddByMappedType(variable.MappedTypeName, used);
            }
        }

        foreach (var type in project.DataTypes)
        {
            foreach (var member in type.Members)
            {
                AddByMappedType(member.MappedTypeName, used);
            }
        }

        foreach (var variable in project.GlobalVariables)
        {
            AddByMappedType(variable.MappedTypeName, used);
        }

        return used;
    }

    private static void AddByMappedType(string? mappedTypeName, SortedSet<string> used)
    {
        if (mappedTypeName is null
            || !mappedTypeName.StartsWith(SupportPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var triconexName = mappedTypeName[SupportPrefix.Length..];
        if (IsProvided(triconexName)) used.Add(triconexName);
    }

    /// <summary>True when the TriStation name is one this generator can actually emit.</summary>
    private static bool IsProvided(string triconexName) =>
        Widths.ContainsKey(triconexName) || FlipFlops.Contains(triconexName);

    /// <summary>
    /// The formal parameters and their Siemens types for a stand-in block, so a call site can be told
    /// what each pin expects. Returns false when the TriStation name has no stand-in.
    /// </summary>
    public static bool TryGetFormalTypes(
        string triconexName,
        out IReadOnlyDictionary<string, string> inputs,
        out IReadOnlyDictionary<string, string> outputs)
    {
        var types = FormalTypes(triconexName);
        if (types is null)
        {
            inputs = EmptyFormals;
            outputs = EmptyFormals;
            return false;
        }

        inputs = types.Value.Inputs;
        outputs = types.Value.Outputs;
        return true;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyFormals =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Input/output formal types of a stand-in block, or null when it is not provided.</summary>
    public static (IReadOnlyDictionary<string, string> Inputs, IReadOnlyDictionary<string, string> Outputs)? FormalTypes(
        string triconexName)
    {
        if (FlipFlops.Contains(triconexName))
        {
            return (
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["S1"] = "Bool", ["R"] = "Bool" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Q1"] = "Bool", ["Q2"] = "Bool" });
        }

        if (!Widths.TryGetValue(triconexName, out var width)) return null;

        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CI"] = "Bool",
            ["INPUT"] = "DWord",
        };
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CO"] = "Bool",
            ["NZ"] = "Bool",
        };
        for (var bit = 1; bit <= width; bit++)
        {
            outputs["Q" + bit.ToString("D2", GenerationHelpers.Culture)] = "Bool";
        }

        return (inputs, outputs);
    }

    /// <summary>
    /// True when the name is one of the TriStation blocks this generator provides, whether or not
    /// the current project calls it.
    /// </summary>
    public static bool IsSupportBlock(string siemensName) =>
        siemensName.StartsWith(SupportPrefix, StringComparison.Ordinal);

    /// <summary>
    /// True when the name is a stand-in block this generator can actually emit. The validator uses
    /// it to keep treating a mapped library instance as a known type instead of reporting it as
    /// missing from the export.
    /// </summary>
    public static bool IsKnownSupportBlock(string siemensName) =>
        IsSupportBlock(siemensName) && IsProvided(siemensName[SupportPrefix.Length..]);

    private static string Render(string triconexName)
    {
        if (FlipFlops.Contains(triconexName)) return RenderFlipFlop(triconexName);

        var width = Widths[triconexName];

        // UNPACK16 only looks at the low-order half of the word; UNPACK32 uses all of it.
        var mask = width == 16 ? "DWORD#16#0000FFFF" : "DWORD#16#FFFFFFFF";

        var builder = new StringBuilder();
        builder.Append("FUNCTION_BLOCK \"").Append(SupportPrefix).Append(triconexName).AppendLine("\"");
        builder.AppendLine("{ S7_Optimized_Access := 'TRUE' }");
        builder.AppendLine("VERSION : 0.1");
        builder.AppendLine("VAR_INPUT");
        builder.AppendLine("    CI : Bool;");
        builder.AppendLine("    INPUT : DWord;");
        builder.AppendLine("END_VAR");
        builder.AppendLine("VAR_OUTPUT");
        builder.AppendLine("    CO : Bool;");
        builder.AppendLine("    NZ : Bool;");
        for (var bit = 1; bit <= width; bit++)
        {
            builder.Append("    Q").Append(bit.ToString("D2", GenerationHelpers.Culture)).AppendLine(" : Bool;");
        }

        builder.AppendLine("END_VAR");
        builder.AppendLine("BEGIN");
        builder.AppendLine("    // CO reports that the block ran, so it follows CI. The outputs keep their");
        builder.AppendLine("    // previous values while CI is false, as the block is not executed.");
        builder.AppendLine("    #CO := #CI;");
        builder.Append("    #NZ := (#INPUT AND ").Append(mask).AppendLine(") <> 0;");
        builder.AppendLine("    IF #CI THEN");

        for (var bit = 1; bit <= width; bit++)
        {
            var literal = "DWORD#16#" + (1u << (bit - 1)).ToString("X8", GenerationHelpers.Culture);
            builder.Append("        #Q").Append(bit.ToString("D2", GenerationHelpers.Culture))
                .Append(" := (#INPUT AND ").Append(literal).AppendLine(") <> 0;");
        }

        builder.AppendLine("    END_IF;");
        builder.AppendLine("END_FUNCTION_BLOCK");

        return builder.ToString();
    }

    /// <summary>
    /// TriStation SR/RS as a function block. SR is reset-dominant and RS set-dominant, which is
    /// also what the TIA LAD instructions of the same name do; Q2 is the inverted output, as in the
    /// TriStation reference. The reset/set priority is called out as a manual review point in the
    /// mapping note.
    /// </summary>
    private static string RenderFlipFlop(string triconexName)
    {
        var resetDominant = string.Equals(triconexName, "SR", StringComparison.OrdinalIgnoreCase);

        var builder = new StringBuilder();
        builder.Append("FUNCTION_BLOCK \"").Append(SupportPrefix).Append(triconexName).AppendLine("\"");
        builder.AppendLine("{ S7_Optimized_Access := 'TRUE' }");
        builder.AppendLine("VERSION : 0.1");
        builder.AppendLine("VAR_INPUT");
        builder.AppendLine("    S1 : Bool;");
        builder.AppendLine("    R : Bool;");
        builder.AppendLine("END_VAR");
        builder.AppendLine("VAR_OUTPUT");
        builder.AppendLine("    Q1 : Bool := FALSE;");
        builder.AppendLine("    Q2 : Bool := FALSE;");
        builder.AppendLine("END_VAR");
        builder.AppendLine("VAR");
        builder.AppendLine("    STATE : Bool := FALSE;");
        builder.AppendLine("END_VAR");
        builder.AppendLine("BEGIN");
        builder.AppendLine("    // The dominant input is checked first, so it wins when both are true. The state is");
        builder.AppendLine("    // held in a static, so Q1 is always assigned before Q2 reads it (TIA otherwise warns");
        builder.AppendLine("    // that the output parameter might not be initialized).");
        if (resetDominant)
        {
            builder.AppendLine("    IF #R THEN");
            builder.AppendLine("        #STATE := FALSE;");
            builder.AppendLine("    ELSIF #S1 THEN");
            builder.AppendLine("        #STATE := TRUE;");
        }
        else
        {
            builder.AppendLine("    IF #S1 THEN");
            builder.AppendLine("        #STATE := TRUE;");
            builder.AppendLine("    ELSIF #R THEN");
            builder.AppendLine("        #STATE := FALSE;");
        }

        builder.AppendLine("    END_IF;");
        builder.AppendLine("    #Q1 := #STATE;");
        builder.AppendLine("    #Q2 := NOT #STATE;");
        builder.AppendLine("END_FUNCTION_BLOCK");

        return builder.ToString();
    }
}