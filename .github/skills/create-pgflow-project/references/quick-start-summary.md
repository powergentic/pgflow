# pgflow quick-start summary

Use the simplest scaffold that matches the user's goal.

## Recommended starting points

### First workflow

Use `basic-script` when the user is:

- new to pgflow
- learning `flow.yml`
- automating deterministic shell steps

### AI harness

Use `script-and-copilot-loop` when the user wants:

- a reusable harness that inspects a target project
- prompt templates in `prompts/`
- a `githubCopilot` action combined with shell scripts
- a pattern that can evolve into build-test-fix loops

## Folder roles

- pgflow project folder: reusable harness containing `flow.yml`, `scripts/`, `prompts/`, and `logs/`
- target working directory: the actual project the flow operates on

## Good defaults

- keep scripts in `scripts/`
- keep prompts in `prompts/`
- start with a small flow and expand later
- suggest `--display-enhanced` for interactive runs
