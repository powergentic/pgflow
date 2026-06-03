using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Core.Abstractions;

public interface IActionRunner
{
    string ActionType { get; }
    Task<ActionResult> RunAsync(ActionExecutionContext context, CancellationToken cancellationToken);
}
