namespace Powergentic.AI.Orchestrator.Core.Models;

public sealed class ActionLogData
{
    public required string ActionId { get; init; }
    public required string ActionName { get; init; }
    public required string ActionType { get; init; }
    public required Dictionary<string, object?> Inputs { get; init; }
    public required ActionResult Result { get; init; }
}
