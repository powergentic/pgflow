using Microsoft.Extensions.Logging;
using System.Text;
using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Core.Services;

public sealed class WorkflowExecutionConsole
{
    private readonly object _sync = new();
    private readonly bool _useErrorStream;
    private readonly bool _enableEnhancedDisplay;
    private readonly bool _isInteractiveConsole;
    private StreamWriter? _transcriptWriter;
    private string? _transcriptPath;
    private readonly List<string> _pendingTranscriptLines = [];
    private WorkflowExecutionStartedEvent? _run;
    private WorkflowActionStartedEvent? _currentAction;
    private int _succeededActions;
    private int _failedActions;
    private int _skippedActions;
    private string? _lastMessage;
    private int _lastDashboardLineCount;

    public WorkflowExecutionConsole(WorkflowExecutionDisplayOptions? options = null, bool useErrorStream = false)
    {
        var resolvedOptions = options ?? new WorkflowExecutionDisplayOptions();
        _useErrorStream = useErrorStream;
        _enableEnhancedDisplay = resolvedOptions.EnableEnhancedDisplay;
        _isInteractiveConsole = resolvedOptions.IsInteractiveConsole;
    }

    public string? TranscriptPath
    {
        get
        {
            lock (_sync)
            {
                return _transcriptPath;
            }
        }
    }

    public void StartTranscript(string logFolder)
    {
        lock (_sync)
        {
            var transcriptPath = Path.Combine(logFolder, "console.log");
            if (string.Equals(_transcriptPath, transcriptPath, StringComparison.OrdinalIgnoreCase) && _transcriptWriter is not null)
            {
                return;
            }

            _transcriptWriter?.Dispose();
            Directory.CreateDirectory(logFolder);
            _transcriptPath = transcriptPath;
            _transcriptWriter = new StreamWriter(new FileStream(_transcriptPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };

            foreach (var line in _pendingTranscriptLines)
            {
                _transcriptWriter.WriteLine(line);
            }

            _pendingTranscriptLines.Clear();
        }
    }

    public void StopTranscript()
    {
        lock (_sync)
        {
            try
            {
                ClearDashboardLocked();
                _transcriptWriter?.Dispose();
            }
            finally
            {
                _transcriptWriter = null;
            }
        }
    }

    public void WriteProgress(string message)
        => WriteLine(message, ConsoleColor.Cyan, "→ ");

    public void WriteSuccess(string message)
        => WriteLine(message, ConsoleColor.Green, "✓ ");

    public void WriteError(string message)
    {
        lock (_sync)
        {
            ClearDashboardLocked();
            var previousColor = Console.ForegroundColor;
            try
            {
                if (!Console.IsErrorRedirected)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                }

                Console.Error.WriteLine($"Error: {message}");
            }
            finally
            {
                if (!Console.IsErrorRedirected)
                {
                    Console.ForegroundColor = previousColor;
                }
            }

            AppendTranscriptLocked($"{DateTimeOffset.UtcNow:O} Error: {message}");
            RenderDashboardLocked();
        }
    }

    public void RecordRunStarted(WorkflowExecutionStartedEvent executionStarted)
    {
        lock (_sync)
        {
            _run = executionStarted;
            _currentAction = null;
            _succeededActions = 0;
            _failedActions = 0;
            _skippedActions = 0;
            _lastMessage = "Flow started";
            AppendTranscriptLocked($"{executionStarted.StartedAt:O} Flow started: {executionStarted.WorkflowName} (RunId={executionStarted.RunId})");
            RenderDashboardLocked();
        }
    }

    public void RecordActionStarted(WorkflowActionStartedEvent actionStarted)
    {
        lock (_sync)
        {
            _currentAction = actionStarted;
            _lastMessage = $"Running '{actionStarted.ActionId}' ({actionStarted.ActionType})";
            AppendTranscriptLocked($"{actionStarted.ActionStartedAt:O} Action started: {actionStarted.ActionId} ({actionStarted.ActionType}) visit {actionStarted.VisitCount}/{actionStarted.MaxVisitsPerAction}");
            RenderDashboardLocked();
        }
    }

