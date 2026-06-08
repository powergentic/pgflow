namespace Powergentic.Flow.Core.Models;

public sealed class CopilotPromptResult
{
    public required string ResponseText { get; init; }
    public string? MessageId { get; init; }
    public string? SessionId { get; init; }
    public string? Model { get; init; }
    public bool TimedOut { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public Dictionary<string, object?> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
