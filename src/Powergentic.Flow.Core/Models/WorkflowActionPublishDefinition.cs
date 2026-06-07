namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowActionPublishDefinition
{
    public string? Title { get; set; }
    public string From { get; set; } = string.Empty;
    public List<string> To { get; set; } = [];
    public string? If { get; set; }
    public int? MaxLength { get; set; }
}
