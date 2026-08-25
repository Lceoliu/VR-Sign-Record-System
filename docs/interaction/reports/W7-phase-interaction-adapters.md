# W7 — Six-phase P0 interaction rules and Unity adapters

Date: 2026-08-26
Worker base supplied by Orchestrator: `335befa`
Workspace: `C:\Users\woshica\.codex\worktrees\69ef\VR-Sign-Record-System`

## 1. Scope completed and deliberately not completed

Completed:

- Added a pure-C# six-phase task-rule session driven only by the immutable W1 `RunPlan`. W1 `InteractionRunStateMachine` remains the sole Run/Phase lifecycle authority: W7 requires an explicit `PhaseExecutionSnapshot`, rejects snapshot drift, self-locks after task completion/GiveUp, and never advances a phase or produces a Run result.
- Added strict P0 rules for all six phases, including complete reset semantics for phases 1, 4, and 6; phase-2 pair validation; phase-5 single/double/triple button subsets and wrong/repeated-button subset reset while retaining the accepted key; and construction/runtime defenses for phase-2 cardinality and a non-empty phase-6 order.
- Added phase-1 `*` backspace and `#` submit semantics end to end: Core input, adapter API, bindings, physical trigger relays, setup labels, validator checks, and tests.
- Added Unity-facing adapters, deterministic presentation, feedback, plan hints, target/digit/password/placement bindings, and bare-hand-filtered trigger relays under the W7-owned `PhaseAdapters/` directory.
- Limited trigger colliders to two explicitly configured bare-hand interactor roots. Limited placement input to `coin_dragon`, `coin_a`, or `coin_b`, and deduplicated multiple colliders belonging to one physical coin until all colliders for that coin exit.
- Implemented hint lifecycle: phase-1 error/completion/GiveUp hides the password; phase-3 success or GiveUp exposes the planned chest-button order; phase-4 completion/GiveUp hides it. Phase-4 GiveUp deterministically opens the chest and releases only the planned key.
- Made disable lifecycle explicit. Target and all three keypad bindings close their colliders and reject direct/UnityEvent seams while disabled. Calling `Configure` while disabled neither subscribes to adapter availability nor reopens colliders/interaction behaviours; later adapter availability changes remain inert until `OnEnable` subscribes and restores the W1-gated availability. Presenters unsubscribe and cannot start coroutines while disabled. Deterministic presentation rebuilds on enable from the current W1 lifecycle snapshot plus W7 task-presentation snapshot, without creating a second lifecycle authority.
- Closed the exact-path review: chest lid and final door use only `Collada visual scene group/ChestUpper_low` and `ce5f462b0dd34333a6588509140a7fb8.fbx/RootNode/Door`. Setup preflight requires both exact paths; creation has no name-token fallback; validation requires the configured `MovingPart` reference and its full relative hierarchy path to equal the frozen constant. Decoy `lid`/`left` hierarchy tests are included.
- Closed the clean-checkout validator review: the Unity-facing validator test first runs the idempotent in-memory setup, then validates it, and does not depend on previously saved scene wiring.
- Closed the editor mutation-safety review: setup performs a complete read-only preflight before its Undo group, records existing-object changes through `Undo.RecordObject`, records created objects/components, validates before commit, and calls `Undo.RevertAllDownToGroup` on failure. A test poisons post-preflight validation and verifies rollback of an existing label.
- Added the W8 observability seam without adding an event or lifecycle behavior. Every `AcceptInput` result, including a gate failure, exposes nullable `InputKind`, `InputTargetId`, `InputSecondaryTargetId`, and `InputDigitValue` copied from the original `PhaseInput`. `TargetId` remains the independent feedback target. GiveUp/non-input results leave all four fields null, and W8 can continue to subscribe exactly once to `ResultProduced`.

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

Latest W8 TDD RED/GREEN harness:

```text
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-red2-20260826\CoreRed.csproj --no-restore --nologo
RED: exit 1; 16 expected CS1061 errors for the four missing ValidationResult Input* properties.
GREEN: exit 0; 0 warnings; 0 errors.

dotnet run --project C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-red2-20260826\Runner.csproj --no-restore --nologo
PURE_PHASE_RULES passed=24 failed=0 total=24
```

Latest disabled-Configure boundary TDD audit:

```text
RED: exit 1; ACTIVE_CONFIGURE_GUARD=False for Target, Digit, Backspace, and Submit.
GREEN: exit 0; ACTIVE_CONFIGURE_GUARD=True for all four bindings.
```

SDK static projects were rebuilt with these commands:

