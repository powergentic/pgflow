using Powergentic.AI.Flow.Core.Models;

namespace Powergentic.AI.Flow.Core.Abstractions;

public interface IActionRunner
{
    string ActionType { get; }
    Task<ActionResult> RunAsync(ActionExecutionContext context, CancellationToken cancellationToken);
}
