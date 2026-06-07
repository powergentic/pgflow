namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowRunResult
{
    public required string RunId { get; init; }
    public required string WorkflowName { get; init; }
    public required string ProjectFolder { get; init; }
    public required string TargetWorkingDirectory { get; init; }
    public required string LogFolder { get; init; }
    public string? ConsoleLogFile { get; init; }
    public bool Succeeded { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public int TransitionCount { get; init; }
    public IReadOnlyList<WorkflowPublishedEntry> PublishedEntries { get; init; } = [];
    public required IReadOnlyList<ActionResult> ActionResults { get; init; }
}
