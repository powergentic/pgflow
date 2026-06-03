using Microsoft.Extensions.DependencyInjection;
using Powergentic.AI.Orchestrator.Core.Abstractions;

namespace Powergentic.AI.Orchestrator.Actions.Script;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScriptAction(this IServiceCollection services)
    {
        services.AddSingleton<IActionRunner, ScriptActionRunner>();
        return services;
    }
}
