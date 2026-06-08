namespace Powergentic.Flow.Core.Models;

public sealed class CopilotPromptRequest
{
    public required string Prompt { get; init; }
    public required string WorkingDirectory { get; init; }
    public string? Agent { get; init; }
    public string? Model { get; init; }
    public string? SystemPrompt { get; init; }
    public bool Streaming { get; init; }
    public bool EnableConfigDiscovery { get; init; } = true;
    public string? GitHubToken { get; init; }
    public Dictionary<string, string?> RequestHeaders { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
