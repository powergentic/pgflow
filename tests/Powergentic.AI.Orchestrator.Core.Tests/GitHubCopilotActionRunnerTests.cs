using ExecutionContextModel = Powergentic.AI.Orchestrator.Core.Models.ExecutionContext;
using Microsoft.Extensions.Logging.Abstractions;
using Powergentic.AI.Orchestrator.Actions.GitHubCopilot;
using Powergentic.AI.Orchestrator.Core.Abstractions;
using Powergentic.AI.Orchestrator.Core.Models;
using Powergentic.AI.Orchestrator.Core.Services;

namespace Powergentic.AI.Orchestrator.Core.Tests;

public class GitHubCopilotActionRunnerTests
{
    [Fact]
    public async Task RunAsync_UsesPromptFileAndWritesResponseFileInTargetWorkingDirectory()
    {
        var projectFolder = CreateProjectFolder();
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);
        Directory.CreateDirectory(Path.Combine(projectFolder, "prompts"));
        await File.WriteAllTextAsync(Path.Combine(projectFolder, "prompts", "review.prompt.md"), "Hello {{name}} from ${place}");

        CopilotPromptRequest? capturedRequest = null;
        var adapter = new FakeCopilotClientAdapter(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new CopilotPromptResult
            {
                ResponseText = "done",
                SessionId = "session-1",
                MessageId = "message-1",
                Model = request.Model,
                OutputTokens = 12,
            });
        });

        try
        {
            var runner = new GitHubCopilotActionRunner(adapter, new NullLogger<GitHubCopilotActionRunner>());
            var context = CreateActionContext(projectFolder, targetWorkingDirectory, new Dictionary<string, object?>
            {
                ["promptFile"] = "prompts/review.prompt.md",
                ["inputs"] = new Dictionary<string, object?>
                {
                    ["name"] = "Copilot",
                    ["place"] = "workspace"
                },
                ["writeResponseTo"] = "output/review.txt",
                ["model"] = "gpt-5"
            });

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.NotNull(capturedRequest);
            Assert.Equal("Hello Copilot from workspace", capturedRequest!.Prompt);
            Assert.Equal(targetWorkingDirectory, capturedRequest.WorkingDirectory);
            Assert.Equal(Path.Combine(targetWorkingDirectory, "output", "review.txt"), result.Outputs["responseFile"]);
            Assert.Equal("done", await File.ReadAllTextAsync(Path.Combine(targetWorkingDirectory, "output", "review.txt")));
            Assert.Equal("12", result.Outputs["outputTokens"]);
            Assert.Equal(targetWorkingDirectory, result.Metadata["workingDirectory"]);
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
        var nested = Path.Combine(targetWorkingDirectory, "nested");
        Directory.CreateDirectory(nested);

        CopilotPromptRequest? capturedRequest = null;
        var adapter = new FakeCopilotClientAdapter(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new CopilotPromptResult
            {
                ResponseText = "done",
                SessionId = "session-1",
                MessageId = "message-1",
                Model = request.Model,
            });
        });

        try
        {
            var runner = new GitHubCopilotActionRunner(adapter, new NullLogger<GitHubCopilotActionRunner>());
            var context = CreateActionContext(projectFolder, targetWorkingDirectory, new Dictionary<string, object?>
            {
                ["prompt"] = "hello",
                ["workingDirectory"] = "nested"
            });

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.NotNull(capturedRequest);
            Assert.Equal(nested, capturedRequest!.WorkingDirectory);
            Assert.Equal(nested, result.Metadata["workingDirectory"]);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ThrowsWhenPromptIsMissing()
    {
        var projectFolder = CreateProjectFolder();
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);
        try
        {
            var runner = new GitHubCopilotActionRunner(new FakeCopilotClientAdapter(_ => throw new InvalidOperationException("should not run")), new NullLogger<GitHubCopilotActionRunner>());
            var context = CreateActionContext(projectFolder, targetWorkingDirectory, new Dictionary<string, object?>());

            await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(context, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    private static string CreateProjectFolder()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), $"orchestrator-copilot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(Path.Combine(projectFolder, "logs", "run-1", "actions"));
        return projectFolder;
    }

    private static ActionExecutionContext CreateActionContext(string projectFolder, string targetWorkingDirectory, Dictionary<string, object?> resolvedInputs)
    {
        var executionContext = new ExecutionContextModel
        {
            Workflow = new WorkflowDefinition { Name = "demo" },
            ProjectFolder = projectFolder,
            TargetWorkingDirectory = targetWorkingDirectory,
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
            Action = new WorkflowActionDefinition
            {
                Id = "review",
                Uses = "githubCopilot",
                With = resolvedInputs
            },
            ResolvedInputs = resolvedInputs,
            ActionLogDirectory = Path.Combine(projectFolder, "logs", "run-1", "actions"),
            ActionLogPrefix = "01-review",
            Expressions = new ExpressionEngine(),
            Logger = NullLogger.Instance,
        };
    }

    private sealed class FakeCopilotClientAdapter(Func<CopilotPromptRequest, Task<CopilotPromptResult>> onPrompt) : ICopilotClientAdapter
    {
        public Task<CopilotPromptResult> PromptAsync(CopilotPromptRequest request, CancellationToken cancellationToken)
            => onPrompt(request);
    }
}
