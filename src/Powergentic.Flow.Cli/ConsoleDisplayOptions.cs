using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Cli;

internal static class ConsoleDisplayOptions
{
    public static WorkflowExecutionDisplayOptions Create(bool enableEnhancedDisplay)
        => new()
        {
            EnableEnhancedDisplay = enableEnhancedDisplay,
            IsInteractiveConsole = !Console.IsOutputRedirected && !Console.IsErrorRedirected,
        };
}
