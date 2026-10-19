using System.Text;
using System.Text.RegularExpressions;
using TriconexToTia.Core.Enums;
using TriconexToTia.Core.Models;

namespace TriconexToTia.Generators;

/// <summary>
/// Turns a parsed FBD network into SCL statements.
///
/// The network is emitted in dependency order (every item after the items that drive it) and
/// otherwise in sheet/row/column order, so the result is stable and readable. Two kinds of call are
/// distinguished, because they emit differently:
/// <list type="bullet">
/// <item>block instances (<c>TON</c>, <c>R_TRIG</c>, project FBs) get a local instance declaration;</item>
/// <item>stateless functions (<c>AND</c>, <c>MOVE</c>, <c>DINT_TO_DWORD</c>) have no instance: a
/// single result is assigned to its target and a function used as an input is inlined.</item>
/// </list>
/// </summary>
public sealed class FbdSclEmitter
{
    private const string CommentPrefix = "// ";

    private readonly StructuredLogic _logic;
    private readonly IReadOnlyCollection<string> _localNames;
    private readonly HashSet<string> _localNamesSet;
    private readonly HashSet<string> _parameterNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _parameterTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyCollection<string> _dbGlobalNames;
    private readonly IReadOnlyDictionary<string, string> _globalTypes;
    private readonly string? _dbQualifier;
    private readonly Dictionary<string, BlockCall> _callsBySheetKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VariableAssignment>> _assignmentsByCall = new(StringComparer.Ordinal);

    /// <summary>Matches the <c>{Formal}</c> placeholders of a rule's inline expression.</summary>
    private static readonly Regex Placeholder = new(
        @"\{(?<formal>[A-Za-z_][A-Za-z0-9_]*)\}",
        RegexOptions.Compiled);
    private readonly HashSet<string> _inlineConsumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _instanceBySheetKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _instanceNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _notes = new();

