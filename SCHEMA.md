# `flow.yml` Schema

This document describes the supported `pgflow` workflow document format as implemented by the current codebase.

It is intentionally technical and implementation-oriented. It documents:

- the YAML shape supported by `flow.yml`
- the action types currently recognized by the runtime
- expression and condition syntax
- path resolution rules
- how `prompts/` and `scripts/` folders are used inside a pgflow project folder
- validation and runtime behavior that affect authoring

## 1. Document model

A workflow file is a YAML document deserialized into `WorkflowDefinition` in `src/Powergentic.Flow.Core/Models/WorkflowDefinition.cs`.

Supported top-level fields:

```yaml
name: string
description: string?
version: int
inputs: map<string, any>
variables: map<string, any>
env: map<string, string?>
execution:
  maxTransitions: int
  maxVisitsPerAction: int
actions:
  - action-definition
```

### Top-level fields

| Field | Type | Required | Default | Notes |
| --- | --- | ---: | --- | --- |
| `name` | `string` | No | `""` | Human-readable workflow name. |
| `description` | `string` | No | `null` | Optional description. |
| `version` | `int` | No | `1` | Informational schema/version marker for the workflow file. |
| `inputs` | `map<string, any>` | No | empty map | Caller-supplied values, typically overridden with `--input`. |
| `variables` | `map<string, any>` | No | empty map | Workflow-owned state/default values, typically overridden with `--var`. |
| `env` | `map<string, string?>` | No | empty map | Environment values added to runtime environment after expression interpolation. |
| `execution` | object | No | `{ maxTransitions: 100, maxVisitsPerAction: 50 }` | Runtime loop guards. |
| `actions` | array | Yes | empty list | Must contain at least one action. |

## 2. YAML parsing behavior

`flow.yml` is loaded by `WorkflowLoader` in `src/Powergentic.Flow.Core/Services/WorkflowLoader.cs`.

Important parser behavior:

- YAML keys are interpreted using camelCase naming.
- Unmatched/unknown YAML properties are ignored by the loader.
- `execution.startAt` is explicitly rejected and causes a load error.
- Execution always starts at the first action in `actions`.

### Unsupported legacy property

The following property is no longer supported:

```yaml
execution:
  startAt: someAction
```

This now throws:

- `execution.startAt is no longer supported. Workflows now always start at the first action defined.`

## 3. `execution` section

The `execution` section maps to `WorkflowExecutionOptions` in `src/Powergentic.Flow.Core/Models/WorkflowExecutionOptions.cs`.

```yaml
execution:
  maxTransitions: 100
  maxVisitsPerAction: 50
```

| Field | Type | Required | Default | Validation |
| --- | --- | ---: | --- | --- |
| `maxTransitions` | `int` | No | `100` | Must be `> 0`. |
| `maxVisitsPerAction` | `int` | No | `50` | Must be `> 0`. |

### Runtime meaning

- `maxTransitions` limits the total number of action transitions performed in a run.
- `maxVisitsPerAction` limits how many times a single action id may be revisited.

These limits are enforced at runtime in `WorkflowExecutor`.

## 4. `actions` section

Each entry in `actions` maps to `WorkflowActionDefinition` in `src/Powergentic.Flow.Core/Models/WorkflowActionDefinition.cs`.

```yaml
actions:
  - id: string
    name: string?
    uses: string
    if: string?
    with: map<string, any>
    outputs: map<string, string?>
    publish:
      - publish-definition
    next:
      - transition-definition
```

| Field | Type | Required | Default | Notes |
| --- | --- | ---: | --- | --- |
| `id` | `string` | Yes | `""` | Must be unique and non-empty. |
| `name` | `string` | No | `null` | Optional display name. |
| `uses` | `string` | Yes | `""` | Action type. Currently `script` and `githubCopilot`. |
| `if` | `string` | No | `null` | Boolean condition expression. If false, action is skipped. |
| `with` | `map<string, any>` | No | empty map | Action-specific inputs. |
| `outputs` | `map<string, string?>` | No | empty map | Synthetic outputs computed from expressions after action execution. |
| `publish` | array | No | `null` | Optional publish rules. |
| `next` | array | No | empty list | Conditional or unconditional transition rules. |

