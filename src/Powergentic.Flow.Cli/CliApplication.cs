using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Powergentic.Flow.Actions.GitHubCopilot;
using Powergentic.Flow.Actions.Script;
using Powergentic.Flow.Core;
using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;

namespace Powergentic.Flow.Cli;

public static class CliApplication
{
    private static string FlowYamlFileName = "flow.yml"; //flow.yml filename

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var command = ParseCommand(args);

            return command.Name switch
            {
                "run" => await RunWorkflowAsync(command, cancellationToken),
                "validate" => await ValidateWorkflowAsync(command, cancellationToken),
                "init" => await InitProjectAsync(command, cancellationToken),
                "logs" => await ShowLogsAsync(command, cancellationToken),
                "version" => WriteVersionAndReturn(0),
                "help" => WriteHelpAndReturn(0),
                _ => WriteError(command.Json, $"Unknown command '{command.Name}'.")
            };
        }
        catch (CliUsageException ex)
        {
            return WriteError(args.Contains("--json", StringComparer.OrdinalIgnoreCase), ex.Message);
        }
        catch (Exception ex)
        {
            return WriteUnhandledError(ex, args.Contains("--json", StringComparer.OrdinalIgnoreCase));
        }
    }

    private static async Task<int> RunWorkflowAsync(ParsedCommand command, CancellationToken cancellationToken)
    {
        var projectFolder = ResolveProjectFolder(command.ProjectFolder);
        var targetWorkingDirectory = ResolveTargetWorkingDirectory(command.TargetWorkingDirectory);
        var workflowFile = ResolveWorkflowFile(projectFolder, command.WorkflowFile);
        EnsureProjectExists(projectFolder);
        EnsureProjectExists(targetWorkingDirectory, "Target working directory");
        EnsureWorkflowExists(workflowFile);

        if (command.DryRun)
        {
            return await ValidateWorkflowAsync(command with { Name = "validate" }, cancellationToken);
        }

        await using var provider = BuildServiceProvider(GetLogLevel(command));
        var executor = provider.GetRequiredService<IWorkflowExecutor>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Cli");
        var loader = provider.GetRequiredService<IWorkflowLoader>();
        var installationProbe = provider.GetRequiredService<ICopilotInstallationProbe>();

        try
        {
            var workflow = await loader.LoadAsync(workflowFile, cancellationToken);
            ApplyInputOverrides(workflow, command.InputOverrides);
            ApplyVariableOverrides(workflow, command.VariableOverrides);
            ApplyEnvironmentOverrides(workflow, command.EnvironmentOverrides);
            EnsureGitHubCopilotInstalledIfRequired(workflow, installationProbe);

            var result = await executor.ExecuteAsync(
                projectFolder,
                targetWorkingDirectory,
                workflowFile,
                command.InputOverrides,
                command.VariableOverrides,
                command.EnvironmentOverrides,
                cancellationToken);
            if (command.Json)
            {
                WriteJson(result);
            }
            else
            {
                logger.LogInformation(
                    "Workflow {WorkflowName} finished. Success={Success}. Target={TargetWorkingDirectory}. Logs={LogFolder}",
                    result.WorkflowName,
                    result.Succeeded,
                    targetWorkingDirectory,
                    result.LogFolder);
            }

            return result.Succeeded ? 0 : 3;
        }
        catch (CliUsageException ex)
        {
            return WriteError(command.Json, ex.Message);
        }
        catch (Exception ex)
        {
            if (command.Json)
            {
                WriteJson(new
                {
                    succeeded = false,
                    command = "run",
                    projectFolder,
                    targetWorkingDirectory,
                    workflowFile,
                    error = ex.Message,
                });
            }
            else
            {
                logger.LogError(ex, "Workflow execution failed.");
            }

            return 10;
        }
    }

    private static async Task<int> ValidateWorkflowAsync(ParsedCommand command, CancellationToken cancellationToken)
    {
        var projectFolder = ResolveProjectFolder(command.ProjectFolder);
        var workflowFile = ResolveWorkflowFile(projectFolder, command.WorkflowFile);
        EnsureProjectExists(projectFolder);
        EnsureWorkflowExists(workflowFile);

        await using var provider = BuildServiceProvider(GetLogLevel(command));
        var loader = provider.GetRequiredService<IWorkflowLoader>();
        var validator = provider.GetRequiredService<IWorkflowValidator>();

        try
        {
            var workflow = await loader.LoadAsync(workflowFile, cancellationToken);
            ApplyInputOverrides(workflow, command.InputOverrides);
            ApplyVariableOverrides(workflow, command.VariableOverrides);
            ApplyEnvironmentOverrides(workflow, command.EnvironmentOverrides);
            var validation = validator.Validate(workflow, projectFolder);

            if (command.Json)
            {
                WriteJson(new
                {
                    succeeded = validation.Succeeded,
                    command = "validate",
                    projectFolder,
                    workflowFile,
                    workflowName = workflow.Name,
                    errors = validation.Errors,
                });
            }
            else if (validation.Succeeded)
            {
                Console.WriteLine($"Workflow '{workflow.Name}' is valid.");
            }
            else
            {
                Console.Error.WriteLine($"Workflow '{workflow.Name}' is invalid:");
                foreach (var error in validation.Errors)
                {
                    Console.Error.WriteLine($"- {error}");
                }
            }

            return validation.Succeeded ? 0 : 2;
        }
        catch (Exception ex)
        {
            if (command.Json)
            {
                WriteJson(new
                {
                    succeeded = false,
                    command = "validate",
                    projectFolder,
                    workflowFile,
                    error = ex.Message,
                });
            }
            else
            {
                Console.Error.WriteLine($"Validation failed: {ex.Message}");
            }

            return 10;
        }
    }

    private static async Task<int> InitProjectAsync(ParsedCommand command, CancellationToken cancellationToken)
    {
        var projectFolder = ResolveProjectFolder(command.ProjectFolder);
        var templateName = command.Template;
        var template = GetTemplateFiles(templateName);

        Directory.CreateDirectory(projectFolder);
        var existingEntries = Directory.EnumerateFileSystemEntries(projectFolder).Any();
        if (existingEntries && !command.Force)
        {
            return WriteError(command.Json, $"Target folder is not empty: {projectFolder}. Use --force to overwrite scaffold files.");
        }

        foreach (var file in template)
        {
            var targetPath = Path.Combine(projectFolder, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, file.Content, cancellationToken);
            if (file.MakeExecutable && !OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(
                        targetPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch
                {
                }
            }
        }

        if (command.Json)
        {
            WriteJson(new
            {
                succeeded = true,
                command = "init",
                projectFolder,
                template = templateName,
                files = template.Select(file => file.RelativePath).ToArray(),
            });
        }
        else
        {
            Console.WriteLine($"Initialized '{templateName}' workflow scaffold at {projectFolder}");
        }

        return 0;
    }

    private static async Task<int> ShowLogsAsync(ParsedCommand command, CancellationToken cancellationToken)
    {
        var projectFolder = ResolveProjectFolder(command.ProjectFolder);
        EnsureProjectExists(projectFolder);

        var logsFolder = Path.Combine(projectFolder, "logs");
        if (!Directory.Exists(logsFolder))
        {
            return WriteError(command.Json, $"Logs folder does not exist: {logsFolder}");
        }

        var runFolders = Directory.GetDirectories(logsFolder)
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (runFolders.Length == 0)
        {
            return WriteError(command.Json, $"No workflow runs were found under {logsFolder}");
        }

        string? selectedRunFolder = null;
        if (!string.IsNullOrWhiteSpace(command.RunId))
        {
            selectedRunFolder = runFolders.FirstOrDefault(path => string.Equals(Path.GetFileName(path), command.RunId, StringComparison.OrdinalIgnoreCase));
            if (selectedRunFolder is null)
            {
                return WriteError(command.Json, $"Run '{command.RunId}' was not found under {logsFolder}");
            }
        }
        else
        {
            selectedRunFolder = runFolders[0];
        }

        var runFile = Path.Combine(selectedRunFolder, "run.json");
        if (!File.Exists(runFile))
        {
            return WriteError(command.Json, $"Run summary is missing: {runFile}");
        }

        await using var stream = File.OpenRead(runFile);
        var run = await JsonSerializer.DeserializeAsync<WorkflowRunResult>(stream, JsonOptions, cancellationToken);
        if (run is null)
        {
            return WriteError(command.Json, $"Unable to read run summary from {runFile}");
        }

        if (command.Json)
        {
            WriteJson(run);
        }
        else
        {
            Console.WriteLine($"Run ID:       {run.RunId}");
            Console.WriteLine($"Workflow:     {run.WorkflowName}");
            Console.WriteLine($"Succeeded:    {run.Succeeded}");
            Console.WriteLine($"Started:      {run.StartedAt:O}");
            Console.WriteLine($"Completed:    {run.CompletedAt:O}");
            Console.WriteLine($"Duration:     {(run.CompletedAt - run.StartedAt).TotalSeconds:F2}s");
            Console.WriteLine($"Transitions:  {run.TransitionCount}");
            Console.WriteLine($"Logs:         {run.LogFolder}");

            if (run.PublishedEntries.Count > 0)
            {
                Console.WriteLine("Published:");
                foreach (var entry in run.PublishedEntries)
                {
                    Console.WriteLine($"- {entry.Title} ({string.Join(", ", entry.To)}):");
                    Console.WriteLine(entry.Content);
                }
            }

            Console.WriteLine("Actions:");

            foreach (var action in run.ActionResults)
            {
                Console.WriteLine($"- {action.ActionId}: {action.Status} ({action.Summary})");
            }
        }

        return 0;
    }

    private static ServiceProvider BuildServiceProvider(LogLevel minimumLevel)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(minimumLevel);
        });
        services.AddFlowCore();
        services.AddScriptAction();
        services.AddGitHubCopilotAction();
        return services.BuildServiceProvider();
    }

    private static LogLevel GetLogLevel(ParsedCommand command)
        => command.Json ? LogLevel.None : command.Verbose ? LogLevel.Debug : LogLevel.Information;

    private static ParsedCommand ParseCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return CreateHelpCommand();
        }

        var first = args[0];
        if (IsHelp(first))
        {
            return CreateHelpCommand();
        }

        if (IsVersion(first))
        {
            return CreateVersionCommand();
        }

        if (IsKnownCommand(first))
        {
            return ParseCommandArguments(first, args[1..]);
        }

        return ParseCommandArguments("run", args);
    }

    private static ParsedCommand ParseCommandArguments(string commandName, string[] args)
    {
        var positionals = new List<string>();
        string? workflowFile = null;
        string? targetWorkingDirectory = null;
        string? runId = null;
        var dryRun = false;
        var verbose = false;
        var json = false;
        var force = false;
        var template = "basic-script";
        var inputOverrides = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var variableOverrides = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var environmentOverrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(arg);
                continue;
            }

            switch (arg)
            {
                case "--workflow":
                    workflowFile = ReadRequiredValue(args, ref i, arg);
                    break;
                case var value when value.StartsWith("--workflow=", StringComparison.Ordinal):
                    workflowFile = value["--workflow=".Length..];
                    break;
                case "--workdir":
                    targetWorkingDirectory = ReadRequiredValue(args, ref i, arg);
                    break;
                case var value when value.StartsWith("--workdir=", StringComparison.Ordinal):
                    targetWorkingDirectory = value["--workdir=".Length..];
                    break;
                case "--input":
                    AddOverride(inputOverrides, ReadRequiredValue(args, ref i, arg));
                    break;
                case var value when value.StartsWith("--input=", StringComparison.Ordinal):
                    AddOverride(inputOverrides, value["--input=".Length..]);
                    break;
                case "--var":
                    AddOverride(variableOverrides, ReadRequiredValue(args, ref i, arg));
                    break;
                case var value when value.StartsWith("--var=", StringComparison.Ordinal):
                    AddOverride(variableOverrides, value["--var=".Length..]);
                    break;
                case "--env":
                    AddOverride(environmentOverrides, ReadRequiredValue(args, ref i, arg));
                    break;
                case var value when value.StartsWith("--env=", StringComparison.Ordinal):
                    AddOverride(environmentOverrides, value["--env=".Length..]);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--latest":
                    break;
                case "--run-id":
                    runId = ReadRequiredValue(args, ref i, arg);
                    break;
                case var value when value.StartsWith("--run-id=", StringComparison.Ordinal):
                    runId = value["--run-id=".Length..];
                    break;
                case "--template":
                    template = ReadRequiredValue(args, ref i, arg);
                    break;
                case var value when value.StartsWith("--template=", StringComparison.Ordinal):
                    template = value["--template=".Length..];
                    break;
                default:
                    throw new CliUsageException($"Unknown option '{arg}'.{Environment.NewLine}{GetHelpText()}");
            }
        }

        var projectFolder = positionals.Count > 0 ? positionals[0] : null;
        if (commandName == "run")
        {
            if (string.IsNullOrWhiteSpace(targetWorkingDirectory) && positionals.Count > 1)
            {
                targetWorkingDirectory = positionals[1];
            }

            if (string.IsNullOrWhiteSpace(workflowFile) && positionals.Count > 2)
            {
                workflowFile = positionals[2];
            }
        }
        else if (string.IsNullOrWhiteSpace(workflowFile) && (commandName is "validate") && positionals.Count > 1)
        {
            workflowFile = positionals[1];
        }

        return new ParsedCommand(commandName, projectFolder, targetWorkingDirectory, workflowFile, dryRun, verbose, json, force, template, runId, inputOverrides, variableOverrides, environmentOverrides);
    }

    private static string ReadRequiredValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new CliUsageException($"Option '{optionName}' requires a value.");
        }

        index++;
        return args[index];
    }

    private static void AddOverride<TValue>(IDictionary<string, TValue> destination, string assignment)
    {
        var separatorIndex = assignment.IndexOf('=');
        if (separatorIndex <= 0)
        {
            throw new CliUsageException($"Override values must use key=value syntax. Received '{assignment}'.");
        }

        var key = assignment[..separatorIndex].Trim();
        var value = assignment[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new CliUsageException($"Override values must include a non-empty key. Received '{assignment}'.");
        }

        destination[key] = ConvertOverrideValue<TValue>(value);
    }

    private static TValue ConvertOverrideValue<TValue>(string value)
    {
        object? converted = typeof(TValue) == typeof(object)
            ? ConvertStringToValue(value)
            : value;
        return (TValue)converted!;
    }

    private static object? ConvertStringToValue(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (int.TryParse(value, out var intValue))
        {
            return intValue;
        }

        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }

    private static bool IsKnownCommand(string value)
        => value is "run" or "validate" or "init" or "logs" or "version";

    private static bool IsHelp(string value)
        => value is "help" or "--help" or "-h" or "/?";

    private static bool IsVersion(string value)
        => value is "version" or "--version";

    private static ParsedCommand CreateHelpCommand()
        => new(
            "help",
            null,
            null,
            null,
            false,
            false,
            false,
            false,
            "basic-script",
            null,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

    private static ParsedCommand CreateVersionCommand()
        => new(
            "version",
            null,
            null,
            null,
            false,
            false,
            false,
            false,
            "basic-script",
            null,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

    private static string ResolveProjectFolder(string? projectFolder)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(projectFolder) ? Environment.CurrentDirectory : projectFolder);

    private static string ResolveTargetWorkingDirectory(string? targetWorkingDirectory)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(targetWorkingDirectory) ? Environment.CurrentDirectory : targetWorkingDirectory);

    private static string ResolveWorkflowFile(string projectFolder, string? workflowFile)
    {
        var file = string.IsNullOrWhiteSpace(workflowFile) ? FlowYamlFileName : workflowFile;
        return Path.IsPathRooted(file)
            ? file
            : Path.GetFullPath(Path.Combine(projectFolder, file));
    }

    private static void EnsureProjectExists(string projectFolder, string label = "Project folder")
    {
        if (!Directory.Exists(projectFolder))
        {
            throw new CliUsageException($"{label} does not exist: {projectFolder}");
        }
    }

    private static void EnsureWorkflowExists(string workflowFile)
    {
        if (!File.Exists(workflowFile))
        {
            throw new CliUsageException($"Workflow file does not exist: {workflowFile}");
        }
    }

    private static void ApplyInputOverrides(WorkflowDefinition workflow, IReadOnlyDictionary<string, object?> inputOverrides)
    {
        foreach (var pair in inputOverrides)
        {
            workflow.Inputs[pair.Key] = pair.Value;
        }
    }

    private static void ApplyVariableOverrides(WorkflowDefinition workflow, IReadOnlyDictionary<string, object?> variableOverrides)
    {
        foreach (var pair in variableOverrides)
        {
            workflow.Variables[pair.Key] = pair.Value;
        }
    }

    private static void ApplyEnvironmentOverrides(WorkflowDefinition workflow, IReadOnlyDictionary<string, string?> environmentOverrides)
    {
        foreach (var pair in environmentOverrides)
        {
            workflow.Env[pair.Key] = pair.Value;
        }
    }

    private static IReadOnlyList<ScaffoldFile> GetTemplateFiles(string templateName)
        => templateName switch
        {
            "basic-script" =>
            [
                new ScaffoldFile(
                    FlowYamlFileName,
                    "name: Basic Script Workflow\nversion: 1\nvariables:\n  greeting: Hello from Powergentic\nexecution:\n  maxTransitions: 5\n  maxVisitsPerAction: 2\nactions:\n  - id: hello\n    uses: script\n    with:\n      shell: bash\n      path: scripts/hello.sh\n      environment:\n        GREETING: ${ variables.greeting }\n    outputs:\n      message: ${ actions.hello.outputs.message }\n"),
                new ScaffoldFile(
                    Path.Combine("scripts", "hello.sh"),
                    "#!/usr/bin/env bash\nset -euo pipefail\n\necho \"$GREETING\"\necho \"message=$GREETING\" >> \"$ORCHESTRATOR_OUTPUT\"\n",
                    MakeExecutable: true),
            ],
            "script-and-copilot-loop" =>
            [
                new ScaffoldFile(
                    FlowYamlFileName,
                    "name: Script And Copilot Loop\nversion: 1\nvariables:\n  needsReview: true\n  targetPath: ${ runtime.targetWorkingDirectory }\nexecution:\n  startAt: prepare\n  maxTransitions: 10\n  maxVisitsPerAction: 4\nactions:\n  - id: prepare\n    uses: script\n    with:\n      shell: bash\n      path: scripts/prepare.sh\n\n  - id: review\n    if: ${{ success() && variables.needsReview == true }}\n    uses: githubCopilot\n    with:\n      promptFile: prompts/review.prompt.md\n      inputs:\n        statusFile: ${ actions.prepare.outputs.statusFile }\n        targetPath: ${ runtime.targetWorkingDirectory }\n      writeResponseTo: output/review.txt\n    outputs:\n      responseFile: ${ actions.review.outputs.responseFile }\n    next:\n      - when: ${{ actions.review.outputs.responseFile == null }}\n        goto: prepare\n\n  - id: done\n    if: ${{ always() }}\n    uses: script\n    with:\n      shell: bash\n      run: echo \"done=true\" >> \"$ORCHESTRATOR_OUTPUT\"\n"),
                new ScaffoldFile(
                    Path.Combine("scripts", "prepare.sh"),
                    "#!/usr/bin/env bash\nset -euo pipefail\nmkdir -p output\nprintf 'ready\\n' > output/status.txt\necho \"statusFile=output/status.txt\" >> \"$ORCHESTRATOR_OUTPUT\"\n",
                    MakeExecutable: true),
                new ScaffoldFile(
                    Path.Combine("prompts", "review.prompt.md"),
                    "Review the workflow status file at ${statusFile} for the project in ${targetPath} and summarize what should happen next.\n"),
            ],
            _ => throw new CliUsageException("Unknown template. Supported templates: basic-script, script-and-copilot-loop.")
        };

    private static void WriteJson<T>(T value)
        => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void EnsureGitHubCopilotInstalledIfRequired(WorkflowDefinition workflow, ICopilotInstallationProbe installationProbe)
    {
        if (!workflow.Actions.Any(action => string.Equals(action.Uses, "githubCopilot", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (installationProbe.IsInstalled())
        {
            return;
        }

        throw new CliUsageException("GitHub Copilot action requires the 'copilot' CLI, but it is not installed or not available on PATH.");
    }

    private static int WriteError(bool json, string message)
    {
        if (json)
        {
            WriteJson(new { succeeded = false, error = message });
        }
        else
        {
            Console.Error.WriteLine(message);
        }

        return 2;
    }

    private static int WriteUnhandledError(Exception ex, bool json)
    {
        if (json)
        {
            WriteJson(new { succeeded = false, error = ex.Message });
        }
        else
        {
            Console.Error.WriteLine(ex.Message);
        }

        return 10;
    }

    private static int WriteHelpAndReturn(int exitCode)
    {
        Console.WriteLine(GetHelpText());
        return exitCode;
    }

    private static int WriteVersionAndReturn(int exitCode)
    {
        Console.WriteLine(GetVersionText());
        return exitCode;
    }

    private static string GetVersionText()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "pgflow v" + informationalVersion.Split('+', 2)[0];
        }

        return "pgflow v" + (assembly.GetName().Version?.ToString() ?? "1.0.0");
    }

    private static string GetHelpText()
        => """

 ╭──────────────────────────────────────────────────────────────────────────────╮
 │                                                                              │
 │   ██████╗   ██████╗ ███████╗██╗      ██████╗ ██╗    ██╗                      │
 │   ██╔══██╗ ██╔════╝ ██╔════╝██║     ██╔═══██╗██║    ██║                      │
 │   ██████╔╝ ██║  ███╗█████╗  ██║     ██║   ██║██║ █╗ ██║                      │
 │   ██╔═══╝  ██║   ██║██╔══╝  ██║     ██║   ██║██║███╗██║                      │
 │   ██║      ╚██████╔╝██║     ███████╗╚██████╔╝╚███╔███╔╝                      │
 │   ╚═╝       ╚═════╝ ╚═╝     ╚══════╝ ╚═════╝  ╚══╝╚══╝                       │
 │                                                                              │
 │   Powergentic Flow                                                           │
 │   Run, validate, scaffold, and inspect workflow executions.                  │
 │                                                                              │
 │   https://powergentic.ai                                                     │
 │                                                                              │
 ╰──────────────────────────────────────────────────────────────────────────────╯

Usage:
  pgflow <command> [project-folder] [options]
  pgflow run <project-folder> [target-working-directory] [options]
  pgflow [project-folder] [target-working-directory] [workflow-file]

Commands:
  run                           Validate and execute a workflow.
  validate                      Load and validate a workflow.
  init                          Scaffold a workflow project.
  logs                          Show the latest run summary or a selected run.
  help                          Show help information.
  version                       Show the current pgflow version.

Options:
  -h, --help                    Show help information.
  --workflow <file>             Override the default workflow file name.
  --workdir <path>              Override the target working directory for run.
  --input key=value             Override a workflow input.
  --var key=value               Override a workflow variable.
  --env key=value               Inject or override an environment value.
  --dry-run                     Validate without executing actions.
  --template <name>             Template for init: basic-script or script-and-copilot-loop.
  --run-id <id>                 Specific run id for the logs command.
  --latest                      Show the latest run summary (default for logs).
  --force                       Allow init in a non-empty target folder.
  --verbose                     Enable verbose console logging.
  --json                        Emit JSON output.

Examples:
  pgflow run samples/basic-script
  pgflow run samples/script-and-copilot-loop ../my-project
  pgflow run samples/script-and-copilot-loop --workdir ../my-project
  pgflow validate .
  pgflow logs samples/basic-script --latest
  pgflow version

""";

    private sealed record ParsedCommand(
        string Name,
        string? ProjectFolder,
        string? TargetWorkingDirectory,
        string? WorkflowFile,
        bool DryRun,
        bool Verbose,
        bool Json,
        bool Force,
        string Template,
        string? RunId,
        IReadOnlyDictionary<string, object?> InputOverrides,
        IReadOnlyDictionary<string, object?> VariableOverrides,
        IReadOnlyDictionary<string, string?> EnvironmentOverrides);

    private sealed record ScaffoldFile(string RelativePath, string Content, bool MakeExecutable = false);

    private sealed class CliUsageException(string message) : Exception(message);
}