    /// <summary>Sheet keys whose output is being inlined right now, used to cut feedback cycles.</summary>
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);

    public FbdSclEmitter(
        StructuredLogic logic,
        IEnumerable<Variable> locals,
        IEnumerable<string>? dbGlobals = null,
        string? dbQualifier = null,
        IReadOnlyDictionary<string, string>? globalTypes = null)
    {
        _logic = logic;
        var localList = locals
            .Where(variable => variable.Direction is VariableDirection.Input or VariableDirection.Output
                or VariableDirection.InOut or VariableDirection.Local)
            .ToList();

        _localNamesSet = localList
            .Select(variable => variable.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _localNames = _localNamesSet;

        // Output and in-out pins are "parameters": TIA warns when one is read before it is
        // assigned, so a self-referential assignment has to be routed through a hold variable.
        foreach (var variable in localList.Where(variable =>
                     variable.Direction is VariableDirection.Output or VariableDirection.InOut))
        {
            _parameterNames.Add(variable.Name);
            _parameterTypes[variable.Name] = variable.EffectiveTypeName;
        }

        // Globals held in the generated data block are referenced through it, not by bare name.
        _dbGlobalNames = dbGlobals?.ToArray() ?? Array.Empty<string>();
        _dbQualifier = dbQualifier;

        // Project-wide type of every global. The same signal can be wired with another type in a
        // diagram; the site type recorded on the item is coerced to this declared type and back.
        _globalTypes = globalTypes is { Count: > 0 }
            ? new Dictionary<string, string>(globalTypes, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var call in logic.Items.OfType<BlockCall>())
        {
            _callsBySheetKey[call.SheetKey] = call;
        }

        // A TriStation call element carries no result connections: the consumers are separate
        // outVariable items that name the call's localId and the formal parameter they read.
        foreach (var assignment in logic.Items.OfType<VariableAssignment>().Where(a => a.IsBlockOutput))
        {
            var producer = Key(assignment.RefSheetNumber, assignment.RefLocalId!);
            if (!_assignmentsByCall.TryGetValue(producer, out var list))
            {
                list = new List<VariableAssignment>();
                _assignmentsByCall[producer] = list;
            }

            list.Add(assignment);
        }

        foreach (var call in logic.Items.OfType<BlockCall>())
        {
            foreach (var argument in call.Inputs.Concat(call.InOuts))
            {
                foreach (var source in argument.Sources.Where(source => source.IsBlockOutput))
                {
                    _inlineConsumers.Add(Key(source.RefSheetNumber, source.RefLocalId!));
                }
            }
        }
    }

    /// <summary>One output of a call together with the variable that receives it.</summary>
    private sealed record ResultBinding(string Formal, string Target, string RawTarget, string? SiteType);

    /// <summary>Local instance declarations required by the emitted calls, in emission order.</summary>
    public List<(string Name, string Type)> InstanceDeclarations { get; } = new();

    /// <summary>
    /// Extra static locals introduced to hold the previous value of a parameter that a network reads
    /// and writes in the same statement (a seal-in), so the parameter is assigned before it is read.
    /// </summary>
    public List<(string Name, string Type)> HoldDeclarations { get; } = new();

    /// <summary>Things that need a human decision, surfaced in the report.</summary>
    public IReadOnlyList<string> Notes => _notes;

    public string Emit()
    {
        var builder = new StringBuilder();

        foreach (var sheetGroup in _logic.Items.GroupBy(item => item.SheetNumber).OrderBy(group => group.Key))
        {
            if (_logic.SheetTitles.Count > 0)
            {
                var title = sheetGroup.Key <= _logic.SheetTitles.Count
                    ? _logic.SheetTitles[sheetGroup.Key - 1]
                    : null;
                var titleLines = string.IsNullOrWhiteSpace(title)
                    ? Array.Empty<string>()
                    : title
                        .Replace("\r\n", "\n", StringComparison.Ordinal)
                        .Split('\n')
                        .Select(line => line.Trim())
                        .ToArray();

                // A SHEET_TITLE macro is a multi-line CDATA value. Only its first line belongs on the
                // "Sheet N:" comment; every further line has to carry the comment prefix too, or TIA
                // parses the leftovers as SCL instructions.
                if (titleLines.Length == 0)
                {
                    builder.Append(CommentPrefix).Append($"Sheet {sheetGroup.Key}");
                    builder.AppendLine();
                }
                else
                {
                    builder.Append(CommentPrefix).Append($"Sheet {sheetGroup.Key}").Append(": ").Append(titleLines[0]);
                    builder.AppendLine();
                    foreach (var line in titleLines.Skip(1))
                    {
                        builder.AppendLine(line.Length == 0 ? "//" : CommentPrefix + line);
                    }
                }
            }

            var emitted = false;
            foreach (var item in Order(sheetGroup.ToList()))
            {
                var text = Render(item);
                if (text is null) continue;
                if (emitted) builder.AppendLine();
                builder.Append(text);
                emitted = true;
            }

            if (emitted) builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Stable topological order: dependencies first, then sheet/row/column. A cycle falls back to
    /// geometric order with a note rather than dropping items.
    /// </summary>
    private List<LogicItem> Order(List<LogicItem> items)
    {
        var remaining = items.ToDictionary(item => item.SheetKey, item => item, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<LogicItem>(items.Count);

        while (remaining.Count > 0)
        {
            var ready = remaining.Values
                .Where(item => Dependencies(item).All(key => done.Contains(key) || !remaining.ContainsKey(key)))
                .OrderBy(item => item.Y)
                .ThenBy(item => item.X)
                .ThenBy(item => item.LocalId, StringComparer.Ordinal)
                .ToList();

            if (ready.Count == 0)
            {
                var cycle = string.Join(", ", remaining.Values.OrderBy(item => item.Y)
                    .ThenBy(item => item.X)
                    .Select(item => $"{item.GetType().Name}@{item.SheetKey}"));
                _notes.Add(
                    $"Sheet cycle detected; kept diagram order for: {cycle}. Check the affected signals by hand.");
                ordered.AddRange(remaining.Values.OrderBy(item => item.Y).ThenBy(item => item.X)
                    .ThenBy(item => item.LocalId, StringComparer.Ordinal));
                break;
            }

            foreach (var item in ready)
            {
                ordered.Add(item);
                done.Add(item.SheetKey);
                remaining.Remove(item.SheetKey);
            }
        }

        return ordered;
    }

    private IEnumerable<string> Dependencies(LogicItem item)
    {
        switch (item)
        {
            case VariableAssignment assignment when assignment.IsBlockOutput:
                yield return Key(assignment.RefSheetNumber, assignment.RefLocalId!);
                break;

            case BlockCall call:
                foreach (var argument in call.Inputs.Concat(call.InOuts))
                {
                    foreach (var source in argument.Sources.Where(source => source.IsBlockOutput))
                    {
                        var key = Key(source.RefSheetNumber, source.RefLocalId!);
                        if (_callsBySheetKey.TryGetValue(key, out var producer) && !producer.IsFunction) yield return key;
                    }
                }

                break;
        }
    }

    private static string Key(int sheet, string localId) => $"{sheet}:{localId}";

    private string? Render(LogicItem item)
    {
        switch (item)
        {
            case LogicComment comment:
                return RenderComment(comment.Text, comment.Annotation);

            case VariableReference reference:
                // An inVariable only carries a value for the items that read it, so it needs no
                // statement of its own; keep the annotation as a comment so nothing is lost.
                return RenderComment(null, reference.Annotation);

            case VariableAssignment assignment:
                return RenderAssignment(assignment);

            case BlockCall call:
                return RenderCall(call);

            default:
                return null;
        }
    }

    private string? RenderComment(string? text, string? annotation)
    {
        var body = !string.IsNullOrWhiteSpace(text) ? text : annotation;
        if (string.IsNullOrWhiteSpace(body)) return null;

        // TriStation stores sheet macros such as "%SHEET_TITLE" in the annotation; those are not
        // human-readable text and would only add noise.
        if (body.TrimStart().StartsWith('%')) return null;

        return string.Join(Environment.NewLine, body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim().Length == 0 ? "//" : CommentPrefix + line.Trim()));
    }

    private string? RenderAssignment(VariableAssignment assignment)
    {
        // When a function call drives this assignment, the function statement already performs the
        // assignment, so emitting it twice would overwrite the value.
        if (assignment.IsBlockOutput)
        {
            var producerKey = Key(assignment.RefSheetNumber, assignment.RefLocalId!);
            if (_callsBySheetKey.TryGetValue(producerKey, out var producer) && producer.IsFunction)
            {
                // The function statement already performs the assignment.
                return null;
            }
        }

        if (TryRenderHoldAssignment(assignment) is { } holdStatement)
        {
            return holdStatement;
        }

        var value = assignment.IsBlockOutput
            ? BlockOutput(assignment.RefSheetNumber, assignment.RefLocalId!, assignment.RefFormalParameter)
            : Qualify(assignment.Expression ?? string.Empty);

        // The type the value carries: the producing output's pin type, or the type of the copied
        // expression. The diagram's declared site type is the fallback when neither is known.
        var fromType = assignment.IsBlockOutput
            ? TypeOfBlockOutput(assignment.RefSheetNumber, assignment.RefLocalId!, assignment.RefFormalParameter)
            : TypeOfExpression(assignment.Expression);

        if (string.IsNullOrWhiteSpace(value))
        {
            _notes.Add(
                $"sheet {assignment.SheetNumber} localId {assignment.LocalId}: " +
                $"no source for assignment to \"{assignment.Target}\"");
            value = "<unspecified>";
        }

        var comment = string.IsNullOrWhiteSpace(assignment.Annotation)
            ? string.Empty
            : $" {CommentPrefix}{assignment.Annotation}";

        value = CoerceToTarget(assignment.Target, value, fromType ?? assignment.SourceTypeName);

        return $"{Qualify(assignment.Target)} := {value};{comment}";
    }

    /// <summary>
    /// A block output that is both read and written in the same network (a seal-in) makes TIA warn
    /// that the parameter "might not be initialized". The previous value is copied through a new
    /// static local so the output is always assigned before it is read. Returns null when the
    /// assignment is not a seal-in on a parameter.
    /// </summary>
    private string? TryRenderHoldAssignment(VariableAssignment assignment)
    {
        if (assignment.IsBlockOutput || string.IsNullOrWhiteSpace(assignment.Expression))
        {
            return null;
        }

        var bare = BareName(assignment.Target);
        if (!_parameterNames.Contains(bare))
        {
            return null;
        }

        var pattern = $@"(?<![A-Za-z0-9_#]){Regex.Escape(bare)}(?![A-Za-z0-9_])";
        if (!Regex.IsMatch(assignment.Expression, pattern))
        {
            return null;
        }

        var hold = ReserveHold(bare);
        var rewritten = Regex.Replace(assignment.Expression, pattern, hold);
        var targetType = _parameterTypes.TryGetValue(bare, out var declared) ? declared : null;
        var holdValue = Coerce(Qualify(rewritten), TypeOfExpression(assignment.Expression), targetType)
            ?? Qualify(rewritten);

        var comment = string.IsNullOrWhiteSpace(assignment.Annotation)
            ? string.Empty
            : $" {CommentPrefix}{assignment.Annotation}";

        return $"{Qualify(hold)} := {holdValue};{Environment.NewLine}"
            + $"{Qualify(assignment.Target)} := {Qualify(hold)};{comment}";
    }

    /// <summary>Allocates a unique static local that carries a seal-in parameter's previous value.</summary>
    private string ReserveHold(string bare)
    {
        var baseName = bare + "_HOLD";
        var name = baseName;
        var suffix = 1;
        while (_localNamesSet.Contains(name) || _instanceNames.Contains(name))
        {
            name = baseName + "_" + suffix.ToString(GenerationHelpers.Culture);
            suffix++;
        }

        _localNamesSet.Add(name);
        var type = _parameterTypes.TryGetValue(bare, out var declared) ? declared : "Bool";
        HoldDeclarations.Add((name, type));
        return name;
    }

    private string? RenderCall(BlockCall call)
    {
        var typeName = call.MappedTypeName ?? call.TypeName;

        var arguments = new List<string>();
        foreach (var argument in call.Inputs.Concat(call.InOuts))
        {
            if (argument.Sources.Count == 0) continue;
            arguments.Add(RenderArgument(argument));
        }

        var omitted = call.Inputs.Concat(call.InOuts).Count(argument => argument.Sources.Count == 0);
        if (omitted > 0)
        {
            _notes.Add(
                $"{call.TypeName} on sheet {call.SheetNumber} (localId {call.LocalId}): {omitted} input(s) are not " +
                "connected in the diagram; the Siemens block keeps its start value for them.");
        }

// An unmapped call is already reported as a manual action by the mapping stage; the
            // inline TODO comment in the generated code carries the same information, so it must
            // not be added to the report a second time.
            if (call.TypeStatus == ConversionStatus.Manual)
            {
                // Documented inline only.
            }

        return call.IsFunction
            ? RenderFunctionCall(call, typeName, arguments)
            : RenderInstanceCall(call, typeName, arguments);
    }

    /// <summary>
    /// A function has no instance. Its results are written through the Siemens <c>OUT =&gt;</c>
    /// parameter form, or assigned directly when the function has a single result.
    /// </summary>
    private string? RenderFunctionCall(BlockCall call, string typeName, List<string> arguments)
    {
        var bindings = ResultBindings(call);

        if (bindings.Count == 0)
        {
            // The result is only read by other calls, so it is inlined there instead.
            if (_inlineConsumers.Contains(call.SheetKey)) return null;
            _notes.Add(
                $"{call.TypeName} on sheet {call.SheetNumber}: no output is wired to a variable and no other " +
                "call reads its result; verify that the call can be dropped.");
            return null;
        }

        // A call mapped to an inline expression has no Siemens name and no output parameters, so every
        // reader of the result is assigned the expression directly.
        if (!string.IsNullOrWhiteSpace(call.InlineExpression))
        {
            return RenderBoundExpression(call, bindings, RenderInlineCall(call));
        }

        // n-ary logic functions render as an infix operator expression: TIA rejects the keyword-style
        // OR(IN1 := ..., IN2 := ...) call form entirely.
        if (!string.IsNullOrWhiteSpace(call.Operator))
        {
            return RenderBoundExpression(call, bindings, RenderOperatorCall(call));
        }

        if (bindings.Count == 1 && call.Outputs.Count <= 1)
        {
            // TIA conversion functions reject the named IN1 formal, so they are always called
            // positionally with the wired value.
            if (IsConversion(typeName))
            {
                var values = WiredValues(call);
                var converted = $"{typeName}({string.Join(", ", values)})";
                converted = CoerceToTarget(bindings[0].RawTarget, converted, bindings[0].SiteType);
                return $"{bindings[0].Target} := {converted};{Describe(call)}";
            }

            var argumentsText = arguments.Count == 0 ? string.Empty : $"({string.Join(", ", arguments)})";
            var callText = CoerceToTarget(bindings[0].RawTarget, $"{typeName}{argumentsText}", bindings[0].SiteType);
            return $"{bindings[0].Target} := {callText};{Describe(call)}";
        }

        var assignments = new List<string>(arguments);
        var index = 0;
        foreach (var binding in bindings)
        {
            var name = string.IsNullOrWhiteSpace(binding.Formal)
                ? "OUT" + index.ToString(GenerationHelpers.Culture)
                : binding.Formal;
            assignments.Add($"{name} => {binding.Target}");
            index++;
        }

        return $"{typeName}({string.Join(", ", assignments)});{Describe(call)}";
    }

    /// <summary>
    /// Pairs the output formals of a call with the variables that read them, keeping the diagram's
    /// formal names so the generated call matches the Siemens library.
    /// </summary>
    private List<ResultBinding> ResultBindings(BlockCall call)
    {
        var bindings = new List<ResultBinding>();

        if (!_assignmentsByCall.TryGetValue(call.SheetKey, out var assignments))
        {
            return bindings;
        }

        var used = new HashSet<VariableAssignment>();
        var formals = call.Outputs
            .Select(output => output.FormalParameter)
            .Where(formal => !string.IsNullOrWhiteSpace(formal))
            .ToList();

        foreach (var formal in formals)
        {
            foreach (var assignment in assignments)
            {
                if (used.Contains(assignment)) continue;
                if (!string.Equals(assignment.RefFormalParameter, formal, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                used.Add(assignment);
                bindings.Add(new ResultBinding(
                    formal!, Qualify(assignment.Target), assignment.Target, assignment.SourceTypeName));
            }
        }

        // Results read by an outVariable without a matching output formal still need a target.
        foreach (var assignment in assignments)
        {
            if (!used.Add(assignment)) continue;
            var formal = string.IsNullOrWhiteSpace(assignment.RefFormalParameter) ? null : assignment.RefFormalParameter;
            bindings.Add(new ResultBinding(
                formal!, Qualify(assignment.Target), assignment.Target, assignment.SourceTypeName));
        }

        return bindings;
    }

    private string RenderInstanceCall(BlockCall call, string typeName, List<string> arguments)
    {
        var instance = InstanceOf(call);

        // A block without wired inputs is still invoked: the static instance keeps its state and
        // runs once per scan, which is what the TriStation diagram means. TIA, however, refuses a
        // timer or edge call whose mandatory inputs are left off, so the unwired state is written
        // out explicitly.
        string argumentsText;
        if (arguments.Count == 0 && TimerTypes.Contains(typeName))
        {
            argumentsText = "(IN := FALSE, PT := T#0s)";
        }
        else if (arguments.Count == 0 && EdgeTypes.Contains(typeName))
        {
            argumentsText = "(CLK := FALSE)";
        }
        else
        {
            argumentsText = arguments.Count == 0 ? "()" : $"({string.Join(", ", arguments)})";
        }

        return $"{instance}{argumentsText};{Describe(call)}";
    }

    private static readonly HashSet<string> TimerTypes = new(StringComparer.OrdinalIgnoreCase) { "TON", "TOF", "TP" };

    private static readonly HashSet<string> EdgeTypes = new(StringComparer.OrdinalIgnoreCase) { "R_TRIG", "F_TRIG" };

    private string RenderArgument(CallArgument argument)
    {
        var name = argument.MappedFormalParameter ?? argument.FormalParameter;
        var value = RenderValue(argument);

        return string.IsNullOrWhiteSpace(name) ? value : $"{name} := {value}";
    }

    /// <summary>The value of one formal: every wired signal, converted if the rule asks for it.</summary>
    private string RenderValue(CallArgument argument)
    {
        var rendered = argument.Sources
            .Select(source => RenderSource(source, argument.ExpectedType))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        if (rendered.Count == 0) return "<unspecified>";

        var value = rendered.Count == 1 ? rendered[0] : string.Join(" | ", rendered);

        return string.IsNullOrWhiteSpace(argument.ValueConversion) ? value : $"{argument.ValueConversion}({value})";
    }

    /// <summary>
    /// Renders one wired signal. When the signal's type differs from the type the consuming pin
    /// expects, it is converted so the pin accepts it. A signal whose type is not known is left
    /// untouched.
    /// </summary>
    private string RenderSource(SignalSource source, string? expectedType)
    {
        var value = ValueOf(source, expectedType);
        if (string.IsNullOrWhiteSpace(expectedType)) return value;
        return Coerce(value, TypeOfSignal(source), expectedType) ?? value;
    }

    /// <summary>
    /// Wraps a value that flows into a global target when the value's type differs from the type the
    /// global actually declares.
    /// </summary>
    private string CoerceToTarget(string target, string value, string? fromType)
    {
        if (string.IsNullOrWhiteSpace(fromType)) return value;
        if (!_globalTypes.TryGetValue(BareName(target), out var tagType)) return value;
        return Coerce(value, fromType, tagType) ?? value;
    }

    /// <summary>The declared type of the signal a source carries, when it can be determined.</summary>
    private string? TypeOfSignal(SignalSource source)
    {
        if (source.IsBlockOutput)
        {
            return source.RefLocalId is null
                ? null
                : TypeOfBlockOutput(source.RefSheetNumber, source.RefLocalId, source.RefFormalParameter);
        }

        return TypeOfExpression(source.Expression);
    }

    /// <summary>The type produced by the output a consumer reads.</summary>
    private string? TypeOfBlockOutput(int sheet, string localId, string? formalParameter)
    {
        if (!_callsBySheetKey.TryGetValue(Key(sheet, localId), out var producer)) return null;

        var output = FindOutput(producer, formalParameter);
        if (output?.ExpectedType is not null) return output.ExpectedType;

        // A single-output block whose output formal is unnamed still pins the type it produces.
        if (producer.Outputs.Count == 1) return producer.Outputs[0].ExpectedType;

        return null;
    }

    private static CallArgument? FindOutput(BlockCall call, string? formalParameter)
    {
        if (string.IsNullOrWhiteSpace(formalParameter)) return null;

        var direct = call.Outputs.FirstOrDefault(output =>
            string.Equals(output.FormalParameter, formalParameter, StringComparison.OrdinalIgnoreCase));
        if (direct is not null) return direct;

        if (call.MappedOutputFormals.TryGetValue(formalParameter, out var mapped))
        {
            return call.Outputs.FirstOrDefault(output =>
                string.Equals(output.FormalParameter, mapped, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    /// <summary>The declared type of a plain global expression, when it names one.</summary>
    private string? TypeOfExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        var trimmed = expression.Trim();
        if (string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return "Bool";
        }

        var name = BareName(expression.Trim());
        return _globalTypes.TryGetValue(name, out var tagType) ? tagType : null;
    }

    private static string BareName(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];
        return name.TrimStart('#').Trim();
    }

    /// <summary>IEC type names a conversion function can be built from.</summary>
    private static readonly HashSet<string> ConvertibleTokens = new(StringComparer.Ordinal)
    {
        "BOOL", "BYTE", "WORD", "DWORD", "INT", "DINT", "SINT", "UINT", "UDINT", "USINT",
        "REAL", "LREAL", "TIME", "S5TIME", "CHAR", "DATE",
    };

    /// <summary>
    /// Wraps <paramref name="value"/> in <c>FROM_TO_TO</c> when both types are primitive and differ.
    /// Returns null when no conversion is needed or possible (unknown or user-defined types).
    /// </summary>
    private static string? Coerce(string value, string? fromType, string? toType)
    {
        var from = TypeToken(fromType);
        var to = TypeToken(toType);
        if (from is null || to is null || from == to) return null;
        return $"{from}_TO_{to}({value})";
    }

    private static string? TypeToken(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;

        var builder = new StringBuilder(type.Length);
        foreach (var character in type)
        {
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToUpperInvariant(character));
        }

        var token = builder.ToString();
        return ConvertibleTokens.Contains(token) ? token : null;
    }

    /// <summary>
    /// Builds the replacement expression for a call mapped to <c>rule.inline</c>, substituting each
    /// <c>{Formal}</c> with the wired signal. A placeholder with no wired signal is reported rather
    /// than silently dropped, because it would otherwise produce an expression that does not compile.
    /// </summary>
    private string RenderInlineCall(BlockCall call)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in call.Inputs.Concat(call.InOuts))
        {
            if (argument.Sources.Count == 0 || string.IsNullOrWhiteSpace(argument.FormalParameter)) continue;
            values[argument.FormalParameter] = RenderValue(argument);
        }

        var missing = new List<string>();
        var text = Placeholder.Replace(call.InlineExpression!, match =>
        {
            if (values.TryGetValue(match.Groups["formal"].Value, out var value)) return value;
            missing.Add(match.Groups["formal"].Value);
            return "<unspecified>";
        });

        if (missing.Count > 0)
        {
            _notes.Add(
                $"{call.TypeName} on sheet {call.SheetNumber}: input(s) {string.Join(", ", missing)} are not " +
                "connected, so the generated expression cannot be evaluated; wire them or hand-fix the call.");
        }

        return text;
    }

    /// <summary>The expression that carries a signal, inlining function calls.</summary>
    private string ValueOf(SignalSource source, string? expectedType = null) => source.IsBlockOutput
        ? BlockOutput(source.RefSheetNumber, source.RefLocalId!, source.RefFormalParameter, expectedType)
        : Qualify(source.Expression ?? string.Empty);

    /// <summary>Every wired input value of a call, in wiring order.</summary>
    private List<string> WiredValues(BlockCall call) =>
        call.Inputs.Concat(call.InOuts)
            .Where(argument => argument.Sources.Count > 0)
            .Select(RenderValue)
            .ToList();

    /// <summary>TIA names its conversion functions with <c>_TO_</c>; those never take a named formal.</summary>
    private static bool IsConversion(string name) =>
        name.Contains("_TO_", StringComparison.OrdinalIgnoreCase);

    /// <summary>An infix operator expression built from the call's wired inputs.</summary>
    private string RenderOperatorCall(BlockCall call)
    {
        var values = WiredValues(call);
        if (values.Count == 0)
        {
            return $"<{call.TypeName}: no input is connected>";
        }

        if (values.Count == 1) return $"({values[0]})";
        return $"({string.Join($" {call.Operator} ", values)})";
    }

    /// <summary>
    /// Assigns one shared expression to every result binding. A target that the expression itself
    /// reads is written last, so the earlier assignments still see the value the diagram read as an
    /// input (the FBD block updates all its outputs from the old inputs simultaneously).
    /// </summary>
    private string RenderBoundExpression(BlockCall call, List<ResultBinding> bindings, string expression)
    {
        var ordered = bindings
            .OrderBy(binding => IsReferencedIn(expression, binding.Target) ? 1 : 0)
            .ToList();

        if (bindings.Count(binding => IsReferencedIn(expression, binding.Target)) > 1)
        {
            _notes.Add(
                $"{call.TypeName} on sheet {call.SheetNumber}: more than one target of the shared " +
                "expression is also read by it, so the old-value update order cannot be preserved " +
                "for all of them; check the affected assignments by hand.");
        }

        // An infix operator (AND/OR/XOR) produces a Bool; an inline/function rule may declare the
        // type it produces; otherwise the diagram's site type is the only clue to the type the
        // expression carries.
        var fromType = !string.IsNullOrWhiteSpace(call.Operator)
            ? "Bool"
            : call.ResultType ?? TypeOfExpression(expression);

        return string.Join(
            Environment.NewLine,
            ordered.Select(binding => RenderBoundAssignment(call, binding, expression, fromType)));
    }

    /// <summary>Renders one output binding, routing a self-read parameter through a hold local.</summary>
    private string RenderBoundAssignment(BlockCall call, ResultBinding binding, string expression, string? fromType)
    {
        var bare = binding.Target[(binding.Target.LastIndexOf('.') + 1)..].TrimStart('#');
        if (_parameterNames.Contains(bare) && IsReferencedIn(expression, binding.Target))
        {
            var hold = ReserveHold(bare);
            var pattern = $@"(?<![A-Za-z0-9_])#?{Regex.Escape(bare)}(?![A-Za-z0-9_])";
            var rewritten = Regex.Replace(expression, pattern, Qualify(hold));
            var holdValue = Coerce(rewritten, fromType, _parameterTypes.GetValueOrDefault(bare)) ?? rewritten;
            return $"{Qualify(hold)} := {holdValue};{Environment.NewLine}{binding.Target} := {Qualify(hold)};";
        }

        var value = CoerceToTarget(binding.RawTarget, expression, fromType ?? binding.SiteType);
        return $"{binding.Target} := {value};";
    }

    /// <summary>True when the expression reads the (last segment of the) target by name.</summary>
    private static bool IsReferencedIn(string expression, string target)
    {
        var bare = target[(target.LastIndexOf('.') + 1)..].TrimStart('#');
        if (bare.Length == 0) return false;
        return Regex.IsMatch(
            expression,
            $@"(?<![A-Za-z0-9_])#?{Regex.Escape(bare)}(?![A-Za-z0-9_])");
    }

    private string BlockOutput(int sheet, string localId, string? formalParameter, string? expectedType = null)
    {
        var key = Key(sheet, localId);
        if (!_callsBySheetKey.TryGetValue(key, out var call))
        {
            _notes.Add(
                $"sheet {sheet} localId {localId}: signal comes from an item outside this block and " +
                "was not converted.");
            return "<unspecified>";
        }

        // A function's output is inlined at its consumer, so a feedback cycle would recurse without
        // end. Cut the cycle at the first repeated key and flag it rather than overflowing the stack.
        if (!_resolving.Add(key))
        {
            // A TriStation feedback wire means the consumer reads the signal's value from the
            // previous scan. The variable the cut output is bound to keeps its value across scans,
            // so reading it reproduces the feedback without recursing.
            if (_assignmentsByCall.TryGetValue(key, out var bindings) && bindings.Count > 0)
            {
                var retained = Qualify(bindings[0].Target);
                _notes.Add(
                    $"sheet {sheet} localId {localId}: circular reference (an output feeds back into itself); " +
                    $"the cycle was cut by reading the retained (previous-scan) value {retained}. " +
                    "Check the affected signals by hand.");
                return retained;
            }

            _notes.Add(
                $"sheet {sheet} localId {localId}: circular reference (an output feeds back into itself) and " +
                "no variable holds its value; a default was substituted. Check the affected signals by hand.");
            return DefaultValueFor(call, expectedType);
        }

        try
        {
            return ResolveBlockOutput(call, formalParameter);
        }
        finally
        {
            _resolving.Remove(key);
        }
    }

    /// <summary>A compile-safe default for a cut signal, typed from its producing block.</summary>
    private static string DefaultValueFor(BlockCall call, string? expectedType)
    {
        var type = expectedType;
        if (string.IsNullOrWhiteSpace(type))
        {
            type = call.Outputs
                .Select(output => output.ExpectedType)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? call.ResultType;
        }

        if (type is null) return "FALSE";
        return type.ToUpperInvariant() switch
        {
            "BOOL" => "FALSE",
            "REAL" or "LREAL" => "0.0",
            "TIME" => "T#0ms",
            _ => "0",
        };
    }

    private string ResolveBlockOutput(BlockCall call, string? formalParameter)
    {
        if (call.IsFunction)
        {
            return InlineFunction(call);
        }

        var instance = InstanceOf(call);

        // The consuming side names the output by its TriStation formal, which the Siemens instruction
        // may spell differently (TDD_I's Q1 becomes TOF's Q).
        if (!string.IsNullOrWhiteSpace(formalParameter)
            && call.MappedOutputFormals.TryGetValue(formalParameter, out var mapped))
        {
            formalParameter = mapped;
        }

        return string.IsNullOrWhiteSpace(formalParameter) ? instance : $"{instance}.{formalParameter}";
    }

    /// <summary>Text that reproduces a function call where its result is needed.</summary>
    private string InlineFunction(BlockCall call)
    {
        if (!string.IsNullOrWhiteSpace(call.InlineExpression)) return RenderInlineCall(call);
        if (!string.IsNullOrWhiteSpace(call.Operator)) return RenderOperatorCall(call);

        var typeName = call.MappedTypeName ?? call.TypeName;

        // TIA conversion functions reject the named IN1 formal, so they are always called positionally.
        if (IsConversion(typeName))
        {
            return $"{typeName}({string.Join(", ", WiredValues(call))})";
        }

        var arguments = call.Inputs.Concat(call.InOuts)
            .Where(argument => argument.Sources.Count > 0)
            .Select(RenderArgument)
            .ToList();
        return arguments.Count == 0 ? typeName + "()" : $"{typeName}({string.Join(", ", arguments)})";
    }

    private string InstanceOf(BlockCall call)
    {
        if (_instanceBySheetKey.TryGetValue(call.SheetKey, out var existing)) return existing;

        // TriStation stores the instance name on the block element and declares it as a local of the
        // enclosing POU, so reusing it keeps the generated declaration from duplicating the one the
        // source interface already contributes. The annotation is a free-text label, not an
        // identifier, and must not become a variable name.
        var baseName = string.IsNullOrWhiteSpace(call.InstanceName) ? call.TypeName : call.InstanceName;
        var candidate = GenerationHelpers.SanitizeIdentifier(baseName);
        if (string.IsNullOrEmpty(candidate)) candidate = "FB";
        if (!char.IsLetter(candidate[0]) && candidate[0] != '_') candidate = "_" + candidate;

        // An instance may not share the name of its type, or the declaration becomes ambiguous.
        var typeName = GenerationHelpers.SanitizeIdentifier(call.MappedTypeName ?? call.TypeName);
        if (string.Equals(candidate, typeName, StringComparison.OrdinalIgnoreCase))
        {
            candidate += "_" + GenerationHelpers.SanitizeIdentifier(call.LocalId);
        }

        var name = candidate;
        var suffix = 1;
        while (!_instanceNames.Add(name))
        {
            name = candidate + "_" + suffix.ToString(GenerationHelpers.Culture);
            suffix++;
        }

        _instanceBySheetKey[call.SheetKey] = name;
        InstanceDeclarations.Add((name, call.MappedTypeName ?? call.TypeName));
        return name;
    }

    private string Describe(BlockCall call)
    {
        var parts = new List<string>();
        if (call.TypeStatus == ConversionStatus.Manual) parts.Add("TODO manual mapping required");
        if (!string.IsNullOrWhiteSpace(call.MappingNote)) parts.Add(CommentPrefix + call.MappingNote);
        if (!string.IsNullOrWhiteSpace(call.Annotation)) parts.Add(CommentPrefix + call.Annotation);
        return parts.Count == 0 ? string.Empty : " " + string.Join(" ", parts);
    }

    /// <summary>
    /// Rewrites a source expression so local names carry the SCL <c>#</c> prefix and names held in
    /// the global data block carry its qualifier. Addressed globals, literals, keywords and already
    /// qualified paths are left alone.
    /// </summary>
    private string Qualify(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return string.Empty;
        return SclCodeTransformer.QualifyReferences(expression, _localNames, _dbGlobalNames, _dbQualifier);
    }
}