using ExecutionContextModel = Powergentic.AI.Orchestrator.Core.Models.ExecutionContext;
using Microsoft.Extensions.Logging.Abstractions;
using Powergentic.AI.Orchestrator.Actions.Script;
using Powergentic.AI.Orchestrator.Core.Models;
using Powergentic.AI.Orchestrator.Core.Services;

namespace Powergentic.AI.Orchestrator.Core.Tests;

public class ScriptActionRunnerTests
{
    [Fact]
    public async Task RunAsync_ExecutesProjectFolderScript()
    {
        var projectFolder = CreateProjectFolder();
        Directory.CreateDirectory(Path.Combine(projectFolder, "scripts"));

        var scriptPath = Path.Combine(projectFolder, "scripts", "hello.sh");
        await File.WriteAllTextAsync(scriptPath, "#!/usr/bin/env bash\nset -euo pipefail\necho \"value=ok\" >> \"$ORCHESTRATOR_OUTPUT\"\n");
        try { File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }

        try
        {
            var actionContext = CreateActionContext(projectFolder, new WorkflowActionDefinition
            {
                Id = "hello",
                Uses = "script",
                With = new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["path"] = "scripts/hello.sh"
                }
            }, new Dictionary<string, object?>
            {
                ["shell"] = "bash",
                ["path"] = "scripts/hello.sh"
            });

            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var result = await runner.RunAsync(actionContext, CancellationToken.None);

            Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
            Assert.Equal("ok", result.Outputs["value"]);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AllowsNonZeroExitWhenConfigured()
    {
        var projectFolder = CreateProjectFolder();
        try
        {
            var actionContext = CreateActionContext(projectFolder, new WorkflowActionDefinition
            {
                Id = "hello",
                Uses = "script",
                With = new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["run"] = "exit 7",
                    ["failOnNonZeroExit"] = "false"
                }
            }, new Dictionary<string, object?>
            {
                ["shell"] = "bash",
                ["run"] = "exit 7",
                ["failOnNonZeroExit"] = "false"
            });

            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var result = await runner.RunAsync(actionContext, CancellationToken.None);

            Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
            Assert.Equal(7, result.ExitCode);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ThrowsForMissingProjectFolderScript()
    {
        var projectFolder = CreateProjectFolder();
        try
        {
            var actionContext = CreateActionContext(projectFolder, new WorkflowActionDefinition
            {
                Id = "hello",
                Uses = "script",
                With = new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["path"] = "scripts/missing.sh"
                }
            }, new Dictionary<string, object?>
            {
                ["shell"] = "bash",
                ["path"] = "scripts/missing.sh"
            });

            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            await Assert.ThrowsAsync<FileNotFoundException>(() => runner.RunAsync(actionContext, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    private static string CreateProjectFolder()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), $"orchestrator-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(Path.Combine(projectFolder, "logs", "run-1", "actions"));
        return projectFolder;
    }

    private static ActionExecutionContext CreateActionContext(string projectFolder, WorkflowActionDefinition action, Dictionary<string, object?> resolvedInputs)
    {
        var executionContext = new ExecutionContextModel
        {
            Workflow = new WorkflowDefinition { Name = "demo" },
            ProjectFolder = projectFolder,
            WorkflowFilePath = Path.Combine(projectFolder, "orchestrator.yml"),
            RunId = "run-1",
            LogFolder = Path.Combine(projectFolder, "logs", "run-1"),
            Variables = new Dictionary<string, object?>(),
            Environment = new Dictionary<string, string?>(),
            StartedAt = DateTimeOffset.UtcNow,
        };

        return new ActionExecutionContext
        {
            ExecutionContext = executionContext,
            Action = action,
            ResolvedInputs = resolvedInputs,
            ActionLogDirectory = Path.Combine(projectFolder, "logs", "run-1", "actions"),
            ActionLogPrefix = "01-hello",
            Expressions = new ExpressionEngine(),
            Logger = NullLogger.Instance,
        };
    }
}