## 5. Supported action types

The runtime currently registers two action types:

- `script`
- `githubCopilot`

If the validator is constructed with registered action runners, any other `uses` value is rejected.

---

## 6. `script` action schema

Script actions are implemented by `ScriptActionRunner` in `src/Powergentic.Flow.Actions.Script/ScriptActionRunner.cs`.

### Script action shape

```yaml
- id: build
  uses: script
  with:
    shell: bash | pwsh
    run: string
    file: string
    path: string
    workingDirectory: string
    environment:
      KEY: value
    failOnNonZeroExit: true | false
```

### Supported script `with` fields

| Field | Type | Required | Notes |
| --- | --- | ---: | --- |
| `shell` | `string` | Yes | Must be `bash` or `pwsh`. |
| `run` | `string` | Conditionally | Inline script content. One of `run`, `file`, or `path` must be supplied. |
| `file` | `string` | Conditionally | Script file path. Relative values resolve from the pgflow project folder. Absolute paths are allowed. |
| `path` | `string` | Conditionally | Script file path resolved from the pgflow project folder. Intended project-relative script reference. |
| `workingDirectory` | `string` | No | Defaults to the target working directory. Relative values resolve from the target working directory. |
| `environment` | `map<string, any>` | No | Extra environment variables for the process. Values are coerced to strings after interpolation. |
| `failOnNonZeroExit` | `bool` or `string` | No | Defaults to `true`. If false, non-zero exit codes still produce `Succeeded` action status. |

### Resolution rules

`with.path`

- always resolves relative to the pgflow project folder
- must point to an existing file in that folder tree

`with.file`

- if absolute, used as-is
- if relative, resolves relative to the pgflow project folder

`with.workingDirectory`

- if omitted, action executes in the target working directory
- if relative, resolves relative to the target working directory
- if absolute, used as-is

### Validation rules

A `script` action is invalid when:

- `with.shell` is missing
- `with.shell` is not `bash` or `pwsh`
- none of `with.run`, `with.file`, or `with.path` is supplied
- referenced `with.file` or `with.path` does not exist when validation has a project folder available

### Runtime environment variables injected into scripts

The runner injects the following environment variables:

- `ORCHESTRATOR_OUTPUT`
- `ORCHESTRATOR_PROJECT_FOLDER`
- `ORCHESTRATOR_TARGET_WORKING_DIRECTORY`
- `ORCHESTRATOR_RUN_ID`

The workflow environment and action-local `with.environment` values are also injected.

### Script outputs protocol

A script can emit outputs by appending `key=value` lines to the file path contained in `ORCHESTRATOR_OUTPUT`.

Example:

```bash
echo "artifactPath=build/output.txt" >> "$ORCHESTRATOR_OUTPUT"
```

Output parsing rules:

- blank lines are ignored
- lines without `=` are ignored
- only the first `=` acts as the separator
- both key and value are trimmed

---

## 7. `githubCopilot` action schema

GitHub Copilot actions are implemented by `GitHubCopilotActionRunner` in `src/Powergentic.Flow.Actions.GitHubCopilot/GitHubCopilotActionRunner.cs`.

### GitHub Copilot action shape

```yaml
- id: implement
  uses: githubCopilot
  with:
    prompt: string
    promptFile: string
    inputs:
      key: value
    writeResponseTo: string
    workingDirectory: string
    sessionId: string
    model: string
    systemPrompt: string
    streaming: true | false
    timeout: 60 | 00:30:00
    gitHubToken: string
    requestHeaders:
      Header-Name: value
```

### Supported GitHub Copilot `with` fields