    public void RecordActionCompleted(WorkflowActionCompletedEvent actionCompleted)
    {
        lock (_sync)
        {
            switch (actionCompleted.Status)
            {
                case ActionExecutionStatus.Succeeded:
                    _succeededActions++;
                    break;
                case ActionExecutionStatus.Failed:
                    _failedActions++;
                    break;
                case ActionExecutionStatus.Skipped:
                    _skippedActions++;
                    break;
            }

            _lastMessage = $"{actionCompleted.ActionId}: {actionCompleted.Status} ({actionCompleted.Summary})";
            AppendTranscriptLocked($"{actionCompleted.ActionCompletedAt:O} Action completed: {actionCompleted.ActionId} {actionCompleted.Status} in {(actionCompleted.ActionCompletedAt - actionCompleted.ActionStartedAt).TotalSeconds:F2}s - {actionCompleted.Summary}");
            RenderDashboardLocked();
        }
    }

    public void RecordPublishedEntry(WorkflowPublishedEntry entry)
    {
        lock (_sync)
        {
            AppendTranscriptLocked($"{DateTimeOffset.UtcNow:O} Published entry: {entry.Title} ({string.Join(", ", entry.To)})");
        }

        if (entry.To.Contains("console", StringComparer.OrdinalIgnoreCase))
        {
            WritePlainBlock($"===== {entry.Title} =====", entry.Content);
        }
    }

    public void RecordRunCompleted(WorkflowRunCompletedEvent runCompleted)
    {
        lock (_sync)
        {
            _currentAction = null;
            _lastMessage = runCompleted.Succeeded ? "Flow completed successfully" : "Flow completed with failures";
            AppendTranscriptLocked($"{runCompleted.CompletedAt:O} Flow completed: succeeded={runCompleted.Succeeded} transitions={runCompleted.TransitionCount} actions={runCompleted.TotalActions}");
            RenderDashboardLocked();
        }
    }

