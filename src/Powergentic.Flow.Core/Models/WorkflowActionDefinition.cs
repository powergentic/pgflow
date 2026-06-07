namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowActionDefinition
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Uses { get; set; } = string.Empty;
    public string? If { get; set; }
    public Dictionary<string, object?> With { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> Outputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<WorkflowActionPublishDefinition>? Publish { get; set; }
    public List<WorkflowTransitionDefinition> Next { get; set; } = [];
}
