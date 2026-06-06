namespace Powergentic.AI.Orchestrator.Core.Tests;

internal static class ConsoleTestSynchronization
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}
