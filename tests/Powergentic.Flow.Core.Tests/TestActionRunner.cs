using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Core.Tests;

internal sealed class TestActionRunner(string actionType, Func<ActionExecutionContext, CancellationToken, Task<ActionResult>> execute) : IActionRunner
{
    public string ActionType { get; } = actionType;

    public Task<ActionResult> RunAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        => execute(context, cancellationToken);
}