| Field | Type | Required | Notes |
| --- | --- | ---: | --- |
| `prompt` | `string` | Conditionally | Inline prompt template. One of `prompt` or `promptFile` must be supplied. |
| `promptFile` | `string` | Conditionally | Prompt template file path. Relative paths resolve from the pgflow project folder. |
| `inputs` | `map<string, any>` | No | Values are expression-resolved before prompt templating. |
| `writeResponseTo` | `string` | No | If set, response text is written to a file. Relative paths resolve from the target working directory after interpolation. |
| `workingDirectory` | `string` | No | Working directory sent to the Copilot adapter. Relative paths resolve from the target working directory. |
| `sessionId` | `string` | No | Optional Copilot session id. Can be used to continue a previous `githubCopilot` session, including via `${ actions.someAction.outputs.sessionId }`. |
| `model` | `string` | No | Defaults to `auto`. |
| `systemPrompt` | `string` | No | Optional system prompt. |
| `streaming` | `bool` or `string` | No | Parsed as a boolean. Defaults to `false`. |
| `timeout` | `number` or `string` | No | Defaults to `60` minutes. Accepts a positive number of minutes or a `TimeSpan` value like `00:30:00`. |
| `gitHubToken` | `string` | No | Overrides `GITHUB_TOKEN` from environment when provided. |
| `requestHeaders` | `map<string, string?>` | No | Non-empty values are forwarded as request headers. |

### Prompt resolution

The prompt template is obtained in this order:

1. `with.promptFile` if present
2. otherwise `with.prompt`

If both are absent or blank, the action fails validation and runtime execution.

### Prompt file resolution

`with.promptFile` resolves relative to the pgflow project folder unless it is already absolute.

### Prompt templating

After `with.inputs` is expression-resolved, prompt templating performs simple case-insensitive placeholder replacement for each key using both forms:

- `{{key}}`
- `${key}`

No other prompt templating syntax is supported.

### Response file resolution

`with.writeResponseTo` resolves relative to the target working directory unless the final interpolated value is absolute.

This means a workflow can deliberately write into the run log folder by using an interpolated absolute path-like runtime value such as:

```yaml
writeResponseTo: ${ runtime.logFolder }/copilot-response.md
```

### GitHub Copilot validation rules

A `githubCopilot` action is invalid when:

- neither `with.prompt` nor `with.promptFile` is supplied
- `with.promptFile` references a missing file when validation has a project folder available

---

## 8. `outputs` schema

The `outputs` field is a map of synthetic outputs added after action execution.

```yaml
outputs:
  responseFile: ${ actions.implement.outputs.responseFile }
  buildSucceeded: ${ actions.build.status }
```

### Behavior

- Each output value is resolved using the expression engine after the action has executed.
- A synthetic output is only written if the action result does not already contain an output with the same key.
- Output keys are case-insensitive at runtime.

## 9. `publish` schema

Each `publish` entry maps to `WorkflowActionPublishDefinition` in `src/Powergentic.Flow.Core/Models/WorkflowActionPublishDefinition.cs`.

### Publish shape

```yaml
publish:
  - title: string
    from: string
    to:
      - console
      - runSummary
    if: string
    maxLength: int
```

### Publish fields

| Field | Type | Required | Notes |
| --- | --- | ---: | --- |
| `title` | `string` | No | Defaults to `action.name` or `action.id`. |
| `from` | `string` | Yes | Interpolated content source. |
| `to` | `array<string>` | Yes | Supported targets: `console`, `runSummary`. |
| `if` | `string` | No | Boolean condition controlling whether the publish entry is created. |
| `maxLength` | `int` | No | Must be `> 0` when specified. Content is truncated to this length. |

### Publish validation rules

A publish definition is invalid when:

- `from` is blank
- `to` is empty
- `to` contains anything other than `console` or `runSummary`
- `maxLength <= 0`

### Default publish behavior

If `publish` is omitted entirely and the action result contains a non-empty output named `response`, pgflow automatically publishes that content to both:

- `console`
- `runSummary`

This default is applied by `WorkflowExecutor.CreatePublishedEntries`.

## 10. `next` schema

Each `next` entry maps to `WorkflowTransitionDefinition` in `src/Powergentic.Flow.Core/Models/WorkflowTransitionDefinition.cs`.

### Transition shape

```yaml
next:
  - when: string
    goto: actionId
```

