using System.Reflection;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Powergentic.Flow.Actions.GitHubCopilot;
using Powergentic.Flow.Actions.Script;
using Powergentic.Flow.Cli;
using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;
using Powergentic.Flow.Core.Services;
using ExecutionContextModel = Powergentic.Flow.Core.Models.ExecutionContext;

namespace Powergentic.Flow.Core.Tests;

public sealed class AdditionalCoverageTests
{
    [Fact]
    public async Task RunLogWriter_WriteActionLogAsync_RecreatesMissingActionsDirectory()
    {
        var projectFolder = CreateProjectFolder("run-log-writer-action-dir");
        var runLogWriter = new RunLogWriter();
        var workflowFilePath = Path.Combine(projectFolder, "flow.yml");
        await File.WriteAllTextAsync(workflowFilePath, "name: demo\nversion: 1\nactions: []\n");

        try
        {
            var paths = runLogWriter.CreatePaths(projectFolder, workflowFilePath, "run-1");
            Directory.Delete(paths.ActionsFolder, recursive: true);

            var actionFilePath = Path.Combine(paths.ActionsFolder, "01-demo.json");
            await runLogWriter.WriteActionLogAsync(new ActionLogData
            {
                ActionId = "demo",
                ActionName = "Demo",
                ActionType = "script",
                Inputs = new Dictionary<string, object?>(),
                Result = new ActionResult
                {
                    ActionId = "demo",
                    Status = ActionExecutionStatus.Succeeded,
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                }
            }, actionFilePath, CancellationToken.None);

            Assert.True(File.Exists(actionFilePath));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void ActionExecutionContext_GetStringMap_HandlesObjectMapStringMapAndMissingValue()
    {
        var context = CreateActionContext(new Dictionary<string, object?>
        {
            ["objectMap"] = new Dictionary<string, object?>
            {
                ["count"] = 2,
                ["flag"] = true,
            },
            ["stringMap"] = new Dictionary<string, string?>
            {
                ["name"] = "demo",
            },
            ["scalar"] = 42,
        });

        var objectMap = context.GetStringMap("objectMap");
        var stringMap = context.GetStringMap("stringMap");
        var missingMap = context.GetStringMap("missing");
        var scalarMap = context.GetStringMap("scalar");

        Assert.Equal("2", objectMap["count"]);
        Assert.Equal("True", objectMap["flag"]);
        Assert.Equal("demo", stringMap["name"]);
        Assert.Empty(missingMap);
        Assert.Empty(scalarMap);
        Assert.Null(context.GetString("missing"));
        Assert.Equal("42", context.GetString("scalar"));
    }

    [Fact]
    public async Task ScriptActionRunner_UsesFileInputAndMergesEnvironmentOverrides()
    {
        var projectFolder = CreateProjectFolder("script-file");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        var externalScript = Path.Combine(projectFolder, "external.sh");
        await File.WriteAllTextAsync(externalScript, "#!/usr/bin/env bash\nset -euo pipefail\necho \"merged=$BASE-$EXTRA\" >> \"$ORCHESTRATOR_OUTPUT\"\n");
        TryMakeExecutable(externalScript);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var action = new WorkflowActionDefinition
            {
                Id = "script-file",
                Uses = "script",
                With = new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["file"] = externalScript,
                    ["environment"] = new Dictionary<string, object?>
                    {
                        ["EXTRA"] = "workflow"
                    }
                }
            };

            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["file"] = externalScript,
                    ["environment"] = new Dictionary<string, object?>
                    {
                        ["EXTRA"] = "workflow"
                    }
                },
                action,
                projectFolder,
                targetWorkingDirectory,
                new Dictionary<string, string?>
                {
                    ["BASE"] = "host",
                    ["EXTRA"] = "env",
                });

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
            Assert.Equal(externalScript, result.Metadata["scriptPath"]);
            Assert.Equal("host-workflow", result.Outputs["merged"]);
            Assert.True(File.Exists(result.StdOutFile));
            Assert.True(File.Exists(result.StdErrFile));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ScriptActionRunner_FailsOnNonZeroExitByDefault()
    {
        var projectFolder = CreateProjectFolder("script-fail");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["run"] = "echo fail >&2\nexit 5",
                },
                new WorkflowActionDefinition
                {
                    Id = "script-fail",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo fail >&2\nexit 5",
                    }
                },
                projectFolder,
                targetWorkingDirectory);

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.Equal(ActionExecutionStatus.Failed, result.Status);
            Assert.Equal(5, result.ExitCode);
            Assert.Contains("code 5", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fail", await File.ReadAllTextAsync(result.StdErrFile!));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ScriptActionRunner_CapturesOutputsBeforeNonZeroExitWhenFailOnNonZeroExitIsFalse()
    {
        var projectFolder = CreateProjectFolder("script-nonzero-outputs");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["run"] = "echo \"coveragePassed=false\" >> \"$ORCHESTRATOR_OUTPUT\"\necho \"coveragePercent=75.00\" >> \"$ORCHESTRATOR_OUTPUT\"\nexit 1",
                    ["failOnNonZeroExit"] = "false",
                },
                new WorkflowActionDefinition
                {
                    Id = "script-nonzero-outputs",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo \"coveragePassed=false\" >> \"$ORCHESTRATOR_OUTPUT\"\necho \"coveragePercent=75.00\" >> \"$ORCHESTRATOR_OUTPUT\"\nexit 1",
                        ["failOnNonZeroExit"] = "false",
                    }
                },
                projectFolder,
                targetWorkingDirectory);

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("false", result.Outputs["coveragePassed"]);
            Assert.Equal("75.00", result.Outputs["coveragePercent"]);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task GitHubCopilotActionRunner_UsesInlinePromptEnvTokenAndRequestHeaders()
    {
        var projectFolder = CreateProjectFolder("copilot-inline");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        CopilotPromptRequest? capturedRequest = null;
        var adapter = new FakeCopilotClientAdapter(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new CopilotPromptResult
            {
                ResponseText = "inline-response",
                SessionId = "session-inline",
                MessageId = "message-inline",
                Model = request.Model,
                Metadata = new Dictionary<string, object?>
                {
                    ["source"] = "test",
                },
            });
        });

        try
        {
            var runner = new GitHubCopilotActionRunner(adapter, new NullLogger<GitHubCopilotActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["prompt"] = "Hello {{name}} and ${name}",
                    ["inputs"] = new Dictionary<string, object?>
                    {
                        ["name"] = "World"
                    },
                    ["workingDirectory"] = "/tmp",
                    ["streaming"] = "true",
                    ["enableConfigDiscovery"] = "false",
                    ["requestHeaders"] = new Dictionary<string, object?>
                    {
                        ["x-trace"] = "abc",
                        ["empty"] = null,
                    }
                },
                new WorkflowActionDefinition
                {
                    Id = "review",
                    Uses = "githubCopilot",
                    With = new Dictionary<string, object?>
                    {
                        ["prompt"] = "Hello {{name}} and ${name}",
                    }
                },
                projectFolder,
                targetWorkingDirectory,
                new Dictionary<string, string?>
                {
                    ["GITHUB_TOKEN"] = "token-from-env",
                });

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.NotNull(capturedRequest);
            Assert.Equal("Hello World and World", capturedRequest!.Prompt);
            Assert.Null(capturedRequest.Agent);
            Assert.Equal("/tmp", capturedRequest.WorkingDirectory);
            Assert.True(capturedRequest.Streaming);
            Assert.False(capturedRequest.EnableConfigDiscovery);
            Assert.Equal("token-from-env", capturedRequest.GitHubToken);
            Assert.Single(capturedRequest.RequestHeaders);
            Assert.Equal("abc", capturedRequest.RequestHeaders["x-trace"]);
            Assert.Null(result.Outputs["responseFile"]);
            Assert.Equal("/tmp", result.Metadata["workingDirectory"]);
            Assert.Equal("test", result.Metadata["source"]);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task Program_Main_DelegatesToCliApplication()
    {
        var projectFolder = CreateProjectFolder("program-main");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Program Demo
version: 1
actions:
  - id: hello
    uses: script
    with:
      shell: bash
      run: echo hello
""");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => Program.Main(["validate", projectFolder]));

            Assert.Equal(0, exitCode);
            Assert.Contains("Program Demo", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_UnknownOptionWithJson_ReturnsJsonError()
    {
        var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["validate", "--json", "--bogus"]));

        Assert.Equal(2, exitCode);
        Assert.Contains("\"succeeded\": false", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unknown option", stdout, StringComparison.Ordinal);
        Assert.Contains("--bogus", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public async Task CliApplication_LogsCommandReportsMissingLogsFolder()
    {
        var projectFolder = CreateProjectFolder("logs-missing");
        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["logs", projectFolder]));

            Assert.Equal(2, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("Logs folder does not exist", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_LogsCommandReportsMissingRunSummaryInJson()
    {
        var projectFolder = CreateProjectFolder("logs-missing-summary");
        Directory.CreateDirectory(Path.Combine(projectFolder, "logs", "2026-06-06T00-00-00Z_test"));

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["logs", projectFolder, "--json"]));

            Assert.Equal(2, exitCode);
            Assert.Contains("Run summary is missing", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void WorkflowValidator_FailsForMissingActionIdDuplicateIdsAndInvalidExecutionLimits()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "validator-demo",
            Execution = new WorkflowExecutionOptions
            {
                MaxTransitions = 0,
                MaxVisitsPerAction = 0,
            },
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo hi"
                    }
                },
                new WorkflowActionDefinition
                {
                    Id = "dup",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo hi"
                    },
                    Next =
                    [
                        new WorkflowTransitionDefinition
                        {
                            Goto = "",
                        }
                    ]
                },
                new WorkflowActionDefinition
                {
                    Id = "dup",
                    Uses = "githubCopilot",
                    With = new Dictionary<string, object?>
                    {
                        ["prompt"] = "review"
                    }
                }
            ]
        };

        var result = new WorkflowValidator().Validate(workflow);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("Every action must have a non-empty id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Duplicate action id 'dup'", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("next transition without goto", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("maxTransitions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("maxVisitsPerAction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkflowValidator_AcceptsConditionalSelfLoopAndExistingPromptFile()
    {
        var projectFolder = CreateProjectFolder("validator-success");
        Directory.CreateDirectory(Path.Combine(projectFolder, "prompts"));
        File.WriteAllText(Path.Combine(projectFolder, "prompts", "review.prompt.md"), "Review this.");

        try
        {
            var workflow = new WorkflowDefinition
            {
                Name = "validator-success",
                Actions =
                [
                    new WorkflowActionDefinition
                    {
                        Id = "prepare",
                        Uses = "script",
                        With = new Dictionary<string, object?>
                        {
                            ["shell"] = "bash",
                            ["run"] = "echo hi"
                        },
                        Next =
                        [
                            new WorkflowTransitionDefinition
                            {
                                When = "${{ failure() }}",
                                Goto = "prepare",
                            }
                        ]
                    },
                    new WorkflowActionDefinition
                    {
                        Id = "review",
                        Uses = "githubCopilot",
                        With = new Dictionary<string, object?>
                        {
                            ["promptFile"] = "prompts/review.prompt.md"
                        }
                    }
                ]
            };

            var result = new WorkflowValidator().Validate(workflow, projectFolder);

            Assert.True(result.Succeeded);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_DefaultRunCommandExecutesWorkflowAndWritesJson()
    {
        var projectFolder = CreateProjectFolder("run-default");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Run Demo
version: 1
actions:
  - id: hello
    uses: script
    with:
      shell: bash
      run: echo "message=done" >> "$ORCHESTRATOR_OUTPUT"
""");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync([projectFolder, targetWorkingDirectory, "flow.yml", "--json"]));

            Assert.Equal(0, exitCode);
            Assert.Contains("\"workflowName\": \"Run Demo\"", stdout, StringComparison.Ordinal);
            Assert.Contains("\"succeeded\": true", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_RunDryRunUsesValidateJsonOutput()
    {
        var projectFolder = CreateProjectFolder("run-dryrun");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Dry Run Demo
version: 1
actions:
  - id: hello
    uses: script
    with:
      shell: bash
      run: echo hello
""");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["run", projectFolder, targetWorkingDirectory, "--dry-run", "--json"]));

            Assert.Equal(0, exitCode);
            Assert.Contains("\"command\": \"validate\"", stdout, StringComparison.Ordinal);
            Assert.Contains("\"workflowName\": \"Dry Run Demo\"", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_RunCommandReportsMissingTargetWorkingDirectoryInJson()
    {
        var projectFolder = CreateProjectFolder("run-missing-target");
        var missingTargetWorkingDirectory = Path.Combine(projectFolder, "missing");

        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Missing Target Demo
version: 1
actions:
  - id: hello
    uses: script
    with:
      shell: bash
      run: echo hello
""");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["run", projectFolder, missingTargetWorkingDirectory, "--json"]));

            Assert.Equal(2, exitCode);
            Assert.Contains("Target working directory does not exist", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_InitCommandSupportsForceAndLoopTemplate()
    {
        var projectFolder = CreateProjectFolder("init-force");
        await File.WriteAllTextAsync(Path.Combine(projectFolder, "existing.txt"), "keep");

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["init", projectFolder, "--template", "script-and-copilot-loop", "--force", "--json"]));

            Assert.Equal(0, exitCode);
            Assert.Contains("script-and-copilot-loop", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(projectFolder, "flow.yml")));
            Assert.DoesNotContain("startAt:", await File.ReadAllTextAsync(Path.Combine(projectFolder, "flow.yml")), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(projectFolder, "scripts", "prepare.sh")));
            Assert.True(File.Exists(Path.Combine(projectFolder, "prompts", "review.prompt.md")));
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_InitCommandReportsUnknownTemplateInJson()
    {
        var projectFolder = CreateProjectFolder("init-unknown-template");
        Directory.Delete(projectFolder, recursive: true);

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["init", projectFolder, "--template", "unknown", "--json"]));

            Assert.Equal(2, exitCode);
            Assert.Contains("Unknown template", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            if (Directory.Exists(projectFolder))
            {
                Directory.Delete(projectFolder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CliApplication_LogsCommandReportsNoRuns()
    {
        var projectFolder = CreateProjectFolder("logs-empty");
        Directory.CreateDirectory(Path.Combine(projectFolder, "logs"));

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["logs", projectFolder]));

            Assert.Equal(2, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("No workflow runs were found", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_LogsCommandReportsMissingRunId()
    {
        var projectFolder = CreateProjectFolder("logs-runid");
        var runFolder = Path.Combine(projectFolder, "logs", "2026-06-06T00-00-00Z_present");
        Directory.CreateDirectory(runFolder);
        await File.WriteAllTextAsync(Path.Combine(runFolder, "run.json"), "{}\n");

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["logs", projectFolder, "--run-id", "missing-run"]));

            Assert.Equal(2, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("was not found", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_LogsCommandReportsUnreadableSummaryAsJsonError()
    {
        var projectFolder = CreateProjectFolder("logs-invalid-json");
        var runFolder = Path.Combine(projectFolder, "logs", "2026-06-06T00-00-00Z_invalid");
        Directory.CreateDirectory(runFolder);
        await File.WriteAllTextAsync(Path.Combine(runFolder, "run.json"), "{ invalid json }");

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["logs", projectFolder, "--json"]));

            Assert.Equal(10, exitCode);
            Assert.Contains("\"succeeded\": false", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_LogsCommandReportsUnreadableSummaryInPlainText()
    {
        var projectFolder = CreateProjectFolder("logs-invalid-text");
        var runFolder = Path.Combine(projectFolder, "logs", "2026-06-06T00-00-00Z_invalid");
        Directory.CreateDirectory(runFolder);
        await File.WriteAllTextAsync(Path.Combine(runFolder, "run.json"), "{ invalid json }");

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["logs", projectFolder]));

            Assert.Equal(10, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("'i' is an invalid start of a property name", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_LogsCommandWritesHumanReadableSummary()
    {
        var projectFolder = CreateProjectFolder("logs-text");
        var runFolder = Path.Combine(projectFolder, "logs", "2026-06-06T00-00-00Z_readable");
        Directory.CreateDirectory(runFolder);
        await File.WriteAllTextAsync(Path.Combine(runFolder, "run.json"), $$"""
{
  "runId": "2026-06-06T00-00-00Z_readable",
  "workflowName": "Readable Workflow",
  "projectFolder": {{JsonSerializer.Serialize(projectFolder)}},
  "targetWorkingDirectory": {{JsonSerializer.Serialize(projectFolder)}},
  "logFolder": {{JsonSerializer.Serialize(runFolder)}},
  "succeeded": true,
  "startedAt": "2026-06-06T00:00:00+00:00",
  "completedAt": "2026-06-06T00:00:05+00:00",
  "transitionCount": 2,
  "publishedEntries": [
    {
      "actionId": "review",
      "title": "Final analysis",
      "content": "A concise summary for operators.",
      "to": ["runSummary"]
    }
  ],
  "actionResults": [
    {
      "actionId": "hello",
      "status": 0,
      "exitCode": 0,
      "summary": "Done",
      "outputs": {},
      "metadata": {},
      "stdOutFile": null,
      "stdErrFile": null,
      "startedAt": "2026-06-06T00:00:00+00:00",
      "completedAt": "2026-06-06T00:00:01+00:00",
      "succeeded": true
    }
  ]
}
""");

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["logs", projectFolder]));

            Assert.Equal(0, exitCode);
            Assert.Contains("Run ID:", stdout, StringComparison.Ordinal);
            Assert.Contains("Readable Workflow", stdout, StringComparison.Ordinal);
            Assert.Contains("Published:", stdout, StringComparison.Ordinal);
            Assert.Contains("Final analysis", stdout, StringComparison.Ordinal);
            Assert.Contains("A concise summary for operators.", stdout, StringComparison.Ordinal);
            Assert.Contains("- hello:", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_ValidateCommandReportsMissingWorkflowInJson()
    {
        var projectFolder = CreateProjectFolder("validate-missing-workflow");

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["validate", projectFolder, "--json"]));

            Assert.Equal(2, exitCode);
            Assert.Contains("Workflow file does not exist", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_ValidateCommandReturnsStructuredJsonForInvalidWorkflow()
    {
        var projectFolder = CreateProjectFolder("validate-invalid-json");

        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Invalid Json Demo
version: 1
actions:
  - id: broken
    uses: script
    with:
      shell: zsh
""");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["validate", projectFolder, "--json"]));

            Assert.Equal(2, exitCode);
            Assert.Contains("\"workflowName\": \"Invalid Json Demo\"", stdout, StringComparison.Ordinal);
            Assert.Contains("\"succeeded\": false", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unsupported shell", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_ReportsMissingOptionValueInJson()
    {
        var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["validate", "--json", "--workflow"]));

        Assert.Equal(2, exitCode);
        Assert.Contains("requires a value", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public async Task CliApplication_ReportsInvalidOverrideSyntaxInJson()
    {
        var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["validate", "--json", "--var", "broken"]));

        Assert.Equal(2, exitCode);
        Assert.Contains("key=value", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public async Task CliApplication_ReportsEmptyOverrideKeyInJson()
    {
        var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["validate", "--json", "--var", " =value"]));

        Assert.Equal(2, exitCode);
        Assert.Contains("non-empty key", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public async Task CliApplication_RunCommandLogsSuccessInPlainText()
    {
        var projectFolder = CreateProjectFolder("run-text-success");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Text Run Demo
version: 1
actions:
  - id: hello
    uses: script
    with:
      shell: bash
      run: echo "message=done" >> "$ORCHESTRATOR_OUTPUT"
""");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["run", projectFolder, targetWorkingDirectory]));

            Assert.Equal(0, exitCode);
            Assert.Contains("Text Run Demo", stdout + stderr, StringComparison.Ordinal);

            var latestRunFolder = Directory.GetDirectories(Path.Combine(projectFolder, "logs"))
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .First();
            var consoleLogFile = Path.Combine(latestRunFolder, "console.log");
            var runSummaryText = await File.ReadAllTextAsync(Path.Combine(latestRunFolder, "run.json"));

            Assert.True(File.Exists(consoleLogFile));
            Assert.Contains("Workflow 'Text Run Demo' finished", await File.ReadAllTextAsync(consoleLogFile), StringComparison.Ordinal);
            Assert.Contains("consoleLogFile", runSummaryText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_RunCommandReportsExecutionErrorInJson()
    {
        var projectFolder = CreateProjectFolder("run-json-error");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Json Run Error
version: 1
actions:
  - id: broken
    uses: script
    with:
      run: echo hi
""");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["run", projectFolder, targetWorkingDirectory, "--json"]));

            Assert.Equal(10, exitCode);
            Assert.Contains("\"command\": \"run\"", stdout, StringComparison.Ordinal);
            Assert.Contains("with.shell", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_RunCommandReportsExecutionErrorInPlainText()
    {
        var projectFolder = CreateProjectFolder("run-text-error");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Text Run Error
version: 1
actions:
  - id: broken
    uses: script
    with:
      run: echo hi
""");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["run", projectFolder, targetWorkingDirectory]));
            var combinedOutput = stdout + stderr;

            Assert.Equal(10, exitCode);
            Assert.Contains("Workflow execution failed", combinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("with.shell", combinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_ValidateCommandReportsLoadErrorInJson()
    {
        var projectFolder = CreateProjectFolder("validate-json-load-error");

        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), "name: [");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["validate", projectFolder, "--json"]));

            Assert.Equal(10, exitCode);
            Assert.Contains("\"command\": \"validate\"", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task WorkflowLoader_RejectsRemovedExecutionStartAtSetting()
    {
        var projectFolder = CreateProjectFolder("loader-startat-removed");

        try
        {
            var workflowFilePath = Path.Combine(projectFolder, "flow.yml");
            await File.WriteAllTextAsync(workflowFilePath, """
name: Removed StartAt Demo
version: 1
execution:
  startAt: prepare
  maxTransitions: 3
actions:
  - id: prepare
    uses: script
    with:
      shell: bash
      run: echo hello
""");

            var loader = new WorkflowLoader();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync(workflowFilePath));

            Assert.Contains("execution.startAt", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("first action", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_ValidateCommandReportsLoadErrorInPlainText()
    {
        var projectFolder = CreateProjectFolder("validate-text-load-error");

        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), "name: [");

            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["validate", projectFolder]));

            Assert.Equal(10, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("Validation failed", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task CliApplication_InitCommandWritesPlainTextSuccess()
    {
        var projectFolder = CreateProjectFolder("init-text-success");
        Directory.Delete(projectFolder, recursive: true);

        try
        {
            var (exitCode, stdout, stderr) = await InvokeConsoleAsync(() => CliApplication.RunAsync(["init", projectFolder, "--template=basic-script"]));

            Assert.Equal(0, exitCode);
            Assert.Contains("Initialized 'basic-script' workflow scaffold", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            if (Directory.Exists(projectFolder))
            {
                Directory.Delete(projectFolder, recursive: true);
            }
        }
    }

    [Fact]
    public void CliApplication_ParseCommandSupportsEqualsOptionsAndTypedOverrides()
    {
        var parsed = InvokePrivateStatic(typeof(CliApplication), "ParseCommand", [new[]
        {
            "run",
            "project",
            "target-from-positional",
            "workflow-from-positional.yml",
            "--workflow=custom.yml",
            "--workdir=target",
            "--input=mode=cli",
            "--var=count=42",
            "--var=missing=null",
            "--env=MODE=prod",
            "--verbose"
        }]);

        Assert.Equal("run", GetProperty<string>(parsed, "Name"));
        Assert.Equal("project", GetProperty<string>(parsed, "ProjectFolder"));
        Assert.Equal("target", GetProperty<string>(parsed, "TargetWorkingDirectory"));
        Assert.Equal("custom.yml", GetProperty<string>(parsed, "WorkflowFile"));
        Assert.True(GetProperty<bool>(parsed, "Verbose"));

        var inputs = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(GetProperty<object>(parsed, "InputOverrides"));
        var variables = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(GetProperty<object>(parsed, "VariableOverrides"));
        var environment = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string?>>(GetProperty<object>(parsed, "EnvironmentOverrides"));

        Assert.Equal("cli", inputs["mode"]);
        Assert.Equal(42, variables["count"]);
        Assert.Null(variables["missing"]);
        Assert.Equal("prod", environment["MODE"]);
    }

    [Fact]
    public void CliApplication_ParseCommandSupportsValidateLogsAndInitSpecificOptions()
    {
        var validate = InvokePrivateStatic(typeof(CliApplication), "ParseCommand", [new[]
        {
            "validate",
            "project",
            "workflow.yml"
        }]);
        var logs = InvokePrivateStatic(typeof(CliApplication), "ParseCommand", [new[]
        {
            "logs",
            "project",
            "--latest",
            "--run-id=run-42"
        }]);
        var init = InvokePrivateStatic(typeof(CliApplication), "ParseCommand", [new[]
        {
            "init",
            "project",
            "--template=script-and-copilot-loop",
            "--force"
        }]);
        var run = InvokePrivateStatic(typeof(CliApplication), "ParseCommand", [new[]
        {
            "run",
            "project",
            "target",
            "--display-enhanced"
        }]);

        Assert.Equal("workflow.yml", GetProperty<string>(validate, "WorkflowFile"));
        Assert.Equal("run-42", GetProperty<string>(logs, "RunId"));
        Assert.Equal("script-and-copilot-loop", GetProperty<string>(init, "Template"));
        Assert.True(GetProperty<bool>(init, "Force"));
        Assert.True(GetProperty<bool>(run, "DisplayEnhanced"));
    }

    [Fact]
    public void ExpressionEngine_InterpolatesCompositeValues()
    {
        var context = new ExecutionContextModel
        {
            Workflow = new WorkflowDefinition { Name = "demo" },
            ProjectFolder = "/tmp/project",
            TargetWorkingDirectory = "/tmp/target",
            WorkflowFilePath = "/tmp/project/flow.yml",
            RunId = "run-1",
            LogFolder = "/tmp/project/logs/run-1",
            Inputs = new Dictionary<string, object?> { ["mode"] = "cli" },
            Variables = new Dictionary<string, object?> { ["name"] = "demo" },
            Environment = new Dictionary<string, string?> { ["GREETING"] = "hello" },
            StartedAt = DateTimeOffset.UtcNow,
        };

        var engine = new ExpressionEngine();
        var value = engine.InterpolateValue(new Dictionary<object, object?>
        {
            ["title"] = "${ variables.name }",
            ["environment"] = new Dictionary<string, string?>
            {
                ["message"] = "${ env.GREETING } ${ variables.name }"
            },
            ["items"] = new List<object?>
            {
                "${ inputs.mode }",
                5,
            }
        }, context);

        var result = Assert.IsType<Dictionary<string, object?>>(value);
        var environment = Assert.IsType<Dictionary<string, string?>>(result["environment"]);
        var items = Assert.IsType<List<object?>>(result["items"]);

        Assert.Equal("demo", result["title"]);
        Assert.Equal("hello demo", environment["message"]);
        Assert.Equal("cli", items[0]);
        Assert.Equal(5, items[1]);
    }

    [Fact]
    public void ExpressionEngine_EvaluatesNullStatusAndTruthinessExpressions()
    {
        var context = new ExecutionContextModel
        {
            Workflow = new WorkflowDefinition { Name = "demo" },
            ProjectFolder = "/tmp/project",
            TargetWorkingDirectory = "/tmp/target",
            WorkflowFilePath = "/tmp/project/flow.yml",
            RunId = "run-1",
            LogFolder = "/tmp/project/logs/run-1",
            Inputs = new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>
            {
                ["enabled"] = "yes",
                ["empty"] = "",
            },
            Environment = new Dictionary<string, string?>(),
            StartedAt = DateTimeOffset.UtcNow,
        };
        context.ActionResults["prepare"] = new ActionResult
        {
            ActionId = "prepare",
            Status = ActionExecutionStatus.Succeeded,
        };

        var engine = new ExpressionEngine();

        Assert.True(engine.EvaluateCondition("${{ actions.prepare.status == 'succeeded' && variables.missing == null && variables.enabled }}", context));
        Assert.True(engine.EvaluateCondition("${{ runtime.unknown == null && variables.enabled != 'no' }}", context));
        Assert.False(engine.EvaluateCondition("${{ variables.empty }}", context));
    }

    [Fact]
    public async Task ScriptActionRunner_ThrowsWhenShellIsMissing()
    {
        var projectFolder = CreateProjectFolder("script-no-shell");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["run"] = "echo hi",
                },
                projectFolder: projectFolder,
                targetWorkingDirectory: targetWorkingDirectory);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(context, CancellationToken.None));

            Assert.Contains("with.shell", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ScriptActionRunner_ThrowsWhenScriptSourceIsMissing()
    {
        var projectFolder = CreateProjectFolder("script-no-source");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                },
                projectFolder: projectFolder,
                targetWorkingDirectory: targetWorkingDirectory);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(context, CancellationToken.None));

            Assert.Contains("with.run", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ScriptActionRunner_ThrowsForUnsupportedShell()
    {
        var projectFolder = CreateProjectFolder("script-bad-shell");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["shell"] = "zsh",
                    ["run"] = "echo hi",
                },
                projectFolder: projectFolder,
                targetWorkingDirectory: targetWorkingDirectory);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(context, CancellationToken.None));

            Assert.Contains("Unsupported shell", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ScriptActionRunner_ReadsTrimmedOutputsAndIgnoresInvalidLines()
    {
        var projectFolder = CreateProjectFolder("script-outputs");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["run"] = "printf '\ninvalid\n name = value \nflag=true\n' >> \"$ORCHESTRATOR_OUTPUT\"",
                },
                projectFolder: projectFolder,
                targetWorkingDirectory: targetWorkingDirectory);

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
            Assert.Equal("value", result.Outputs["name"]);
            Assert.Equal("true", result.Outputs["flag"]);
            Assert.False(result.Outputs.ContainsKey("invalid"));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ScriptActionRunner_UsesAbsoluteWorkingDirectoryAndRemovesNullEnvironmentOverrides()
    {
        var projectFolder = CreateProjectFolder("script-absolute-workdir");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        var absoluteWorkingDirectory = Path.Combine(projectFolder, "absolute-working-directory");
        Directory.CreateDirectory(targetWorkingDirectory);
        Directory.CreateDirectory(absoluteWorkingDirectory);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["workingDirectory"] = absoluteWorkingDirectory,
                    ["run"] = "echo \"base=${BASE:-missing}\" >> \"$ORCHESTRATOR_OUTPUT\"\npwd >> pwd.txt",
                    ["environment"] = new Dictionary<string, object?>
                    {
                        ["BASE"] = null,
                    }
                },
                projectFolder: projectFolder,
                targetWorkingDirectory: targetWorkingDirectory,
                environment: new Dictionary<string, string?>
                {
                    ["BASE"] = "from-host",
                });

            var result = await runner.RunAsync(context, CancellationToken.None);
            var workingDirectory = (await File.ReadAllTextAsync(Path.Combine(absoluteWorkingDirectory, "pwd.txt"))).Trim();

            Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
            Assert.Equal(absoluteWorkingDirectory, result.Metadata["workingDirectory"]);
            Assert.Equal("from-host", result.Outputs["base"]);
            Assert.True(Directory.Exists(workingDirectory));
            Assert.True(File.Exists(Path.Combine(workingDirectory, "pwd.txt")));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ScriptActionRunner_ResolvesRelativeFileAgainstProjectFolder()
    {
        var projectFolder = CreateProjectFolder("script-relative-file");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        var scriptsFolder = Path.Combine(projectFolder, "scripts");
        Directory.CreateDirectory(targetWorkingDirectory);
        Directory.CreateDirectory(scriptsFolder);

        var scriptPath = Path.Combine(scriptsFolder, "relative.sh");
        await File.WriteAllTextAsync(scriptPath, "#!/usr/bin/env bash\nset -euo pipefail\necho \"fromFile=ok\" >> \"$ORCHESTRATOR_OUTPUT\"\n");
        TryMakeExecutable(scriptPath);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["file"] = "scripts/relative.sh",
                },
                projectFolder: projectFolder,
                targetWorkingDirectory: targetWorkingDirectory);

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
            Assert.Equal(scriptPath, result.Metadata["scriptPath"]);
            Assert.Equal("ok", result.Outputs["fromFile"]);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task ScriptActionRunner_ThrowsForMissingReferencedFile()
    {
        var projectFolder = CreateProjectFolder("script-missing-file");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        try
        {
            var runner = new ScriptActionRunner(new NullLogger<ScriptActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["shell"] = "bash",
                    ["file"] = "scripts/missing.sh",
                },
                projectFolder: projectFolder,
                targetWorkingDirectory: targetWorkingDirectory);

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => runner.RunAsync(context, CancellationToken.None));

            Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task GitHubCopilotActionRunner_PreservesInlineGitHubTokenWhenEnvironmentTokenExists()
    {
        var projectFolder = CreateProjectFolder("copilot-inline-token");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        CopilotPromptRequest? capturedRequest = null;
        var adapter = new FakeCopilotClientAdapter(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new CopilotPromptResult
            {
                ResponseText = "done",
                SessionId = "session-token",
                MessageId = "message-token",
                Model = request.Model,
            });
        });

        try
        {
            var runner = new GitHubCopilotActionRunner(adapter, new NullLogger<GitHubCopilotActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["prompt"] = "hello",
                    ["gitHubToken"] = "inline-token",
                },
                new WorkflowActionDefinition
                {
                    Id = "review",
                    Uses = "githubCopilot",
                    With = new Dictionary<string, object?>
                    {
                        ["prompt"] = "hello",
                        ["gitHubToken"] = "inline-token",
                    }
                },
                projectFolder,
                targetWorkingDirectory,
                new Dictionary<string, string?>
                {
                    ["GITHUB_TOKEN"] = "environment-token",
                });

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.NotNull(capturedRequest);
            Assert.Null(capturedRequest!.Agent);
            Assert.Equal("inline-token", capturedRequest.GitHubToken);
            Assert.Equal("done", result.Outputs["response"]);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task GitHubCopilotActionRunner_UsesConfiguredAgent()
    {
        var projectFolder = CreateProjectFolder("copilot-agent");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(targetWorkingDirectory);

        CopilotPromptRequest? capturedRequest = null;
        var adapter = new FakeCopilotClientAdapter(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new CopilotPromptResult
            {
                ResponseText = "done",
                SessionId = "session-agent",
                MessageId = "message-agent",
                Model = request.Model,
            });
        });

        try
        {
            var runner = new GitHubCopilotActionRunner(adapter, new NullLogger<GitHubCopilotActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["prompt"] = "hello",
                    ["agent"] = "reviewer",
                },
                new WorkflowActionDefinition
                {
                    Id = "review",
                    Uses = "githubCopilot",
                    With = new Dictionary<string, object?>
                    {
                        ["prompt"] = "hello",
                        ["agent"] = "reviewer",
                    }
                },
                projectFolder,
                targetWorkingDirectory);

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.NotNull(capturedRequest);
            Assert.Equal("reviewer", capturedRequest!.Agent);
            Assert.Equal("done", result.Outputs["response"]);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task GitHubCopilotActionRunner_UsesAbsoluteWriteResponsePath()
    {
        var projectFolder = CreateProjectFolder("copilot-absolute-response-path");
        var targetWorkingDirectory = Path.Combine(projectFolder, "target");
        var responsePath = Path.Combine(projectFolder, "responses", "review.txt");
        Directory.CreateDirectory(targetWorkingDirectory);

        var adapter = new FakeCopilotClientAdapter(request => Task.FromResult(new CopilotPromptResult
        {
            ResponseText = "stored-response",
            SessionId = "session-absolute",
            MessageId = "message-absolute",
            Model = request.Model,
        }));

        try
        {
            var runner = new GitHubCopilotActionRunner(adapter, new NullLogger<GitHubCopilotActionRunner>());
            var context = CreateActionContext(
                new Dictionary<string, object?>
                {
                    ["prompt"] = "hello",
                    ["writeResponseTo"] = responsePath,
                },
                new WorkflowActionDefinition
                {
                    Id = "review",
                    Uses = "githubCopilot",
                    With = new Dictionary<string, object?>
                    {
                        ["prompt"] = "hello",
                        ["writeResponseTo"] = responsePath,
                    }
                },
                projectFolder,
                targetWorkingDirectory);

            var result = await runner.RunAsync(context, CancellationToken.None);

            Assert.Equal(responsePath, result.Outputs["responseFile"]);
            Assert.Equal(responsePath, result.Metadata["responseFile"]);
            Assert.Equal("stored-response", await File.ReadAllTextAsync(responsePath));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void CopilotAgentDiscovery_LoadAgentsFromWorkspace_UsesNameWhenDisplayNameMissing()
    {
        var projectFolder = CreateProjectFolder("copilot-agents-name-fallback");
        var agentsFolder = Path.Combine(projectFolder, ".github", "agents");
        Directory.CreateDirectory(agentsFolder);
        var agentPath = Path.Combine(agentsFolder, "review.agent.md");
        File.WriteAllText(agentPath, """
---
name: code reviewer
description: Reviews code changes
---
Review the changed files and summarize the important findings.
""");

        var logger = new ListLogger();

        try
        {
            var agents = CopilotAgentDiscovery.LoadAgentsFromWorkspace(projectFolder, logger);

            var agent = Assert.Single(agents);
            Assert.Equal("code reviewer", agent.Name);
            Assert.Equal("code reviewer", agent.DisplayName);
            Assert.Equal("Reviews code changes", agent.Description);
            Assert.Equal("Review the changed files and summarize the important findings.", agent.Prompt);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void CopilotAgentDiscovery_LoadAgentsFromWorkspace_ParsesYamlFrontmatterAndLogsLoadedAgents()
    {
        var projectFolder = CreateProjectFolder("copilot-agents");
        var agentsFolder = Path.Combine(projectFolder, ".github", "agents");
        Directory.CreateDirectory(agentsFolder);
        var agentPath = Path.Combine(agentsFolder, "review.agent.md");
        File.WriteAllText(agentPath, """
---
name: reviewer
displayName: Code Reviewer
description: Reviews code changes
tools:
  - read_file
  - grep_search
skills:
  - review
model: gpt-5
infer: true
---
Review the changed files and summarize the important findings.
""");

        var logger = new ListLogger();

        try
        {
            var agents = CopilotAgentDiscovery.LoadAgentsFromWorkspace(projectFolder, logger);

            var agent = Assert.Single(agents);
            Assert.Equal("reviewer", agent.Name);
            Assert.Equal("Code Reviewer", agent.DisplayName);
            Assert.Equal("Reviews code changes", agent.Description);
            Assert.Equal("gpt-5", agent.Model);
            Assert.True(agent.Infer);
            Assert.Equal(["read_file", "grep_search"], agent.Tools);
            Assert.Equal(["review"], agent.Skills);
            Assert.Equal("Review the changed files and summarize the important findings.", agent.Prompt);
            Assert.Contains(logger.Messages, message => message.Contains("Loaded 1 custom agent", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message => message.Contains("reviewer", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void CopilotAgentDiscovery_LoadAgentsFromWorkspace_UsesDefaultsWithoutFrontmatter()
    {
        var projectFolder = CreateProjectFolder("copilot-agents-defaults");
        var agentsFolder = Path.Combine(projectFolder, ".github", "agents", "nested");
        Directory.CreateDirectory(agentsFolder);
        var agentPath = Path.Combine(agentsFolder, "triage.agent.md");
        File.WriteAllText(agentPath, "Triage the issue and propose next steps.\n");

        var logger = new ListLogger();

        try
        {
            var agents = CopilotAgentDiscovery.LoadAgentsFromWorkspace(projectFolder, logger);

            var agent = Assert.Single(agents);
            Assert.Equal("triage", agent.Name);
            Assert.Equal("triage", agent.DisplayName);
            Assert.NotNull(agent.Description);
            Assert.Contains(".github/agents/nested/triage.agent.md", agent.Description.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.Equal("Triage the issue and propose next steps.", agent.Prompt);
            Assert.Contains(logger.Messages, message => message.Contains("triage", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void ServiceCollectionExtensions_RegisterGitHubCopilotServices()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddGitHubCopilotAction();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICopilotClientAdapter)
            && descriptor.ImplementationType == typeof(CopilotClientAdapter));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IActionRunner)
            && descriptor.ImplementationType == typeof(GitHubCopilotActionRunner));
    }

    [Fact]
    public void Program_MainReturnsCliApplicationTask()
    {
        var task = Program.Main(["help"]);

        Assert.NotNull(task);
    }

    private static ActionExecutionContext CreateActionContext(
        Dictionary<string, object?> resolvedInputs,
        WorkflowActionDefinition? action = null,
        string? projectFolder = null,
        string? targetWorkingDirectory = null,
        Dictionary<string, string?>? environment = null)
    {
        projectFolder ??= CreateProjectFolder("action-context");
        targetWorkingDirectory ??= Path.Combine(projectFolder, "target");
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(targetWorkingDirectory);
        Directory.CreateDirectory(Path.Combine(projectFolder, "logs", "run-1", "actions"));

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
            Environment = environment ?? new Dictionary<string, string?>(),
            StartedAt = DateTimeOffset.UtcNow,
        };

        return new ActionExecutionContext
        {
            ExecutionContext = executionContext,
            Action = action ?? new WorkflowActionDefinition
            {
                Id = "demo",
                Uses = "script",
                With = resolvedInputs,
            },
            ResolvedInputs = resolvedInputs,
            ActionLogDirectory = Path.Combine(projectFolder, "logs", "run-1", "actions"),
            ActionLogPrefix = "01-demo",
            Expressions = new ExpressionEngine(),
            Logger = NullLogger.Instance,
        };
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> InvokeConsoleAsync(Func<Task<int>> action)
    {
        await ConsoleTestSynchronization.Gate.WaitAsync();
        try
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdout = new StringWriter(new StringBuilder());
            using var stderr = new StringWriter(new StringBuilder());
            Console.SetOut(stdout);
            Console.SetError(stderr);

            try
            {
                var exitCode = await action();
                return (exitCode, StripStartupBanner(stdout.ToString()), StripStartupBanner(stderr.ToString()));
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
        finally
        {
            ConsoleTestSynchronization.Gate.Release();
        }
    }

    private static string StripStartupBanner(string text)
    {
        var normalizedText = System.Text.RegularExpressions.Regex.Replace(text, "\\u001B\\[[0-9;]*m", string.Empty);
        var bannerStart = normalizedText.IndexOf("╭──────────────────────────────────────────────────────────────────────────────╮", StringComparison.Ordinal);
        if (bannerStart >= 0)
        {
            var bannerEnd = normalizedText.IndexOf("╰──────────────────────────────────────────────────────────────────────────────╯", bannerStart, StringComparison.Ordinal);
            if (bannerEnd >= 0)
            {
                var contentStart = bannerEnd + "╰──────────────────────────────────────────────────────────────────────────────╯".Length;
                normalizedText = normalizedText[contentStart..];
            }
        }

        var lines = normalizedText
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => !line.StartsWith("→ ", StringComparison.Ordinal))
            .ToArray();

        return string.Join(Environment.NewLine, lines).TrimStart();
    }

    private static string CreateProjectFolder(string name)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), $"orchestrator-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectFolder);
        return projectFolder;
    }

    private static object InvokePrivateStatic(Type type, string methodName, object?[] arguments)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {type.FullName}.");

        return method.Invoke(null, arguments)
            ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on {instance.GetType().FullName}.");

        return (T)property.GetValue(instance)!;
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private static void TryMakeExecutable(string scriptPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
        }
    }

    private sealed class FakeCopilotClientAdapter(Func<CopilotPromptRequest, Task<CopilotPromptResult>> onPrompt) : ICopilotClientAdapter
    {
        public Task<CopilotPromptResult> PromptAsync(CopilotPromptRequest request, CancellationToken cancellationToken)
            => onPrompt(request);
    }
}
