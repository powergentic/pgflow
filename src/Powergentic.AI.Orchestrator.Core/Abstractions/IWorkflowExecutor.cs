using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Core.Abstractions;

public interface IWorkflowExecutor
{
    Task<WorkflowRunResult> ExecuteAsync(
        string projectFolder,
        string workflowFilePath,
        IReadOnlyDictionary<string, object?>? variableOverrides = null,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        CancellationToken cancellationToken = default);
}
