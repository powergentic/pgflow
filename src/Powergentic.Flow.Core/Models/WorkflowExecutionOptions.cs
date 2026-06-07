namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowExecutionOptions
{
    public int MaxTransitions { get; set; } = 100;
    public int MaxVisitsPerAction { get; set; } = 50;
}
