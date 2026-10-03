using System.Text;

namespace TriconexToTia.Generators;

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

    public static string PrefixLocalVariables(string code, IEnumerable<string> variableNames)
    {
        var names = new HashSet<string>(variableNames, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0 || string.IsNullOrEmpty(code)) return code;

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
                var shouldPrefix = previous != '#' && previous != '.'
                    && names.Contains(identifier) && !Keywords.Contains(identifier);
                if (shouldPrefix) builder.Append('#');
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
