using Microsoft.Extensions.Logging;
using Powergentic.AI.Orchestrator.Core.Services;

namespace Powergentic.AI.Orchestrator.Core.Models;

public sealed class ActionExecutionContext
{
    public required ExecutionContext ExecutionContext { get; init; }
    public required WorkflowActionDefinition Action { get; init; }
    public required Dictionary<string, object?> ResolvedInputs { get; init; }
    public required string ActionLogPrefix { get; init; }
    public required string ActionLogDirectory { get; init; }
    public required ExpressionEngine Expressions { get; init; }
    public required ILogger Logger { get; init; }

    public string ProjectFolder => ExecutionContext.ProjectFolder;
    public string TargetWorkingDirectory => ExecutionContext.TargetWorkingDirectory;
    public string RunId => ExecutionContext.RunId;
    public IReadOnlyDictionary<string, string?> Environment => ExecutionContext.Environment;

    public string? GetString(string key)
        => ResolvedInputs.TryGetValue(key, out var value) ? value?.ToString() : null;

    public Dictionary<string, string?> GetStringMap(string key)
    {
        if (!ResolvedInputs.TryGetValue(key, out var value) || value is null)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }

        if (value is Dictionary<string, object?> objectMap)
        {
            return objectMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        if (value is Dictionary<string, string?> stringMap)
        {
            return new(stringMap, StringComparer.OrdinalIgnoreCase);
        }

        return new(StringComparer.OrdinalIgnoreCase);
    }
}
