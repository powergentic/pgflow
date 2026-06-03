using System.Text.Json;
using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Core.Services;

public sealed class RunLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public WorkflowPaths CreatePaths(string projectFolder, string workflowFilePath, string runId)
    {
        var logsFolder = Path.Combine(projectFolder, "logs");
        var runFolder = Path.Combine(logsFolder, runId);
        var actionsFolder = Path.Combine(runFolder, "actions");
        Directory.CreateDirectory(actionsFolder);

        return new WorkflowPaths
        {
            ProjectFolder = projectFolder,
            WorkflowFilePath = workflowFilePath,
            LogsFolder = logsFolder,
            RunFolder = runFolder,
            ActionsFolder = actionsFolder,
        };
    }

    public async Task WriteResolvedWorkflowAsync(WorkflowPaths paths, WorkflowDefinition workflow, CancellationToken cancellationToken)
    {
        var content = JsonSerializer.Serialize(workflow, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(paths.RunFolder, "workflow-resolved.json"), content, cancellationToken);
    }

    public async Task WriteActionLogAsync(ActionLogData logData, string actionFilePath, CancellationToken cancellationToken)
    {
        var content = JsonSerializer.Serialize(logData, JsonOptions);
        await File.WriteAllTextAsync(actionFilePath, content, cancellationToken);
    }

    public async Task WriteRunSummaryAsync(WorkflowRunResult result, string runFilePath, CancellationToken cancellationToken)
    {
        var content = JsonSerializer.Serialize(result, JsonOptions);
        await File.WriteAllTextAsync(runFilePath, content, cancellationToken);
    }
}
