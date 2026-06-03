using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Core.Abstractions;

public interface ICopilotClientAdapter
{
    Task<CopilotPromptResult> PromptAsync(CopilotPromptRequest request, CancellationToken cancellationToken);
}
