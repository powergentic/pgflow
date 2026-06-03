using Powergentic.AI.Orchestrator.Core.Abstractions;
using Powergentic.AI.Orchestrator.Core.Models;
using Powergentic.AI.Orchestrator.Core.Services;

namespace Powergentic.AI.Orchestrator.Core.Tests;

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

    private static string CreateTempProjectFolder()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), $"orchestrator-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectFolder);
        return projectFolder;
    }
}
