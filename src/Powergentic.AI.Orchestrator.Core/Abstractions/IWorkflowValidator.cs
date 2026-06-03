using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Core.Abstractions;

public interface IWorkflowValidator
{
    WorkflowValidationResult Validate(WorkflowDefinition workflow, string? projectFolder = null);
}
