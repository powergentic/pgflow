---
name: create-pgflow-project
description: 'Create a new pgflow project scaffold with flow.yml, scripts, prompts, and starter assets. Use when the user wants to start a new pgflow workflow, scaffold a flow harness, create a first flow.yml project, or set up script and GitHub Copilot templates.'
argument-hint: 'Describe the project folder, desired template, and whether the flow should include GitHub Copilot.'
user-invocable: true
---

# Create pgflow Project

Create a new pgflow project scaffold for the user.

## When to use

Use this skill when the user wants to:

- create a new `pgflow` project
- scaffold `flow.yml` plus `scripts/` and `prompts/`
- start from a working template instead of authoring from scratch
- build either a simple script-only harness or a script-plus-Copilot harness

## Supported scaffold types

- `basic-script`
- `script-and-copilot-loop`

The built-in runtime templates currently support exactly those two scaffold shapes.

## Procedure

1. Determine the destination folder for the new pgflow project.
2. Determine which scaffold type best matches the user's goal:
   - `basic-script` for first-time users or deterministic shell automation
   - `script-and-copilot-loop` for an AI harness that combines scripts and `githubCopilot`
3. Create the project directory structure.
4. Add `flow.yml` and the template files matching the chosen scaffold.
5. Preserve executable shell scripts.
6. If the user wants a custom variant, start from the nearest scaffold and adapt the content rather than inventing unsupported schema.
7. Recommend validating or running the flow after creation.

## Files to create

### `basic-script`

Create these files:

- `flow.yml`
- `scripts/hello.sh`

Reference template content:

- [basic flow](./assets/basic-script/flow.yml)
- [basic script](./assets/basic-script/scripts-hello.sh)

### `script-and-copilot-loop`

Create these files:

- `flow.yml`
- `scripts/prepare.sh`
- `prompts/review.prompt.md`

Reference template content:

- [Copilot loop flow](./assets/script-and-copilot-loop/flow.yml)
- [prepare script](./assets/script-and-copilot-loop/scripts-prepare.sh)
- [review prompt](./assets/script-and-copilot-loop/prompts-review.prompt.md)

## Rules

- Use paths relative to the new project folder.
- Keep prompt templates in `prompts/`.
- Keep helper shell scripts in `scripts/`.
- Do not add unsupported action types or schema fields.
- If the user asks for a different action design, ensure it still conforms to the supported schema in [SCHEMA reference](./references/schema-summary.md).

## Authoring guidance

- `script` actions should use `bash` or `pwsh` only.
- `githubCopilot` actions should use `prompt` or `promptFile`.
- Relative script and prompt file paths resolve from the pgflow project folder.
- `writeResponseTo` resolves from the target working directory unless the flow intentionally writes into `${ runtime.logFolder }`.

## Validation guidance

After scaffolding, suggest one of these commands:

- `pgflow validate <project-folder>`
- `pgflow run <project-folder>`
- `pgflow run <project-folder> <target-working-directory> --display-enhanced`

For broader usage examples, see [quick start](./references/quick-start-summary.md).
