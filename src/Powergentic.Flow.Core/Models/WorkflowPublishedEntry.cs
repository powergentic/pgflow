namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowPublishedEntry
{
    public string ActionId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<string> To { get; init; } = [];
}
