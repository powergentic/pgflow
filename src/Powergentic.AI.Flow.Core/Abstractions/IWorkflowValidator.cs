using Powergentic.AI.Flow.Core.Models;

namespace Powergentic.AI.Flow.Core.Abstractions;

public interface IWorkflowValidator
{
    WorkflowValidationResult Validate(WorkflowDefinition workflow, string? projectFolder = null);
}
