using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Core.Abstractions;

public interface IWorkflowExecutionObserver
{
    void OnRunStarted(WorkflowExecutionStartedEvent executionStarted);

    void OnActionStarted(WorkflowActionStartedEvent actionStarted);

    void OnActionCompleted(WorkflowActionCompletedEvent actionCompleted);

    void OnPublishedEntry(WorkflowPublishedEntry entry);

    void OnRunCompleted(WorkflowRunCompletedEvent runCompleted);
}
