using Powergentic.AI.Orchestrator.Core.Abstractions;
using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Core.Services;

public sealed class WorkflowValidator(IEnumerable<IActionRunner>? actionRunners = null) : IWorkflowValidator
{
    private static readonly HashSet<string> SupportedShells = new(["bash", "pwsh"], StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> registeredActionTypes = actionRunners?
        .Select(r => r.ActionType)
        .Where(actionType => !string.IsNullOrWhiteSpace(actionType))
        .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public WorkflowValidationResult Validate(WorkflowDefinition workflow, string? projectFolder = null)
    {
        var result = new WorkflowValidationResult();

        if (workflow.Actions.Count == 0)
        {
            result.Errors.Add("Workflow must define at least one action.");
            return result;
        }

        if (workflow.Execution.MaxTransitions <= 0)
        {
            result.Errors.Add("execution.maxTransitions must be greater than zero.");
        }

        if (workflow.Execution.MaxVisitsPerAction <= 0)
        {
            result.Errors.Add("execution.maxVisitsPerAction must be greater than zero.");
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in workflow.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id))
            {
                result.Errors.Add("Every action must have a non-empty id.");
                continue;
            }

            if (!seenIds.Add(action.Id))
            {
                result.Errors.Add($"Duplicate action id '{action.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(action.Uses))
            {
                result.Errors.Add($"Action '{action.Id}' must specify uses.");
                continue;
            }

            if (registeredActionTypes.Count > 0 && !registeredActionTypes.Contains(action.Uses))
            {
                result.Errors.Add($"Action '{action.Id}' uses unsupported action type '{action.Uses}'.");
            }

            ValidateActionConfiguration(action, projectFolder, result);

            foreach (var transition in action.Next)
            {
                if (string.IsNullOrWhiteSpace(transition.Goto))
                {
                    result.Errors.Add($"Action '{action.Id}' has a next transition without goto.");
                    continue;
                }

                if (string.Equals(transition.Goto, action.Id, StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(transition.When))
                {
                    result.Errors.Add($"Action '{action.Id}' has an unconditional self-loop transition.");
                }
            }
        }

        var actionIds = workflow.Actions
            .Where(a => !string.IsNullOrWhiteSpace(a.Id))
            .Select(a => a.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(workflow.Execution.StartAt) && !actionIds.Contains(workflow.Execution.StartAt))
        {
            result.Errors.Add($"execution.startAt '{workflow.Execution.StartAt}' does not match an action id.");
        }

        foreach (var action in workflow.Actions)
        {
            foreach (var transition in action.Next)
            {
                if (!string.IsNullOrWhiteSpace(transition.Goto) && !actionIds.Contains(transition.Goto))
                {
                    result.Errors.Add($"Action '{action.Id}' transitions to missing action '{transition.Goto}'.");
                }
            }
        }

        return result;
    }

    private static void ValidateActionConfiguration(WorkflowActionDefinition action, string? projectFolder, WorkflowValidationResult result)
    {
        if (string.Equals(action.Uses, "script", StringComparison.OrdinalIgnoreCase))
        {
            ValidateScriptAction(action, projectFolder, result);
            return;
        }

        if (string.Equals(action.Uses, "githubCopilot", StringComparison.OrdinalIgnoreCase))
        {
            ValidateGitHubCopilotAction(action, projectFolder, result);
        }
    }

    private static void ValidateScriptAction(WorkflowActionDefinition action, string? projectFolder, WorkflowValidationResult result)
    {
        var shell = GetString(action.With, "shell");
        if (string.IsNullOrWhiteSpace(shell))
        {
            result.Errors.Add($"Action '{action.Id}' requires 'with.shell'.");
        }
        else if (!SupportedShells.Contains(shell))
        {
            result.Errors.Add($"Action '{action.Id}' has unsupported shell '{shell}'. Expected 'bash' or 'pwsh'.");
        }

        var hasRun = !string.IsNullOrWhiteSpace(GetString(action.With, "run"));
        var file = GetString(action.With, "file");
        var path = GetString(action.With, "path");
        if (!hasRun && string.IsNullOrWhiteSpace(file) && string.IsNullOrWhiteSpace(path))
        {
            result.Errors.Add($"Action '{action.Id}' requires one of 'with.run', 'with.file', or 'with.path'.");
        }

        ValidateReferencedFile(action.Id, "with.file", file, projectFolder, result);
        ValidateReferencedFile(action.Id, "with.path", path, projectFolder, result);
    }

    private static void ValidateGitHubCopilotAction(WorkflowActionDefinition action, string? projectFolder, WorkflowValidationResult result)
    {
        var prompt = GetString(action.With, "prompt");
        var promptFile = GetString(action.With, "promptFile");
        if (string.IsNullOrWhiteSpace(prompt) && string.IsNullOrWhiteSpace(promptFile))
        {
            result.Errors.Add($"Action '{action.Id}' requires 'with.prompt' or 'with.promptFile'.");
        }

        ValidateReferencedFile(action.Id, "with.promptFile", promptFile, projectFolder, result);
    }

    private static void ValidateReferencedFile(string actionId, string fieldName, string? filePath, string? projectFolder, WorkflowValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(projectFolder))
        {
            return;
        }

        var absolutePath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.GetFullPath(Path.Combine(projectFolder, filePath));

        if (!File.Exists(absolutePath))
        {
            result.Errors.Add($"Action '{actionId}' references missing file for {fieldName}: '{filePath}'.");
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var value) ? value?.ToString() : null;
}
