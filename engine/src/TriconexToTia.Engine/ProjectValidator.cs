using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;
using TriconexToTia.Core.Results;
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
            result.Add(ValidationIssue.Fatal(
                $"Duplicate block name \"{group.Key}\" ({group.Count()} definitions).",
                first.SourceLine));
        }
    }

    private void CheckTypeReferences(PlcProject project, ValidationResult result)
    {
        foreach (var block in project.Blocks)
        {
            foreach (var variable in block.Variables)
            {
                if (!IsKnownType(variable.TypeName))
                {
                    result.Add(ValidationIssue.Fatal(
                        $"Variable \"{variable.Name}\" in block \"{block.Name}\" uses undefined type \"{variable.TypeName}\".",
                        variable.SourceLine,
                        block.Name));
                }
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
            if (string.IsNullOrWhiteSpace(block.Code))
            {
                result.Add(ValidationIssue.Warning(
                    $"Block \"{block.Name}\" has no code. An empty block was generated.",
                    block.SourceLine,
                    block.Name));
            }
        }
    }

    /// <summary>
    /// FBD, LD and SFC bodies cannot be carried into SCL verbatim, so the
    /// conversion is attempted but must be verified by hand.
    /// </summary>
    private static void CheckNonTextLanguages(PlcProject project, ValidationResult result)
    {
        foreach (var block in project.Blocks)
        {
            if (block.Language is not (PlcLanguage.FBD or PlcLanguage.LD or PlcLanguage.SFC))
            {
                continue;
            }

            result.Add(ValidationIssue.Warning(
                $"Block \"{block.Name}\" is written in {block.Language}. An SCL conversion was attempted; verify the logic by hand.",
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

        if (_mappings.Types.ContainsKey(raw)) return true;
        if (_userTypes.Contains(raw)) return true;

        return false;
    }
}
