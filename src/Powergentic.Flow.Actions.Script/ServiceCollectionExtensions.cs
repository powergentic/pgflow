using Microsoft.Extensions.DependencyInjection;
using Powergentic.Flow.Core.Abstractions;

namespace Powergentic.Flow.Actions.Script;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScriptAction(this IServiceCollection services)
    {
        services.AddSingleton<IActionRunner, ScriptActionRunner>();
        return services;
    }
}
