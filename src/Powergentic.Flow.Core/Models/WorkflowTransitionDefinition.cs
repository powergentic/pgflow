namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowTransitionDefinition
{
    public string? When { get; set; }
    public string Goto { get; set; } = string.Empty;
}
