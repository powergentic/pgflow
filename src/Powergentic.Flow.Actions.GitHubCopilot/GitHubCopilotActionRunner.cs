using System.Globalization;
using Microsoft.Extensions.Logging;
using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Actions.GitHubCopilot;

public sealed class GitHubCopilotActionRunner(ICopilotClientAdapter copilotClient, ILogger<GitHubCopilotActionRunner> logger) : IActionRunner
{
    public string ActionType => "githubCopilot";

    public async Task<ActionResult> RunAsync(ActionExecutionContext context, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var prompt = await ResolvePromptAsync(context, cancellationToken);
        var writeResponseTo = context.GetString("writeResponseTo");
        var responsePath = string.IsNullOrWhiteSpace(writeResponseTo)
            ? null
            : ResolvePath(context.TargetWorkingDirectory, writeResponseTo);
        var configuredWorkingDirectory = context.GetString("workingDirectory");
        var workingDirectory = string.IsNullOrWhiteSpace(configuredWorkingDirectory)
            ? context.TargetWorkingDirectory
            : ResolvePath(context.TargetWorkingDirectory, configuredWorkingDirectory);

        context.Environment.TryGetValue("GITHUB_TOKEN", out var envToken);

        var configuredModel = context.GetString("model");
        var configuredEnableConfigDiscovery = context.GetString("enableConfigDiscovery");
        var configuredAgent = context.GetString("agent");
        var configuredSessionId = context.GetString("sessionId");
        var configuredTimeout = context.GetString("timeout");
        var request = new CopilotPromptRequest
        {
            Prompt = prompt,
            WorkingDirectory = workingDirectory,
            SessionId = string.IsNullOrWhiteSpace(configuredSessionId) ? null : configuredSessionId,
            Agent = string.IsNullOrWhiteSpace(configuredAgent) ? null : configuredAgent,
            Model = string.IsNullOrWhiteSpace(configuredModel) ? "auto" : configuredModel,
            SystemPrompt = context.GetString("systemPrompt"),
            Streaming = bool.TryParse(context.GetString("streaming"), out var streaming) && streaming,
            Timeout = ResolveTimeout(configuredTimeout, context.Action.Id),
            EnableConfigDiscovery = !bool.TryParse(configuredEnableConfigDiscovery, out var enableConfigDiscovery) || enableConfigDiscovery,
            GitHubToken = context.GetString("gitHubToken") ?? envToken,
            RequestHeaders = context.GetStringMap("requestHeaders")
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
        };

        logger.LogInformation("Sending prompt to GitHub Copilot for action '{ActionId}' in '{WorkingDirectory}'", context.Action.Id, workingDirectory);
        var result = await copilotClient.PromptAsync(request, cancellationToken);

        if (!string.IsNullOrWhiteSpace(responsePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(responsePath)!);
            await File.WriteAllTextAsync(responsePath, result.ResponseText, cancellationToken);
        }

        var outputs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["response"] = result.ResponseText,
            ["responseFile"] = responsePath,
            ["sessionId"] = result.SessionId,
            ["messageId"] = result.MessageId,
            ["model"] = result.Model,
            ["timedOut"] = result.TimedOut.ToString().ToLowerInvariant(),
        };

        if (result.OutputTokens is not null)
        {
            outputs["outputTokens"] = result.OutputTokens.ToString();
        }

        var metadata = new Dictionary<string, object?>(result.Metadata ?? new Dictionary<string, object?>(), StringComparer.OrdinalIgnoreCase)
        {
            ["workingDirectory"] = workingDirectory,
            ["responseFile"] = responsePath,
            ["timedOut"] = result.TimedOut,
        };

        return new ActionResult
        {
            ActionId = context.Action.Id,
            Status = ActionExecutionStatus.Succeeded,
            Summary = result.TimedOut ? "GitHub Copilot prompt timed out." : "GitHub Copilot prompt completed.",
            Outputs = outputs,
            Metadata = metadata,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }

    private static async Task<string> ResolvePromptAsync(ActionExecutionContext context, CancellationToken cancellationToken)
    {
        var inlinePrompt = context.GetString("prompt");
        var promptFile = context.GetString("promptFile");
        var promptTemplate = !string.IsNullOrWhiteSpace(promptFile)
            ? await File.ReadAllTextAsync(ResolvePath(context.ProjectFolder, promptFile), cancellationToken)
            : inlinePrompt;

        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            throw new InvalidOperationException($"Action '{context.Action.Id}' requires 'with.prompt' or 'with.promptFile'.");
        }

        var inputs = context.ResolvedInputs.TryGetValue("inputs", out var inputsValue) && inputsValue is Dictionary<string, object?> objectMap
            ? objectMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        return ApplyTemplate(promptTemplate, inputs);
    }

    private static string ApplyTemplate(string template, Dictionary<string, string?> inputs)
    {
        var content = template;
        foreach (var input in inputs)
        {
            content = content.Replace($"{{{{{input.Key}}}}}", input.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            content = content.Replace($"${{{input.Key}}}", input.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return content;
    }

    private static TimeSpan ResolveTimeout(string? configuredTimeout, string actionId)
    {
        if (string.IsNullOrWhiteSpace(configuredTimeout))
        {
            return CopilotPromptRequest.DefaultTimeout;
        }

        if (double.TryParse(configuredTimeout, NumberStyles.Float, CultureInfo.InvariantCulture, out var timeoutMinutes))
        {
            if (timeoutMinutes > 0)
            {
                return TimeSpan.FromMinutes(timeoutMinutes);
            }

            throw new InvalidOperationException($"Action '{actionId}' has invalid 'with.timeout'. Expected a positive duration.");
        }

        if (TimeSpan.TryParse(configuredTimeout, CultureInfo.InvariantCulture, out var timeout) && timeout > TimeSpan.Zero)
        {
            return timeout;
        }

        throw new InvalidOperationException($"Action '{actionId}' has invalid 'with.timeout'. Use a positive number of minutes or a TimeSpan value like '00:30:00'.");
    }

    private static string ResolvePath(string projectFolder, string path)
        => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(projectFolder, path));
}
