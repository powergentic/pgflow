namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowActionCompletedEvent
{
    public required string RunId { get; init; }
    public required string WorkflowName { get; init; }
    public required string ActionId { get; init; }
    public required string ActionName { get; init; }
    public required string ActionType { get; init; }
    public required ActionExecutionStatus Status { get; init; }
    public required string Summary { get; init; }
    public required DateTimeOffset FlowStartedAt { get; init; }
    public required DateTimeOffset ActionStartedAt { get; init; }
    public required DateTimeOffset ActionCompletedAt { get; init; }
    public required int TransitionCount { get; init; }
    public required int MaxTransitions { get; init; }
    public required int VisitCount { get; init; }
    public required int MaxVisitsPerAction { get; init; }
    public int? ExitCode { get; init; }
}
