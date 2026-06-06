namespace Powergentic.AI.Flow.Core.Models;

public sealed class WorkflowExecutionOptions
{
    public string? StartAt { get; set; }
    public int MaxTransitions { get; set; } = 100;
    public int MaxVisitsPerAction { get; set; } = 20;
}
