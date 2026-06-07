namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowExecutionStartedEvent
{
    public required string RunId { get; init; }
    public required string WorkflowName { get; init; }
    public required string ProjectFolder { get; init; }
    public required string TargetWorkingDirectory { get; init; }
    public required string WorkflowFilePath { get; init; }
    public required string LogFolder { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required int MaxTransitions { get; init; }
    public required int MaxVisitsPerAction { get; init; }
}
