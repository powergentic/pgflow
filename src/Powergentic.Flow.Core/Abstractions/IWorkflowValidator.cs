using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Core.Abstractions;

public interface IWorkflowValidator
{
    WorkflowValidationResult Validate(WorkflowDefinition workflow, string? projectFolder = null);
}