### Transition fields

| Field | Type | Required | Notes |
| --- | --- | ---: | --- |
| `when` | `string` | No | Boolean condition. If omitted, the transition is unconditional. |
| `goto` | `string` | Yes | Destination action id. |

### Transition resolution semantics

Transitions are evaluated in list order.

The first transition whose `when` is blank or evaluates to `true` wins.

If no transition matches, execution falls through to the next action in file order.

### Transition validation rules

A transition is invalid when:

- `goto` is blank
- `goto` refers to an action id that does not exist
- it is an unconditional self-loop (`goto: currentActionId` without `when`)
- it is an unconditional `goto` to the immediately following action in file order

### Important note about skipped actions

`if` controls whether the action body executes.

It does **not** bypass transition resolution. After a skipped action, pgflow still evaluates `next` and then falls through to the next action in file order if no transition matches.

## 11. Condition and interpolation syntax

Expressions are implemented by `ExpressionEngine` in `src/Powergentic.Flow.Core/Services/ExpressionEngine.cs`.

There are two related mechanisms:

- string interpolation using `${ ... }`
- boolean conditions using either raw expressions or `${{ ... }}`

### 11.1 String interpolation

Any string field passed through the expression engine can use `${ ... }` interpolation.

Examples:

```yaml
${ variables.userPrompt }
${ runtime.targetWorkingDirectory }
${ actions.build.outputs.reportFile }
```

Supported roots:

| Root | Form | Meaning |
| --- | --- | --- |
| `inputs` | `${ inputs.name }` | Workflow input value by key |
| `variables` | `${ variables.name }` | Workflow variable by key |
| `env` | `${ env.NAME }` | Runtime environment value by key |
| `runtime` | `${ runtime.projectFolder }` | Runtime metadata |
| `actions` | `${ actions.build.outputs.reportFile }` | Prior action output or status |

Supported `runtime.*` fields:

- `runtime.projectFolder`
- `runtime.targetWorkingDirectory`
- `runtime.runId`
- `runtime.logFolder`
- `runtime.currentActionId`

Supported `actions.*` forms:

- `actions.<actionId>.outputs.<key>`
- `actions.<actionId>.status`

### 11.2 Condition expressions

Boolean conditions are used by action `if`, publish `if`, and transition `when`.

Examples:

```yaml
if: ${{ success() }}
if: ${{ variables.needsReview == true }}
when: ${{ actions.build.outputs.buildSucceeded != 'true' }}
when: ${{ failure() || actions.test.status == 'failed' }}
```

Supported condition syntax:

- `==`
- `!=`
- `&&`
- `||`
- `!`
- parentheses for grouping
- literals: `true`, `false`, `null`, quoted strings
- helper functions: `success()`, `failure()`, `always()`

### 11.3 Condition helper functions

| Function | Meaning |
| --- | --- |
| `success()` | True when no prior action result has `Failed` status |
| `failure()` | True when any prior action result has `Failed` status |
| `always()` | Always true |

### 11.4 Value lookup limitations

The current implementation resolves dictionary keys as single keys, not deep object paths.

Supported examples:

- `${ variables.userPrompt }`
- `${ actions.prepare.outputs.workspaceSummary }`

Not supported as nested traversal semantics:

- `${ variables.someObject.nestedValue }`

If nested objects are stored in a map, the current expression engine does not recursively walk dotted child paths.

## 12. Project folder, `scripts/`, and `prompts/`

A pgflow workflow is authored inside a **pgflow project folder**. This is the folder that contains `flow.yml` and supporting assets.

A typical layout is:

```text
my-flow/
  flow.yml
  prompts/
    review.prompt.md
  scripts/
    prepare.sh
    run-tests.sh
  logs/
```

### Project folder vs target working directory

These are distinct concepts:

- **project folder**: where `flow.yml`, `prompts/`, `scripts/`, and run logs live
- **target working directory**: the external folder that actions operate on by default

### `scripts/` folder usage

`scripts/` is a convention for storing workflow-owned scripts beside `flow.yml`.

