using Microsoft.Extensions.DependencyInjection;
using Powergentic.AI.Flow.Core.Abstractions;

namespace Powergentic.AI.Flow.Actions.Script;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScriptAction(this IServiceCollection services)
    {
        services.AddSingleton<IActionRunner, ScriptActionRunner>();
        return services;
    }
}
