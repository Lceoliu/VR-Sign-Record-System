# Interaction Development Progress

Updated: 2026-08-25
Overall state: first parallel implementation batch active

| Work ID | Scope | Dependencies | Worker task | State | Integration |
| --- | --- | --- | --- | --- | --- |
| W1 | Interaction Core: contracts, Run Plan, condition allocator, passwords, six-phase state machine, EditMode tests | Contract V1 | `W1 Interaction Core` · `01a03916-2d8c-7162-bb51-2513ed2fdbff` · worktree `d4ff` | In progress | Not reviewed |
| W2 | Wang 31-sentence resolver, integrity validation, build-time staging tool, tests | Contract V1 | `W2 Instruction Content Staging` · `01a03916-2d87-7ac0-865f-0638e51856a5` · worktree `2664` | In progress | Not reviewed |
| W3 | Host Interaction API, repository, webcam Study mode, backend/frontend tests | Contract V1 | `W3 Host Interaction Mode` · `01a03916-2d85-7842-9c70-b1b7abcb1184` · worktree `b57e` | In progress | Not reviewed |
| W4 | Dual build entry and clean Interaction scene bootstrap | Contract V1 | `W4 Dual Build and Clean Scene` · `01a03917-bd12-7740-81c3-d3d5dd00d49f` · worktree `c99f` | In progress | Not reviewed |
| W5 | Independent ghost player, bubble, Replay, immediate-hit pointing | W1,W2,W4 | unassigned | Blocked | Not started |
| W6 | Local capture and Quest–Host client | W1,W3,W4 | unassigned | Blocked | Not started |
| W7 | Six simplified interaction adapters | W1,W4 | unassigned | Blocked | Not started |
| W8 | Local Unity/Host integration and batched Quest validation package | W2..W7 | Orchestrator | Blocked | Not started |

## Current gates

- UnitySkills REST service: not listening on ports 8090–8100 at orchestration start.
- Local Unity Editor validation: pending first integrated Unity change.
- Quest validation: intentionally deferred and batched.
- Existing user OpenXR settings change: preserved, excluded from orchestration commits.

## Integration log

- 2026-08-25: Contract V1 and Orchestrator policy created before worker dispatch.
- 2026-08-25: W1, W2, and W3 dispatched from baseline `35fcc93` into independent detached Codex worktrees; every worker prompt explicitly forbids Git commands.
- 2026-08-25: W4 dispatched after confirming its build/scene tooling can stay independent of W1 Core types.
