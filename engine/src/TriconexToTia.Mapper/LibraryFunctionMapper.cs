using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Results;

namespace TriconexToTia.Mapper;

/// <summary>
/// Scans SCL/ST code for calls to Triconex library functions and records how
/// (or whether) each one can be mapped. Pure IEC functions with the same name
/// need no rewrite; TR_* functions become manual actions.
/// </summary>
public sealed class LibraryFunctionMapper
{
    private static readonly Regex CallPattern = new(
        @"(?<![A-Za-z0-9_])(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IF", "THEN", "ELSIF", "ELSE", "END_IF", "CASE", "OF", "END_CASE",
        "FOR", "TO", "BY", "DO", "END_FOR", "WHILE", "END_WHILE", "REPEAT",
        "UNTIL", "END_REPEAT", "RETURN", "EXIT", "CONTINUE", "NOT", "AND",
        "OR", "XOR", "MOD", "TRUE", "FALSE", "VAR", "END_VAR", "FUNCTION",
        "FUNCTION_BLOCK", "PROGRAM", "TYPE", "END_TYPE", "STRUCT", "END_STRUCT",
    };

    private readonly Mappings _mappings;

    public LibraryFunctionMapper(Mappings mappings) => _mappings = mappings;

    public void Scan(string code, string blockName, int sourceLine, MappingResult result)
    {
        if (string.IsNullOrWhiteSpace(code)) return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in CallPattern.Matches(code))
        {
            var name = match.Groups["name"].Value;
            if (!seen.Add(name)) continue;
            if (Keywords.Contains(name)) continue;

            if (_mappings.LibraryFunctions.TryGetValue(name, out var rule))
            {
                var status = Mappings.ParseStatus(rule.Status);
                result.Record(new MappingRecord
                {
                    Category = "LibraryFunction",
                    Source = name,
                    Target = rule.Siemens,
                    Status = status,
                    Note = rule.Note,
                    SourceLine = sourceLine,
                });

                if (status == ConversionStatus.Manual)
                {
                    result.AddManualAction(new ManualAction
                    {
                        Title = $"{name} in \"{blockName}\"",
                        Reason = rule.Note ?? "No Siemens equivalent.",
                        Action = rule.Action ?? "Replace with the equivalent Siemens instruction and re-test.",
                        SourceLine = sourceLine,
                    });
                }
                continue;
            }

            var pattern = _mappings.ManualPatterns
                .FirstOrDefault(p => name.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase));
            if (pattern is not null)
            {
                result.Record(new MappingRecord
                {
                    Category = "LibraryFunction",
                    Source = name,
                    Target = null,
                    Status = ConversionStatus.Manual,
                    Note = pattern.Note,
                    SourceLine = sourceLine,
                });
                result.AddManualAction(new ManualAction
                {
                    Title = $"{name} in \"{blockName}\"",
                    Reason = pattern.Note,
                    Action = pattern.Action,
                    SourceLine = sourceLine,
                });
            }
        }
    }
}

