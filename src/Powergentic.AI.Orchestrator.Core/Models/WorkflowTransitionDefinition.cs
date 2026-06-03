namespace Powergentic.AI.Orchestrator.Core.Models;

public sealed class WorkflowTransitionDefinition
{
    public string? When { get; set; }
    public string Goto { get; set; } = string.Empty;
}
