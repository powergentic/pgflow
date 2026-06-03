namespace Powergentic.AI.Orchestrator.Core.Models;

public sealed class WorkflowValidationResult
{
    public List<string> Errors { get; } = [];
    public bool Succeeded => Errors.Count == 0;
}
