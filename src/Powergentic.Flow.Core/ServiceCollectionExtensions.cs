using Microsoft.Extensions.DependencyInjection;
using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Services;

namespace Powergentic.Flow.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFlowCore(this IServiceCollection services)
    {
        services.AddSingleton<ExpressionEngine>();
        services.AddSingleton<RunLogWriter>();
        services.AddSingleton<IWorkflowLoader, WorkflowLoader>();
        services.AddSingleton<IWorkflowValidator, WorkflowValidator>();
        services.AddSingleton<IWorkflowExecutor, WorkflowExecutor>();
        return services;
    }
}