```text
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\Core.csproj --no-restore --nologo
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\PhaseAdapters.csproj --no-restore --nologo
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\W7Editor.csproj --no-restore --nologo
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\UnityTests.csproj --no-restore --nologo
```

Direct static compilation used Unity 6000.5.6f1's bundled `MonoBleedingEdge\bin\mono.exe` to host its Roslyn `csc.exe`, with `/noconfig /nostdlib+ /langversion:9.0`, coherent Unity netstandard 2.1 references for runtime/editor code, and Unity's 4.7.2 API references for Core/tests. Outputs are in `C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826`:

```text
CORE_NET472_COMPILE=0
CORE_NETSTANDARD21_COMPILE=0
CORE_TEST_COMPILE=0
PURE_PHASE_RULES passed=24 failed=0 total=24
PHASE_ADAPTERS_COMPILE=0
W7_EDITOR_COMPILE=0
UNITY_TEST_SOURCE_COMPILE=0
```

`PhaseAdapters` has only the two existing optional serialized-audio `CS0649` warnings for `acceptedClip` and `errorClip`; Core, Editor, and test source builds have zero warnings/errors.

Harness distinction: initial direct-compiler attempts used a Roslyn executable without its required host dependency, then mixed net472 Core references with netstandard Unity assemblies. One attempted reference also named a nonexistent standalone SceneManagement module. These were harness/reference-selection failures, not product compilation errors. The corrected coherent-reference commands above all pass; no PackageCache investigation was performed.

The 24 executed pure-C# tests cover all 31 `TaskVariant` routes, snapshot-required activation, playback/replay availability, snapshot drift, all six happy paths, unplanned targets, phase-1 backspace/full reset, phase-2 mismatch/malformed-plan defense, phase-4 full reset/planned-key release, phase-5 1/2/3-target subsets plus wrong/repeated reset to key-only progress, phase-6 ordered/full reset behavior, future/completed locks, snapshot-validated non-chainable GiveUp, phase-4 fallback, Abort/new-Run local reset, disable/re-enable, non-duplicated/self-locked completion, presentation snapshot retention, and original-input observability for Pair secondary target, Digit value, Backspace/Submit kind, gate failure, and null GiveUp metadata.

Fifteen Unity-facing NUnit test methods were source-compiled successfully but were not executed without Unity. They cover the adapter API and clean-checkout setup/validation path, exact-path decoy/wrong-hierarchy rejection, transactional rollback, W1 snapshot authority, relay hand-root filtering, placement filtering/deduplication, keypad physical relays, hint lifecycle, phase-4 GiveUp presentation, phase-5 visual reset, disabled presentation rehydration, disabled target/keypad direct-entry rejection, and disabled `Configure` remaining unsubscribed/closed across adapter availability changes until re-enable.

Static audit result:

```text
W7_CS_COUNT=21
PHASE_ADAPTER_CS_COUNT=17
MISSING_META_COUNT=0
ASSET_GUID_RECORD_COUNT=470
DUPLICATE_GUID_GROUPS=0
CHEST_EXACT_PATH_COUNT=1
FINAL_EXACT_PATH_COUNT=1
PRESENTATION_BINDING_FUZZY_FALLBACKS=0
EXACT_PATH_VALIDATOR_CALLS=3
UNDO_RECORD_OBJECT_CALLS=1
UNDO_ROLLBACK_CALLS=1
PREFLIGHT_BEFORE_UNDO=True
PRESENTATION_REBUILD_FROM_AUTHORITY=4
TARGET_AND_KEYPAD_DISABLE_COLLIDER_CHECKS=4/4
TARGET_AND_KEYPAD_DIRECT_ENTRY_GUARDS=4/4
TARGET_AND_KEYPAD_ACTIVE_CONFIGURE_GUARDS=4/4
INPUT_METADATA_FIELDS=4
RESULT_PRODUCED_EVENT_DECLARATIONS=1
PUBLISH_INPUT_CALLS=8
CORE_ASMDEF_AUTO_REFERENCED=True
CORE_ASMDEF_NO_ENGINE_REFERENCES=True
CORE_ASMDEF_REFERENCE_COUNT=0
PHASE_ADAPTER_ASMDEF_COUNT=0
CORE_FORBIDDEN_REFERENCE_MATCHES=0
W7_FORBIDDEN_SCOPE_REFERENCE_MATCHES=0
CORE_TEST_METHODS=24
UNITY_FACING_TEST_METHODS=15
INTERACTION_SCENE_BYTES=408990
INTERACTION_SCENE_SHA256=A394EABE4D72739C625BA5A261F36DF86A87B15C201CCB02F53A134B1DF027B5
ROADMAP_EXISTS=False
```

