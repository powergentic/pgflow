using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Core.Abstractions;

public interface ICopilotClientAdapter
{
    Task<CopilotPromptResult> PromptAsync(CopilotPromptRequest request, CancellationToken cancellationToken);
}
