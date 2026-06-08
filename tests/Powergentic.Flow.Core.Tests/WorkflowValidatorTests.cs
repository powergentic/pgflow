using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;
using Powergentic.Flow.Core.Services;

namespace Powergentic.Flow.Core.Tests;

public class WorkflowValidatorTests
{
    [Fact]
    public void Validate_FailsForMissingGotoTarget()
    {
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
                        ["run"] = "echo hi"
                    },
                    Next = [ new WorkflowTransitionDefinition { Goto = "missing" } ]
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FailsWhenActionUsesIsMissing()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "first"
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("must specify uses", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FailsForUnsupportedScriptShell()
    {
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
                        ["shell"] = "zsh",
                        ["run"] = "echo hi"
                    }
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("unsupported shell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FailsForMissingReferencedScriptFile()
    {
        var projectFolder = CreateTempProjectFolder();
        try
        {
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
                            ["path"] = "scripts/missing.sh"
                        }
                    }
                ]
            };

            var validator = new WorkflowValidator();
            var result = validator.Validate(workflow, projectFolder);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, error => error.Contains("with.path", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void Validate_FailsWhenCopilotPromptIsMissing()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "review",
                    Uses = "githubCopilot"
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("with.prompt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SucceedsWhenCopilotEnableConfigDiscoveryIsProvided()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "review",
                    Uses = "githubCopilot",
                    With = new Dictionary<string, object?>
                    {
                        ["prompt"] = "Review the project",
                        ["enableConfigDiscovery"] = false,
                    }
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_SucceedsWhenCopilotTimeoutIsProvided()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "review",
                    Uses = "githubCopilot",
                    With = new Dictionary<string, object?>
                    {
                        ["prompt"] = "Review the project",
                        ["timeout"] = "00:30:00",
                    }
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_SucceedsWhenCopilotSessionIdIsProvided()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "review",
                    Uses = "githubCopilot",
                    With = new Dictionary<string, object?>
                    {
                        ["prompt"] = "Review the project",
                        ["sessionId"] = "shared-session",
                    }
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_FailsForUnconditionalSelfLoop()
    {
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
                        ["run"] = "echo hi"
                    },
                    Next = [ new WorkflowTransitionDefinition { Goto = "first" } ]
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("self-loop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FailsForUnsupportedActionTypeWhenRunnersAreKnown()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "first",
                    Uses = "unknown"
                }
            ]
        };

        var validator = new WorkflowValidator(new IActionRunner[]
        {
            new TestActionRunner("script", (_, _) => Task.FromResult(new ActionResult()))
        });
        var result = validator.Validate(workflow);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("unsupported action type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FailsForUnnecessaryGotoToNextAction()
    {
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
                        ["run"] = "echo hi"
                    },
                    Next = [ new WorkflowTransitionDefinition { Goto = "second" } ]
                },
                new WorkflowActionDefinition
                {
                    Id = "second",
                    Uses = "script",
                    With = new Dictionary<string, object?>
                    {
                        ["shell"] = "bash",
                        ["run"] = "echo hi again"
                    }
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("unnecessary unconditional goto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FailsForPublishEntryWithoutFrom()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "demo",
            Actions =
            [
                new WorkflowActionDefinition
                {
                    Id = "review",
                    Uses = "githubCopilot",
                    With = new Dictionary<string, object?>
                    {
                        ["prompt"] = "Review the project"
                    },
                    Publish =
                    [
                        new WorkflowActionPublishDefinition
                        {
                            Title = "Missing source",
                            To = ["runSummary"]
                        }
                    ]
                }
            ]
        };

        var validator = new WorkflowValidator();
        var result = validator.Validate(workflow);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("publish", StringComparison.OrdinalIgnoreCase)
            && error.Contains("from", StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateTempProjectFolder()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), $"orchestrator-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectFolder);
        return projectFolder;
    }
}
