using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Core.Abstractions;

public interface IWorkflowLoader
{
    Task<WorkflowDefinition> LoadAsync(string workflowFilePath, CancellationToken cancellationToken = default);
}
