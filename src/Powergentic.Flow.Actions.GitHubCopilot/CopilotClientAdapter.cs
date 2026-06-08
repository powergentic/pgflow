using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
        var liveOutput = new CopilotLiveOutputLogger(liveOutputLogger, request.Streaming);

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

    private sealed class CopilotLiveOutputLogger(ILogger logger, bool streamingEnabled)
    {
        private const int FlushThreshold = 320;

        private readonly Lock _sync = new();
        private readonly StringBuilder _responseBuffer = new();
        private readonly StringBuilder _thoughtBuffer = new();
        private bool _bannerWritten;
        private bool _completed;
        private bool _sawResponseDelta;
        private bool _sawThoughtDelta;
        private CopilotSection _currentSection;
        private string? _lastIntent;
        private string? _lastThoughtBlock;
        private string? _lastResponseBlock;

        public void HandleEvent(SessionEvent sessionEvent)
        {
            lock (_sync)
            {
                switch (sessionEvent)
                {
                    case AssistantIntentEvent { Data.Intent: { Length: > 0 } intent }:
                        LogIntent(intent);
                        break;

                    case AssistantReasoningDeltaEvent { Data.DeltaContent: { Length: > 0 } thoughtDelta }:
                        _sawThoughtDelta = true;
                        if (streamingEnabled)
                        {
                            AppendChunk(_thoughtBuffer, CopilotSection.Thought, thoughtDelta);
                        }
                        break;

                    case AssistantMessageDeltaEvent { Data.DeltaContent: { Length: > 0 } responseDelta }:
                        _sawResponseDelta = true;
                        if (streamingEnabled)
                        {
                            AppendChunk(_responseBuffer, CopilotSection.Response, responseDelta);
                        }
                        break;

                    case AssistantReasoningEvent { Data: { Content: { Length: > 0 } } reasoningEventData } when !_sawThoughtDelta:
                        AppendText(CopilotSection.Thought, reasoningEventData.Content);
                        break;

                    case AssistantMessageEvent { Data: { ReasoningText: { Length: > 0 } } messageEventData } when !_sawThoughtDelta:
                        AppendText(CopilotSection.Thought, messageEventData.ReasoningText);
                        break;

                    case AssistantMessageEvent { Data: { Content: { Length: > 0 } } messageEventData } when !_sawResponseDelta:
                        AppendText(CopilotSection.Response, messageEventData.Content);
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
                    AppendText(CopilotSection.Thought, responseData.ReasoningText);
                }

                if (!_sawResponseDelta && !string.IsNullOrWhiteSpace(responseData?.Content))
                {
                    AppendText(CopilotSection.Response, responseData.Content);
                }

                FlushBuffer(_thoughtBuffer, CopilotSection.Thought);
                FlushBuffer(_responseBuffer, CopilotSection.Response);

                if (_bannerWritten && !_completed)
                {
                    logger.LogInformation("╰─ Turn complete");
                    _completed = true;
                }
            }
        }

        private void LogIntent(string intent)
        {
            var normalized = NormalizeContent(intent);
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(_lastIntent, normalized, StringComparison.Ordinal))
            {
                return;
            }

            EnsureBanner();
            _currentSection = CopilotSection.None;
            _lastIntent = normalized;
            logger.LogInformation("├─ Intent: {Intent}", normalized);
        }

        private void AppendText(CopilotSection section, string content)
        {
            var normalized = NormalizeContent(content);
            if (string.IsNullOrWhiteSpace(normalized) || IsDuplicate(section, normalized))
            {
                return;
            }

            BeginSection(section);
            WriteBlock(normalized);
        }

        private void AppendChunk(StringBuilder buffer, CopilotSection section, string chunk)
        {
            foreach (var character in NormalizeNewlines(chunk))
            {
                if (character == '\n')
                {
                    FlushBuffer(buffer, section);
                    continue;
                }

                buffer.Append(character);
                if (buffer.Length >= FlushThreshold)
                {
                    FlushBuffer(buffer, section);
                }
            }

            if (buffer.Length > 0 && ShouldFlushOnChunkBoundary(chunk, buffer.Length, section))
            {
                FlushBuffer(buffer, section);
            }
        }

        private void FlushBuffer(StringBuilder buffer, CopilotSection section)
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var content = buffer.ToString();
            buffer.Clear();
            AppendText(section, content);
        }

        private void BeginSection(CopilotSection section)
        {
            EnsureBanner();
            if (_currentSection == section)
            {
                return;
            }

            _currentSection = section;
            logger.LogInformation(section switch
            {
                CopilotSection.Thought => "├─ Thought",
                CopilotSection.Response => "├─ Response",
                _ => "├─ Copilot"
            });
        }

        private void WriteBlock(string content)
        {
            foreach (var line in NormalizeLines(content))
            {
                if (line.Length == 0)
                {
                    logger.LogInformation("│");
                    continue;
                }

                logger.LogInformation("│  {Line}", line);
            }
        }

        private void EnsureBanner()
        {
            if (_bannerWritten)
            {
                return;
            }

            logger.LogInformation("╭─ GitHub Copilot");
            _bannerWritten = true;
        }

        private bool IsDuplicate(CopilotSection section, string content)
        {
            ref var lastBlock = ref section == CopilotSection.Thought
                ? ref _lastThoughtBlock
                : ref _lastResponseBlock;

            if (string.Equals(lastBlock, content, StringComparison.Ordinal))
            {
                return true;
            }

            lastBlock = content;
            return false;
        }

        private static bool ShouldFlushOnChunkBoundary(string chunk, int bufferLength, CopilotSection section)
        {
            var normalizedChunk = chunk.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            if (normalizedChunk.Length == 0 || normalizedChunk[^1] == '\n')
            {
                return false;
            }

            var preferredLength = section == CopilotSection.Thought ? 24 : 48;
            var forcedLength = section == CopilotSection.Thought ? 64 : 120;
            if (bufferLength >= forcedLength)
            {
                return true;
            }

            var trailingCharacter = normalizedChunk[^1];
            return bufferLength >= preferredLength
                && (char.IsWhiteSpace(trailingCharacter)
                    || trailingCharacter is '.' or ',' or ':' or ';' or '!' or '?' or ')' or ']' or '}');
        }

        private static string NormalizeContent(string content)
            => string.Join('\n', NormalizeLines(content)).Trim();

        private static IEnumerable<char> NormalizeNewlines(string content)
        {
            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            foreach (var character in normalized)
            {
                yield return character;
            }
        }

        private static string[] NormalizeLines(string content)
            => content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

        private enum CopilotSection
        {
            None,
            Thought,
            Response,
        }
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