    public void WriteDiagnostic(string categoryName, LogLevel logLevel, string message, Exception? exception = null)
    {
        lock (_sync)
        {
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.UtcNow.ToString("O"));
            builder.Append(' ');
            builder.Append(logLevel.ToString().ToLowerInvariant());
            builder.Append(' ');
            builder.Append(categoryName);
            builder.Append(": ");
            builder.Append(message);

            if (exception is not null)
            {
                builder.Append(" :: ");
                builder.Append(exception.GetType().Name);
                builder.Append(": ");
                builder.Append(exception.Message);
            }

            AppendTranscriptLocked(builder.ToString());
        }
    }

    public void WriteStreamingLine(string message)
    {
        lock (_sync)
        {
            ClearDashboardLocked();
            WriteConsoleLineLocked(message, GetStreamingColor(message), null);
            AppendTranscriptLocked(message);
            RenderDashboardLocked();
        }
    }

    public void WritePlainBlock(string header, string content)
    {
        lock (_sync)
        {
            ClearDashboardLocked();
            WriteConsoleLineLocked(header, null, null);
            AppendTranscriptLocked(header);

            foreach (var line in NormalizeLines(content))
            {
                WriteConsoleLineLocked(line, null, null);
                AppendTranscriptLocked(line);
            }

            WriteConsoleLineLocked(string.Empty, null, null);
            AppendTranscriptLocked(string.Empty);
            RenderDashboardLocked();
        }
    }

    private void WriteLine(string message, ConsoleColor? color, string? prefix)
    {
        lock (_sync)
        {
            ClearDashboardLocked();
            WriteConsoleLineLocked(message, color, prefix);
            AppendTranscriptLocked($"{DateTimeOffset.UtcNow:O} {message}");
            RenderDashboardLocked();
        }
    }

    private void RenderDashboardLocked()
    {
        if (!_enableEnhancedDisplay || !_isInteractiveConsole || _run is null)
        {
            return;
        }

        var lines = BuildDashboardLinesLocked();
        ClearDashboardLocked();

        foreach (var line in lines)
        {
            Console.Out.WriteLine(line);
        }

        _lastDashboardLineCount = lines.Count;
    }

    private List<string> BuildDashboardLinesLocked()
    {
        var width = GetDashboardWidthLocked();
        var innerWidth = width - 2;
        var flowElapsed = DateTimeOffset.UtcNow - _run!.StartedAt;
        var currentActionStartedAt = _currentAction?.ActionStartedAt;
        var currentActionElapsed = currentActionStartedAt.HasValue ? DateTimeOffset.UtcNow - currentActionStartedAt.Value : TimeSpan.Zero;
        var currentActionName = _currentAction is null
            ? "(idle)"
            : string.Equals(_currentAction.ActionName, _currentAction.ActionId, StringComparison.OrdinalIgnoreCase)
                ? _currentAction.ActionId
                : $"{_currentAction.ActionName} [{_currentAction.ActionId}]";
        var currentActionType = _currentAction?.ActionType ?? "-";
        var currentActionStarted = _currentAction?.ActionStartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        var visit = _currentAction is null ? "-" : $"{_currentAction.VisitCount}/{_currentAction.MaxVisitsPerAction}";
        var transitionCount = _currentAction?.TransitionCount ?? 0;
        var transitions = $"{transitionCount}/{_run.MaxTransitions}";
        var state = _currentAction is not null
            ? "running"
            : _failedActions > 0
                ? "completed with failures"
                : "waiting";
        var results = $"ok {_succeededActions}   fail {_failedActions}   skip {_skippedActions}";
        var progress = BuildProgressBar(transitionCount, _run.MaxTransitions, 18);

        return
        [
            BuildBorder('╔', '╗', '═', innerWidth, "pgflow • live run"),
            BuildTwoColumnRow("Flow", _run.WorkflowName, "Run", _run.RunId, innerWidth),
            BuildTwoColumnRow("State", state, "Elapsed", FormatDuration(flowElapsed), innerWidth),
            BuildSeparator(innerWidth),
            BuildTwoColumnRow("Action", currentActionName, "Type", currentActionType, innerWidth),
            BuildTwoColumnRow("Started", currentActionStarted, "Visit", visit, innerWidth),
            BuildTwoColumnRow("Action Time", FormatDuration(currentActionElapsed), "Transitions", transitions, innerWidth),
            BuildSingleColumnRow("Progress", progress, innerWidth),
            BuildSingleColumnRow("Logs", ShortenForDisplay(_run.LogFolder, innerWidth - 14), innerWidth),
            BuildSeparator(innerWidth),
            BuildSingleColumnRow("Results", results, innerWidth),
            BuildSingleColumnRow("Last", _lastMessage ?? "-", innerWidth),
            BuildBorder('╚', '╝', '═', innerWidth)
        ];
    }

    private void ClearDashboardLocked()
    {
        if (_lastDashboardLineCount <= 0 || !_isInteractiveConsole)
        {
            return;
        }

        for (var i = 0; i < _lastDashboardLineCount; i++)
        {
            Console.Out.Write("\u001b[1A");
            Console.Out.Write("\r\u001b[2K");
        }

        _lastDashboardLineCount = 0;
    }

    private void WriteConsoleLineLocked(string message, ConsoleColor? color, string? prefix)
    {
        var writer = _useErrorStream ? Console.Error : Console.Out;
        var formatted = (prefix ?? string.Empty) + message;
        var canColorize = color.HasValue && !_useErrorStream && !Console.IsOutputRedirected;
        if (canColorize)
        {
            var previousColor = Console.ForegroundColor;
            var resolvedColor = color.GetValueOrDefault();
            try
            {
                Console.ForegroundColor = resolvedColor;
                writer.WriteLine(formatted);
            }
            finally
            {
                Console.ForegroundColor = previousColor;
            }

            return;
        }

        writer.WriteLine(formatted);
    }

    private static ConsoleColor? GetStreamingColor(string message)
    {
        if (message.StartsWith("╭─ GitHub Copilot", StringComparison.Ordinal))
        {
            return ConsoleColor.Cyan;
        }

        if (message.StartsWith("├─ Intent:", StringComparison.Ordinal))
        {
            return ConsoleColor.Magenta;
        }

        if (message.StartsWith("├─ Thought", StringComparison.Ordinal))
        {
            return ConsoleColor.Yellow;
        }

        if (message.StartsWith("├─ Response", StringComparison.Ordinal))
        {
            return ConsoleColor.Green;
        }

        if (message.StartsWith("╰─ Turn complete", StringComparison.Ordinal))
        {
            return ConsoleColor.DarkGray;
        }

        return null;
    }

    private void AppendTranscriptLocked(string line)
    {
        if (_transcriptWriter is not null)
        {
            _transcriptWriter.WriteLine(line);
            return;
        }

        _pendingTranscriptLines.Add(line);
    }

    private static IEnumerable<string> NormalizeLines(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static string BuildBorder(char left, char right, char fill, int innerWidth, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return $"{left}{new string(fill, innerWidth)}{right}";
        }

        var text = $" {title.Trim()} ";
        if (text.Length >= innerWidth)
        {
            text = ShortenForDisplay(text, innerWidth);
        }

        var leftFill = (innerWidth - text.Length) / 2;
        var rightFill = innerWidth - text.Length - leftFill;
        return $"{left}{new string(fill, leftFill)}{text}{new string(fill, rightFill)}{right}";
    }

    private static string BuildSeparator(int innerWidth)
        => $"╟{new string('─', innerWidth)}╢";

    private static string BuildTwoColumnRow(string leftLabel, string leftValue, string rightLabel, string rightValue, int innerWidth)
    {
        var columnWidth = innerWidth - 5;
        var leftWidth = (int)Math.Round(columnWidth * 0.62, MidpointRounding.AwayFromZero);
        var rightWidth = columnWidth - leftWidth;
        var left = FormatLabeledValue(leftLabel, leftValue, leftWidth);
        var right = FormatLabeledValue(rightLabel, rightValue, rightWidth);
        return $"║ {left} │ {right} ║";
    }

    private static string BuildSingleColumnRow(string label, string value, int innerWidth)
    {
        var content = FormatLabeledValue(label, value, innerWidth - 2);
        return $"║ {content} ║";
    }

    private static string FormatLabeledValue(string label, string value, int width)
    {
        const int labelWidth = 11;
        var normalized = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        var prefix = label.Length >= labelWidth
            ? $"{label[..(labelWidth - 1)]} "
            : label.PadRight(labelWidth);
        return ShortenForDisplay($"{prefix} {normalized}", width).PadRight(width);
    }

    private static string ShortenForDisplay(string value, int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= maxWidth)
        {
            return value;
        }

        if (maxWidth <= 3)
        {
            return value[..maxWidth];
        }

        return $"{value[..(maxWidth - 1)]}…";
    }

    private static string BuildProgressBar(int current, int total, int width)
    {
        var safeTotal = Math.Max(total, 1);
        var safeCurrent = Math.Clamp(current, 0, safeTotal);
        var filled = (int)Math.Round((double)safeCurrent / safeTotal * width, MidpointRounding.AwayFromZero);
        var empty = Math.Max(width - filled, 0);
        return $"[{new string('█', filled)}{new string('░', empty)}] {safeCurrent}/{safeTotal}";
    }

    private static int GetDashboardWidthLocked()
    {
        try
        {
            var windowWidth = Console.WindowWidth;
            if (windowWidth <= 0)
            {
                return 100;
            }

            return Math.Clamp(windowWidth - 1, 72, 140);
        }
        catch
        {
            return 100;
        }
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
}
