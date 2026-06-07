# Quick Start

This guide helps you create your first `pgflow` project and then adapt the same pattern into an AI harness for your next software project.

For the full technical reference, see [SCHEMA.md](SCHEMA.md).

## What pgflow is

`pgflow` separates two concerns:

- **pgflow project folder**: the reusable harness that contains `flow.yml`, prompt templates, helper scripts, and logs
- **target working directory**: the actual repository or folder the workflow should inspect or modify

This makes it easy to reuse one workflow across many projects.

## Prerequisites

You need:

- a downloaded `pgflow` release build available on your machine
- `bash` on macOS/Linux, or `pwsh` for PowerShell-based flows
- GitHub Copilot access for workflows that use `githubCopilot`
- optionally `GITHUB_TOKEN` if you want token-based auth

The examples below assume the `pgflow` executable is already installed and available on your `PATH`.

## 1. Create your first pgflow project

Create a new folder anywhere you want:

```text
my-first-flow/
  flow.yml
  scripts/
  prompts/
```

Recommended conventions:

- keep executable helper scripts in `scripts/`
- keep Copilot prompt templates in `prompts/`
- keep generated logs under `logs/` and let `pgflow` create that folder automatically

## 2. Add a simple `flow.yml`

Create `my-first-flow/flow.yml`:

```yaml
name: My First Flow
version: 1
variables:
  greeting: Hello from pgflow
actions:
  - id: hello
    name: Print a greeting
    uses: script
    with:
      shell: bash
      path: scripts/hello.sh
      environment:
        GREETING: ${ variables.greeting }
    outputs:
      message: ${ actions.hello.outputs.message }
```

Create `my-first-flow/scripts/hello.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

echo "$GREETING"
echo "message=$GREETING" >> "$ORCHESTRATOR_OUTPUT"
```

Make it executable:

```bash
chmod +x my-first-flow/scripts/hello.sh
```

## 3. Validate and run it

From anywhere you want to run the flow:

```bash
pgflow validate my-first-flow
pgflow run my-first-flow --display-enhanced
```

What happens:

- `pgflow` loads `my-first-flow/flow.yml`
- it resolves `scripts/hello.sh` from the pgflow project folder
- it runs the script in your current shell directory unless you pass `--workdir`
- it writes run artifacts under `my-first-flow/logs/<run-id>/`

Useful flags while learning:

- `--display-enhanced` for the live dashboard
- `--dry-run` to validate without executing
- `--verbose` for more console diagnostics
- `--workdir <path>` to point the flow at another project

## 4. Inspect the run logs

After a run, inspect the latest log bundle:

```bash
pgflow logs my-first-flow --latest
```

A run folder typically contains:

- `run.json`
- `console.log`
- per-action stdout and stderr logs
- published run summary content

## 5. Turn the same structure into an AI harness

Once the basic script flow works, add a `githubCopilot` action so the flow can inspect a real project and produce guidance, plans, or implementation steps.

A good beginner pattern is:

1. run a script action to inspect the target repo
2. save summary outputs
3. feed those outputs into a `githubCopilot` prompt
4. publish the response to the console and save it in the run log folder

## 6. Example AI harness flow

Create `my-first-flow/flow.yml` like this:

```yaml
name: Project Planning Harness
version: 1
variables:
  goal: Create a practical implementation plan for this project
actions:
  - id: inspect
    name: Inspect target workspace
    uses: script
    with:
      shell: bash
      path: scripts/inspect.sh

  - id: plan
    name: Ask Copilot for a project plan
    uses: githubCopilot
    with:
      promptFile: prompts/plan.prompt.md
      inputs:
        targetPath: ${ runtime.targetWorkingDirectory }
        goal: ${ variables.goal }
        workspaceSummary: ${ actions.inspect.outputs.workspaceSummary }
      writeResponseTo: ${ runtime.logFolder }/project-plan.md
    publish:
      - title: Project plan
        from: ${ actions.plan.outputs.response }
        to:
          - console
          - runSummary
```

Create `my-first-flow/scripts/inspect.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

summary="Target directory: $ORCHESTRATOR_TARGET_WORKING_DIRECTORY"
summary+=$'\n'
summary+="Top-level entries:"
summary+=$'\n'
summary+="$(ls -1 "$ORCHESTRATOR_TARGET_WORKING_DIRECTORY" | head -20)"

echo "$summary"
{
  echo "workspaceSummary<<EOF"
  echo "$summary"
  echo "EOF"
} >> "$ORCHESTRATOR_OUTPUT"
```

Create `my-first-flow/prompts/plan.prompt.md`:

```md
You are helping me plan work for a software project.

Goal:
{{goal}}

Target path:
{{targetPath}}

Workspace summary:
{{workspaceSummary}}

Please produce:
1. A short assessment of the current project state
2. The next 5 implementation steps
3. Risks or missing information
4. A recommended first coding task
```

## 7. Run the harness against a real project

Point the harness at another repository or working folder:

```bash
pgflow run my-first-flow ../my-project --display-enhanced
```

In this run:

- `my-first-flow` is the reusable harness
- `../my-project` is the target working directory
- `scripts/inspect.sh` is resolved from `my-first-flow/scripts/`
- `prompts/plan.prompt.md` is resolved from `my-first-flow/prompts/`
- `project-plan.md` is written into that run's log folder

## 8. How to use pgflow as your next-project harness

A practical setup is to keep one harness per workflow style, for example:

```text
automation/
  plan-feature/
  implement-feature/
  review-release/
```

Each harness can share the same pattern:

- `flow.yml` defines the orchestration
- `scripts/` gathers facts or performs deterministic work
- `prompts/` contains reusable AI instructions
- `logs/` captures every run

Typical uses:

- inspect a repository and generate a plan
- implement a feature in a target project
- run build and test loops with AI in the middle
- analyze failures and propose fixes
- prepare release notes or status summaries

## 9. Authoring tips

Start simple:

- begin with one `script` action
- add one `githubCopilot` action next
- only add `next` transitions when you need branching or loops
- keep prompt templates in files, not inline strings, once they get longer
- use `writeResponseTo` to save important AI outputs into the run folder

Useful mental model:

- use **scripts** for deterministic actions
- use **Copilot** for reasoning, planning, summarization, and code generation
- use **outputs** to pass data from one action to the next

## 10. Next steps

After this guide, the best follow-up references are:

- [README.md](README.md) for CLI usage and examples
- [SCHEMA.md](SCHEMA.md) for the full supported `flow.yml` schema
- `samples/basic-script/` for the smallest working example
- `samples/script-and-copilot-loop/` for a larger AI-assisted loop
