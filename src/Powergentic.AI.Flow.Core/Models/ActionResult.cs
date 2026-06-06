namespace Powergentic.AI.Flow.Core.Models;

public sealed class ActionResult
{
    public string ActionId { get; init; } = string.Empty;
    public ActionExecutionStatus Status { get; set; } = ActionExecutionStatus.Succeeded;
    public int? ExitCode { get; set; }
    public string Summary { get; set; } = string.Empty;
    public Dictionary<string, string?> Outputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? StdOutFile { get; set; }
    public string? StdErrFile { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public bool Succeeded => Status == ActionExecutionStatus.Succeeded;
}
