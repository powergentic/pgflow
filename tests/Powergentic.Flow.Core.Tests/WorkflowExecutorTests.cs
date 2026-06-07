using Microsoft.Extensions.Logging.Abstractions;
using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;
using Powergentic.Flow.Core.Services;

namespace Powergentic.Flow.Core.Tests;

public class WorkflowExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_UsesTransitionsAndSkipsConditionalAction()
    {
        var projectFolder = CreateProjectFolder();
        var workflowFilePath = Path.Combine(projectFolder, "flow.yml");
        await File.WriteAllTextAsync(workflowFilePath, "name: placeholder");

        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Execution = new WorkflowExecutionOptions
            {
                StartAt = "prepare",
                MaxTransitions = 5,
                MaxVisitsPerAction = 2,
            },
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "prepare",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo prepare"
                    },
                    Outputs = new Dictionary<string, string?>
                    {
                        ["route"] = "go"
                    },
                    Next =
                    [
                        new WorkflowTransitionDefinition
                        {
                            When = "${{ actions.prepare.outputs.route == 'go' }}",
                            Goto = "skipMe"
                        }
                    ]
                },
                new WorkflowActionDefinition
                {
                    Id = "skipMe",
                    Uses = "script",
                    If = "${{ false }}",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo skipped"
                    }
                },
                new WorkflowActionDefinition
                {
                    Id = "finish",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo done"
                    }
                }
            ]
        };

        var executor = CreateExecutor(workflow,
        [
            new TestActionRunner("script", (context, _) => Task.FromResult(new ActionResult
            {
                ActionId = context.Action.Id,
                Status = ActionExecutionStatus.Succeeded,
                Outputs = context.Action.Id == "prepare"
                    ? new Dictionary<string, string?> { ["route"] = "go" }
                    : new Dictionary<string, string?>(),
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            }))
        ]);

        try
        {
            var result = await executor.ExecuteAsync(projectFolder, projectFolder, workflowFilePath);

            Assert.True(result.Succeeded);
            Assert.Equal(projectFolder, result.ProjectFolder);
            Assert.Equal(projectFolder, result.TargetWorkingDirectory);
            Assert.Equal(3, result.ActionResults.Count);
            Assert.Equal(ActionExecutionStatus.Skipped, result.ActionResults.Single(r => r.ActionId == "skipMe").Status);
            Assert.Equal(3, result.TransitionCount);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenActionFails()
    {
        var projectFolder = CreateProjectFolder();
        var workflowFilePath = Path.Combine(projectFolder, "flow.yml");
        await File.WriteAllTextAsync(workflowFilePath, "name: placeholder");

        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "first",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo first"
                    }
                },
                new WorkflowActionDefinition
                {
                    Id = "second",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo second"
                    }
                }
            ]
        };

        var executor = CreateExecutor(workflow,
        [
            new TestActionRunner("script", (context, _) => Task.FromResult(new ActionResult
            {
                ActionId = context.Action.Id,
                Status = ActionExecutionStatus.Failed,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            }))
        ]);

        try
        {
            var result = await executor.ExecuteAsync(projectFolder, projectFolder, workflowFilePath);

            Assert.False(result.Succeeded);
            Assert.Single(result.ActionResults);
            Assert.Equal("first", result.ActionResults[0].ActionId);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenLoopGuardIsExceeded()
    {
        var projectFolder = CreateProjectFolder();
        var workflowFilePath = Path.Combine(projectFolder, "flow.yml");
        await File.WriteAllTextAsync(workflowFilePath, "name: placeholder");

        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Execution = new WorkflowExecutionOptions
            {
                StartAt = "loop",
                MaxTransitions = 2,
                MaxVisitsPerAction = 5,
            },
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "loop",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo loop"
                    },
                    Next = [ new WorkflowTransitionDefinition { Goto = "loop", When = "${{ always() }}" } ]
                }
            ]
        };

        var executor = CreateExecutor(workflow,
        [
            new TestActionRunner("script", (context, _) => Task.FromResult(new ActionResult
            {
                ActionId = context.Action.Id,
                Status = ActionExecutionStatus.Succeeded,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            }))
        ]);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(projectFolder, projectFolder, workflowFilePath));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AppliesInputVariableAndEnvironmentOverrides()
    {
        var projectFolder = CreateProjectFolder();
        var workflowFilePath = Path.Combine(projectFolder, "flow.yml");
        await File.WriteAllTextAsync(workflowFilePath, "name: placeholder");

        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Inputs = new Dictionary<string, object?>
            {
                ["mode"] = "default"
            },
            Variables = new Dictionary<string, object?>
            {
                ["message"] = "original"
            },
            Env = new Dictionary<string, string?>
            {
                ["MODE"] = "${ inputs.mode }-${ variables.message }"
            },
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "first",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo first",
                        ["summary"] = "${ inputs.mode }/${ variables.message }"
                    }
                }
            ]
        };

        ActionExecutionContext? capturedContext = null;
        var executor = CreateExecutor(workflow,
        [
            new TestActionRunner("script", (context, _) =>
            {
                capturedContext = context;
                return Task.FromResult(new ActionResult
                {
                    ActionId = context.Action.Id,
                    Status = ActionExecutionStatus.Succeeded,
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                });
            })
        ]);

        try
        {
            var result = await executor.ExecuteAsync(
                projectFolder,
                projectFolder,
                workflowFilePath,
                new Dictionary<string, object?> { ["mode"] = "cli" },
                new Dictionary<string, object?> { ["message"] = "overridden" },
                new Dictionary<string, string?> { ["EXTERNAL_FLAG"] = "enabled" });

            Assert.True(result.Succeeded);
            Assert.NotNull(capturedContext);
            Assert.Equal(projectFolder, capturedContext!.ExecutionContext.TargetWorkingDirectory);
            Assert.Equal("cli", capturedContext.ExecutionContext.Inputs["mode"]);
            Assert.Equal("overridden", capturedContext.ExecutionContext.Variables["message"]);
            Assert.Equal("cli-overridden", capturedContext.ExecutionContext.Environment["MODE"]);
            Assert.Equal("enabled", capturedContext.ExecutionContext.Environment["EXTERNAL_FLAG"]);
            Assert.Equal("cli/overridden", capturedContext.ResolvedInputs["summary"]);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PublishesResponseOutputByDefaultWhenPublishIsNotConfigured()
    {
        var projectFolder = CreateProjectFolder();
        var workflowFilePath = Path.Combine(projectFolder, "flow.yml");
        await File.WriteAllTextAsync(workflowFilePath, "name: placeholder");

        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "analyze",
                    Name = "Analyze final state",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo analyze"
                    }
                }
            ]
        };

        var executor = CreateExecutor(workflow,
        [
            new TestActionRunner("script", (context, _) => Task.FromResult(new ActionResult
            {
                ActionId = context.Action.Id,
                Status = ActionExecutionStatus.Succeeded,
                Outputs = new Dictionary<string, string?>
                {
                    ["response"] = "Ship the TODO app with the new Bootstrap form flow."
                },
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            }))
        ]);

        try
        {
            var result = await executor.ExecuteAsync(projectFolder, projectFolder, workflowFilePath);

            var publishedEntry = Assert.Single(result.PublishedEntries);
            Assert.Equal("analyze", publishedEntry.ActionId);
            Assert.Equal("Analyze final state", publishedEntry.Title);
            Assert.Equal("Ship the TODO app with the new Bootstrap form flow.", publishedEntry.Content);
            Assert.Contains("console", publishedEntry.To, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("runSummary", publishedEntry.To, StringComparer.OrdinalIgnoreCase);

            var runSummaryPath = Path.Combine(result.LogFolder, "run.json");
            var runSummaryJson = await File.ReadAllTextAsync(runSummaryPath);
            Assert.Contains("publishedEntries", runSummaryJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Analyze final state", runSummaryJson, StringComparison.Ordinal);
            Assert.Contains("Bootstrap form flow", runSummaryJson, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UsesExplicitPublishConfigurationInsteadOfDefaultResponsePublishing()
    {
        var projectFolder = CreateProjectFolder();
        var workflowFilePath = Path.Combine(projectFolder, "flow.yml");
        await File.WriteAllTextAsync(workflowFilePath, "name: placeholder");

        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "analyze",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo analyze"
                    },
                    Publish =
                    [
                        new WorkflowActionPublishDefinition
                        {
                            Title = "Final analysis",
                            From = "${ actions.analyze.outputs.summary }",
                            To = ["runSummary"]
                        }
                    ]
                }
            ]
        };

        var executor = CreateExecutor(workflow,
        [
            new TestActionRunner("script", (context, _) => Task.FromResult(new ActionResult
            {
                ActionId = context.Action.Id,
                Status = ActionExecutionStatus.Succeeded,
                Outputs = new Dictionary<string, string?>
                {
                    ["response"] = "Default response that should not be auto-published.",
                    ["summary"] = "Configured summary for the run report."
                },
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            }))
        ]);

        try
        {
            var result = await executor.ExecuteAsync(projectFolder, projectFolder, workflowFilePath);

            var publishedEntry = Assert.Single(result.PublishedEntries);
            Assert.Equal("Final analysis", publishedEntry.Title);
            Assert.Equal("Configured summary for the run report.", publishedEntry.Content);
            Assert.DoesNotContain("console", publishedEntry.To, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("runSummary", publishedEntry.To, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    private static WorkflowExecutor CreateExecutor(WorkflowDefinition workflow, IEnumerable<IActionRunner> actionRunners)
        => new(
            new FakeWorkflowLoader(workflow),
            new WorkflowValidator(actionRunners),
            actionRunners,
            new ExpressionEngine(),
            new RunLogWriter(),
            new NullLogger<WorkflowExecutor>());

    private static string CreateProjectFolder()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), $"orchestrator-executor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectFolder);
        return projectFolder;
    }

    private sealed class FakeWorkflowLoader(WorkflowDefinition workflow) : IWorkflowLoader
    {
        public Task<WorkflowDefinition> LoadAsync(string workflowFilePath, CancellationToken cancellationToken = default)
            => Task.FromResult(workflow);
    }
}
