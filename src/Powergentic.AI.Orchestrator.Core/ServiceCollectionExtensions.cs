using Microsoft.Extensions.DependencyInjection;
using Powergentic.AI.Orchestrator.Core.Abstractions;
using Powergentic.AI.Orchestrator.Core.Services;

namespace Powergentic.AI.Orchestrator.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrchestratorCore(this IServiceCollection services)
    {
        services.AddSingleton<ExpressionEngine>();
        services.AddSingleton<RunLogWriter>();
        services.AddSingleton<IWorkflowLoader, WorkflowLoader>();
        services.AddSingleton<IWorkflowValidator, WorkflowValidator>();
        services.AddSingleton<IWorkflowExecutor, WorkflowExecutor>();
        return services;
    }
}
