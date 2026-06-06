namespace Powergentic.AI.Orchestrator.Core.Models;

public sealed class ExecutionContext
{
    public required WorkflowDefinition Workflow { get; init; }
    public required string ProjectFolder { get; init; }
    public required string TargetWorkingDirectory { get; init; }
    public required string WorkflowFilePath { get; init; }
    public required string RunId { get; init; }
    public required string LogFolder { get; init; }
    public required Dictionary<string, object?> Inputs { get; init; }
    public required Dictionary<string, object?> Variables { get; init; }
    public required Dictionary<string, string?> Environment { get; init; }
    public Dictionary<string, ActionResult> ActionResults { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ActionVisitCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TransitionCount { get; set; }
    public string? CurrentActionId { get; set; }

    public ActionResult? GetActionResult(string actionId)
        => ActionResults.TryGetValue(actionId, out var result) ? result : null;
}
