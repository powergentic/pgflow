namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowActionStartedEvent
{
    public required string RunId { get; init; }
    public required string WorkflowName { get; init; }
    public required string ActionId { get; init; }
    public required string ActionName { get; init; }
    public required string ActionType { get; init; }
    public required DateTimeOffset FlowStartedAt { get; init; }
    public required DateTimeOffset ActionStartedAt { get; init; }
    public required int TransitionCount { get; init; }
    public required int MaxTransitions { get; init; }
    public required int VisitCount { get; init; }
    public required int MaxVisitsPerAction { get; init; }
    public required string TargetWorkingDirectory { get; init; }
    public required string LogFolder { get; init; }
}
