namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowPaths
{
    public required string ProjectFolder { get; init; }
    public required string WorkflowFilePath { get; init; }
    public required string LogsFolder { get; init; }
    public required string RunFolder { get; init; }
    public required string ActionsFolder { get; init; }
    public required string ConsoleLogFile { get; init; }
}
