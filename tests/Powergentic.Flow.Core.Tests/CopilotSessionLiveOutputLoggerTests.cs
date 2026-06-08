using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using Powergentic.Flow.Actions.GitHubCopilot;
using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Core.Tests;

public sealed class CopilotSessionLiveOutputLoggerTests
{
    [Fact]
    public void Complete_WritesThoughtFromFinalReasoningText_WhenResponseWasAlreadyWrittenFirst()
    {
        var logger = new ListLogger();
        var liveOutput = new CopilotSessionLiveOutputLogger(logger, streamingEnabled: true);

        liveOutput.LogRequest(new CopilotPromptRequest
        {
            Prompt = "prompt",
            WorkingDirectory = "/tmp",
            Streaming = true,
            Model = "auto",
        });
        liveOutput.HandleResponse("Final response", "gpt-5");

        liveOutput.Complete(reasoningText: "Final thought", responseText: "Final response", model: "gpt-5");

        var output = string.Join("\n", logger.Messages);
        Assert.Contains("├─ Thought", output, StringComparison.Ordinal);
        Assert.Contains("│  Final thought", output, StringComparison.Ordinal);
        Assert.Contains("├─ Response", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Complete_WritesBufferedThoughtDelta_WhenStreamingDisabled()
    {
        var logger = new ListLogger();
        var liveOutput = new CopilotSessionLiveOutputLogger(logger, streamingEnabled: false);

        liveOutput.LogRequest(new CopilotPromptRequest
        {
            Prompt = "prompt",
            WorkingDirectory = "/tmp",
            Streaming = false,
            Model = "auto",
        });
        liveOutput.HandleThoughtDelta("Thinking through ");
        liveOutput.HandleThoughtDelta("the problem");
        liveOutput.Complete(reasoningText: null, responseText: null, model: null);

        var output = string.Join("\n", logger.Messages);
        Assert.Contains("├─ Thought", output, StringComparison.Ordinal);
        Assert.Contains("│  Thinking through the problem", output, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleResponse_WritesModelUsedImmediatelyAfterResponseHeading()
    {
        var logger = new ListLogger();
        var liveOutput = new CopilotSessionLiveOutputLogger(logger, streamingEnabled: true);

        liveOutput.LogRequest(new CopilotPromptRequest
        {
            Prompt = "prompt",
            WorkingDirectory = "/tmp",
            Streaming = true,
            Model = "auto",
        });
        liveOutput.HandleResponse("Response text", "gpt-5.1");
        liveOutput.Complete(reasoningText: null, responseText: "Response text", model: "gpt-5.1");

        var responseIndex = logger.Messages.FindIndex(message => string.Equals(message, "├─ Response", StringComparison.Ordinal));
        Assert.NotEqual(-1, responseIndex);
        Assert.True(responseIndex + 2 < logger.Messages.Count);
        Assert.Equal("│  Model Used: gpt-5.1", logger.Messages[responseIndex + 1]);
        Assert.Equal("│  Response text", logger.Messages[responseIndex + 2]);
        Assert.Equal(1, logger.Messages.Count(message => string.Equals(message, "│  Model Used: gpt-5.1", StringComparison.Ordinal)));
    }

    [Fact]
    public void HandleEvent_IgnoresUnknownSessionEvents()
    {
        var logger = new ListLogger();
        var liveOutput = new CopilotSessionLiveOutputLogger(logger, streamingEnabled: true);

        liveOutput.LogRequest(new CopilotPromptRequest
        {
            Prompt = "prompt",
            WorkingDirectory = "/tmp",
            Streaming = true,
            Model = "auto",
        });
        liveOutput.HandleEvent(new SessionStartEvent
        {
            Data = new SessionStartData
            {
                SessionId = "session-1",
                CopilotVersion = "1.0.0",
                Producer = "test",
                StartTime = DateTimeOffset.UtcNow,
                Version = 1,
            },
        });

        var output = string.Join("\n", logger.Messages);
        Assert.DoesNotContain("JSON RAWRAWRAW", output, StringComparison.Ordinal);
        Assert.DoesNotContain("├─ Thought", output, StringComparison.Ordinal);
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
