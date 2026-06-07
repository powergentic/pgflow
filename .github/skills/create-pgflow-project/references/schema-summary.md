# pgflow scaffold schema summary

Use only currently supported `pgflow` schema.

## Top-level fields

- `name`
- `description`
- `version`
- `inputs`
- `variables`
- `env`
- `execution`
- `actions`

## Supported action types

- `script`
- `githubCopilot`

## Common action fields

- `id`
- `name`
- `uses`
- `if`
- `with`
- `outputs`
- `publish`
- `next`

## `script` action essentials

Required shape:

- `uses: script`
- `with.shell`: `bash` or `pwsh`
- at least one of `with.run`, `with.file`, or `with.path`

Common fields:

- `with.path`
- `with.file`
- `with.workingDirectory`
- `with.environment`
- `with.failOnNonZeroExit`

## `githubCopilot` action essentials

Required shape:

- `uses: githubCopilot`
- one of `with.prompt` or `with.promptFile`

Common fields:

- `with.inputs`
- `with.writeResponseTo`
- `with.workingDirectory`
- `with.model`
- `with.systemPrompt`
- `with.streaming`
- `with.gitHubToken`
- `with.requestHeaders`

## Path rules

- `promptFile`, `path`, and relative `file` values resolve from the pgflow project folder
- relative `workingDirectory` values resolve from the target working directory
- logs are written under `<project-folder>/logs/<run-id>/`

See the repository `SCHEMA.md` for the complete technical reference.
