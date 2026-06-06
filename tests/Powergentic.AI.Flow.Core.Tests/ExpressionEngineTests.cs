using ExecutionContextModel = Powergentic.AI.Flow.Core.Models.ExecutionContext;
using Powergentic.AI.Flow.Core.Models;
using Powergentic.AI.Flow.Core.Services;

namespace Powergentic.AI.Flow.Core.Tests;

public class ExpressionEngineTests
{
    [Fact]
    public void InterpolateString_ResolvesInputsVariablesAndActionOutputs()
    {
        var context = CreateContext();
        context.ActionResults["prepare"] = new ActionResult
        {
            ActionId = "prepare",
            Status = ActionExecutionStatus.Succeeded,
            Outputs = new Dictionary<string, string?> { ["file"] = "output.txt" },
        };

        var engine = new ExpressionEngine();
        var result = engine.InterpolateString("${ inputs.mode }/${ variables.name }/${ actions.prepare.outputs.file }", context);

        Assert.Equal("cli/demo/output.txt", result);
    }

    [Fact]
    public void InterpolateString_ResolvesRuntimeValues()
    {
        var context = CreateContext();
        context.CurrentActionId = "review";

        var engine = new ExpressionEngine();
        var result = engine.InterpolateString("${ runtime.projectFolder }/${ runtime.targetWorkingDirectory }/${ runtime.currentActionId }/${ runtime.runId }", context);

        Assert.Equal("/tmp/project//tmp/target/review/run-1", result);
    }

    [Fact]
    public void EvaluateCondition_SupportsBooleanExpressions()
    {
        var context = CreateContext();
        context.ActionResults["prepare"] = new ActionResult
        {
            ActionId = "prepare",
            Status = ActionExecutionStatus.Succeeded,
            Outputs = new Dictionary<string, string?> { ["retry"] = "true" },
        };

        var engine = new ExpressionEngine();
        var result = engine.EvaluateCondition("${{ success() && actions.prepare.outputs.retry == 'true' }}", context);

        Assert.True(result);
    }

    [Fact]
    public void EvaluateCondition_RespectsNegationAndParentheses()
    {
        var context = CreateContext();
        context.Variables["enabled"] = true;
        context.ActionResults["prepare"] = new ActionResult
        {
            ActionId = "prepare",
            Status = ActionExecutionStatus.Succeeded,
            Outputs = new Dictionary<string, string?> { ["retry"] = "false" },
        };

        var engine = new ExpressionEngine();
        var result = engine.EvaluateCondition("${{ !(actions.prepare.outputs.retry == 'true') && (variables.enabled == true || failure()) }}", context);

        Assert.True(result);
    }

    [Fact]
    public void EvaluateCondition_SupportsFailureHelper()
    {
        var context = CreateContext();
        context.ActionResults["prepare"] = new ActionResult
        {
            ActionId = "prepare",
            Status = ActionExecutionStatus.Failed,
        };

        var engine = new ExpressionEngine();

        Assert.True(engine.EvaluateCondition("${{ failure() }}", context));
        Assert.False(engine.EvaluateCondition("${{ success() }}", context));
        Assert.True(engine.EvaluateCondition("${{ always() }}", context));
    }

    private static ExecutionContextModel CreateContext()
        => new()
        {
            Workflow = new WorkflowDefinition { Name = "demo" },
            ProjectFolder = "/tmp/project",
            TargetWorkingDirectory = "/tmp/target",
            WorkflowFilePath = "/tmp/project/flow.yml",
            RunId = "run-1",
            LogFolder = "/tmp/project/logs/run-1",
            Inputs = new Dictionary<string, object?> { ["mode"] = "cli" },
            Variables = new Dictionary<string, object?> { ["name"] = "demo" },
            Environment = new Dictionary<string, string?>(),
            StartedAt = DateTimeOffset.UtcNow,
        };
}
