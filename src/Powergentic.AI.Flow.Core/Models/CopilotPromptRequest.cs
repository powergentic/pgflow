namespace Powergentic.AI.Flow.Core.Models;

public sealed class CopilotPromptRequest
{
    public required string Prompt { get; init; }
    public required string WorkingDirectory { get; init; }
    public string? Model { get; init; }
    public string? SystemPrompt { get; init; }
    public bool Streaming { get; init; }
    public string? GitHubToken { get; init; }
    public Dictionary<string, string?> RequestHeaders { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
