using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;
using Powergentic.Flow.Core.Services;

namespace Powergentic.Flow.Actions.GitHubCopilot;

public sealed class CopilotClientAdapter(ILogger<CopilotClientAdapter> logger, ILoggerFactory loggerFactory) : ICopilotClientAdapter
{
    private static bool executableBitVerified;
    private static readonly Lock ExecutableBitVerificationLock = new();

    public async Task<CopilotPromptResult> PromptAsync(CopilotPromptRequest request, CancellationToken cancellationToken)
    {
        EnsureExtractedCopilotCliIsExecutable();
        var customAgents = CopilotAgentDiscovery.LoadAgentsFromWorkspace(request.WorkingDirectory, logger);
        var liveOutputLogger = loggerFactory.CreateLogger(WorkflowExecutionStreamingLoggerProvider.CategoryName);
        var liveOutput = new CopilotSessionLiveOutputLogger(liveOutputLogger, request.Streaming);

        var options = new CopilotClientOptions
        {
            WorkingDirectory = request.WorkingDirectory,
            Logger = logger,
            GitHubToken = request.GitHubToken,
            UseLoggedInUser = string.IsNullOrWhiteSpace(request.GitHubToken),
        };

        await using var client = new CopilotClient(options);
        await client.StartAsync(cancellationToken);

        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            SessionId = request.SessionId,
            Agent = request.Agent,
            Model = request.Model,
            Streaming = request.Streaming,
            WorkingDirectory = request.WorkingDirectory,
            OnPermissionRequest = PermissionHandler.ApproveAll,
            EnableConfigDiscovery = request.EnableConfigDiscovery,
            CustomAgents = customAgents,
            OnEvent = liveOutput.HandleEvent,
        }, cancellationToken);

        var prompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? request.Prompt
            : $"System instructions:\n{request.SystemPrompt}\n\nUser prompt:\n{request.Prompt}";

        liveOutput.LogRequest(request);

        var headers = request.RequestHeaders
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!, StringComparer.OrdinalIgnoreCase);

        AssistantMessageData? responseData = null;
        var timedOut = false;
        try
        {
            var response = await session.SendAndWaitAsync(new MessageOptions
            {
                Prompt = prompt,
                RequestHeaders = headers,
            }, request.Timeout, cancellationToken);

            responseData = response?.Data;
        }
        catch (TimeoutException ex)
        {
            timedOut = true;
            logger.LogWarning(ex, "GitHub Copilot prompt timed out after {Timeout} in {WorkingDirectory}", request.Timeout, request.WorkingDirectory);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            logger.LogWarning(ex, "GitHub Copilot prompt timed out after {Timeout} in {WorkingDirectory}", request.Timeout, request.WorkingDirectory);
        }
        finally
        {
            liveOutput.Complete(responseData);
        }

        if (responseData is null)
        {
            return new CopilotPromptResult
            {
                ResponseText = string.Empty,
                SessionId = session.SessionId,
                Model = request.Model,
                TimedOut = timedOut,
                InputTokens = null,
                OutputTokens = null,
                Metadata = new Dictionary<string, object?>
                {
                    ["workingDirectory"] = request.WorkingDirectory,
                    ["timedOut"] = timedOut,
                },
            };
        }

        return new CopilotPromptResult
        {
            ResponseText = responseData.Content ?? string.Empty,
            MessageId = responseData.MessageId,
            SessionId = session.SessionId,
            Model = responseData.Model ?? request.Model,
            TimedOut = timedOut,
            InputTokens = null,
            OutputTokens = ToNullableInt(responseData.OutputTokens),
            Metadata = new Dictionary<string, object?>
            {
                ["workingDirectory"] = request.WorkingDirectory,
                ["timedOut"] = timedOut,
            },
        };
    }

    private static void EnsureExtractedCopilotCliIsExecutable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || executableBitVerified)
        {
            return;
        }

        lock (ExecutableBitVerificationLock)
        {
            if (executableBitVerified)
            {
                return;
            }

            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var cacheBase = Path.Combine(home, ".net", "pgflow");
                if (!Directory.Exists(cacheBase))
                {
                    executableBitVerified = true;
                    return;
                }

                foreach (var copilotPath in Directory.EnumerateFiles(cacheBase, "copilot", SearchOption.AllDirectories))
                {
                    try
                    {
                        var psi = new ProcessStartInfo("chmod", $"+x \"{copilotPath}\"")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        };
                        using var process = Process.Start(psi);
                        process?.WaitForExit();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            executableBitVerified = true;
        }
    }

    private static int? ToNullableInt(long? value)
        => value is null ? null : checked((int)value.Value);
}
