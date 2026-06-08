using System.Text;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Actions.GitHubCopilot;

internal class CopilotSessionLiveOutputLogger(ILogger logger, bool streamingEnabled)
{
    private const int FlushThreshold = 320;

    private readonly Lock _sync = new();
    private readonly StringBuilder _responseBuffer = new();
    private readonly StringBuilder _thoughtBuffer = new();
    private bool _bannerWritten;
    private bool _completed;
    private bool _sawResponseDelta;
    private bool _sawThoughtDelta;
    private bool _wroteActualModelLine;
    private bool _wroteResponseOutput;
    private bool _wroteThoughtOutput;
    private CopilotSection _currentSection;
    private string? _actualModel;
    private string? _latestResponseText;
    private string? _latestThoughtText;
    private string? _lastIntent;
    private string? _lastThoughtBlock;
    private string? _lastResponseBlock;

    public void LogRequest(CopilotPromptRequest request)
    {
        lock (_sync)
        {
            EnsureBanner();
            LogRequestValue("Agent", string.IsNullOrWhiteSpace(request.Agent) ? "(default)" : request.Agent);
            LogRequestValue("Model", string.IsNullOrWhiteSpace(request.Model) ? "auto" : request.Model);
            LogRequestBlock("System Prompt", request.SystemPrompt);
            LogRequestBlock("Prompt", request.Prompt);
            _currentSection = CopilotSection.None;
        }
    }

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
                    HandleThoughtDelta(thoughtDelta);
                    break;

                case AssistantMessageDeltaEvent { Data.DeltaContent: { Length: > 0 } responseDelta }:
                    HandleResponseDelta(responseDelta);
                    break;

                case AssistantReasoningEvent { Data: { Content: { Length: > 0 } } reasoningEventData }:
                    HandleThought(reasoningEventData.Content);
                    break;

                case AssistantMessageEvent { Data: var messageEventData }:
                    if (!string.IsNullOrWhiteSpace(messageEventData.ReasoningText))
                    {
                        HandleThought(messageEventData.ReasoningText);
                    }

                    if (!string.IsNullOrWhiteSpace(messageEventData.Content))
                    {
                        HandleResponse(messageEventData.Content, messageEventData.Model);
                    }
                    break;

                default:
                    break;
            }
        }
    }

    public void HandleIntent(string intent)
    {
        lock (_sync)
        {
            LogIntent(intent);
        }
    }

    public void HandleThoughtDelta(string thoughtDelta)
    {
        lock (_sync)
        {
            _sawThoughtDelta = true;
            if (streamingEnabled)
            {
                AppendChunk(_thoughtBuffer, CopilotSection.Thought, thoughtDelta);
            }
            else
            {
                BufferChunk(_thoughtBuffer, thoughtDelta);
            }
        }
    }

    public void HandleResponseDelta(string responseDelta)
    {
        lock (_sync)
        {
            _sawResponseDelta = true;
            if (streamingEnabled)
            {
                AppendChunk(_responseBuffer, CopilotSection.Response, responseDelta);
            }
            else
            {
                BufferChunk(_responseBuffer, responseDelta);
            }
        }
    }

    public void HandleThought(string content)
    {
        lock (_sync)
        {
            CaptureLatestThought(content);
            if (!_sawThoughtDelta)
            {
                AppendText(CopilotSection.Thought, content);
            }
        }
    }

    public void HandleResponse(string content, string? model = null)
    {
        lock (_sync)
        {
            CaptureActualModel(model);
            CaptureLatestResponse(content);
            if (!_sawResponseDelta)
            {
                AppendText(CopilotSection.Response, content);
            }
        }
    }

    public void Complete(AssistantMessageData? responseData)
        => Complete(responseData?.ReasoningText, responseData?.Content, responseData?.Model);

    public void Complete(string? reasoningText, string? responseText, string? model)
    {
        lock (_sync)
        {
            CaptureActualModel(model);
            CaptureLatestThought(reasoningText);
            CaptureLatestResponse(responseText);
            FlushThoughtForCompletion();
            FlushResponseForCompletion();

            if (_bannerWritten && !_completed)
            {
                logger.LogInformation("╰─ Turn complete");
                _completed = true;
            }
        }
    }

    private void FlushThoughtForCompletion()
    {
        if (!_wroteThoughtOutput && !string.IsNullOrWhiteSpace(_latestThoughtText))
        {
            _thoughtBuffer.Clear();
            AppendText(CopilotSection.Thought, _latestThoughtText);
            return;
        }

        FlushBuffer(_thoughtBuffer, CopilotSection.Thought);
    }

    private void FlushResponseForCompletion()
    {
        if (!_wroteResponseOutput && !string.IsNullOrWhiteSpace(_latestResponseText))
        {
            _responseBuffer.Clear();
            AppendText(CopilotSection.Response, _latestResponseText);
            return;
        }

        FlushBuffer(_responseBuffer, CopilotSection.Response);
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

    private void LogRequestValue(string label, string? value)
    {
        EnsureBanner();
        logger.LogInformation("├─ {Label}: {Value}", label, string.IsNullOrWhiteSpace(value) ? "(none)" : value);
    }

    private void LogRequestBlock(string label, string? content)
    {
        EnsureBanner();
        logger.LogInformation("├─ {Label}", label);
        var normalized = NormalizeContent(content ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            logger.LogInformation("│  (none)");
            return;
        }

        WriteBlock(normalized);
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
        if (section == CopilotSection.Thought)
        {
            _wroteThoughtOutput = true;
        }
        else if (section == CopilotSection.Response)
        {
            _wroteResponseOutput = true;
        }
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

    private static void BufferChunk(StringBuilder buffer, string chunk)
    {
        foreach (var character in NormalizeNewlines(chunk))
        {
            buffer.Append(character);
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
            if (section == CopilotSection.Response)
            {
                LogActualModelIfAvailable();
            }

            return;
        }

        _currentSection = section;
        logger.LogInformation(section switch
        {
            CopilotSection.Thought => "├─ Thought",
            CopilotSection.Response => "├─ Response",
            _ => "├─ Copilot"
        });

        if (section == CopilotSection.Response)
        {
            LogActualModelIfAvailable();
        }
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

    private void CaptureActualModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        _actualModel = model;
        if (_currentSection == CopilotSection.Response)
        {
            LogActualModelIfAvailable();
        }
    }

    private void CaptureLatestThought(string? content)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            _latestThoughtText = content;
        }
    }

    private void CaptureLatestResponse(string? content)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            _latestResponseText = content;
        }
    }

    private void LogActualModelIfAvailable()
    {
        if (_wroteActualModelLine || string.IsNullOrWhiteSpace(_actualModel))
        {
            return;
        }

        logger.LogInformation("│  Model Used: {Model}", _actualModel);
        _wroteActualModelLine = true;
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
