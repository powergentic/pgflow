using Microsoft.Extensions.DependencyInjection;
using Powergentic.Flow.Core.Abstractions;

namespace Powergentic.Flow.Actions.GitHubCopilot;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubCopilotAction(this IServiceCollection services)
    {
        services.AddSingleton<ICopilotInstallationProbe, CopilotInstallationProbe>();
        services.AddSingleton<ICopilotClientAdapter, CopilotClientAdapter>();
        services.AddSingleton<IActionRunner, GitHubCopilotActionRunner>();
        return services;
    }
}
