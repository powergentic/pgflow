using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Actions.GitHubCopilot;

public sealed class CopilotClientAdapter(ILogger<CopilotClientAdapter> logger) : ICopilotClientAdapter
{
    private static bool executableBitVerified;
    private static readonly Lock ExecutableBitVerificationLock = new();

    public async Task<CopilotPromptResult> PromptAsync(CopilotPromptRequest request, CancellationToken cancellationToken)
    {
        EnsureExtractedCopilotCliIsExecutable();
        var customAgents = CopilotAgentDiscovery.LoadAgentsFromWorkspace(request.WorkingDirectory, logger);
        var liveOutput = new CopilotLiveOutputLogger(logger, request.Streaming);

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

        var headers = request.RequestHeaders
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!, StringComparer.OrdinalIgnoreCase);

        AssistantMessageData? responseData = null;
        try
        {
            var response = await session.SendAndWaitAsync(new MessageOptions
            {
                Prompt = prompt,
                RequestHeaders = headers,
            }, TimeSpan.FromMinutes(10), cancellationToken);

            responseData = response?.Data;
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
                InputTokens = null,
                OutputTokens = null,
                Metadata = new Dictionary<string, object?>
                {
                    ["workingDirectory"] = request.WorkingDirectory,
                },
            };
        }

        return new CopilotPromptResult
        {
            ResponseText = responseData.Content ?? string.Empty,
            MessageId = responseData.MessageId,
            SessionId = session.SessionId,
            Model = responseData.Model ?? request.Model,
            InputTokens = null,
            OutputTokens = ToNullableInt(responseData.OutputTokens),
            Metadata = new Dictionary<string, object?>
            {
                ["workingDirectory"] = request.WorkingDirectory,
            },
        };
    }

    private sealed class CopilotLiveOutputLogger(ILogger logger, bool streamingEnabled)
    {
        private const int FlushThreshold = 120;

        private readonly Lock _sync = new();
        private readonly StringBuilder _responseBuffer = new();
        private readonly StringBuilder _thoughtBuffer = new();
        private bool _sawResponseDelta;
        private bool _sawThoughtDelta;

        public void HandleEvent(SessionEvent sessionEvent)
        {
            lock (_sync)
            {
                switch (sessionEvent)
                {
                    case AssistantIntentEvent { Data.Intent: { Length: > 0 } intent }:
                        logger.LogInformation("Copilot intent: {Intent}", intent);
                        break;

                    case AssistantReasoningDeltaEvent { Data.DeltaContent: { Length: > 0 } thoughtDelta }:
                        _sawThoughtDelta = true;
                        if (streamingEnabled)
                        {
                            AppendChunk(_thoughtBuffer, "Copilot thought", thoughtDelta);
                        }
                        break;

                    case AssistantMessageDeltaEvent { Data.DeltaContent: { Length: > 0 } responseDelta }:
                        _sawResponseDelta = true;
                        if (streamingEnabled)
                        {
                            AppendChunk(_responseBuffer, "Copilot response", responseDelta);
                        }
                        break;

                    case AssistantReasoningEvent { Data: { Content: { Length: > 0 } } reasoningEventData } when !_sawThoughtDelta:
                        AppendText("Copilot thought", reasoningEventData.Content);
                        break;

                    case AssistantMessageEvent { Data: { ReasoningText: { Length: > 0 } } messageEventData } when !_sawThoughtDelta:
                        AppendText("Copilot thought", messageEventData.ReasoningText);
                        break;

                    case AssistantMessageEvent { Data: { Content: { Length: > 0 } } messageEventData } when !_sawResponseDelta:
                        AppendText("Copilot response", messageEventData.Content);
                        break;
                }
            }
        }

        public void Complete(AssistantMessageData? responseData)
        {
            lock (_sync)
            {
                if (!_sawThoughtDelta && !string.IsNullOrWhiteSpace(responseData?.ReasoningText))
                {
                    AppendText("Copilot thought", responseData.ReasoningText);
                }

                if (!_sawResponseDelta && !string.IsNullOrWhiteSpace(responseData?.Content))
                {
                    AppendText("Copilot response", responseData.Content);
                }

                FlushBuffer(_thoughtBuffer, "Copilot thought");
                FlushBuffer(_responseBuffer, "Copilot response");
            }
        }

        private void AppendText(string label, string content)
        {
            foreach (var line in NormalizeLines(content))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    logger.LogInformation("{Label}: {Content}", label, line);
                }
            }
        }

        private void AppendChunk(StringBuilder buffer, string label, string chunk)
        {
            foreach (var character in NormalizeNewlines(chunk))
            {
                if (character == '\n')
                {
                    FlushBuffer(buffer, label);
                    continue;
                }

                buffer.Append(character);
                if (buffer.Length >= FlushThreshold)
                {
                    FlushBuffer(buffer, label);
                }
            }
        }

        private void FlushBuffer(StringBuilder buffer, string label)
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var content = buffer.ToString();
            buffer.Clear();
            if (!string.IsNullOrWhiteSpace(content))
            {
                logger.LogInformation("{Label}: {Content}", label, content);
            }
        }

        private static IEnumerable<char> NormalizeNewlines(string content)
        {
            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            foreach (var character in normalized)
            {
                yield return character;
            }
        }

        private static IEnumerable<string> NormalizeLines(string content)
            => content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
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