The two legitimate semantic `"left"` uses remaining in setup are exclusively for left bare-hand interactor-root discovery; neither chest/final presentation binding contains a fuzzy-name fallback.

## 4. Generated files or local prerequisites intentionally not versioned

- Roslyn outputs and copied NUnit/Unity reference assemblies exist only below the `%LOCALAPPDATA%\Temp\signvr-w7-*` directories named above and are not repository content.
- The installed Unity 6000.5.6f1 managed assemblies were used only as references for static compilation; Unity itself was not launched.
- The requested roadmap file `meetings/手语交互系统_后续开发任务路线图.md` is absent from this worktree (`ROADMAP_EXISTS=False`). The other required context/contracts and W1/W4 reports were read.
- Imported model hierarchies depend on hydrated LFS payloads in the authoritative workspace; this worker did not generate or version imported artifacts.

## 5. Contract deviations, integration assumptions, risks, and debt

- No frozen interaction-contract schema was changed. W7 exposes typed results/events only. `ValidationResult.PhaseCompleted` means “task completion requested”; it is not a W1 phase transition or Run completion. There is no W7 `RunCompleted` symbol.
- The integrating controller must supply snapshots produced by the same immutable plan configured into W7. W7 verifies snapshot phase/lifecycle availability but does not duplicate W1's full plan identity.
- GiveUp is accepted only with the currently synchronized snapshot and `GiveUpAvailable=true`; W7 then self-locks. W1 must perform the actual GiveUp transition and provide the next snapshot.
- W8 must subscribe only to the existing `ResultProduced` event and read `InputKind`, `InputTargetId`, `InputSecondaryTargetId`, and `InputDigitValue` for detail JSON. It must not reinterpret feedback `TargetId` as the attempted input and must not add parallel subscriptions for the same interaction.
- Bare-hand root discovery uses semantically named left/right hand-interactor hierarchies and excludes controller names. Existing serialized roots can be reused, but the validator fails unless exactly two valid left/right hand-only roots are present.
- The physical relay accepts a collider only when its transform is the configured root or a descendant. Actual Meta hand-collider ancestry, trigger timing, and multi-collider enter/exit order remain PlayMode/device risks.
- Deterministic visual feedback is implemented; accepted/error audio clips are optional serialized references, which accounts for the two benign `CS0649` warnings.
- No setup was applied to the saved `InteractionLab.unity` in this worktree, so authoritative scene wiring remains an Orchestrator integration step.
- Recorded follow-up debt, intentionally not refactored in this targeted review: `W7InteractionPhaseAdaptersSetup.cs` is too large and should later be split by preflight/setup/validation responsibility; the digit/backspace/submit keypad bindings repeat availability/subscription code and should later share an internal helper after behavior is stable.

## 6. Recommended Orchestrator review steps

1. Review/selectively integrate the listed files, hydrate LFS assets, and retain the two exact chest-lid/final-door path constants. No W7 scene YAML is expected from this worker.
2. In the authoritative Unity 6000 editor, run W7 Setup on `InteractionLab`, then W7 Validate. Confirm the caller's prior scene setup and unsaved-scene prompt behavior are preserved. If automatic hand discovery cannot resolve the project hierarchy, explicitly assign the coordinator's two allowed roots to the left/right hand-only interactor roots and rerun validation; do not weaken the validator.
3. Wire lifecycle in this order: `Configure(machine.Plan)`; enable the coordinator; call `Synchronize(machine.CurrentPhase)` on Run start and every FirstPlayback/Active/ReplayPlayback/phase transition; on W7 task completion call W1 `CompletePhase`, then synchronize W7 with W1's new snapshot. For GiveUp, pass the current W1 snapshot to W7, invoke W1 `GiveUpPhase`, then synchronize the resulting snapshot. Abort/reset W1 separately and call W7's local `Abort`/`Reset`; configure a new immutable plan for a new Run.
4. For W8, attach one handler to `ResultProduced` and serialize the four nullable input fields into the detail JSON; do not add an alternate interaction event stream.
5. Run the 24 Core EditMode tests and 15 W7 Unity-facing EditMode tests after setup, followed by PlayMode checks for disable/configure/adapter-toggle/re-enable, exact moving-part paths, trigger filtering, multi-collider placement, hint visibility, deterministic fallback animations, and rollback behavior.
6. On Quest, verify both naked-hand collider roots, all `0`–`9`/`*`/`#` trigger proxies, coin placement deduplication, phase-4 GiveUp key release, phase-5 visual reset, presentation rehydration, and final-door animation. Record the build identity; no device validation is claimed here.
