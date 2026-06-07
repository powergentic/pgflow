namespace Powergentic.Flow.Core.Models;

public sealed class WorkflowValidationResult
{
    public List<string> Errors { get; } = [];
    public bool Succeeded => Errors.Count == 0;
}
