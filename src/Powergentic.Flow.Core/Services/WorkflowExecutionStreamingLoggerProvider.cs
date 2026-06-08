using Microsoft.Extensions.Logging;

namespace Powergentic.Flow.Core.Services;

public sealed class WorkflowExecutionStreamingLoggerProvider(WorkflowExecutionConsole executionConsole) : ILoggerProvider
{
    public const string CategoryName = "Powergentic.Flow.Streaming";

    public ILogger CreateLogger(string categoryName)
        => string.Equals(categoryName, CategoryName, StringComparison.Ordinal)
            ? new WorkflowExecutionStreamingLogger(executionConsole)
            : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class WorkflowExecutionStreamingLogger(WorkflowExecutionConsole executionConsole) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            executionConsole.WriteStreamingLine(message);

            if (exception is not null)
            {
                executionConsole.WriteStreamingLine($"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
