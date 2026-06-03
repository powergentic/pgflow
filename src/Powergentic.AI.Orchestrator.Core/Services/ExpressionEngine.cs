using System.Text.RegularExpressions;
using ExecutionContextModel = Powergentic.AI.Orchestrator.Core.Models.ExecutionContext;
using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Core.Services;

public sealed class ExpressionEngine
{
    private static readonly Regex InterpolationRegex = new(@"\$\{\s*(?<expr>[^}]+?)\s*\}", RegexOptions.Compiled);

    public string InterpolateString(string? input, ExecutionContextModel context)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        return InterpolationRegex.Replace(input, match =>
        {
            var expr = match.Groups["expr"].Value.Trim();
            var resolved = ResolvePath(expr, context);
            return resolved?.ToString() ?? string.Empty;
        });
    }

    public object? InterpolateValue(object? value, ExecutionContextModel context)
        => value switch
        {
            null => null,
            string s => InterpolateString(s, context),
            Dictionary<object, object?> yamlMap => yamlMap.ToDictionary(kvp => kvp.Key.ToString() ?? string.Empty, kvp => InterpolateValue(kvp.Value, context), StringComparer.OrdinalIgnoreCase),
            Dictionary<string, object?> objectMap => objectMap.ToDictionary(kvp => kvp.Key, kvp => InterpolateValue(kvp.Value, context), StringComparer.OrdinalIgnoreCase),
            Dictionary<string, string?> stringMap => stringMap.ToDictionary(kvp => kvp.Key, kvp => InterpolateString(kvp.Value, context), StringComparer.OrdinalIgnoreCase),
            IEnumerable<object?> list => list.Select(item => InterpolateValue(item, context)).ToList(),
            _ => value
        };

    public bool EvaluateCondition(string? condition, ExecutionContextModel context)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        var expression = condition.Trim();
        if (expression.StartsWith("${{") && expression.EndsWith("}}"))
        {
            expression = expression[3..^2].Trim();
        }

        return EvaluateBooleanExpression(expression, context);
    }

    public Dictionary<string, object?> ResolveInputs(Dictionary<string, object?> source, ExecutionContextModel context)
        => source.ToDictionary(kvp => kvp.Key, kvp => InterpolateValue(kvp.Value, context), StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> ResolveOutputs(Dictionary<string, string?> source, ExecutionContextModel context)
        => source.ToDictionary(kvp => kvp.Key, kvp => InterpolateString(kvp.Value, context), StringComparer.OrdinalIgnoreCase);

    private bool EvaluateBooleanExpression(string expression, ExecutionContextModel context)
    {
        expression = expression.Trim();

        if (expression.StartsWith("(") && expression.EndsWith(")") && IsBalanced(expression[1..^1]))
        {
            return EvaluateBooleanExpression(expression[1..^1], context);
        }

        var orParts = SplitTopLevel(expression, "||");
        if (orParts.Count > 1)
        {
            return orParts.Any(part => EvaluateBooleanExpression(part, context));
        }

        var andParts = SplitTopLevel(expression, "&&");
        if (andParts.Count > 1)
        {
            return andParts.All(part => EvaluateBooleanExpression(part, context));
        }

        if (expression.StartsWith('!'))
        {
            return !EvaluateBooleanExpression(expression[1..], context);
        }

        foreach (var op in new[] { "==", "!=" })
        {
            var idx = IndexOfTopLevel(expression, op);
            if (idx >= 0)
            {
                var left = EvaluateOperand(expression[..idx], context);
                var right = EvaluateOperand(expression[(idx + op.Length)..], context);
                var equals = string.Equals(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);
                return op == "==" ? equals : !equals;
            }
        }

        return CoerceToBool(EvaluateOperand(expression, context));
    }

    private object? EvaluateOperand(string operand, ExecutionContextModel context)
    {
        operand = operand.Trim();

        if (string.Equals(operand, "success()", StringComparison.OrdinalIgnoreCase))
        {
            return context.ActionResults.Values.All(r => r.Status != ActionExecutionStatus.Failed);
        }

        if (string.Equals(operand, "failure()", StringComparison.OrdinalIgnoreCase))
        {
            return context.ActionResults.Values.Any(r => r.Status == ActionExecutionStatus.Failed);
        }

        if (string.Equals(operand, "always()", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (bool.TryParse(operand, out var boolValue))
        {
            return boolValue;
        }

        if (string.Equals(operand, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if ((operand.StartsWith('"') && operand.EndsWith('"')) || (operand.StartsWith('\'') && operand.EndsWith('\'')))
        {
            return operand[1..^1];
        }

        return ResolvePath(operand, context);
    }

    private object? ResolvePath(string path, ExecutionContextModel context)
    {
        var trimmed = path.Trim();
        if (trimmed.StartsWith("variables.", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveDictionaryPath(context.Variables, trimmed[10..]);
        }

        if (trimmed.StartsWith("env.", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveDictionaryPath(context.Environment, trimmed[4..]);
        }

        if (trimmed.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[8..].ToLowerInvariant() switch
            {
                "projectfolder" => context.ProjectFolder,
                "runid" => context.RunId,
                "logfolder" => context.LogFolder,
                "currentactionid" => context.CurrentActionId,
                _ => null,
            };
        }

        if (trimmed.StartsWith("actions.", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = trimmed[8..];
            var firstDot = remainder.IndexOf('.');
            if (firstDot < 0)
            {
                return null;
            }

            var actionId = remainder[..firstDot];
            var actionPath = remainder[(firstDot + 1)..];
            var actionResult = context.GetActionResult(actionId);
            if (actionResult is null)
            {
                return null;
            }

            if (actionPath.StartsWith("outputs.", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveDictionaryPath(actionResult.Outputs, actionPath[8..]);
            }

            if (string.Equals(actionPath, "status", StringComparison.OrdinalIgnoreCase))
            {
                return actionResult.Status.ToString().ToLowerInvariant();
            }
        }

        return trimmed;
    }

    private static object? ResolveDictionaryPath<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> source, string key)
        where TKey : notnull
    {
        if (typeof(TKey) != typeof(string))
        {
            return null;
        }

        var stringMap = source.ToDictionary(kvp => kvp.Key!.ToString()!, kvp => (object?)kvp.Value, StringComparer.OrdinalIgnoreCase);
        return stringMap.TryGetValue(key, out var value) ? value : null;
    }

    private static bool CoerceToBool(object? value)
        => value switch
        {
            null => false,
            bool b => b,
            string s when bool.TryParse(s, out var b) => b,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true,
        };

    private static List<string> SplitTopLevel(string expression, string separator)
    {
        var parts = new List<string>();
        var start = 0;
        var depth = 0;

        for (var index = 0; index <= expression.Length - separator.Length; index++)
        {
            switch (expression[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
            }

            if (depth == 0 && expression.AsSpan(index).StartsWith(separator, StringComparison.Ordinal))
            {
                parts.Add(expression[start..index].Trim());
                start = index + separator.Length;
                index += separator.Length - 1;
            }
        }

        if (parts.Count == 0)
        {
            return [expression.Trim()];
        }

        parts.Add(expression[start..].Trim());
        return parts;
    }

    private static int IndexOfTopLevel(string expression, string token)
    {
        var depth = 0;
        for (var index = 0; index <= expression.Length - token.Length; index++)
        {
            switch (expression[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
            }

            if (depth == 0 && expression.AsSpan(index).StartsWith(token, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsBalanced(string expression)
    {
        var depth = 0;
        foreach (var ch in expression)
        {
            if (ch == '(') depth++;
            if (ch == ')') depth--;
            if (depth < 0) return false;
        }

        return depth == 0;
    }
}
