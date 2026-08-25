# W7 — Six-phase P0 interaction rules and Unity adapters

Date: 2026-08-26
Worker base supplied by Orchestrator: `335befa`
Workspace: `C:\Users\woshica\.codex\worktrees\69ef\VR-Sign-Record-System`

## 1. Scope completed and deliberately not completed

Completed:

- Added a pure-C# six-phase task-rule session driven only by the immutable W1 `RunPlan`. W1 `InteractionRunStateMachine` remains the sole Run/Phase lifecycle authority: W7 requires an explicit `PhaseExecutionSnapshot`, rejects snapshot drift, self-locks after task completion/GiveUp, and never advances a phase or produces a Run result.
- Added strict P0 rules for all six phases, including complete reset semantics for phases 1, 4, and 6; phase-2 pair validation; phase-5 single/double/triple button subsets and wrong/repeated-button subset reset while retaining the accepted key; phase-2 cardinality and phase-6 non-empty-order defenses.
- Added phase-1 `*` backspace and `#` submit semantics end to end: Core input, adapter API, bindings, physical trigger relays, setup labels, validation, and tests.
- Added Unity-facing adapters, deterministic presentation, feedback, plan hints, target/digit/password/placement bindings, and bare-hand-filtered trigger relays under the W7-owned `PhaseAdapters/` directory.
- Limited trigger colliders to two explicitly configured bare-hand interactor roots. Limited placement input to `coin_dragon`, `coin_a`, or `coin_b`, and deduplicated multiple colliders belonging to one physical coin until all of that coin's colliders exit.
- Implemented hint lifecycle: phase-1 error/completion/GiveUp hides the password; phase-3 success or GiveUp exposes the planned chest-button order; phase-4 completion/GiveUp hides it. Phase-4 GiveUp deterministically opens the chest and releases only the planned key.
- Made disable lifecycle explicit: coordinator/adapters/presenters unsubscribe or disable local state on `OnDisable`; presentations do not consume results or start coroutines while disabled; re-enabling never bypasses the last W1 snapshot gate.
- Added an idempotent W7 setup/validator that checks the coordinator-to-six-adapter graph, target-to-phase ownership, keypad digit/backspace/submit colliders and relays, placement/presenter/presentation references, and allowed bare-hand roots. Setup/Validate menu entry points preserve and restore the caller's scene setup. The Orchestrator-provided exact paths are retained: `Collada visual scene group/ChestUpper_low` and `ce5f462b0dd34333a6588509140a7fb8.fbx/RootNode/Door`.

Deliberately not completed:

- Did not edit or save `InteractionLab.unity`, any other scene YAML, W4 `InteractionLabSceneTool`, Editor build settings, OpenXR/URP settings, Host, recording/content staging, W5, or W6 code.
- Did not add JSONL output, a replacement Run controller, or any reference from Core rules to Unity, `MonoBehaviour`, scene objects, or `RecordingCoordinator`.
- Did not modify the Core asmdef or create a shared runtime asmdef/directory.
- Did not start Unity, run Unity EditMode/PlayMode tests, build Android, or test on Quest in this isolated worktree.

## 2. Files created or changed

Pure rules and tests:

- `signvr_unity/Assets/Scripts/Interaction/Core/PhaseInteractionRules.cs` and `.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Core/PhaseInteractionRulesTests.cs` and `.meta`

Unity runtime adapters (`signvr_unity/Assets/Scripts/Interaction/PhaseAdapters/`, each with its `.meta`):

- `InteractionDeterministicPresentation.cs`
- `InteractionDigitBinding.cs`
- `InteractionFeedbackPresenter.cs`
- `InteractionPasswordBackspaceBinding.cs`
- `InteractionPasswordSubmitBinding.cs`
- `InteractionPhaseAdapter.cs`
- `InteractionPhaseCoordinator.cs`
- `InteractionPlacementBinding.cs`
- `InteractionPlanHintPresenter.cs`
- `InteractionTargetBinding.cs`
- `InteractionTriggerRelay.cs`
- `PhaseOneInteractionAdapter.cs`
- `PhaseTwoInteractionAdapter.cs`
- `PhaseThreeInteractionAdapter.cs`
- `PhaseFourInteractionAdapter.cs`
- `PhaseFiveInteractionAdapter.cs`
- `PhaseSixInteractionAdapter.cs`

Editor setup/validation and Unity-facing tests:

- `signvr_unity/Assets/Editor/W7InteractionPhaseAdaptersSetup.cs` and `.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/PhaseAdapters/W7InteractionPhaseAdaptersTests.cs` and `.meta`
- `docs/interaction/reports/W7-phase-interaction-adapters.md`

## 3. Commands/tests executed and exact results

No Git command and no Unity Editor process was run.

TDD RED evidence was written to `C:\Users\woshica\AppData\Local\Temp\signvr-w7-lifecycle-red`:

- Core production compile: exit `0`.
- Core test compile against the pre-fix API: exit `1`, with 10 expected `CS1061`/`CS1501` errors for the missing snapshot synchronization and snapshot-validated GiveUp seams.

Final static verification was run with the Roslyn C# compiler 3.11.0 from .NET SDK 5.0.414. Outputs are in `C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-final3-20260826`:

```text
CORE_NET472_COMPILE=0
CORE_NETSTANDARD21_COMPILE=0
CORE_TEST_COMPILE=0
PURE_PHASE_RULES passed=22 failed=0 total=22
InteractionFeedbackPresenter optional acceptedClip/errorClip CS0649 only
PHASE_ADAPTERS_COMPILE=0
W7_EDITOR_COMPILE=0
UNITY_TEST_SOURCE_COMPILE=0
```

