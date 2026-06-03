# Powergentic AI Workflow Orchestrator CLI

`pgflow` is a local workflow orchestrator for running scripted steps and GitHub Copilot-driven steps from a project folder.

It is designed to help you automate repeatable development workflows such as preparing files, running scripts, looping over actions, and capturing run logs.

![pgflow screenshot](docs/images/pgflow-screenshot.png)

## What pgflow does

- Loads a workflow from `orchestrator.yml`
- Resolves variables, environment values, action inputs, and action outputs
- Runs `script` actions with `bash` or `pwsh`
- Runs `githubCopilot` actions through `GitHub.Copilot.SDK`
- Supports conditional execution with `if`, `next`, and `goto`
- Writes per-run logs under the target project folder

## Requirements

To build and use `pgflow`, you need:

- .NET SDK 10.0 or newer
- `bash` for shell-based workflows on macOS/Linux, or `pwsh` for PowerShell-based workflows
- GitHub Copilot access for workflows that use `githubCopilot`
- optionally, `GITHUB_TOKEN` if token-based auth is preferred

## Quick start

From the repository root:

1. Build the solution
2. Run the tests
3. Run a sample workflow

```bash
dotnet build Powergentic.AI.Orchestrator.slnx
dotnet test Powergentic.AI.Orchestrator.slnx
dotnet run --project src/Powergentic.AI.Orchestrator.Cli -- run samples/basic-script
```

If you already built the CLI, you can also run the produced executable directly:

```bash
./src/Powergentic.AI.Orchestrator.Cli/bin/Debug/net10.0/pgflow run samples/basic-script
```

## Building pgflow

Build from the repository root:

```bash
dotnet build Powergentic.AI.Orchestrator.slnx
```

Run the test suite:

```bash
dotnet test Powergentic.AI.Orchestrator.slnx
```

Run the CLI during development:

```bash
dotnet run --project src/Powergentic.AI.Orchestrator.Cli -- --help
```

## Using pgflow

The CLI is named `pgflow`.

General usage:

```text
pgflow <command> [project-folder] [options]
pgflow [project-folder] [workflow-file]
```

Defaults:

- `project-folder` defaults to the current directory
- `workflow-file` defaults to `orchestrator.yml`

### Commands

- `pgflow run [project-folder]` - validate and execute a workflow
- `pgflow validate [project-folder]` - load and validate a workflow
- `pgflow init [project-folder]` - scaffold a workflow project
- `pgflow logs [project-folder]` - show the latest run summary or a selected run
- `pgflow help` - show help information

### Common options

- `-h`, `--help` - show help information
- `--version` - show the current pgflow version
- `--workflow <file>` - override the default workflow file name
- `--var key=value` - override a workflow variable
- `--env key=value` - inject or override an environment value
- `--dry-run` - validate without executing actions
- `--template <name>` - template for `init`
- `--run-id <id>` - select a specific run for `logs`
- `--latest` - show the latest run summary
- `--force` - allow `init` in a non-empty folder
- `--verbose` - enable verbose console logging
- `--json` - emit JSON output

### Common examples

Validate a workflow:

```bash
pgflow validate samples/basic-script
```

Run a workflow:

```bash
pgflow run samples/basic-script
```

Run with variable and environment overrides:

```bash
pgflow run samples/basic-script --var greeting=Hello --env NAME=Chris
```

Scaffold a new project:

```bash
pgflow init my-flow
```

Show logs for the latest run:

```bash
pgflow logs samples/basic-script --latest
```

Show the installed pgflow version:

```bash
pgflow --version
```

## Getting started with a workflow

A `pgflow` project usually starts with an `orchestrator.yml` file in the project folder.

Example:

```yaml
name: Basic Demo
version: 1
variables:
  greeting: Hello
actions:
  - id: hello
    uses: script
    with:
      shell: bash
      run: echo "${ variables.greeting } from pgflow"
```

Save that as `orchestrator.yml`, then run:

```bash
pgflow run .
```

## Workflow structure

A workflow can contain these top-level fields:

- `name`
- `description`
- `version`
- `variables`
- `env`
- `execution`
- `actions`

Common execution guard fields:

- `execution.startAt`
- `execution.maxTransitions`
- `execution.maxVisitsPerAction`

Common action fields:

- `id`
- `name`
- `uses`
- `if`
- `with`
- `outputs`
- `next`

## Script actions

Example:

```yaml
- id: hello
  uses: script
  with:
    shell: bash
    path: scripts/hello.sh
    environment:
      GREETING: Hello
```

Supported `with` fields:

- `shell`: `bash` or `pwsh`
- `run`: inline script content
- `file` or `path`: script file path
- `workingDirectory`: optional working directory
- `environment`: extra environment variables
- `failOnNonZeroExit`: defaults to `true`

Runtime environment variables exposed to scripts:

- `ORCHESTRATOR_OUTPUT`
- `ORCHESTRATOR_PROJECT_FOLDER`
- `ORCHESTRATOR_RUN_ID`

Scripts can emit outputs by appending `key=value` lines to `$ORCHESTRATOR_OUTPUT`.

## GitHub Copilot actions

Example:

```yaml
- id: review
  uses: githubCopilot
  with:
    promptFile: prompts/review.prompt.md
    inputs:
      statusFile: ${ actions.prepare.outputs.statusFile }
    writeResponseTo: output/review.txt
```

Supported `with` fields:

- `prompt` or `promptFile`
- `inputs`
- `writeResponseTo`
- `workingDirectory`
- `model`
- `systemPrompt`
- `streaming`
- `gitHubToken`
- `requestHeaders`

Prompt placeholders support both `{{name}}` and `${name}` forms.

## Logs and run output

Each run writes under `<project-folder>/logs/<run-id>/`.

Typical files include:

- `workflow-resolved.json`
- `run.json`
- `actions/*.json`
- script stdout/stderr files when applicable

Use the CLI to inspect the latest run:

```bash
pgflow logs <project-folder> --latest
```

## Samples in this repository

- `samples/basic-script` - minimal script-only workflow
- `samples/script-and-copilot-loop` - script + Copilot loop example

Try them with:

```bash
pgflow run samples/basic-script
pgflow run samples/script-and-copilot-loop
```

## Solution layout

- `src/Powergentic.AI.Orchestrator.Cli` - CLI entrypoint
- `src/Powergentic.AI.Orchestrator.Core` - workflow models, validation, execution, logging
- `src/Powergentic.AI.Orchestrator.Actions.Script` - local script runner
- `src/Powergentic.AI.Orchestrator.Actions.GitHubCopilot` - Copilot action runner and SDK adapter
- `tests/Powergentic.AI.Orchestrator.Core.Tests` - unit tests

## Next steps for new users

A good path to get started is:

1. build the solution
2. run the tests
3. run `samples/basic-script`
4. inspect the generated logs
5. scaffold a new flow with `pgflow init`
6. adapt the sample workflow for your own project
