using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Powergentic.AI.Orchestrator.Core.Abstractions;
using Powergentic.AI.Orchestrator.Core.Models;

namespace Powergentic.AI.Orchestrator.Actions.Script;

public sealed class ScriptActionRunner(ILogger<ScriptActionRunner> logger) : IActionRunner
{
    public string ActionType => "script";

    public async Task<ActionResult> RunAsync(ActionExecutionContext context, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var shell = context.GetString("shell")?.Trim().ToLowerInvariant();
        var inlineScript = context.GetString("run");
        var fileValue = context.GetString("file");
        var pathValue = context.GetString("path");
        var workingDirectory = ResolveWorkingDirectory(context, context.GetString("workingDirectory"));
        var environment = context.GetStringMap("environment");
        var failOnNonZeroExit = !bool.TryParse(context.GetString("failOnNonZeroExit"), out var parsed) || parsed;

        if (string.IsNullOrWhiteSpace(shell))
        {
            throw new InvalidOperationException($"Action '{context.Action.Id}' requires 'with.shell'.");
        }

        var scriptPath = await ResolveScriptPathAsync(context, shell, inlineScript, fileValue, pathValue, cancellationToken);
        var outputFile = Path.Combine(context.ActionLogDirectory, $"{context.ActionLogPrefix}.outputs.txt");
        var stdOutFile = Path.Combine(context.ActionLogDirectory, $"{context.ActionLogPrefix}.stdout.log");
        var stdErrFile = Path.Combine(context.ActionLogDirectory, $"{context.ActionLogPrefix}.stderr.log");

        Directory.CreateDirectory(context.ActionLogDirectory);

        var startInfo = BuildStartInfo(shell, scriptPath, workingDirectory, context, environment, outputFile);
        using var process = new Process { StartInfo = startInfo };

        logger.LogInformation("Executing {Shell} script for action {ActionId}: {ScriptPath}", shell, context.Action.Id, scriptPath);
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        await File.WriteAllTextAsync(stdOutFile, stdOut, cancellationToken);
        await File.WriteAllTextAsync(stdErrFile, stdErr, cancellationToken);

        var outputs = await ReadOutputsAsync(outputFile, cancellationToken);
        var succeeded = process.ExitCode == 0 || !failOnNonZeroExit;

        return new ActionResult
        {
            ActionId = context.Action.Id,
            Status = succeeded ? ActionExecutionStatus.Succeeded : ActionExecutionStatus.Failed,
            ExitCode = process.ExitCode,
            Summary = succeeded ? "Script completed." : $"Script exited with code {process.ExitCode}.",
            Outputs = outputs,
            Metadata = new Dictionary<string, object?>
            {
                ["shell"] = shell,
                ["scriptPath"] = scriptPath,
                ["workingDirectory"] = workingDirectory,
            },
            StdOutFile = stdOutFile,
            StdErrFile = stdErrFile,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ProcessStartInfo BuildStartInfo(string shell, string scriptPath, string workingDirectory, ActionExecutionContext context, Dictionary<string, string?> environment, string outputFile)
    {
        var startInfo = shell switch
        {
            "bash" => new ProcessStartInfo("bash") { ArgumentList = { scriptPath } },
            "pwsh" => new ProcessStartInfo("pwsh") { ArgumentList = { "-File", scriptPath } },
            _ => throw new InvalidOperationException($"Unsupported shell '{shell}'.")
        };

        startInfo.WorkingDirectory = workingDirectory;
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.Environment["ORCHESTRATOR_OUTPUT"] = outputFile;
        startInfo.Environment["ORCHESTRATOR_PROJECT_FOLDER"] = context.ProjectFolder;
        startInfo.Environment["ORCHESTRATOR_TARGET_WORKING_DIRECTORY"] = context.TargetWorkingDirectory;
        startInfo.Environment["ORCHESTRATOR_RUN_ID"] = context.RunId;

        foreach (var pair in context.Environment)
        {
            if (pair.Value is not null)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in environment)
        {
            if (pair.Value is not null)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        return startInfo;
    }

    private static string ResolveWorkingDirectory(ActionExecutionContext context, string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return context.TargetWorkingDirectory;
        }

        return Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(context.TargetWorkingDirectory, configured));
    }

    private static async Task<string> ResolveScriptPathAsync(ActionExecutionContext context, string shell, string? inlineScript, string? fileValue, string? pathValue, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            var projectRelativePath = Path.GetFullPath(Path.Combine(context.ProjectFolder, pathValue));
            if (!File.Exists(projectRelativePath))
            {
                throw new FileNotFoundException($"Script file '{pathValue}' was not found in the workflow project folder.", projectRelativePath);
            }

            return projectRelativePath;
        }

        if (!string.IsNullOrWhiteSpace(fileValue))
        {
            var resolvedFilePath = Path.IsPathRooted(fileValue)
                ? fileValue
                : Path.GetFullPath(Path.Combine(context.ProjectFolder, fileValue));

            if (!File.Exists(resolvedFilePath))
            {
                throw new FileNotFoundException($"Script file '{fileValue}' was not found.", resolvedFilePath);
            }

            return resolvedFilePath;
        }

        if (string.IsNullOrWhiteSpace(inlineScript))
        {
            throw new InvalidOperationException($"Action '{context.Action.Id}' requires one of 'with.run', 'with.file', or 'with.path'.");
        }

        var extension = shell switch
        {
            "bash" => ".sh",
            "pwsh" => ".ps1",
            _ => ".txt"
        };

        var tempScriptPath = Path.Combine(context.ActionLogDirectory, $"{context.ActionLogPrefix}{extension}");
        await File.WriteAllTextAsync(tempScriptPath, inlineScript, cancellationToken);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && shell == "bash")
        {
            try
            {
                File.SetUnixFileMode(tempScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch
            {
            }
        }

        return tempScriptPath;
    }

    private static async Task<Dictionary<string, string?>> ReadOutputsAsync(string outputFile, CancellationToken cancellationToken)
    {
        var outputs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(outputFile))
        {
            return outputs;
        }

        var lines = await File.ReadAllLinesAsync(outputFile, cancellationToken);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            outputs[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return outputs;
    }
}
