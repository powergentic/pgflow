using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Core.Abstractions;

public interface IActionRunner
{
    string ActionType { get; }
    Task<ActionResult> RunAsync(ActionExecutionContext context, CancellationToken cancellationToken);
}
