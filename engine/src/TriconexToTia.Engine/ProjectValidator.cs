using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;
using TriconexToTia.Generators;
using TriconexToTia.Mapper;

namespace TriconexToTia.Engine;

/// <summary>
/// Stage 2 of the pipeline. Runs every structural check before anything is
/// generated; a single fatal issue stops generation.
/// </summary>
public sealed class ProjectValidator
{
    private static readonly Regex ArrayPattern = new(
        @"^\s*ARRAY\s*\[[^\]]+\]\s*OF\s+(?<element>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StringPattern = new(
        @"^\s*STRING\s*(\[\s*\d+\s*\])?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Mappings _mappings;
    private readonly HashSet<string> _userTypes;

    public ProjectValidator(Mappings mappings)
    {
        _mappings = mappings;
        _userTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public ValidationResult Validate(PlcProject project)
    {
        var result = new ValidationResult();

        foreach (var type in project.DataTypes)
        {
            _userTypes.Add(type.Name.Trim());
        }

        foreach (var block in project.Blocks)
        {
            _userTypes.Add(block.Name.Trim());
        }

        CheckDuplicateBlocks(project, result);
        CheckTypeReferences(project, result);
        CheckDuplicateVariables(project, result);
        CheckEmptyBlocks(project, result);
        CheckNonTextLanguages(project, result);
        CheckDanglingSignals(project, result);
        CollectUnrecognizedElements(project, result);

        return result;
    }

    private static void CheckDuplicateBlocks(PlcProject project, ValidationResult result)
    {
        var groups = project.Blocks
            .GroupBy(block => block.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in groups)
        {
            var first = group.First();

            // Siemens block names are case-insensitive. Case-insensitive duplicates that nothing
            // references from SCL text are renamed automatically before validation; this warning is
            // only reached for a duplicate whose name appears in SCL text, where the reference cannot
            // be rewritten safely.
            result.Add(ValidationIssue.Warning(
                $"Block name \"{group.Key}\" ({group.Count()} definitions: " +
                string.Join(", ", group.Select(block => $"\"{block.Name}\"")) +
                ") is case-insensitively ambiguous and is referenced from SCL text. Rename one block " +
                "in the source system; Siemens keeps only one of them.",
                first.SourceLine,
                first.Name));
        }
    }

    private void CheckTypeReferences(PlcProject project, ValidationResult result)
    {
        foreach (var block in project.Blocks)
        {
            foreach (var variable in block.Variables)
            {
                if (IsKnownType(variable.TypeName)) continue;

                // <derived name="..."/> references a function block or data type that is not part of
                // this export. TriStation library blocks (R_TRIG, TMR, ...) have no SCL equivalent,
                // so this is a manual task rather than a fatal error.
                result.Add(variable.IsDerivedType
                    ? ValidationIssue.Warning(
                        $"Variable \"{variable.Name}\" in block \"{block.Name}\" is an instance of " +
                        $"\"{variable.TypeName}\", which is not defined in this export. Provide the " +
                        "Siemens equivalent library block or convert it by hand.",
                        variable.SourceLine,
                        block.Name)
                    : ValidationIssue.Fatal(
                        $"Variable \"{variable.Name}\" in block \"{block.Name}\" uses undefined type \"{variable.TypeName}\".",
                        variable.SourceLine,
                        block.Name));
            }

            if (block is Function function && !string.IsNullOrWhiteSpace(function.ReturnType)
                && !IsKnownType(function.ReturnType))
            {
                result.Add(ValidationIssue.Fatal(
                    $"Function \"{function.Name}\" has undefined return type \"{function.ReturnType}\".",
                    function.SourceLine));
            }
        }

        foreach (var type in project.DataTypes)
        {
            foreach (var member in type.Members)
            {
                if (!IsKnownType(member.TypeName))
                {
                    result.Add(ValidationIssue.Fatal(
                        $"Member \"{member.Name}\" of type \"{type.Name}\" uses undefined type \"{member.TypeName}\".",
                        member.SourceLine,
                        type.Name));
                }
            }
        }

        foreach (var variable in project.GlobalVariables)
        {
            if (!string.IsNullOrWhiteSpace(variable.TypeName) && variable.TypeName != "ANY"
                && !IsKnownType(variable.TypeName))
            {
                result.Add(ValidationIssue.Warning(
                    $"Global variable \"{variable.Name}\" uses unmapped type \"{variable.TypeName}\".",
                    variable.SourceLine));
            }
        }
    }

    private static void CheckDuplicateVariables(PlcProject project, ValidationResult result)
    {
        foreach (var block in project.Blocks)
        {
            var duplicates = block.Variables
                .GroupBy(variable => variable.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            foreach (var group in duplicates)
            {
                result.Add(ValidationIssue.Fatal(
                    $"Duplicate variable \"{group.Key}\" in block \"{block.Name}\".",
                    group.First().SourceLine,
                    block.Name));
            }
        }
    }

    private static void CheckEmptyBlocks(PlcProject project, ValidationResult result)
    {
        foreach (var block in project.Blocks)
        {
            var isEmpty = block.Logic switch
            {
                TextualLogic textual => string.IsNullOrWhiteSpace(textual.Code),
                StructuredLogic structured => structured.Items.Count == 0,
                null => string.IsNullOrWhiteSpace(block.Code),
                _ => false,
            };

            if (!isEmpty) continue;

            result.Add(ValidationIssue.Warning(
                $"Block \"{block.Name}\" has no code. An empty block was generated.",
                block.SourceLine,
                block.Name));
        }
    }

    /// <summary>
    /// FBD networks are reconstructed into SCL from the resolved connection graph, LD and SFC
    /// bodies are carried over verbatim. Both must be checked by an engineer because the
    /// generated code is not a line-by-line copy of the source.
    /// </summary>
    private static void CheckNonTextLanguages(PlcProject project, ValidationResult result)
    {
        foreach (var block in project.Blocks)
        {
            switch (block.Language)
            {
                case PlcLanguage.FBD:
                    result.Add(ValidationIssue.Warning(
                        $"Block \"{block.Name}\" is an FBD diagram. SCL was generated from the resolved " +
                        "connection graph; verify the logic against the TriStation diagram.",
                        block.SourceLine,
                        block.Name));
                    break;
                case PlcLanguage.LD:
                case PlcLanguage.SFC:
                    result.Add(ValidationIssue.Warning(
                        $"Block \"{block.Name}\" is written in {block.Language} and has no SCL equivalent. " +
                        "The original diagram XML is written next to the generated block; recreate it in TIA Portal.",
                        block.SourceLine,
                        block.Name));
                    break;
            }
        }
    }

    /// <summary>
    /// A signal that no call consumes and no assignment writes is either dead logic or a wiring
    /// the reader failed to resolve, so both cases are reported.
    /// </summary>
    private void CheckDanglingSignals(PlcProject project, ValidationResult result)
    {
        foreach (var block in project.Blocks)
        {
            if (block.Logic is not StructuredLogic logic) continue;

            var consumed = new HashSet<string>(StringComparer.Ordinal);
            var produced = new HashSet<string>(StringComparer.Ordinal);

            foreach (var call in logic.Items.OfType<BlockCall>())
            {
                foreach (var argument in call.Inputs.Concat(call.InOuts))
                {
                    foreach (var source in argument.Sources.Where(source => source.RefLocalId is not null))
                    {
                        consumed.Add($"{source.RefSheetNumber}:{source.RefLocalId}");
                    }
                }
            }

            foreach (var assignment in logic.Items.OfType<VariableAssignment>())
            {
                if (assignment.RefLocalId is not null)
                {
                    consumed.Add($"{assignment.RefSheetNumber}:{assignment.RefLocalId}");
                }
                produced.Add(assignment.SheetKey);
            }

            foreach (var item in logic.Items)
            {
                if (item is VariableReference or BlockCall) produced.Add(item.SheetKey);
            }

            if (logic.CrossSheetReferences.Count > 0)
            {
                result.Add(ValidationIssue.Warning(
                    $"Block \"{block.Name}\": {logic.CrossSheetReferences.Count} FBD connection(s) reference a " +
                    "localId that is not present on the same sheet and were matched across sheets " +
                    $"({string.Join("; ", logic.CrossSheetReferences.Take(5))}). Verify them in the diagram.",
                    block.SourceLine,
                    block.Name));
            }

            var dangling = logic.Items
                .Where(item => produced.Contains(item.SheetKey) && !consumed.Contains(item.SheetKey))
                .ToList();

            if (dangling.Count == 0) continue;

            result.Add(ValidationIssue.Warning(
                $"Block \"{block.Name}\": {dangling.Count} FBD item(s) produce a value that nothing " +
                "consumes (localId " + string.Join(", ", dangling.Select(item => item.SheetKey)) +
                "). They may be dead logic or unconnected; review the diagram.",
                block.SourceLine,
                block.Name));
        }
    }

    private static void CollectUnrecognizedElements(PlcProject project, ValidationResult result)
    {
        foreach (var element in project.UnrecognizedElements)
        {
            result.Add(ValidationIssue.Info(
                $"Unrecognized XML element <{element.ElementName}> (in {element.ParentPath}). Skipped; review if it contains logic.",
                element.SourceLine));
        }
    }

    private bool IsKnownType(string typeName)
    {
        var raw = (typeName ?? string.Empty).Trim();
        if (raw.Length == 0) return false;

        raw = raw.Trim('"');

        var arrayMatch = ArrayPattern.Match(raw);
        if (arrayMatch.Success)
        {
            return IsKnownType(arrayMatch.Groups["element"].Value);
        }

        if (StringPattern.IsMatch(raw)) return true;

        // A library block that resolves to a Siemens name does not have to appear in this export:
        // it comes from the Siemens library, or is emitted into Library/ as a stand-in block. Either
        // way it will exist in the target project, so it is a known type. Mapping has not run yet at
        // this point, so the rule is resolved here rather than read off the variable.
        if (_mappings.LibraryFunctions.TryGetValue(raw, out var rule)
            && !string.IsNullOrWhiteSpace(rule.Siemens))
        {
            return true;
        }

        if (_mappings.Types.ContainsKey(raw)) return true;
        if (_userTypes.Contains(raw)) return true;

        return false;
    }
}
