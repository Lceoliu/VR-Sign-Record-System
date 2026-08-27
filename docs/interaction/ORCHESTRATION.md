# Interaction Development Orchestration

Updated: 2026-08-25
Orchestrator: the primary Codex task on `feat/integrated-vr-interactions-recording`

## Authority and Git policy

- The primary task owns dependency planning, integration, all Git commands, commits, merges, conflict resolution, release gates, and user-facing progress.
- Worker tasks run in separate Codex worktrees. They may inspect, edit, and test files in their worktree, but must not run any `git` command or modify repository history.
- A worker finishes by reporting changed files, tests and exact outcomes, remaining risks, and any contract deviation. It does not commit or merge.
- The Orchestrator reviews the worktree diff, runs proportional tests, and performs the merge or selective integration.
- Existing user changes, especially `signvr_unity/Assets/XR/Settings/OpenXRPackageSettings.asset`, are preserved and are not absorbed into unrelated commits.

## Source of truth

1. [`CONTEXT.md`](../../CONTEXT.md) and [`docs/adr`](../adr) define terminology and architectural decisions.
2. [`INTERACTION_CONTRACT_V1.md`](INTERACTION_CONTRACT_V1.md) freezes the cross-task code and wire contract.
3. The detailed local execution roadmap remains under the ignored `meetings/` directory.
4. [`PROGRESS.md`](PROGRESS.md) is maintained only by the Orchestrator and records task/thread/integration status.
5. Workers write implementation notes under [`reports/`](reports/README.md); they do not edit `PROGRESS.md`.

## Integration order

```text
Contract baseline
├─ W1 Interaction Core ───────────┐
├─ W2 Content staging ────────────┼─ Orchestrator review/integration
└─ W3 Host interaction mode ──────┘
                                   ↓
                          Scene/build bootstrap
                                   ↓
                     Ghost/UI/pointing + capture client
                                   ↓
                        Six phase Unity adapters
                                   ↓
                    Local Editor + Host integration gate
                                   ↓
                       Batched Quest validation package
```

## Test policy

- Prefer pure C#, Python, backend, frontend, and Unity EditMode tests before scene or device work.
- Use the local Unity Editor for compilation, EditMode/PlayMode tests, scene validation, and builds whenever available.
- Quest validation is reserved for naked-hand tracking, device-only XR behavior, Android file I/O, performance, and final end-to-end verification.
- Device-dependent tasks do not block unrelated work. Several validated scenes or separately installable test applications may be batched into one later human test session.
- A worker must not claim device validation unless it actually ran on Quest and reports the build identity and device result.

## InteractionLab seated positioning aid

InteractionLab always includes a head-following world-space Poke button labelled
`向视线方向移动 10 cm`. Each successful naked-hand Poke translates the complete XR
subtree exactly 0.1 metres along the HMD's world-space forward vector at the
click instant. The full X/Y/Z direction is retained: looking up, down, sideways,
or behind the authored starting view therefore produces the corresponding
three-dimensional translation.

This is an intentionally collision-free seated test/operator aid. It does not
use `CharacterController`, gravity, or planar thumbstick locomotion and may pass
through furniture, walls, or the floor. The application-owned
`VRPlayer/InteractionSeatedRigOffset` sits above `OVRCameraRig`; the fixed
`VRPlayer` root and Meta/OpenXR-owned tracking origin retain their established
ownership. The HMD-child `InteractionSeatedMoveCanvas` stays reachable after
movement, while static task targets remain in the authored world frame.

Because capture samples HMD and hands in world space, using this aid changes the
subsequently recorded HMD/hand world coordinates. The accumulated offset is
runtime-only and resets to zero whenever InteractionLab is reloaded. Primary
implementation is `Assets/Scripts/Interaction/InteractionSeatedRigMover.cs`;
idempotent assembly is in `Assets/Editor/InteractionSeatedMoveSetup.cs`, invoked
by the InteractionLab scene generator and enforced by its validator.

## Worker completion contract

Every worker report must include:

1. Scope completed and scope deliberately not completed.
2. Files created/changed.
3. Commands/tests executed and exact results.
4. Generated files or local prerequisites that are intentionally not versioned.
5. Contract deviations or integration assumptions.
6. Recommended Orchestrator review steps.