It is not auto-discovered.

It is used when `script` actions reference project-relative files, typically through:

```yaml
with:
  path: scripts/prepare.sh
```

or:

```yaml
with:
  file: scripts/prepare.sh
```

Because these paths resolve from the project folder, `scripts/` should contain deterministic helper scripts that are part of the workflow definition itself.

### `prompts/` folder usage

`prompts/` is a convention for storing workflow-owned prompt templates beside `flow.yml`.

It is not auto-discovered.

It is used when `githubCopilot` actions reference a prompt file, typically through:

```yaml
with:
  promptFile: prompts/review.prompt.md
```

Because `promptFile` resolves from the project folder, `prompts/` should contain reusable prompt templates that belong to the workflow, not to the target repository.

### Authoring guidance

Use the project folder for assets that define the automation itself:

- `flow.yml`
- prompt templates in `prompts/`
- helper scripts in `scripts/`
- per-run logs in `logs/`

Use the target working directory for the repository or content being acted on.

## 13. Execution semantics

### Start action

Execution always begins at the first action listed in `actions`.

### Default ordering

If `next` is omitted or no `next` entry matches, execution continues to the next action in file order.

### Failure handling

If an action returns `Failed`, workflow execution stops immediately.

### Skip handling

If an action `if` condition evaluates to false:

- the action receives status `Skipped`
- no action runner executes
- transition resolution still occurs

## 14. Run artifacts relevant to schema authors

Each run writes logs under:

```text
<project-folder>/logs/<run-id>/
```

Important files:

- `workflow-resolved.json` — resolved workflow snapshot
- `run.json` — run summary
- `console.log` — transcript of operator-facing output and mirrored diagnostics
- `actions/<nn>-<actionId>.json` — per-action summary
- `actions/<nn>-<actionId>.stdout.log` / `.stderr.log` — script process logs when applicable

These artifacts are not part of the YAML schema, but they are the main observability output produced by the workflow engine.

## 15. Minimal complete example

```yaml
name: Example Workflow
version: 1
inputs:
  audience: Developers
variables:
  greeting: Hello
env:
  APP_MODE: ${ inputs.audience }
execution:
  maxTransitions: 10
  maxVisitsPerAction: 3
actions:
  - id: hello
    name: Run greeting script
    uses: script
    if: ${{ always() }}
    with:
      shell: bash
      path: scripts/hello.sh
      environment:
        GREETING: ${ variables.greeting }
    outputs:
      message: ${ actions.hello.outputs.message }

  - id: summarize
    uses: githubCopilot
    with:
      promptFile: prompts/summarize.prompt.md
      inputs:
        message: ${ actions.hello.outputs.message }
        targetPath: ${ runtime.targetWorkingDirectory }
      writeResponseTo: ${ runtime.logFolder }/summary.md
    publish:
      - title: Summary
        from: ${ actions.summarize.outputs.response }
        to:
          - console
          - runSummary
```

## 16. Authoritative implementation references

The following files define or enforce the behavior described above:

- `src/Powergentic.Flow.Core/Models/WorkflowDefinition.cs`
- `src/Powergentic.Flow.Core/Models/WorkflowActionDefinition.cs`
- `src/Powergentic.Flow.Core/Models/WorkflowActionPublishDefinition.cs`
- `src/Powergentic.Flow.Core/Models/WorkflowTransitionDefinition.cs`
- `src/Powergentic.Flow.Core/Models/WorkflowExecutionOptions.cs`
- `src/Powergentic.Flow.Core/Services/WorkflowLoader.cs`
- `src/Powergentic.Flow.Core/Services/WorkflowValidator.cs`
- `src/Powergentic.Flow.Core/Services/ExpressionEngine.cs`
- `src/Powergentic.Flow.Core/Services/WorkflowExecutor.cs`
- `src/Powergentic.Flow.Actions.Script/ScriptActionRunner.cs`
- `src/Powergentic.Flow.Actions.GitHubCopilot/GitHubCopilotActionRunner.cs`

If implementation and documentation diverge, the implementation is authoritative.
