using System.Text;
using Powergentic.AI.Flow.Cli;

namespace Powergentic.AI.Flow.Core.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_WithoutArgumentsDisplaysHelp()
    {
        var (exitCode, stdout, stderr) = await InvokeAsync([]);

        Assert.Equal(0, exitCode);
        Assert.Contains("pgflow", stdout, StringComparison.Ordinal);
        Assert.Contains("Usage:", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public async Task RunAsync_HelpDisplaysAsciiArtTitle()
    {
        var (exitCode, stdout, stderr) = await InvokeAsync(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("██████╗", stdout, StringComparison.Ordinal);
        Assert.Contains("Examples:", stdout, StringComparison.Ordinal);
        Assert.Contains("--workdir <path>", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public async Task RunAsync_VersionCommandDisplaysVersion()
    {
        var (exitCode, stdout, stderr) = await InvokeAsync(["version"]);

        Assert.Equal(0, exitCode);
        Assert.Matches(@"pgflow v\d+\.\d+\.\d+(?:\.\d+)?\r?\n$", stdout);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public async Task RunAsync_ValidateCommandReturnsSuccessForValidWorkflow()
    {
        var projectFolder = CreateProjectFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Validation Demo
version: 1
actions:
  - id: hello
    uses: script
    with:
      shell: bash
      run: echo hello
""");

            var (exitCode, stdout, stderr) = await InvokeAsync(["validate", projectFolder]);

            Assert.Equal(0, exitCode);
            Assert.Contains("Validation Demo", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ValidateCommandReturnsFailureForInvalidWorkflow()
    {
        var projectFolder = CreateProjectFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Invalid Demo
version: 1
actions:
  - id: broken
    uses: script
    with:
      shell: zsh
""");

            var (exitCode, stdout, stderr) = await InvokeAsync(["validate", projectFolder]);

            Assert.Equal(2, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("unsupported shell", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_InitCommandCreatesBasicTemplate()
    {
        var projectFolder = CreateProjectFolder();
        Directory.Delete(projectFolder, recursive: true);

        try
        {
            var (exitCode, _, stderr) = await InvokeAsync(["init", projectFolder, "--template", "basic-script"]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.True(File.Exists(Path.Combine(projectFolder, "flow.yml")));
            Assert.True(File.Exists(Path.Combine(projectFolder, "scripts", "hello.sh")));
        }
        finally
        {
            if (Directory.Exists(projectFolder))
            {
                Directory.Delete(projectFolder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_LogsCommandReturnsJsonForLatestRun()
    {
        var projectFolder = CreateProjectFolder();
        var runFolder = Path.Combine(projectFolder, "logs", "2026-06-02T23-52-57Z_test");
        Directory.CreateDirectory(runFolder);
        await File.WriteAllTextAsync(Path.Combine(runFolder, "run.json"), """
{
  "runId": "2026-06-02T23-52-57Z_test",
  "workflowName": "Basic Script Workflow",
  "projectFolder": "PROJECT_FOLDER",
  "targetWorkingDirectory": "TARGET_FOLDER",
  "logFolder": "LOG_FOLDER",
  "succeeded": true,
  "startedAt": "2026-06-02T23:52:57+00:00",
  "completedAt": "2026-06-02T23:52:58+00:00",
  "transitionCount": 1,
  "actionResults": [
    {
      "actionId": "hello",
      "status": 0,
      "exitCode": 0,
      "summary": "Script completed.",
      "outputs": {},
      "metadata": {},
      "stdOutFile": null,
      "stdErrFile": null,
      "startedAt": "2026-06-02T23:52:57+00:00",
      "completedAt": "2026-06-02T23:52:58+00:00",
      "succeeded": true
    }
  ]
}
"""
            .Replace("PROJECT_FOLDER", projectFolder.Replace("\\", "\\\\"), StringComparison.Ordinal)
            .Replace("TARGET_FOLDER", projectFolder.Replace("\\", "\\\\"), StringComparison.Ordinal)
            .Replace("LOG_FOLDER", runFolder.Replace("\\", "\\\\"), StringComparison.Ordinal));

        try
        {
            var (exitCode, stdout, stderr) = await InvokeAsync(["logs", projectFolder, "--json"]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("\"runId\": \"2026-06-02T23-52-57Z_test\"", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ValidateCommandAppliesInputVarAndEnvOverrides()
    {
        var projectFolder = CreateProjectFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectFolder, "flow.yml"), """
name: Override Demo
version: 1
inputs:
  mode: default
variables:
  enabled: false
env:
  MODE: ${ inputs.mode }
actions:
  - id: hello
    uses: script
    if: ${{ variables.enabled == true }}
    with:
      shell: bash
      run: echo ${ env.MODE }
""");

            var (exitCode, stdout, stderr) = await InvokeAsync([
                "validate",
                projectFolder,
                "--input", "mode=cli",
                "--var", "enabled=true",
                "--env", "MODE=enabled"]);

            Assert.Equal(0, exitCode);
            Assert.Contains("Override Demo", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> InvokeAsync(string[] args)
    {
        await ConsoleTestSynchronization.Gate.WaitAsync();
        try
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdout = new StringWriter(new StringBuilder());
            using var stderr = new StringWriter(new StringBuilder());
            Console.SetOut(stdout);
            Console.SetError(stderr);

            try
            {
                var exitCode = await CliApplication.RunAsync(args);
                return (exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
        finally
        {
            ConsoleTestSynchronization.Gate.Release();
        }
    }

    private static string CreateProjectFolder()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), $"orchestrator-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectFolder);
        return projectFolder;
    }
}