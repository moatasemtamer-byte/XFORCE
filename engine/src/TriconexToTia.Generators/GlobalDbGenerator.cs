using System.Text;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Interfaces;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Generators;

/// <summary>
/// Emits a global data block (<c>DATA_BLOCK</c> SCL source) holding the converted TriStation
/// global variables.
///
/// Global variables are ordinary program variables, not PLC tags, so they cannot live in a
/// tag table. Points that do carry a hardware address are emitted by
/// <see cref="TagTableXmlGenerator"/> instead; only the address-free ones land here.
/// </summary>
public sealed class GlobalDbGenerator : IGenerator
{
    private const string DbName = "GlobalData";

    /// <summary>
    /// The access path SCL code uses to reach a member of this block, e.g. <c>"GlobalData".Name</c>.
    /// </summary>
    public const string ReferencePrefix = "\"" + DbName + "\".";

    /// <summary>
    /// The globals that belong in this block. A global is only stored here when it carries no
    /// hardware address: an SCL declaration cannot hold a <c>LogicalAddress</c>, so addressed
    /// points stay in the tag table written by <see cref="TagTableXmlGenerator"/>.
    /// </summary>
    public static IEnumerable<GlobalVariable> DatabaseGlobals(PlcProject project) =>
        project.GlobalVariables.Where(variable => string.IsNullOrWhiteSpace(variable.LogicalAddress));

    public string Id => "global-db";

    public string Description => "Global data block source file";

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        var variables = DatabaseGlobals(context.Project).ToArray();

        if (variables.Length == 0)
        {
            yield break;
        }

        var builder = new StringBuilder();
        builder.Append("DATA_BLOCK \"").Append(DbName).AppendLine("\"");
        builder.AppendLine("{ S7_Optimized_Access := 'TRUE' }");
        builder.AppendLine("VERSION : 0.1");
        builder.AppendLine("VAR");

        foreach (var variable in variables)
        {
            if (!string.IsNullOrWhiteSpace(variable.Comment))
            {
                builder.Append("    // ").AppendLine(variable.Comment);
            }

            builder.Append("    ").Append(variable.Name).Append(" : ").Append(variable.EffectiveTypeName).AppendLine(";");
        }

        builder.AppendLine("END_VAR");
        builder.AppendLine();
        builder.AppendLine("BEGIN");
        builder.AppendLine("END_DATA_BLOCK");

        yield return new GeneratedFile
        {
            RelativePath = $"Globals/{DbName}.db",
            Content = builder.ToString(),
            Kind = ArtifactKind.Block,
            EmitBom = true,
        };
    }
}