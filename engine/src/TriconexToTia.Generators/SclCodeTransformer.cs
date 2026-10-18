using System.Text;
using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;

namespace TriconexToTia.Generators;

/// <summary>One variable the TriStation ST declaration contributes to the generated block.</summary>
internal sealed record StDeclaration(string Name, string Type, VariableDirection Direction);

/// <summary>The statements and declarations recovered from a TriStation ST block body.</summary>
internal sealed record StBlockBody(
    string Statements,
    IReadOnlyList<StDeclaration> Declarations,
    IReadOnlyList<string> UnterminatedTypes,
    string Keyword,
    string? ReturnType);

/// <summary>
/// Rewrites an ST body for Siemens SCL. The only structural change in the ST
/// path is prefixing every reference to a block-local variable with '#'.
/// String literals and comments are left untouched.
/// </summary>
internal static class SclCodeTransformer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IF", "THEN", "ELSIF", "ELSE", "END_IF", "CASE", "OF", "END_CASE",
        "FOR", "TO", "BY", "DO", "END_FOR", "WHILE", "END_WHILE", "REPEAT",
        "UNTIL", "END_REPEAT", "RETURN", "EXIT", "CONTINUE", "GOTO",
        "NOT", "AND", "OR", "XOR", "MOD", "TRUE", "FALSE",
        "VAR", "END_VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_TEMP",
        "FUNCTION", "FUNCTION_BLOCK", "ORGANIZATION_BLOCK", "BEGIN", "END_FUNCTION",
        "END_FUNCTION_BLOCK", "END_ORGANIZATION_BLOCK", "PROGRAM", "TYPE", "END_TYPE",
        "STRUCT", "END_STRUCT", "RETAIN", "NON_RETAIN", "CONSTANT", "AT", "REF_TO",
    };

    private static readonly Regex BeginPattern = new(@"\bBEGIN\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex EndPattern = new(
        @"\bEND_(?:FUNCTION_BLOCK|FUNCTION|PROGRAM|ORGANIZATION_BLOCK)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex VarSectionPattern = new(
        @"\b(?<kind>VAR(?:_(?:INPUT|OUTPUT|IN_OUT|TEMP|STAT))?|CONSTANT)\b(?<body>.*?)\bEND_VAR\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex DeclarationPattern = new(
        @"\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\([^()]*\))?\s*:(?<type>[^;\r\n]+);",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A declaration line left behind when the type is missing: <c>IN2:</c>.</summary>
    private static readonly Regex NakedDeclarationPattern = new(
        @"^[ \t]*(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*:[ \t]*(?://.*)?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// TriStation writes a complete block declaration into the CDATA section, while the generated
    /// SCL provides its own wrapper. This splits that declaration into the statements to emit plus
    /// any declarations the XML interface did not already carry.
    /// </summary>
    public static StBlockBody? UnwrapBlock(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var begin = BeginPattern.Match(code);
        string header;
        int statementsStart;
        int statementsEnd;

        if (begin.Success)
        {
            var end = EndPattern.Match(code, begin.Index + begin.Length);
            if (!end.Success) return null;

            header = code.Substring(0, begin.Index);
            statementsStart = begin.Index + begin.Length;
            statementsEnd = end.Index;
        }
        else
        {
            // A TriStation ST block has no BEGIN keyword: its statements follow the last
            // declaration section directly.
            var end = EndPattern.Match(code);
            if (!end.Success) return null;

            Match? lastSection = null;
            foreach (Match section in VarSectionPattern.Matches(code))
            {
                if (section.Index >= end.Index) break;
                lastSection = section;
            }

            // Without any declarations the block header is the first line.
            statementsStart = lastSection is not null
                ? lastSection.Index + lastSection.Length
                : (code.IndexOf('\n') is var newline && newline >= 0 ? newline + 1 : 0);
            header = code.Substring(0, statementsStart);
            statementsEnd = end.Index;
        }

        var statements = code.Substring(statementsStart, statementsEnd - statementsStart).Trim();

        var keywordMatch = Regex.Match(
            header,
            @"\b(?<keyword>FUNCTION_BLOCK|FUNCTION|PROGRAM|ORGANIZATION_BLOCK)\b\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)?\s*(?::\s*(?<type>[A-Za-z_][A-Za-z0-9_.]*))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var keyword = keywordMatch.Success
            ? keywordMatch.Groups["keyword"].Value.ToUpperInvariant()
            : "FUNCTION_BLOCK";
        var returnType = keyword == "FUNCTION" && keywordMatch.Groups["type"].Success
            ? keywordMatch.Groups["type"].Value
            : null;

        var declarations = new List<StDeclaration>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match section in VarSectionPattern.Matches(header))
        {
            var direction = DirectionOf(section.Groups["kind"].Value);
            var bodyText = section.Groups["body"].Value;

            foreach (Match declaration in DeclarationPattern.Matches(bodyText))
            {
                var name = declaration.Groups["name"].Value;
                var type = NormalizeType(declaration.Groups["type"].Value);
                if (type.Length == 0 || !seen.Add(name)) continue;

                declarations.Add(new StDeclaration(name, type, direction));
            }

            // A declaration the export left without a data type cannot be emitted; the generator
            // reports it so a human can supply the type.
            foreach (Match naked in NakedDeclarationPattern.Matches(bodyText))
            {
                var name = naked.Groups["name"].Value;
                if (!seen.Add(name)) continue;
                declarations.Add(new StDeclaration(name, string.Empty, direction));
            }
        }

        var unterminated = declarations
            .Where(declaration => declaration.Type.Length == 0)
            .Select(declaration => declaration.Name)
            .ToList();

        return new StBlockBody(
            statements,
            declarations.Where(declaration => declaration.Type.Length > 0).ToList(),
            unterminated,
            keyword,
            returnType);
    }

    private static VariableDirection DirectionOf(string sectionKeyword) =>
        sectionKeyword.ToUpperInvariant() switch
        {
            "VAR_INPUT" => VariableDirection.Input,
            "VAR_OUTPUT" => VariableDirection.Output,
            "VAR_IN_OUT" => VariableDirection.InOut,
            _ => VariableDirection.Local,
        };

    /// <summary>Collapses whitespace so a declaration can be emitted on one line.</summary>
    private static string NormalizeType(string type)
    {
        var collapsed = Regex.Replace(type, @"\s+", " ").Trim();
        return collapsed;
    }

    private static readonly Regex SpacedNotEqualPattern = new(@"<[ \t]+>", RegexOptions.CultureInvariant);
    private static readonly Regex SpacedLessEqualPattern = new(@"<[ \t]+=", RegexOptions.CultureInvariant);
    private static readonly Regex SpacedGreaterEqualPattern = new(@">[ \t]+=", RegexOptions.CultureInvariant);

    /// <summary>
    /// Repairs comparison operators the TriStation export spaced out (<c>&lt; &gt;</c>,
    /// <c>&lt; =</c>, <c>&gt; =</c>), which Siemens SCL reads as separate operators.
    /// </summary>
    public static string NormalizeOperators(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;

        var normalized = SpacedNotEqualPattern.Replace(code, "<>");
        normalized = SpacedLessEqualPattern.Replace(normalized, "<=");
        normalized = SpacedGreaterEqualPattern.Replace(normalized, ">=");
        return normalized;
    }

    public static string PrefixLocalVariables(string code, IEnumerable<string> variableNames) =>
        QualifyReferences(code, variableNames, Array.Empty<string>(), null);

    /// <summary>
    /// Rewrites identifier references for SCL: block-local names gain the <c>#</c> prefix, and names
    /// held in the global data block gain the block qualifier (<c>"GlobalData".Name</c>). A local
    /// declaration always wins over a global of the same name, because the local shadows it.
    /// </summary>
    public static string QualifyReferences(
        string code,
        IEnumerable<string> localNames,
        IEnumerable<string> dbGlobalNames,
        string? dbQualifier)
    {
        var names = new HashSet<string>(localNames, StringComparer.OrdinalIgnoreCase);
        var dbNames = new HashSet<string>(dbGlobalNames, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(code)) return code;
        if (names.Count == 0 && (dbNames.Count == 0 || string.IsNullOrEmpty(dbQualifier))) return code;

        var builder = new StringBuilder(code.Length + 32);
        var index = 0;
        var length = code.Length;
        var previous = '\0';

        while (index < length)
        {
            var current = code[index];

            if (current == '/' && index + 1 < length && code[index + 1] == '/')
            {
                var end = code.IndexOf('\n', index);
                if (end < 0) end = length;
                builder.Append(code, index, end - index);
                index = end;
                continue;
            }

            if (current == '(' && index + 1 < length && code[index + 1] == '*')
            {
                var end = code.IndexOf("*)", index + 2, StringComparison.Ordinal);
                end = end < 0 ? length : end + 2;
                builder.Append(code, index, end - index);
                index = end;
                previous = '\0';
                continue;
            }

            if (current == '\'' || current == '"')
            {
                var quote = current;
                builder.Append(current);
                index++;
                while (index < length)
                {
                    var inner = code[index];
                    builder.Append(inner);
                    index++;
                    if (inner == quote)
                    {
                        if (index < length && code[index] == quote)
                        {
                            builder.Append(quote);
                            index++;
                            continue;
                        }
                        break;
                    }
                }
                previous = quote;
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                var start = index;
                while (index < length && (char.IsLetterOrDigit(code[index]) || code[index] == '_'))
                {
                    index++;
                }

                var identifier = code.Substring(start, index - start);
                var referable = previous != '#' && previous != '.'
                    && !Keywords.Contains(identifier);
                if (referable)
                {
                    if (names.Contains(identifier))
                    {
                        builder.Append('#');
                    }
                    else if (dbNames.Contains(identifier))
                    {
                        builder.Append(dbQualifier);
                    }
                }

                builder.Append(identifier);
                previous = identifier[^1];
                continue;
            }

            builder.Append(current);
            if (!char.IsWhiteSpace(current)) previous = current;
            index++;
        }

        return builder.ToString();
    }
}
