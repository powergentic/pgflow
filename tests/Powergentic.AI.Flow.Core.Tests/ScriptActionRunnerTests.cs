using ExecutionContextModel = Powergentic.AI.Flow.Core.Models.ExecutionContext;
using Microsoft.Extensions.Logging.Abstractions;
using Powergentic.AI.Flow.Actions.Script;
using Powergentic.AI.Flow.Core.Models;
using Powergentic.AI.Flow.Core.Services;

namespace Powergentic.AI.Flow.Core.Tests;

public class ScriptActionRunnerTests
{
    [Fact]
    public async Task RunAsync_ExecutesProjectFolderScriptInTargetWorkingDirectory()
    {
        var projectFolder = CreateProjectFolder();
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);
        Directory.CreateDirectory(Path.Combine(projectFolder, "scripts"));

        var scriptPath = Path.Combine(projectFolder, "scripts", "hello.sh");
        await File.WriteAllTextAsync(scriptPath, "#!/usr/bin/env bash\nset -euo pipefail\npwd > working-dir.txt\necho \"target=$ORCHESTRATOR_TARGET_WORKING_DIRECTORY\" >> \"$ORCHESTRATOR_OUTPUT\"\n");
        try { File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }

        try
        {
            var actionContext = CreateActionContext(projectFolder, targetWorkingDirectory, new WorkflowActionDefinition
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
            var reportedWorkingDirectory = (await File.ReadAllTextAsync(Path.Combine(targetWorkingDirectory, "working-dir.txt"))).Trim();

            Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
            Assert.Equal(targetWorkingDirectory, result.Metadata["workingDirectory"]);
            Assert.Equal(targetWorkingDirectory, result.Outputs["target"]);
            Assert.True(Directory.Exists(reportedWorkingDirectory));
            Assert.True(File.Exists(Path.Combine(reportedWorkingDirectory, "working-dir.txt")));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ResolvesRelativeWorkingDirectoryFromTargetWorkingDirectory()
    {
        var projectFolder = CreateProjectFolder();
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(Path.Combine(targetWorkingDirectory, "nested"));

        try
        {
            var actionContext = CreateActionContext(projectFolder, targetWorkingDirectory, new WorkflowActionDefinition
            {
                Id = "hello",
                Uses = "script",
                With = new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["run"] = "pwd > nested.txt",
                    ["workingDirectory"] = "nested"
                }
            }, new Dictionary<string, object?>
            {
                ["shell"] = "bash",
                ["run"] = "pwd > nested.txt",
                ["workingDirectory"] = "nested"
            });

            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var result = await runner.RunAsync(actionContext, CancellationToken.None);
            var expected = Path.Combine(targetWorkingDirectory, "nested");
            var reportedWorkingDirectory = (await File.ReadAllTextAsync(Path.Combine(expected, "nested.txt"))).Trim();

            Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
            Assert.Equal(expected, result.Metadata["workingDirectory"]);
            Assert.True(Directory.Exists(reportedWorkingDirectory));
            Assert.True(File.Exists(Path.Combine(reportedWorkingDirectory, "nested.txt")));
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
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);
        try
        {
            var actionContext = CreateActionContext(projectFolder, targetWorkingDirectory, new WorkflowActionDefinition
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
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);
        try
        {
            var actionContext = CreateActionContext(projectFolder, targetWorkingDirectory, new WorkflowActionDefinition
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

    private static ActionExecutionContext CreateActionContext(string projectFolder, string targetWorkingDirectory, WorkflowActionDefinition action, Dictionary<string, object?> resolvedInputs)
    {
        var executionContext = new ExecutionContextModel
        {
            Workflow = new WorkflowDefinition { Name = "demo" },
            ProjectFolder = projectFolder,
            TargetWorkingDirectory = targetWorkingDirectory,
            WorkflowFilePath = Path.Combine(projectFolder, "flow.yml"),
            RunId = "run-1",
            LogFolder = Path.Combine(projectFolder, "logs", "run-1"),
            Inputs = new Dictionary<string, object?>(),
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
