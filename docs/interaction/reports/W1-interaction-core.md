# W1 Interaction Core completion report

Date: 2026-08-25
Worker baseline supplied by Orchestrator: `35fcc93`

## Scope completed

- Added a pure C# `SignVR.Interaction.Core` module with no Unity Engine,
  `MonoBehaviour`, scene-object, Recorder, Host, or `RecordingCoordinator`
  dependency.
- Added the three canonical `AssistanceCondition` values and derived text /
  pointing visibility, with no representable `PointingOnly` value.
- Added all frozen V1 Run states, Phase states, Phase results, and a Run result.
- Added the six inclusive sentence ranges (`001-003`, `004-012`, `013-015`,
  `016-018`, `019-025`, `026-031`) and a 31-entry logical `TaskVariant`
  catalog with no range offset or gap.
- Added immutable exact `InstructionContentReference`, 31-entry Pilot
  `InstructionContentCatalog`, `TaskVariant`, `RunPhasePlan`, `RunPlan`,
  four-unique-digit `SafePassword`, and four-button `ChestButtonOrder` models.
- Added the session-local shuffled `AssistanceBlockAllocator`. Each block uses
  all three conditions once; a successful Start commits the slot; abort exposes
  no operation that could return it; creating a new allocator starts a fresh
  application-session block.
- Added deterministic `RunPlanGenerator` randomization using an explicit
  SplitMix64 implementation with rejection-sampled bounded integers. Six
  sentence draws, safe digits, and chest-button draws are reproducible from the
  seed while Run ID / UTC identity are supplied separately and default to UTC +
  `Guid.NewGuid()`.
- Added a six-phase `InteractionRunStateMachine` covering Start, Host schedule,
  synchronized Run start, first-playback completion, one replay, Completed,
  Stuck, 180-second log-only timeout, task-error progress reset, completing,
  aborting, terminal results, faulting, and explicit reset to PreStart. Terminal
  results retain the immutable plan and all six partial Phase snapshots.
- Added Editor-only NUnit tests for every minimum case in the W1 assignment.

Deliberately not completed:

- No Unity interaction, presentation, capture, network, scene, or XR adapter.
- No Recorder, Host, scene, OpenXR, package-setting, or existing user-file edit.
- No latest-Take resolver or build-time content staging; W2 supplies the exact
  31-entry Pilot content catalog consumed here.
- No JSON / JSONL serializer and no required-event emission adapter.
- No Quest build or device validation.

## Files created

Production assets:

- `signvr_unity/Assets/Scripts/Interaction.meta`
- `signvr_unity/Assets/Scripts/Interaction/Core.meta`
- `signvr_unity/Assets/Scripts/Interaction/Core/AssistanceBlockAllocator.cs`
- `signvr_unity/Assets/Scripts/Interaction/Core/AssistanceBlockAllocator.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/Core/InteractionContracts.cs`
- `signvr_unity/Assets/Scripts/Interaction/Core/InteractionContracts.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/Core/InteractionRunStateMachine.cs`
- `signvr_unity/Assets/Scripts/Interaction/Core/InteractionRunStateMachine.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/Core/PhaseSentenceCatalog.cs`
- `signvr_unity/Assets/Scripts/Interaction/Core/PhaseSentenceCatalog.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/Core/RunPlan.cs`
- `signvr_unity/Assets/Scripts/Interaction/Core/RunPlan.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/Core/RunPlanGenerator.cs`
- `signvr_unity/Assets/Scripts/Interaction/Core/RunPlanGenerator.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/Core/SignVR.Interaction.Core.asmdef`
- `signvr_unity/Assets/Scripts/Interaction/Core/SignVR.Interaction.Core.asmdef.meta`

Test assets:

- `signvr_unity/Assets/Tests/EditMode/Interaction.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/CoreTestData.cs`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/CoreTestData.cs.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/InteractionRunStateMachineTests.cs`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/InteractionRunStateMachineTests.cs.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/PhaseAndAssistanceTests.cs`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/PhaseAndAssistanceTests.cs.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/RunPlanGeneratorTests.cs`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/RunPlanGeneratorTests.cs.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/SignVR.Interaction.Core.Tests.asmdef`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/SignVR.Interaction.Core.Tests.asmdef.meta`

Report:

- `docs/interaction/reports/W1-interaction-core.md`

No existing repository file was changed. `docs/interaction/PROGRESS.md` was not
edited. No Git command was run.

## Commands and test results

### Successful checks

1. Forbidden dependency scan over Core and its tests:
   `rg -n "UnityEngine|MonoBehaviour|RecordingCoordinator|Assembly-CSharp|PointingOnly" ...`
   returned no matches.
2. Assembly / asset validation parsed both asmdef files, confirmed exactly one
   Interaction product asmdef, confirmed the product asmdef has zero references
   and `noEngineReferences: true`, checked all source assets have `.meta` files,
   and checked all added GUIDs for duplicates. Result:
   `PRODUCT_ASMDEF_COUNT=1`, `VALIDATION_ERROR_COUNT=0`,
   `STATIC_SCOPE_VALIDATION=PASS`.
3. Roslyn compiled all Core sources and then all NUnit sources against .NET Core
   reference assemblies. Exact result: `CORE_CSC_EXIT=0`, `TEST_CSC_EXIT=0`.
4. Final Roslyn check compiled Core and NUnit sources against the installed .NET
   Framework 4.7.2 reference surface plus Unity's packaged NUnit framework.
   Exact result: `FINAL_CORE_CSC_EXIT=0`, `FINAL_TEST_CSC_EXIT=0`.
5. The 23 compiled NUnit cases were invoked in a standalone reflection smoke
   runner, so the actual assertions and state transitions executed outside
   Unity. Exact result: `FINAL_REFLECTION_NUNIT_PASSED=23`,
   `FINAL_REFLECTION_NUNIT_FAILED=0`.

The 23 cases cover:

- all six inclusive boundaries, exact `001-031` grouping, in-range sampling,
  reachability of every sentence, and a fixed-seed distribution-skew guard;
- each three-Run Assistance block, multiple reshuffles, application-session
  restart, and Abort consuming slot 0 before the next Start uses slot 1;
- unique safe digits, legal four-button permutations, same-seed reproduction,
  distinct Run IDs, six resolved content references, and nested immutable
  sequence semantics;
- Start / schedule / run start, all-six normal completion, Replay before/after
  availability and second-use rejection, wrong-input progress reset with plan
  identity retained, Stuck, partial Abort, Faulted result, and an idempotent
  180-second timeout that leaves Run / Phase state unchanged.

### Unity Editor attempt — not a test result

An initial isolated-worktree command invoked installed Unity `6000.5.6f1` in
batch mode for assembly `SignVR.Interaction.Core.Tests`. The launcher returned
`UNITY_EXIT=0` while its child Editor process continued, no `results.xml` was
created, and therefore the exit code is not treated as success. The log recorded
licensing-client signature / access-token handshake errors, later initialized a
Personal license after about 29 seconds, then began rebuilding the absent local
Asset Database. Per the Orchestrator's follow-up instruction, the worker stopped
its own Unity PID `43952` during `Application.AssetDatabase Initial Refresh
Start` and did not retry.

Evidence is retained locally at:

- `C:/Users/woshica/AppData/Local/Temp/signvr-w1-core-0652076270ac4adabb73a131c2637aa9/unity.log`
- `C:/Users/woshica/AppData/Local/Temp/signvr-w1-core-0652076270ac4adabb73a131c2637aa9/`
  (no `results.xml`)

**Unity EditMode tests remain pending for the Orchestrator to run in the already
working main Editor.** No Editor, PlayMode, build, or Quest result is claimed by
this worker.

## Local / generated prerequisites not versioned

- The interrupted isolated Editor launch created `signvr_unity/Library/`, used
  only as a local package / compiler cache and not intended for versioning.
- Final standalone compile artifacts are under
  `C:/Users/woshica/AppData/Local/Temp/signvr-w1-final-4c8aad7d9c7f455cb1cd074e68c03c32/`
  and are not intended for versioning.
- The batch launch created `signvr_unity/UserSettings/EditorUserSettings.asset`
  and `Search.settings`. Read-only comparison showed peer Codex worktrees did
  not contain them, so both generated files were removed; no UserSettings asset
  remains in the delivery.
- W2 must provide one exact, integrity-checked `InstructionContentReference` for
  every sentence `001-031`, with signer `wang`, before a plan can be generated.

## Contract deviations and integration assumptions

No incompatible Contract V1 deviation is known.

- Task Variants intentionally use scene-independent logical IDs. Later Unity
  adapters must map `box_*`, `coin_*`, `plate_*`, `picture_frame_*`, `key_*`,
  `button_*`, and `breaker_*` IDs to authored scene objects without moving that
  scene knowledge into Core.
- The exact latest-completed-Take selection rule is owned by W2. Core validates
  and freezes W2's resolved 31-entry catalog; it does not recalculate recency.
- For phases 1-5, `CompletePhase` / `GiveUpPhase` receive the actual monotonic
  timestamp at which the next phase's first playback begins. This becomes the
  next phase's 180-second timer origin. For phase 6 the argument is the terminal
  completion / give-up time.
- State-machine return values and snapshots are intended to drive the required
  JSONL events. Event naming / serialization stays in a later adapter.
- Production should use the default Run identity source (UTC + new GUID). The
  injectable identity delegates exist so deterministic tests can hold identity
  constant while proving seeded content reproduction.

## Risks and recommended Orchestrator review

1. Import in the main Unity `6000.5.6f1` Editor and run EditMode assembly
   `SignVR.Interaction.Core.Tests`. Expected discovery: 23 cases; expected result:
   23 passed, 0 failed. This is the remaining validation gate.
2. Confirm the product asmdef compiles with `noEngineReferences: true` and that
   no assembly reference is auto-added during import.
3. Compare all 31 logical Task Variant mappings with the Recorder catalog,
   especially the phase-2 3x3 coin/plate order, phase-5 seven non-empty button
   subsets, and phase-6 six breaker permutations, before adapters bind scene
   objects.
4. Integrate W2 by constructing `InstructionContentCatalog` from the staged
   manifest's exact resolved Take / artifact / SHA-256 values. Keep serialized
   sentence IDs as three-digit `001-031` values.
5. When adding the Unity adapter, call timeout observation from a monotonic clock,
   use the boolean return to emit `phase_timeout` only once, and never treat it
   as a Phase transition.
6. Verify serializer output preserves the frozen enum spellings and V1 JSON
   field names; serialization itself is intentionally outside W1.
