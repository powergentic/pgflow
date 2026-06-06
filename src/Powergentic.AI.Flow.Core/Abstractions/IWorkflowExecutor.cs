using Powergentic.AI.Flow.Core.Models;

namespace Powergentic.AI.Flow.Core.Abstractions;

public interface IWorkflowExecutor
{
    Task<WorkflowRunResult> ExecuteAsync(
        string projectFolder,
        string targetWorkingDirectory,
        string workflowFilePath,
        IReadOnlyDictionary<string, object?>? inputOverrides = null,
        IReadOnlyDictionary<string, object?>? variableOverrides = null,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        CancellationToken cancellationToken = default);
}
