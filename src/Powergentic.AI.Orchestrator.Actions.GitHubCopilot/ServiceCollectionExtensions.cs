using Microsoft.Extensions.DependencyInjection;
using Powergentic.AI.Orchestrator.Core.Abstractions;

namespace Powergentic.AI.Orchestrator.Actions.GitHubCopilot;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubCopilotAction(this IServiceCollection services)
    {
        services.AddSingleton<ICopilotClientAdapter, CopilotClientAdapter>();
        services.AddSingleton<IActionRunner, GitHubCopilotActionRunner>();
        return services;
    }
}
