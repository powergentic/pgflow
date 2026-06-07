using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Powergentic.Flow.Actions.GitHubCopilot;

public sealed class CopilotInstallationProbe : ICopilotInstallationProbe
{
    private const int PollIntervalMilliseconds = 50;
    private const int MaxWaitMilliseconds = 1500;
    private static readonly Regex VersionPattern = new(@"\b\d+\.\d+(?:\.\d+)?(?:[-+][A-Za-z0-9.-]+)?\b", RegexOptions.Compiled);
    private readonly Func<(bool Exited, string StandardOutput, string StandardError)> runVersionCommand;

    public CopilotInstallationProbe()
        : this(RunVersionCommand)
    {
    }

    public CopilotInstallationProbe(Func<(bool Exited, string StandardOutput, string StandardError)> runVersionCommand)
    {
        this.runVersionCommand = runVersionCommand ?? throw new ArgumentNullException(nameof(runVersionCommand));
    }

    public bool IsInstalled()
    {
        try
        {
            var result = runVersionCommand();
            var output = string.Join(Environment.NewLine, [result.StandardOutput, result.StandardError])
                .Trim();

            return !string.IsNullOrWhiteSpace(output) && VersionPattern.IsMatch(output);
        }
        catch
        {
            return false;
        }
    }

    private static (bool Exited, string StandardOutput, string StandardError) RunVersionCommand()
    {
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var syncLock = new object();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "copilot",
                Arguments = "--version",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        process.OutputDataReceived += (_, args) => AppendCapturedLine(standardOutput, args.Data, syncLock);
        process.ErrorDataReceived += (_, args) => AppendCapturedLine(standardError, args.Data, syncLock);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        var exited = false;
        for (var waited = 0; waited < MaxWaitMilliseconds; waited += PollIntervalMilliseconds)
        {
            if (process.WaitForExit(PollIntervalMilliseconds))
            {
                exited = true;
                process.WaitForExit();
                break;
            }

            if (ContainsVersion(standardOutput, standardError, syncLock))
            {
                break;
            }
        }

        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(PollIntervalMilliseconds);
            }
            catch
            {
            }
        }

        return (exited, ReadCapturedText(standardOutput, syncLock), ReadCapturedText(standardError, syncLock));
    }

    private static bool ContainsVersion(StringBuilder standardOutput, StringBuilder standardError, object syncLock)
        => VersionPattern.IsMatch(string.Join(Environment.NewLine, [ReadCapturedText(standardOutput, syncLock), ReadCapturedText(standardError, syncLock)]));

    private static string ReadCapturedText(StringBuilder builder, object syncLock)
    {
        lock (syncLock)
        {
            return builder.ToString();
        }
    }

    private static void AppendCapturedLine(StringBuilder builder, string? line, object syncLock)
    {
        if (line is null)
        {
            return;
        }

        lock (syncLock)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
        }
    }
}
