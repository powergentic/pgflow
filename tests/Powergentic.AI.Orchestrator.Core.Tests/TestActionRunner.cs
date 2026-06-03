using Powergentic.AI.Orchestrator.Core.Abstractions;
using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Core.Tests;

internal sealed class TestActionRunner(string actionType, Func<ActionExecutionContext, CancellationToken, Task<ActionResult>> execute) : IActionRunner
{
    public string ActionType { get; } = actionType;

    public Task<ActionResult> RunAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        => execute(context, cancellationToken);
}
