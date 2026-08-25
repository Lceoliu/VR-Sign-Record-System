# Worker Reports

Each worker task adds one Markdown report named after its Work ID, for example `W1-interaction-core.md`. Reports contain scope, changed files, tests with exact results, local/generated prerequisites, contract deviations, risks, and recommended integration checks.

Workers do not edit `../PROGRESS.md`; the Orchestrator owns that file to avoid coordination conflicts.
