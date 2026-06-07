using Microsoft.Extensions.Logging;

namespace Powergentic.Flow.Core.Services;

public sealed class WorkflowExecutionTranscriptLoggerProvider(WorkflowExecutionConsole executionConsole) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new WorkflowExecutionTranscriptLogger(categoryName, executionConsole);

    public void Dispose()
    {
    }

    private sealed class WorkflowExecutionTranscriptLogger(string categoryName, WorkflowExecutionConsole executionConsole) : ILogger
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

            executionConsole.WriteDiagnostic(categoryName, logLevel, message, exception);
        }
    }
}