The 22 executed pure-C# tests cover all 31 `TaskVariant` routes, snapshot-required activation, playback/replay availability, snapshot drift, all six happy paths, unplanned targets, phase-1 backspace and full reset, phase-2 mismatch and malformed plan defense, phase-4 full reset and planned-key release, phase-5 1/2/3-target subsets plus wrong/repeated reset to key-only progress, phase-6 ordered/full reset behavior, future/completed locks, snapshot-validated non-chainable GiveUp, phase-4 fallback, Abort/new-Run local reset, disable/re-enable, and non-duplicated/self-locked completion.

Nine Unity-facing NUnit test methods were source-compiled successfully but were not executed without Unity. They cover adapter API/validation, W1 snapshot authority, relay hand-root filtering, placement filtering/deduplication, hint lifecycle, phase-4 GiveUp presentation, phase-5 visual reset, and disabled presentation behavior.

Static audit result:

```text
W7_CS_COUNT=21
PHASE_ADAPTER_CS_COUNT=17
MISSING_META_COUNT=0
RUNTIME_FILENAME_TYPE_MISMATCH_COUNT=0
ASSET_GUID_RECORD_COUNT=470
DUPLICATE_GUID_GROUPS=0
CORE_ASMDEF_AUTO_REFERENCED=True
CORE_ASMDEF_NO_ENGINE_REFERENCES=True
CORE_ASMDEF_REFERENCE_COUNT=0
PHASE_ADAPTER_ASMDEF_COUNT=0
CORE_FORBIDDEN_REFERENCE_MATCHES=0
W7_FORBIDDEN_SCOPE_REFERENCE_MATCHES=0
RUN_COMPLETED_SYMBOL_MATCHES=0
OLD_NOARG_GIVEUP_MATCHES=0
INTERACTION_SCENE_BYTES=408990
INTERACTION_SCENE_SHA256=A394EABE4D72739C625BA5A261F36DF86A87B15C201CCB02F53A134B1DF027B5
ROADMAP_EXISTS=False
```

## 4. Generated files or local prerequisites intentionally not versioned

- Roslyn outputs and copied NUnit/Unity reference assemblies exist only under the two `%LOCALAPPDATA%\Temp\signvr-w7-*` directories named above and are not repository content.
- The installed Unity 6000.5.6f1 managed assemblies were used only as references for static compilation; Unity itself was not launched.
- The requested roadmap file `meetings/手语交互系统_后续开发任务路线图.md` is absent from this worktree (`ROADMAP_EXISTS=False`). The other required context/contracts and W1/W4 reports were read.
- Imported model hierarchies depend on hydrated LFS payloads in the authoritative workspace; this worker did not generate or version imported artifacts.

## 5. Contract deviations or integration assumptions

- No frozen interaction-contract schema was changed. W7 exposes typed results/events only. `ValidationResult.PhaseCompleted` means “task completion requested”; it is not a W1 phase transition or Run completion. There is no W7 `RunCompleted` symbol.
- The integrating controller must supply snapshots produced by the same immutable plan configured into W7. W7 verifies snapshot phase/lifecycle availability but does not duplicate W1's full plan identity.
- GiveUp is accepted only with the currently synchronized snapshot and `GiveUpAvailable=true`; W7 then self-locks. W1 must perform the actual GiveUp transition and provide the next snapshot.
- Bare-hand root discovery uses semantically named left/right hand-interactor hierarchies and excludes controller names. Existing serialized roots can be reused, but the validator fails unless exactly two valid left/right hand-only roots are present.
- The physical relay accepts a collider only when its transform is the configured root or a descendant. Actual Meta hand-collider ancestry and trigger timing remain device/PlayMode risks.
- Deterministic visual feedback is implemented; accepted/error audio clips are optional serialized references, which accounts for the two benign `CS0649` warnings.
- No setup was applied to `InteractionLab.unity` in this worktree, so authoritative scene wiring is intentionally pending Orchestrator execution.

## 6. Recommended Orchestrator review steps

1. Review/selectively integrate the listed files, hydrate LFS assets, and keep the exact chest-lid/final-door paths already present in `W7InteractionPhaseAdaptersSetup.cs`.
2. In the authoritative Unity 6000 editor, run the W7 Setup menu on `InteractionLab`, then run W7 Validate. Confirm the caller's prior scene setup and unsaved-scene prompt behavior are preserved. If automatic hand discovery cannot resolve the project hierarchy, explicitly assign the coordinator's two `Allowed Interactor Roots` to the left/right hand-only interactor roots and rerun validation; do not weaken the validator to admit controller or arbitrary colliders.
3. Wire lifecycle in this order: `Configure(machine.Plan)`; enable the active coordinator; call `Synchronize(machine.CurrentPhase)` on Run start and every FirstPlayback/Active/ReplayPlayback/phase transition; on W7 task completion call W1 `CompletePhase` and then synchronize W7 with W1's new snapshot. For GiveUp, pass the current W1 snapshot to W7, invoke W1 `GiveUpPhase`, then synchronize the resulting snapshot. Abort/reset W1 separately and call the W7 local `Abort`/`Reset`; configure a new immutable plan for a new Run.
4. Run the 22 Core EditMode tests and 9 W7 Unity-facing EditMode tests after setup, followed by PlayMode checks for disable/re-enable, trigger filtering, multi-collider placement, hint visibility, and deterministic fallback animations.
5. On Quest, verify both naked-hand collider roots, all `0`–`9`/`*`/`#` trigger proxies, coin placement deduplication, phase-4 GiveUp key release, phase-5 visual reset, and final-door animation. Record the build identity; no device validation is claimed here.
