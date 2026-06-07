using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Core.Abstractions;

public interface IWorkflowLoader
{
    Task<WorkflowDefinition> LoadAsync(string workflowFilePath, CancellationToken cancellationToken = default);
}
