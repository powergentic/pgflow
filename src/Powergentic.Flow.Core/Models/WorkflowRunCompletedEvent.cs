namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowRunCompletedEvent
{
    public required string RunId { get; init; }
    public required string WorkflowName { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required bool Succeeded { get; init; }
    public required int TransitionCount { get; init; }
    public required int TotalActions { get; init; }
    public required int SucceededActions { get; init; }
    public required int FailedActions { get; init; }
    public required int SkippedActions { get; init; }
    public required string LogFolder { get; init; }
}
