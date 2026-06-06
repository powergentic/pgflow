using Powergentic.AI.Flow.Core.Models;

namespace Powergentic.AI.Flow.Core.Abstractions;

public interface IWorkflowLoader
{
    Task<WorkflowDefinition> LoadAsync(string workflowFilePath, CancellationToken cancellationToken = default);
}
