using Powergentic.AI.Flow.Core.Models;

namespace Powergentic.AI.Flow.Core.Abstractions;

public interface ICopilotClientAdapter
{
    Task<CopilotPromptResult> PromptAsync(CopilotPromptRequest request, CancellationToken cancellationToken);
}
