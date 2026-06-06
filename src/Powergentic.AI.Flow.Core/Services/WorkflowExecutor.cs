using Microsoft.Extensions.Logging;
using ExecutionContextModel = Powergentic.AI.Flow.Core.Models.ExecutionContext;
using Powergentic.AI.Flow.Core.Abstractions;
using Powergentic.AI.Flow.Core.Models;

namespace Powergentic.AI.Flow.Core.Services;

public sealed class WorkflowExecutor(
    IWorkflowLoader loader,
    IWorkflowValidator validator,
    IEnumerable<IActionRunner> actionRunners,
    ExpressionEngine expressions,
    RunLogWriter logWriter,
    ILogger<WorkflowExecutor> logger) : IWorkflowExecutor
{
    public async Task<WorkflowRunResult> ExecuteAsync(
        string projectFolder,
        string targetWorkingDirectory,
        string workflowFilePath,
        IReadOnlyDictionary<string, object?>? inputOverrides = null,
        IReadOnlyDictionary<string, object?>? variableOverrides = null,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        CancellationToken cancellationToken = default)
    {
        var workflow = await loader.LoadAsync(workflowFilePath, cancellationToken);
        ApplyInputOverrides(workflow, inputOverrides);
        ApplyVariableOverrides(workflow, variableOverrides);

        var validation = validator.Validate(workflow, projectFolder);
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
        }

        var runId = $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH-mm-ssZ}_{Guid.NewGuid():N}";
        var paths = logWriter.CreatePaths(projectFolder, workflowFilePath, runId);
        var environment = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString(), StringComparer.OrdinalIgnoreCase);

        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        var inputs = new Dictionary<string, object?>(workflow.Inputs, StringComparer.OrdinalIgnoreCase);
        var variables = new Dictionary<string, object?>(workflow.Variables, StringComparer.OrdinalIgnoreCase);
        var startedAt = DateTimeOffset.UtcNow;

        foreach (var env in workflow.Env)
        {
            environment[env.Key] = expressions.InterpolateString(env.Value, new ExecutionContextModel
            {
                Workflow = workflow,
                ProjectFolder = projectFolder,
                TargetWorkingDirectory = targetWorkingDirectory,
                WorkflowFilePath = workflowFilePath,
                RunId = runId,
                LogFolder = paths.RunFolder,
                Inputs = inputs,
                Variables = variables,
                Environment = environment,
                StartedAt = startedAt,
            });
        }

        var context = new ExecutionContextModel
        {
            Workflow = workflow,
            ProjectFolder = projectFolder,
            TargetWorkingDirectory = targetWorkingDirectory,
            WorkflowFilePath = workflowFilePath,
            RunId = runId,
            LogFolder = paths.RunFolder,
            Inputs = inputs,
            Variables = variables,
            Environment = environment,
            StartedAt = startedAt,
        };
        var publishedEntries = new List<WorkflowPublishedEntry>();

        await logWriter.WriteResolvedWorkflowAsync(paths, workflow, cancellationToken);

        var actionsById = workflow.Actions.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        var currentActionId = workflow.Execution.StartAt ?? workflow.Actions[0].Id;

        while (!string.IsNullOrWhiteSpace(currentActionId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            context.TransitionCount++;
            if (context.TransitionCount > workflow.Execution.MaxTransitions)
            {
                throw new InvalidOperationException($"Workflow exceeded maxTransitions of {workflow.Execution.MaxTransitions}.");
            }

            if (!actionsById.TryGetValue(currentActionId, out var action))
            {
                throw new InvalidOperationException($"Unknown action '{currentActionId}'.");
            }

            context.CurrentActionId = action.Id;
            context.ActionVisitCounts[action.Id] = context.ActionVisitCounts.GetValueOrDefault(action.Id) + 1;
            if (context.ActionVisitCounts[action.Id] > workflow.Execution.MaxVisitsPerAction)
            {
                throw new InvalidOperationException($"Action '{action.Id}' exceeded maxVisitsPerAction of {workflow.Execution.MaxVisitsPerAction}.");
            }

            var shouldRun = expressions.EvaluateCondition(action.If, context);
            ActionResult result;
            if (!shouldRun)
            {
                result = new ActionResult
                {
                    ActionId = action.Id,
                    Status = ActionExecutionStatus.Skipped,
                    Summary = "Action skipped by condition.",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                };
            }
            else
            {
                var resolvedInputs = expressions.ResolveInputs(action.With, context);
                var runner = actionRunners.FirstOrDefault(r => string.Equals(r.ActionType, action.Uses, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"No action runner registered for '{action.Uses}'.");

                var ordinal = workflow.Actions.FindIndex(a => string.Equals(a.Id, action.Id, StringComparison.OrdinalIgnoreCase)) + 1;
                var prefix = $"{ordinal:00}-{action.Id}";
                var actionContext = new ActionExecutionContext
                {
                    ExecutionContext = context,
                    Action = action,
                    ResolvedInputs = resolvedInputs,
                    ActionLogDirectory = paths.ActionsFolder,
                    ActionLogPrefix = prefix,
                    Expressions = expressions,
                    Logger = logger,
                };

                logger.LogInformation("Running action {ActionId} ({ActionType})", action.Id, action.Uses);
                result = await runner.RunAsync(actionContext, cancellationToken);
                result.StartedAt = result.StartedAt == default ? DateTimeOffset.UtcNow : result.StartedAt;
                result.CompletedAt = result.CompletedAt == default ? DateTimeOffset.UtcNow : result.CompletedAt;
            }

            context.ActionResults[action.Id] = result;

            foreach (var output in expressions.ResolveOutputs(action.Outputs, context))
            {
                if (!result.Outputs.ContainsKey(output.Key))
                {
                    result.Outputs[output.Key] = output.Value;
                }
            }

            var actionPublishedEntries = CreatePublishedEntries(action, context);
            publishedEntries.AddRange(actionPublishedEntries);
            WritePublishedConsoleEntries(actionPublishedEntries);

            var actionLogPath = Path.Combine(paths.ActionsFolder, $"{workflow.Actions.FindIndex(a => a.Id == action.Id) + 1:00}-{action.Id}.json");
            await logWriter.WriteActionLogAsync(new ActionLogData
            {
                ActionId = action.Id,
                ActionName = action.Name ?? action.Id,
                ActionType = action.Uses,
                Inputs = shouldRun ? expressions.ResolveInputs(action.With, context) : new Dictionary<string, object?>(),
                Result = result,
            }, actionLogPath, cancellationToken);

            if (result.Status == ActionExecutionStatus.Failed)
            {
                break;
            }

            currentActionId = ResolveNextActionId(workflow, action, context);
        }

        context.CompletedAt = DateTimeOffset.UtcNow;
        var runResult = new WorkflowRunResult
        {
            RunId = context.RunId,
            WorkflowName = workflow.Name,
            ProjectFolder = context.ProjectFolder,
            TargetWorkingDirectory = context.TargetWorkingDirectory,
            LogFolder = paths.RunFolder,
            StartedAt = context.StartedAt,
            CompletedAt = context.CompletedAt.Value,
            TransitionCount = context.TransitionCount,
            PublishedEntries = publishedEntries,
            ActionResults = context.ActionResults.Values.OrderBy(r => workflow.Actions.FindIndex(a => a.Id == r.ActionId)).ToList(),
            Succeeded = context.ActionResults.Values.All(r => r.Status != ActionExecutionStatus.Failed),
        };

        await logWriter.WriteRunSummaryAsync(runResult, Path.Combine(paths.RunFolder, "run.json"), cancellationToken);
        return runResult;
    }

    private static void ApplyInputOverrides(WorkflowDefinition workflow, IReadOnlyDictionary<string, object?>? inputOverrides)
    {
        if (inputOverrides is null)
        {
            return;
        }

        foreach (var pair in inputOverrides)
        {
            workflow.Inputs[pair.Key] = pair.Value;
        }
    }

    private static void ApplyVariableOverrides(WorkflowDefinition workflow, IReadOnlyDictionary<string, object?>? variableOverrides)
    {
        if (variableOverrides is null)
        {
            return;
        }

        foreach (var pair in variableOverrides)
        {
            workflow.Variables[pair.Key] = pair.Value;
        }
    }

    private List<WorkflowPublishedEntry> CreatePublishedEntries(WorkflowActionDefinition action, ExecutionContextModel context)
    {
        var entries = new List<WorkflowPublishedEntry>();

        foreach (var publish in action.Publish)
        {
            if (!expressions.EvaluateCondition(publish.If, context))
            {
                continue;
            }

            var content = expressions.InterpolateString(publish.From, context);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (publish.MaxLength is > 0 && content.Length > publish.MaxLength.Value)
            {
                content = content[..publish.MaxLength.Value];
            }

            entries.Add(new WorkflowPublishedEntry
            {
                ActionId = action.Id,
                Title = string.IsNullOrWhiteSpace(publish.Title) ? action.Name ?? action.Id : publish.Title,
                Content = content,
                To = publish.To.ToArray(),
            });
        }

        return entries;
    }

    private static void WritePublishedConsoleEntries(IEnumerable<WorkflowPublishedEntry> entries)
    {
        foreach (var entry in entries.Where(entry => entry.To.Contains("console", StringComparer.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"===== {entry.Title} =====");
            Console.WriteLine(entry.Content);
            Console.WriteLine();
        }
    }

    private string? ResolveNextActionId(WorkflowDefinition workflow, WorkflowActionDefinition action, ExecutionContextModel context)
    {
        foreach (var transition in action.Next)
        {
            if (string.IsNullOrWhiteSpace(transition.When) || expressions.EvaluateCondition(transition.When, context))
            {
                return transition.Goto;
            }
        }

        var index = workflow.Actions.FindIndex(a => string.Equals(a.Id, action.Id, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < workflow.Actions.Count - 1 ? workflow.Actions[index + 1].Id : null;
    }
}
