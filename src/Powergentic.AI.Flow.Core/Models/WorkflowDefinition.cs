namespace Powergentic.AI.Flow.Core.Models;

public sealed class WorkflowDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public Dictionary<string, object?> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> Env { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public WorkflowExecutionOptions Execution { get; set; } = new();
    public List<WorkflowActionDefinition> Actions { get; set; } = [];
}
