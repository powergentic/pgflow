using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Core.Services;

public sealed class WorkflowExecutionObserver(WorkflowExecutionConsole executionConsole) : IWorkflowExecutionObserver
{
    public void OnRunStarted(WorkflowExecutionStartedEvent executionStarted)
    {
        executionConsole.StartTranscript(executionStarted.LogFolder);
        executionConsole.RecordRunStarted(executionStarted);
    }

    public void OnActionStarted(WorkflowActionStartedEvent actionStarted)
        => executionConsole.RecordActionStarted(actionStarted);

    public void OnActionCompleted(WorkflowActionCompletedEvent actionCompleted)
        => executionConsole.RecordActionCompleted(actionCompleted);

    public void OnPublishedEntry(WorkflowPublishedEntry entry)
        => executionConsole.RecordPublishedEntry(entry);

    public void OnRunCompleted(WorkflowRunCompletedEvent runCompleted)
        => executionConsole.RecordRunCompleted(runCompleted);
}
