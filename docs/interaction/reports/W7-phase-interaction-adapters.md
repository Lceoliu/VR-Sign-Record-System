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
- Made hint visibility authoritative and reconstructable. `InteractionTaskPresentationSnapshot` now carries `SafePasswordVisible` and `ChestOrderVisible`; the Core session updates them for reveal, full reset, completion, GiveUp, Abort, and new-Run configuration. `InteractionPlanHintPresenter` always rebuilds from that snapshot after enable/configure/recreation, so a revealed password or phase-3/GiveUp chest order survives presenter loss, while phase-1 and phase-4 terminal/reset states cannot resurrect it. Phase-4 GiveUp still deterministically opens the chest and releases only the planned key.
- Made disable lifecycle explicit. Target, all three keypad bindings, and placement close their colliders and reject direct/UnityEvent seams while disabled. Calling `Configure` while disabled neither subscribes to adapter availability nor reopens colliders/interaction behaviours; later adapter availability changes remain inert until runtime `OnEnable` subscribes and restores the W1-gated availability. Every coordinator/adapter/binding/presenter CLR subscription now additionally requires `Application.isPlaying && isActiveAndEnabled`, so Editor setup is serialized configuration only. Presenters unsubscribe and cannot start coroutines while disabled. Deterministic presentation rebuilds on enable from the current W1 lifecycle snapshot plus W7 task-presentation snapshot, without creating a second lifecycle authority.
- Standardized `Configure` as validate-then-swap across target, digit, backspace, submit, placement, hint, feedback, deterministic presentation, and its hinge/target-state/key bindings. Every explicit throwing argument is validated before unsubscription, output mutation, or authored-state release. A failed Configure preserves publisher A, its single subscription, visible/collider state, and teardown restoration capability. A legal A-to-B Configure restores old colliders/behaviours and placement trigger flags, hides old hint output, ends old feedback at idle, restores old deterministic hinges/target states/keys to authored state, then captures B and reapplies authority. Destroy releases the currently owned B output.
- Closed the feedback-audio ownership gap. `ResetFeedback`, legal A-to-B transfer, disable, Run reset/configure, and destroy stop the currently owned `AudioSource`; Configure validates before stopping A and initializes B without stopping audio that B already owns. A PlayMode counterexample uses a runtime-created two-second `AudioClip` and real `AudioSource` instances to assert invalid Configure leaves A playing/subscribed, legal transfer stops only A, and disable/destroy stop the current B source.
- Extended every `PlannedKeyReleaseBinding` authored snapshot with root local position/rotation and null-aligned per-Rigidbody local position/rotation plus linear/angular velocity. Lock/reset clears velocity only while a body is already dynamic and then locks it; an already-kinematic body is never made dynamic merely to clear velocity. This avoids the Unity 6 PhysX error caused by briefly making a legal kinematic/non-convex actor dynamic. Restoration returns authored-dynamic bodies to their recorded velocities after unlocking; authored-kinematic bodies remain locked and receive no invalid velocity write. The PlayMode regression now includes a real kinematic Rigidbody with a non-convex `MeshCollider`, uses `LogAssert.NoUnexpectedReceived`, and keeps the existing dynamic/kinematic/null-aligned pose-and-motion restoration coverage.
- Fixed destroyed-publisher recovery in `InteractionPhaseAdapter`: `Unsubscribe` now clears local `resultSubscribed` ownership even when Unity's overloaded-null reports the destroyed coordinator as null, while still detaching normally from a live coordinator. The PlayMode counterexample subscribes to A, destroys A, configures B, and observes B's public `InputAccepted` chain exactly once before and after a second reconfigure. A read-only audit found the other Unity-publisher subscribers already clear their local flags outside publisher-null guards; the coordinator's remaining session subscription is pure C# and is not the same defect.
- Closed the exact-path review: chest lid and final door use only `Collada visual scene group/ChestUpper_low` and `ce5f462b0dd34333a6588509140a7fb8.fbx/RootNode/Door`. Setup preflight requires both exact paths; creation has no name-token fallback; validation requires the configured `MovingPart` reference and its full relative hierarchy path to equal the frozen constant. Decoy `lid`/`left` hierarchy tests are included.
- Closed the clean-checkout validator and dirty-scene isolation review. Each test creates a uniquely tokened temporary asset folder, copies the on-disk InteractionLab asset, opens only that copy `Additive`, and adds an exact test-ownership marker. Test-only setup/validation/strip/count/find APIs fail closed unless both the 32-hex path token and exactly one marker are present. Five negative cases cover a valid 32-hex path with no marker, duplicate markers, a malformed token, a malformed prefix, and a malformed scene suffix. The fixture strips W7 wiring from the copy, seeds exactly one production-compatible `TestLeftHandInteractors` and `TestRightHandInteractors` transform when absent, proves validation fails from zero W7 wiring, proves unsaved setup succeeds, removes `W7SafeSubmit` and proves validation fails, and never saves the copy. The outer clean and dirty sentinel fixtures now also copy the canonical scene to unique temporary assets and open those copies `Additive`; the clean fixture saves only its temporary copy after adding its hierarchy, while the dirty fixture mutates its already-saved copy in memory and calls `MarkSceneDirty`. No `EditorSceneManager.NewScene` or runtime `SceneManager.CreateScene` call remains, so an untitled dirty Test Runner scene cannot block fixture construction. A loaded/dirty canonical InteractionLab and an unrelated dirty unsaved scene both retain their in-memory sentinel, value, loaded/dirty state, and active-scene context. Active-scene restoration is attempted only when the captured scene remains valid and loaded, and is skipped if already active. No test uses `Single`, `Assert.Ignore`, or `Assert.Explicit`. Canonical InteractionLab bytes/SHA-256 are checked after every fixture even if close or temporary-asset deletion fails.
- Closed the ownership/teardown review. Strip/count recognizes only exact generated roots/IDs or explicit `SignVR.Interaction.PhaseAdapters` components; arbitrary objects such as `W7Notes` and `W7UserContent` survive. Component-free generated IDs still make count nonzero. There is no Player/runtime teardown seam. Under `UNITY_EDITOR` only, nine external-state-owning W7 components implement an explicit `IInteractionOwnedStateTeardown` contract. Strip invokes it before `DestroyImmediate`, disables the component fail-closed, and releases target pose/scale, Rigidbody kinematic/gravity state, behaviours, colliders/trigger flags, hint/feedback outputs, and deterministic/key presentation state. `InteractionTriggerRelay.Configure` is now the sole owner of relay Collider `isTrigger` mutation: setup records the Collider for Undo but does not pre-write it, so Configure captures the real authored false value before enabling the trigger. Repeated Configure retains the first authored snapshot and repeated teardown remains idempotent. The sole remaining setup `isTrigger=true` write belongs to `InteractionPlacementBinding`, not a relay. The Player IL contains neither the Editor teardown interface nor any implementation method. Normal private `OnDestroy` restoration remains as an idempotent runtime safety path.
- Closed the editor mutation-safety review: setup performs a complete read-only preflight before its Undo group, records existing-object changes through `Undo.RecordObject`, records created objects/components, validates before commit, and calls `Undo.RevertAllDownToGroup` on failure. One test poisons post-preflight validation and verifies rollback of an existing label. A second rollback test starts from an old adapter reference, forces a mid-setup validation failure, verifies Undo restores that reference, and publishes adapter A/B availability through public behavior while checking the `UNITY_INCLUDE_TESTS` invocation diagnostic and collider state. It has no dependency on private subscription flags or compiler-generated event backing fields.
- Added a real PlayMode Unity Test Runner assembly with eleven `[UnityTest]` cases. They exercise the actual `Application.isPlaying` CLR chain for Coordinator, Adapter, Target/Digit/Backspace/Submit/Placement availability/reset subscribers, and Hint/Feedback/Deterministic-presentation RunConfigured/RunReset/ResultProduced subscribers across active configure, disable, disabled configure, re-enable, repeated reconfigure, live and destroyed publisher A-to-B replacement, invalid Configure rollback, distinct output A/B ownership, audio ownership, planned-key full authored restoration, destroy cleanup, and authority reconstruction. The added runtime lifecycle case advances authority while presentation is disabled, then proves re-enable reconstructs the completed chest lid, four buttons, and released key while a terminal snapshot keeps Coordinator/Adapter/Target input closed. Coordinator/Adapter use public event counters. The eight binding/presenter subscribers expose small read-only total and per-event counters only under `UNITY_INCLUDE_TESTS`; direct player compilation proves that diagnostic type/API is compiled out of Study Player code. Publisher A is asserted inert and publisher B exact-once for each subscribed event, including Target/Placement `ResetPerformed` and all three presenter event types.
- Reconciled non-play direct seams without creating Editor CLR subscriptions. `InteractionPhaseCoordinator.AcceptInput` and `GiveUpCurrentPhase` now explicitly refresh adapter availability only when the synchronous runtime session subscription is absent; completed/GiveUp task locks therefore close the adapter in EditMode/direct automation as well as PlayMode. EditMode no longer treats property toggles on non-`ExecuteAlways` MonoBehaviours as proof of runtime `OnEnable`; it verifies fail-closed Configure, no Editor subscriptions, terminal-snapshot gating, and explicit authority reconstruction, while PlayMode owns automatic lifecycle assertions.
- Closed the final direct-fallback sensitivity gap without shipping an Editor/test seam. Completion and GiveUp EditMode counterexamples subscribe only to the coordinator's public `ResultProduced` event and assert zero forwarded calls, so removing the `Application.isPlaying` session-subscription guard is observable. In the same cases the coordinator, current adapter, target binding, and configured collider close synchronously; repeated `Coordinator.Enable`, `Adapter.Enable`, and binding disable/re-enable cannot bypass the current task/GiveUp lock. Only `UNITY_EDITOR || UNITY_INCLUDE_TESTS` builds compile the matching-scene `Resources.FindObjectsOfTypeAll` refresh, its coordinator helper, and `InteractionTargetBinding.RefreshAvailabilityWithoutRuntimeSubscription`; the formal Player assembly contains none of those methods or the global-scan call. Normal PlayMode/runtime continues to use the existing single subscribed result chain.
- Made `InteractionPhaseAdapter.Enable` fail closed against configured authority, including Unity fake-null. A configured adapter can become available only when its coordinator is still a live Unity object, has a W1 snapshot selecting that exact phase, and has enabled authority; no snapshot, a future/non-current phase, completion/GiveUp/terminal lock, disabled coordinator, or destroyed coordinator all remain closed after a manual `Enable`. Only a true never-configured CLR null, detected with `object.ReferenceEquals`, receives the intentionally standalone exception. The destroyed-publisher PlayMode counterexample now observes adapter/binding/collider closure and rejected input between destroying A and configuring B, then retains B recovery and exact-once assertions.
- Isolated the EditMode adapter-authority counterexample from the caller's scene. It now runs inside the existing test-owned additive InteractionLab-copy fixture, which makes the temporary scene active before either test object is created, explicitly destroys the test root, independently closes/deletes the temporary resources, restores the prior active scene, and verifies the canonical asset hash. The existing clean saved previous-active-scene regression exercises the same fixture and asserts loaded/active/clean state, root count, sentinel values, and hierarchy are unchanged.
- Closed the additive-scene pollution risk. Both temporary-copy fixtures make the opened copy active and verify activation before any marker, hand-root, setup, or test-owned `GameObject` creation. Test-only mutation APIs fail closed unless their target scene is active; the ownership marker is born directly in that scene and is destroyed on an injected post-create failure, removing the create-then-move window. A saved, loaded, active, clean test-owned source scene now survives successful setup, an expected primary fixture failure, a guard rejection, and injected creation failure with its active/loaded/clean state, root count, sentinel values, and parent hierarchy unchanged. Canonical loaded/dirty and unrelated dirty-unsaved regressions remain.
- Incorporated the Orchestrator's Unity 6000.5.6f1 evidence. All eleven PlayMode leaves passed individually, including fake-null recovery, disabled/configure ownership, per-event A-to-B detachment, audio, hint, planned-key restoration, terminal snapshots, and the real runtime subscription chain. After the preceding product/fixture fixes, the latest root-side EditMode rerun produced 21 pass / 3 fail. The remaining RED leaves were the two outer sentinel fixtures blocked by `NewScene(Additive)` in the Test Runner's untitled dirty scene and the strip test's NUnit `Has.Count` constraint on an `Array`. This follow-up removes that construction path and uses `Has.Length`; the three changed leaves still require root-side Unity rerun, and this worker claims only post-fix static green.
- Made both `InteractionPlacementBinding.AcceptPlacement` overloads fully fail closed. A null coin binding, unconfigured adapter, disabled component, disabled adapter, disallowed coin, or null downstream result returns null without throwing or changing the coin parent, transform, kinematic/gravity flags, or velocities. PlayMode source tests invoke both overloads while unconfigured and while the component/adapter are independently disabled.
- Added the W8 observability seam without adding an event or lifecycle behavior. Every `AcceptInput` result, including a gate failure, exposes nullable `InputKind`, `InputTargetId`, `InputSecondaryTargetId`, and `InputDigitValue` copied from the original `PhaseInput`. `TargetId` remains the independent feedback target. GiveUp/non-input results leave all four fields null, and W8 can continue to subscribe exactly once to `ResultProduced`.

Deliberately not completed:

- Did not edit or save `InteractionLab.unity`, any other scene YAML, W4 `InteractionLabSceneTool`, Editor build settings, OpenXR/URP settings, Host, recording/content staging, W5, or W6 code.
- Did not add JSONL output, a replacement Run controller, or any reference from Core rules to Unity, `MonoBehaviour`, scene objects, or `RecordingCoordinator`.
- Did not modify the Core asmdef or create a shared runtime asmdef/directory.
- Did not start Unity, run Unity EditMode/PlayMode tests, build Android, or test on Quest in this isolated worktree.

## 2. Files created or changed

This final fixture follow-up changed exactly these existing files (no `.meta` or
scene asset content changed):

- `signvr_unity/Assets/Tests/EditMode/Interaction/PhaseAdapters/W7InteractionPhaseAdaptersTests.cs`
- `docs/interaction/reports/W7-phase-interaction-adapters.md`

The complete W7 file set remains:

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
- `InteractionSubscriptionDiagnostic.cs`
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
- `signvr_unity/Assets/Tests/PlayMode.meta`
- `signvr_unity/Assets/Tests/PlayMode/Interaction.meta`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/PhaseAdapters.meta`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/PhaseAdapters/SignVR.Interaction.PhaseAdapters.PlayMode.Tests.asmdef` and `.meta`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/PhaseAdapters/W7InteractionPhaseAdaptersPlayModeTests.cs` and `.meta`
- `docs/interaction/reports/W7-phase-interaction-adapters.md`

## 3. Commands/tests executed and exact results

No Git command and no Unity Editor process was run.

Latest temporary-scene-copy/NUnit-array TDD/static gate:

```text
ROOT_UNITY_VERSION=6000.5.6f1
ROOT_UNITY_EDITMODE_BEFORE_FIX=21 passed, 3 failed, 24 total (real RED)
ROOT_UNITY_PLAYMODE=11 passed, 0 failed, 11 total (real green)
RED_LEAVES=CleanActiveSavedSceneSurvivesSuccessfulAndFailedFixtures; IsolatedSetupPreservesDirtyUnsavedUserScene; StripUsesExactOwnershipAndRestoresAuthoredRuntimeState
RED_NEWSCENE_SEAM_TOKENS=5
RED_ARRAY_COUNT_ASSERTIONS=2
RED_PROCESS_EXIT=1

GREEN_NEWSCENE_TOKENS=0
GREEN_ARRAY_COUNT_ASSERTIONS=0
GREEN_ARRAY_LENGTH_ASSERTIONS=2
GREEN_OUTER_COPY_OPEN_FIXTURES=2
GREEN_OUTER_CANONICAL_CHECKS=2

PURE_PHASE_RULES passed=24 failed=0 total=24
CORE_SDK=0 warnings, 0 errors
PHASE_ADAPTERS_PLAYER_SDK=2 expected serialized-audio CS0649 warnings, 0 errors
PHASE_ADAPTERS_UNITY_INCLUDE_TESTS_SDK=0 warnings, 0 errors
PHASE_ADAPTERS_UNITY_EDITOR_SDK=2 expected serialized-audio CS0649 warnings, 0 errors
PHASE_ADAPTERS_UNITY_EDITOR_AND_TEST_SDK=0 warnings, 0 errors
W7_EDITOR_SDK=0 own-source errors (2 inherited serialized-audio warnings)
W7_EDITOR_WITH_UNITY_TESTS_SDK=0 warnings, 0 errors
UNITY_EDITMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
UNITY_PLAYMODE_TEST_SOURCE_SDK=0 warnings, 0 errors

PLAYER_EDITOR_TEARDOWN_TYPES=0
PLAYER_SUBSCRIPTION_DIAGNOSTIC_TYPES=0
PLAYER_REFRESH_METHODS=0
PLAYER_GLOBAL_SCAN_CALLS=0
PLAYER_RELAY_CONFIGURE_SET_ISTRIGGER_CALLS=1

CORE_TEST_METHODS=24
UNITY_EDITMODE_TEST_METHODS=24
UNITY_PLAYMODE_TEST_METHODS=11
W7_IGNORE_EXPLICIT_ATTRIBUTES=0
EDITOR_NEWSCENE_TOKENS=0
RELEVANT_UNITY_ASSET_COUNT=24
W7_MISSING_META_COUNT=0
ALL_ASSETS_MISSING_META_EXCLUDING_TILDE_PATHS=0
ALL_ASSET_GUID_RECORD_COUNT=476
DUPLICATE_GUID_GROUPS=0
LEFTOVER_TEST_TEMP_ASSET_FOLDERS=0
INTERACTION_SCENE_BYTES=408990
INTERACTION_SCENE_SHA256=A394EABE4D72739C625BA5A261F36DF86A87B15C201CCB02F53A134B1DF027B5
NON_GIT_TEXT_FILES_CHECKED=37
NON_GIT_TRAILING_WHITESPACE_MATCHES=0
NON_GIT_MERGE_MARKER_MATCHES=0
```

The fresh static assemblies are under
`%LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417`. The two scene
fixtures copy `InteractionLab.unity` into unique temporary asset folders before
opening them `Additive`; the dirty-user fixture modifies only its temporary
loaded copy and deliberately leaves it dirty in memory. Cleanup independently
closes the copy, restores the previous active scene, deletes the temporary
folder (including its transient `.meta`), and verifies the canonical bytes and
SHA-256 while preserving any primary test failure. The worker did not execute
Unity; the three leaves above remain a root-side rerun gate.

Immediately preceding relay-ownership TDD/static gate:

```text
RED_TEST_FALSE_BASELINE=True
RED_SETUP_RELAY_PREWRITE_COUNT=5
RED_PROCESS_EXIT=1

GREEN_SETUP_RELAY_PREWRITE_COUNT=0
GREEN_PLACEMENT_OWNER_WRITE_COUNT=1
GREEN_TEST_FALSE_BASELINE_COUNT=1
GREEN_TEST_REPEAT_CONFIGURE_COUNT=1
GREEN_TEST_REPEAT_TEARDOWN_COUNT=1

PURE_PHASE_RULES passed=24 failed=0 total=24
CORE_SDK=0 warnings, 0 errors
PHASE_ADAPTERS_PLAYER_SDK=2 expected serialized-audio CS0649 warnings, 0 errors
PHASE_ADAPTERS_UNITY_INCLUDE_TESTS_SDK=0 warnings, 0 errors
PHASE_ADAPTERS_UNITY_EDITOR_SDK=2 expected serialized-audio CS0649 warnings, 0 errors
PHASE_ADAPTERS_UNITY_EDITOR_AND_TEST_SDK=0 warnings, 0 errors
W7_EDITOR_SDK=0 own-source errors (2 inherited serialized-audio warnings)
W7_EDITOR_WITH_UNITY_TESTS_SDK=0 warnings, 0 errors
UNITY_EDITMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
UNITY_PLAYMODE_TEST_SOURCE_SDK=0 warnings, 0 errors

PLAYER_EDITOR_TEARDOWN_TYPES=0
PLAYER_SUBSCRIPTION_DIAGNOSTIC_TYPES=0
PLAYER_REFRESH_METHODS=0
PLAYER_GLOBAL_SCAN_CALLS=0
PLAYER_RELAY_CONFIGURE_SET_ISTRIGGER_CALLS=1
EDITOR_SETUP_DIRECT_SET_ISTRIGGER_CALLS=1 (placement only)

CORE_TEST_METHODS=24
UNITY_EDITMODE_TEST_METHODS=24
UNITY_PLAYMODE_TEST_METHODS=11
```

The fresh build output is
`%LOCALAPPDATA%\Temp\signvr-w7-relay-owner-20260826-074427`. The enhanced
EditMode test removes the setup-created relay, establishes an explicit authored
`isTrigger=false`, runs setup again, repeats public Configure twice, moves and
renames the proxy so exact W7 object ownership cannot hide restoration, then
runs strip twice. Before the product edit the setup's five pre-writes made that
scenario restore true; after deleting only those five writes it restores false.
Unity execution of the enhanced leaf remains a root-side gate.

Earlier root-side Unity evidence and its nine-leaf follow-up:

```text
ROOT_UNITY_VERSION=6000.5.6f1
ROOT_IMPORTED_COMMIT=8b37f25
ROOT_UNITY_EDITMODE_EARLIER=15 passed, 9 failed, 24 total (historical RED)
ROOT_UNITY_PLAYMODE=11 passed, 0 failed, 11 total
ROOT_INTERACTIONLAB_AFTER_PLAYMODE=loaded, active, clean, 34 roots
ROOT_INTERACTIONLAB_DISK_SHA_AFTER_EDITMODE=unchanged

RED_KINEMATIC_NONCONVEX_COUNTEREXAMPLE=True
RED_STOP_AND_LOCK_FORCES_DYNAMIC=True
RED_PROCESS_EXIT=1

GREEN_EDIT_SCENE_HELPER_EDITOR_NEWSCENE=historical intermediate; now removed
GREEN_EDIT_SCENE_HELPER_RUNTIME_CREATESCENE=False
GREEN_POISON_COLLIDER_BEFORE_RELAY=2/2
GREEN_STOP_AND_LOCK_FORCES_DYNAMIC=False
GREEN_EDIT_PRESENTATION_AUTHORITY_BASELINE=True
GREEN_EDITOR_STRIP_TEARDOWN_CALLS=1
GREEN_EDITOR_TEARDOWN_IMPLEMENTERS=9
GREEN_PLAYMODE_LOGASSERT_EXPECT_CALLS=0
GREEN_PLAYMODE_NO_UNEXPECTED_LOG_GATES=4

PURE_PHASE_RULES passed=24 failed=0 total=24
CORE_SDK=0 warnings, 0 errors
PHASE_ADAPTERS_PLAYER_SDK=2 expected serialized-audio CS0649 warnings, 0 errors
PHASE_ADAPTERS_UNITY_INCLUDE_TESTS_SDK=0 warnings, 0 errors
PHASE_ADAPTERS_UNITY_EDITOR_SDK=0 errors (2 expected serialized-audio warnings)
PHASE_ADAPTERS_UNITY_EDITOR_AND_TEST_SDK=0 warnings, 0 errors
W7_EDITOR_SDK=0 own-source errors (2 inherited serialized-audio warnings)
W7_EDITOR_WITH_UNITY_TESTS_SDK=0 warnings, 0 errors
UNITY_EDITMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
UNITY_PLAYMODE_TEST_SOURCE_SDK=0 warnings, 0 errors

PLAYER_EDITOR_TEARDOWN_TYPES=0
PLAYER_EDITOR_TEARDOWN_METHODS=0
PLAYER_SUBSCRIPTION_DIAGNOSTIC_TYPES=0
PLAYER_REFRESH_METHODS=0
PLAYER_GLOBAL_SCAN_CALLS=0
UNITY_TEST_SUBSCRIPTION_DIAGNOSTIC_TYPES=1
UNITY_EDITOR_TEARDOWN_TYPES=1
UNITY_EDITOR_TEARDOWN_METHODS=10 (interface declaration plus 9 implementations)

CORE_TEST_METHODS=24
UNITY_EDITMODE_TEST_METHODS=24
UNITY_PLAYMODE_TEST_METHODS=11
W7_IGNORE_EXPLICIT_ATTRIBUTES=0
RELEVANT_UNITY_ASSET_COUNT=24
W7_MISSING_META_COUNT=0
ALL_ASSET_GUID_RECORD_COUNT=476
DUPLICATE_GUID_GROUPS=0
INTERACTION_SCENE_BYTES=408990
INTERACTION_SCENE_SHA256=A394EABE4D72739C625BA5A261F36DF86A87B15C201CCB02F53A134B1DF027B5
NON_GIT_TEXT_FILES_CHECKED=49
NON_GIT_TRAILING_WHITESPACE_MATCHES=0
NON_GIT_MERGE_MARKER_MATCHES=0
```

The earlier nine distinct failing EditMode leaves were
`CleanActiveSavedSceneSurvivesSuccessfulAndFailedFixtures`,
`IsolatedSetupPreservesDirtyUnsavedUserScene`,
`CleanInteractionLabSetupIsCompleteAndAssetIsUnchanged`,
`FailedSetupRestoresReferencesWithoutEditorSubscriptions`,
`LoadedInteractionLabMemoryStateSurvivesIsolatedSetup`,
`SceneValidatorRejectsWrongMovingPartHierarchy`,
`FailedSetupRollsBackChangesToExistingObjects`,
`ReenabledPresentationRestoresMissedDoorButtonsAndKey`, and
`StripUsesExactOwnershipAndRestoresAuthoredRuntimeState`. The shared causes were
the runtime-only scene API, unsafe kinematic-to-dynamic transition, unstable
RequireComponent poison, missing explicit EditMode presentation baseline, and
reliance on `OnDestroy` for Editor strip. All five causes now have deletion-
sensitive source/test coverage. Root subsequently reran the full EditMode suite:
six of those nine turned green, leaving only
`CleanActiveSavedSceneSurvivesSuccessfulAndFailedFixtures`,
`IsolatedSetupPreservesDirtyUnsavedUserScene`, and
`StripUsesExactOwnershipAndRestoresAuthoredRuntimeState` RED (21/24 overall).
The first two were blocked before their assertions by `NewScene(Additive)` under
the Test Runner's untitled dirty scene. The third reached its final ownership
assertions but applied `Has.Count` to an `Array`. Current source uses temporary
canonical asset copies plus `OpenScene(Additive)` and `Has.Length`; those three
leaves await root rerun.

The earlier static assemblies are under
`%LOCALAPPDATA%\Temp\signvr-w7-root-fix-20260826-072132`. Mono.Cecil 0.11.1
inspected their actual Player/test/Editor method bodies. Player contains neither
the Editor teardown interface/methods nor the prior test diagnostics/global
scan. The Unity 11/11 result above is real root evidence for imported `8b37f25`,
not a claim that this worker ran Unity after the current changes.

The same static matrix was rerun for this final fixture change:

```text
dotnet run --project C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-red2-20260826\Runner.csproj --no-restore
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\Core.csproj --no-restore --no-incremental -p:DefineConstants= -o %LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417\core -v:minimal
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\PhaseAdapters.csproj --no-restore --no-incremental -p:DefineConstants= -o %LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417\player -v:minimal
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\PhaseAdapters.csproj --no-restore --no-incremental -p:DefineConstants=UNITY_INCLUDE_TESTS -o %LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417\test -v:minimal
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\PhaseAdapters.csproj --no-restore --no-incremental -p:DefineConstants=UNITY_EDITOR -o %LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417\editor-runtime -v:minimal
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\W7Editor.csproj --no-restore --no-incremental -p:DefineConstants=UNITY_EDITOR -o %LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417\editor -v:minimal
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\UnityTests.csproj --no-restore --no-incremental -p:DefineConstants= -o %LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417\edit-tests -v:minimal
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\PlayModeTests.csproj --no-restore --no-incremental -p:DefineConstants=UNITY_INCLUDE_TESTS -o %LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417\play-tests -v:minimal
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\PhaseAdapters.csproj --no-restore --no-incremental -p:DefineConstants=UNITY_EDITOR%3BUNITY_INCLUDE_TESTS -o %LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417\editor-test-runtime -v:minimal
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\W7Editor.csproj --no-restore --no-incremental -p:DefineConstants=UNITY_EDITOR%3BUNITY_INCLUDE_TESTS -o %LOCALAPPDATA%\Temp\signvr-w7-fixture-copy-20260826-080417\editor-with-tests -v:minimal
```

Final Player-fallback/adapter-authority P2 TDD slice:

```text
RED_FAIL_OPEN_ADAPTER_ENABLE=True; process exit=1

PURE_PHASE_RULES passed=24 failed=0 total=24
CORE_SDK=0 warnings, 0 errors
PHASE_ADAPTERS_PLAYER_SDK=2 expected serialized-audio CS0649 warnings, 0 errors
PHASE_ADAPTERS_UNITY_INCLUDE_TESTS_SDK=0 warnings, 0 errors
W7_EDITOR_SDK=0 own-source errors (2 inherited serialized-audio warnings)
UNITY_EDITMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
UNITY_PLAYMODE_TEST_SOURCE_SDK_ISOLATED_OUTPUT=0 warnings, 0 errors

PLAYER_COORDINATOR_SCAN_SEAM_PRESENT=False
PLAYER_BINDING_REFRESH_SEAM_PRESENT=False
PLAYER_IL_FIND_OBJECTS_OF_TYPE_ALL_CALLS=0
PLAYER_IL_BINDING_REFRESH_CALLS=0
PLAYER_IL_COORDINATOR_REFRESH_CALLS=0
TEST_COORDINATOR_SCAN_SEAM_PRESENT=True
TEST_BINDING_REFRESH_SEAM_PRESENT=True
NORMAL_RUNTIME_SESSION_SUBSCRIPTION_GATE_UNCHANGED=True
EDITOR_TEST_DIRECT_REFRESH_CALLS=2
EDITOR_TEST_GLOBAL_SCAN_CALLS=1
FAIL_OPEN_NONCURRENT_BRANCHES=0
FAIL_OPEN_NO_SNAPSHOT_BRANCHES=0
COMPLETION_GIVEUP_DELETION_SENSITIVE_TESTS=2
ADAPTER_ENABLE_COUNTEREXAMPLE_TESTS=1

CORE_TEST_METHODS=24
UNITY_EDITMODE_TEST_METHODS=24
UNITY_PLAYMODE_TEST_METHODS=11
IGNORE_EXPLICIT_MATCHES=0
RELEVANT_UNITY_ASSET_COUNT=24
W7_MISSING_META_COUNT=0
ALL_ASSET_GUID_RECORD_COUNT=476
DUPLICATE_GUID_GROUPS=0
ALL_ASSETS_MISSING_META_EXCLUDING_TILDE_PATHS=0
NON_GIT_TEXT_FILES_CHECKED=49
NON_GIT_TRAILING_WHITESPACE_MATCHES=0
NON_GIT_MERGE_MARKER_MATCHES=0
INTERACTION_SCENE_BYTES=408990
INTERACTION_SCENE_SHA256=A394EABE4D72739C625BA5A261F36DF86A87B15C201CCB02F53A134B1DF027B5
GIT_COMMANDS_RUN=0
```

The Player SDK assembly was rebuilt with no `UNITY_EDITOR` or
`UNITY_INCLUDE_TESTS` define, then inspected through reflection and Mono.Cecil
IL operand analysis. The coordinator scan helper, binding refresh seam, and
`FindObjectsOfTypeAll` call are absent. Rebuilding the same runtime sources with
`UNITY_INCLUDE_TESTS` proves both helpers remain available for the existing
completion/GiveUp EditMode counterexamples. The normal runtime
`Application.isPlaying` session-subscription condition and result event path were
not changed.

One initial parallel SDK invocation made two projects share the same temporary
output folder and produced an `MSB3061` XML-copy cleanup warning after the
PlayMode C# compiler had succeeded. A sequential rebuild to
`signvr-w7-review-playmode-isolated-20260826` completed with zero warnings and
zero errors; this was harness output contention, not a product diagnostic.

Latest Orchestrator-supplied Unity RED evidence and worker GREEN source gate:

```text
UNITY_EDITMODE_BEFORE_FIX=8 passed, 13 failed, 21 total
UNITY_CORE_PHASE_RULES=24 passed, 0 failed, 24 total
UNITY_PLAYMODE_BEFORE_FIX=9 passed, 1 failed, 10 total
PLAYMODE_RED_LEAF=ConfigureFailureIsAtomicAndInputOwnershipMovesToB
PLAYMODE_RED_ROW=InteractionDigitBinding
PLAYMODE_RED_ASSERT=colliderA.enabled expected true, actual false
PLAYMODE_RED_ROOT_CAUSE=previous table row left shared publisher A disabled

W7_REGRESSION_SOURCE_AUDIT=GREEN (5/5)
ATOMIC_CONFIGURE_TABLE_PRECONDITION=GREEN
PURE_PHASE_RULES=24 passed, 0 failed, 24 total
UNITY_EDITMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
UNITY_PLAYMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
UNITY_PLAYMODE_TEST_SOURCE_DIRECT=0 warnings, 0 errors
```

The 13 EditMode failures split into five lifecycle-boundary cases and eight
fixture cases. The lifecycle correction preserves the
`Application.isPlaying && isActiveAndEnabled` subscription rule: completed
task/terminal snapshot and disabled-Configure behavior remain directly tested,
while automatic disable/re-enable and missed-presentation reconstruction are
covered in PlayMode. The fixture correction adds exact left/right test hand
roots, uses a runtime-created unsaved sentinel scene, never closes a pre-existing
user scene, and guards active-scene restoration with valid/loaded/already-active
checks. The worker did not execute the post-fix Unity leaves because launching
Unity was prohibited.

Fixture cleanup now keeps the original test exception as the top-level thrown
exception after running every recovery action. Any close/delete/hash recovery
failures are attached under `Exception.Data["W7FixtureCleanupFailures"]`; they
remain auditable without replacing the primary failure.

One direct-Roslyn harness invocation in this turn mixed a Mono/net472 Core
output with Unity's netstandard-2.1 assemblies and therefore emitted `CS0012`
reference errors before product analysis. Rebuilding Core and every dependent
source against one coherent NETStandard.Library.Ref 2.1 reference set produced
zero errors. This was a harness reference-profile mismatch, not a product
compile failure; no PackageCache investigation was performed.

Final worker-side regression after all edits:

```text
PURE_PHASE_RULES passed=24 failed=0 total=24
CORE_SDK=0 warnings, 0 errors
PHASE_ADAPTERS_PLAYER_SDK=2 expected serialized-audio CS0649 warnings, 0 errors
PHASE_ADAPTERS_UNITY_INCLUDE_TESTS_SDK=0 warnings, 0 errors
W7_EDITOR_SDK=0 own-source errors (2 inherited serialized-audio warnings)
UNITY_EDITMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
UNITY_PLAYMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
FINAL_PLAYER_FALLBACK_SOURCE_AUDIT=GREEN
FINAL_FALLBACK_SCENE_SOURCE_AUDIT=GREEN (16/16)
CORE_TEST_METHODS=24
UNITY_EDITMODE_TEST_METHODS=24
UNITY_PLAYMODE_TEST_METHODS=11
IGNORE_EXPLICIT_OCCURRENCES=0
EDITOR_NEWSCENE_TOKEN_OCCURRENCES=0
RELEVANT_ASSET_COUNT=24
MISSING_META_COUNT=0
ALL_ASSET_GUID_RECORD_COUNT=476
DUPLICATE_GUID_GROUPS=0
LEFTOVER_TEST_TEMP_ASSET_FOLDERS=0
INTERACTION_LAB_BYTES=408990
INTERACTION_LAB_SHA256=A394EABE4D72739C625BA5A261F36DF86A87B15C201CCB02F53A134B1DF027B5
NON_GIT_TEXT_FILES_CHECKED=49
TRAILING_WHITESPACE_LINES=0
CONFLICT_MARKER_LINES=0
GIT_COMMANDS_RUN=0
```

Final direct-fallback/additive-scene TDD slice (Unity execution remained
prohibited, so RED/GREEN used mutation-sensitive source semantics plus both
independent compiler paths):

```text
RED: exit 1
RED_ADAPTER_ENABLE_IGNORES_CURRENT_TASK_AUTHORITY=True
RED_TEST_MARKER_REQUIRES_ACTIVE_SCENE=False
RED_TEST_MARKER_CREATE_THEN_MOVE_WINDOW=True
RED_DIRECT_FALLBACK_CLOSES_TARGET_COLLIDER=False

GREEN: exit 0
PUBLIC_RESULT_ZERO_COMPLETION=True
PUBLIC_RESULT_ZERO_GIVEUP=True
COMPLETION_GIVEUP_ADAPTER_BINDING_COLLIDER_LOCK=True
COORDINATOR_ADAPTER_BINDING_REENABLE_STAYS_LOCKED=True
TEMP_COPY_ACTIVE_BEFORE_MARKER=True
TEMP_COPY_ACTIVE_BEFORE_SETUP=True
EDITOR_MARKER_ACTIVE_FAIL_CLOSED=True
INACTIVE_TEST_MARKER_GUARD_NEGATIVE=True
EDITOR_MARKER_BORN_IN_TARGET=True
CLEAN_SAVED_SOURCE_SUCCESS_FAILURE_REGRESSION=True
CREATE_FAILURE_CLEANUP=True
```

The public event count is intentionally zero in EditMode because only
`HandleSessionResult` forwards `ResultProduced`, and the session subscription is
runtime-only. Deleting the explicit completion/GiveUp fallback leaves the
adapter/collider open; deleting the `Application.isPlaying` guard produces a
nonzero public result count; deleting the adapter authority gate lets direct
`Adapter.Enable` reopen the binding. These are independent observable failures,
not private-field checks.

Latest kinematic-reset/destroyed-publisher TDD slice (PlayMode counterexamples
were written first; source-semantic RED/GREEN was executable without violating
the Unity launch prohibition):

```text
RED: exit 1
RED_COUNTEREXAMPLES=2/2
RED_KINEMATIC_FIXTURE=True
RED_LOCKED_VELOCITY_REFERENCES=8
RED_NO_UNEXPECTED_LOG_GATES=3
RED_KINEMATIC_BEFORE_CLEAR=True
RED_UNCONDITIONAL_AUTHORED_VELOCITY_WRITE=True
RED_DESTROYED_PUBLISHER_FLAG_CLEAR_OUTSIDE_GUARD=False

GREEN: exit 0
GREEN_STOP_AND_LOCK_DYNAMIC_WRITE_ORDER=True
GREEN_SET_RELEASED_USES_STOP_AND_LOCK=True
GREEN_AUTHORED_KINEMATIC_SKIPS_VELOCITY_WRITE=True
GREEN_DESTROYED_PUBLISHER_FLAG_CLEAR_OUTSIDE_GUARD=True
```

`PlannedKeyReleaseRestoresAuthoredPoseAndMotion` now covers dynamic and
kinematic bodies plus zero-speed/log behavior. The new tenth PlayMode case is
`AdapterRecoversAfterDestroyedCoordinatorReplacement`. Neither was executed
without Unity; both source-compiled successfully.

Latest audio/key/per-event TDD slice (PlayMode counterexamples were authored
first; the executable RED/GREEN gate was a source-semantic audit because this
worker was explicitly forbidden to launch Unity):

```text
RED: exit 1
RED_PLAYMODE_COUNTEREXAMPLES=3/3
RED_FEEDBACK_AUDIO_STOP_CALLS=0
RED_PLANNED_KEY_POSE_VELOCITY_FIELD_HITS=0
RED_NAMED_EVENT_COUNTER_HITS=0

GREEN: exit 0
GREEN_FEEDBACK_AUDIO_STOP_CALLS=1
GREEN_CONFIGURE_STOPS_NEW_B=False
GREEN_KEY_SNAPSHOT_FIELDS=6/6
GREEN_NAMED_EVENT_COUNTERS=5/5
GREEN_EVENT_DETACH_GROUPS=5/5
```

The three new PlayMode source cases are
`FeedbackPresenterStopsOnlyItsOwnedAudioOutput`,
`PlannedKeyReleaseRestoresAuthoredPoseAndMotion`, and
`EachSubscribedEventMovesFromPublisherAToBExactlyOnce`. They compile against
real Unity `AudioSource`/`AudioClip`, `Transform`, and `Rigidbody` APIs; actual
Unity Test Runner execution remains an Orchestrator gate.

Final hint-authority TDD slice:

```text
dotnet run --project C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-red2-20260826\Runner.csproj --configuration Release
RED: exit 1; 9 CS1061 errors for missing SafePasswordVisible/ChestOrderVisible snapshot properties.
GREEN: PURE_PHASE_RULES passed=24 failed=0 total=24
```

The PlayMode counterexamples for disable/enable reconstruction, presenter
recreation, invalid Configure rollback, distinct A/B output ownership, destroy
cleanup, and unconfigured Placement were added before/with the minimal runtime
changes. They were source-compiled only because this worker was explicitly
forbidden to launch Unity; no Unity Test Runner pass is claimed.

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

Earlier third-round P2 RED/GREEN audit (its temporary public teardown seam was
subsequently removed by the final audit below):

```text
RED_PLAYMODE_ASMDEF_EXISTS=False
RED_PLAYMODE_TEST_EXISTS=False
RED_PLAYMODE_UNITYTEST_COUNT=0
RED_EDIT_FIXTURE_SINGLE_COUNT=2
RED_EDIT_FIXTURE_RESTORE_SCENE_SETUP_COUNT=1
RED_FUZZY_W7_PREFIX_OWNERSHIP_COUNT=1
RED_TEARDOWN_RESTORE_SEAM_COUNT=0

GREEN_PLAYMODE_UNITYTEST_COUNT=3
GREEN_EDIT_FIXTURE_ADDITIVE_COUNT=2
GREEN_EDIT_FIXTURE_SINGLE_COUNT=0
GREEN_EDIT_FIXTURE_RESTORE_SCENE_SETUP_COUNT=0
GREEN_DIRTY_SCENE_REGRESSION_COUNT=1
GREEN_FUZZY_W7_PREFIX_OWNERSHIP_COUNT=0
GREEN_TEARDOWN_RESTORE_SEAM_COUNT=1
GREEN_USER_PREFIX_SURVIVAL_ASSERTS=True
GREEN_COMPONENT_FREE_GENERATED_STATE_COUNT_ASSERT=True
```

Final pre-integration Placement/subscription/isolation/teardown TDD audit:

```text
RED_PLACEMENT_NULL_RESULT_GUARD=False
RED_PLACEMENT_NULL_BINDING_FAIL_CLOSED=False
RED_SUBSCRIPTION_DIAGNOSTIC_TYPE_EXISTS=False
RED_PUBLIC_RUNTIME_TEARDOWN_API=True
RED_LOADED_INTERACTIONLAB_ASSERT_IGNORE_COUNT=1
RED_OUTER_FIXTURES_WITH_SEQUENTIAL_CLOSE_THEN_RESTORE=2

GREEN_PLACEMENT_NULL_RESULT_GUARD=True
GREEN_PLACEMENT_BOTH_DISABLED_OVERLOADS_AND_UNCHANGED_PHYSICS=True
GREEN_TEST_ONLY_SUBSCRIPTION_DIAGNOSTICS=8
GREEN_PUBLISHER_A_TO_B_REPLACEMENT_COVERAGE=8 subscribers plus Adapter
GREEN_PUBLIC_RUNTIME_TEARDOWN_API=0
GREEN_PRIVATE_ONDESTROY_RESTORE_PATH=1
GREEN_LOADED_INTERACTIONLAB_ASSERT_IGNORE_COUNT=0
GREEN_TEMP_ASSET_COPY_AND_EXACT_MARKER=True
GREEN_INDEPENDENT_FIXTURE_CLEANUP_CALLS=4
GREEN_CLEANUP_FAILURE_BEHAVIOR_TEST=1
```

SDK static projects were rebuilt with these commands:

```text
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\Core.csproj --configuration Release --no-restore --nologo -t:Rebuild
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\PhaseAdapters.csproj --configuration Release --no-restore --nologo -t:Rebuild -p:DefineConstants= -o C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-player-fakenull-final-20260826
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\PhaseAdapters.csproj --configuration Release --no-restore --nologo -t:Rebuild -p:DefineConstants=UNITY_INCLUDE_TESTS -o C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-runtime-tests-fakenull-final-20260826
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\W7Editor.csproj --configuration Release --no-restore --nologo -t:Rebuild -o C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-editor-fakenull-final-20260826
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\UnityTests.csproj --configuration Release --no-restore --nologo -t:Rebuild -o C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-edit-tests-scene-final-20260826
dotnet build C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-green4-20260826\PlayModeTests.csproj --configuration Release --no-restore --nologo -t:Rebuild -o C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-play-tests-fakenull-final2-20260826

CORE_SDK=0 warnings, 0 errors
PHASE_ADAPTERS_PLAYER_SDK=2 expected serialized-audio CS0649 warnings, 0 errors
PHASE_ADAPTERS_UNITY_INCLUDE_TESTS_SDK=0 warnings, 0 errors
W7_EDITOR_SDK=0 product-source errors (2 serialized-audio dependency warnings)
UNITY_EDITMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
UNITY_PLAYMODE_TEST_SOURCE_SDK=0 warnings, 0 errors
```

The immediately preceding full-scope gate also used Unity 6000.5.6f1's bundled `MonoBleedingEdge\bin\mono.exe` to host its Roslyn `csc.exe`, with `/nostdlib+ /langversion:9.0` and coherent Unity netstandard 2.1 references for Core/runtime/editor and Unity-facing source. Those prior direct outputs remain in `C:\Users\woshica\AppData\Local\Temp\signvr-w7-review-direct6-20260826` and are recorded for continuity, but the final conditional/fake-null/fixture edits are claimed against the fresh SDK builds and Mono.Cecil Player IL audit above, not against these older binaries:

```text
CORE_NET472_DIRECT=0
CORE_NETSTANDARD21_DIRECT=0
CORE_TEST_DIRECT_FINAL=0
PURE_PHASE_RULES passed=24 failed=0 total=24
PHASE_ADAPTERS_PLAYER_DIRECT=0 errors (2 expected serialized-audio CS0649 warnings)
PHASE_ADAPTERS_UNITY_INCLUDE_TESTS_DIRECT=0 warnings, 0 errors
W7_EDITOR_DIRECT=0
UNITY_EDITMODE_TEST_SOURCE_DIRECT=0
UNITY_PLAYMODE_TEST_SOURCE_DIRECT_FINAL=0
```

The Player-only `PhaseAdapters` build has the two expected serialized-audio `CS0649` warnings for `acceptedClip` and `errorClip`; Core, the `UNITY_INCLUDE_TESTS` runtime variant, Editor product source, and test source builds have zero own-source warnings/errors.

Harness distinction: historical direct invocations included invalid `UnityEngine\+\*.dll` reference construction and an overlong net472 reference list. In this final pass, one preliminary PhaseAdapters command used Mono's default profile against Unity netstandard 2.1 assemblies and therefore produced only `CS0012` reference-profile errors before coherent product analysis; the corrected `/nostdlib+` NETStandard.Library.Ref command compiled cleanly. The Mono host also printed one `abort_threads` shutdown diagnostic after the EditMode test compiler had returned success; the compiler exit remained 0 with no C# diagnostic. These were harness/profile messages, not product compilation errors; no PackageCache investigation was performed.

The 24 executed pure-C# tests cover all 31 `TaskVariant` routes, snapshot-required activation, playback/replay availability, snapshot drift, all six happy paths, unplanned targets, phase-1 backspace/full reset, phase-2 mismatch/malformed-plan defense, phase-4 full reset/planned-key release, phase-5 1/2/3-target subsets plus wrong/repeated reset to key-only progress, phase-6 ordered/full reset behavior, future/completed locks, snapshot-validated non-chainable GiveUp, phase-4 fallback, Abort/new-Run local reset, disable/re-enable, non-duplicated/self-locked completion, presentation snapshot retention, and original-input observability for Pair secondary target, Digit value, Backspace/Submit kind, gate failure, and null GiveUp metadata.

Twenty-four Unity-facing EditMode NUnit methods and eleven PlayMode `[UnityTest]` methods source-compiled successfully. The EditMode suite additionally covers temporary-copy setup from zero W7 wiring with exact left/right test hand roots, the five strict marker/token/path negative cases, canonical loaded/dirty InteractionLab preservation, dirty in-memory and clean saved temporary-copy preservation, clean saved previous-active-scene preservation across success/failure/guard paths, injected creation cleanup, zero public result forwarding in non-play completion/GiveUp, configured-adapter fail-closed behavior before a W1 snapshot/for a non-current phase/under disabled authority, the standalone-adapter exception, independent cleanup after a simulated close failure, exact generated ownership, component-free generated-state counting, and authored Behaviour/Collider/Rigidbody/pose restoration through the Editor-only pre-destroy contract. The exact PlayMode methods are `RuntimeSubscriptionChainDeliversOnceAcrossLifecycle`, `AvailabilitySubscribersStayClosedWhileDisabled`, `ConfigureFailureIsAtomicAndInputOwnershipMovesToB`, `ResultPresentersUseRealPlayModeSubscriptions`, `RuntimeReenableHonorsTerminalSnapshotAndRebuildsChest`, `HintPresenterRebuildsAuthorityAcrossLifecycle`, `PresenterConfigureIsAtomicAndTransfersOutputOwnership`, `FeedbackPresenterStopsOnlyItsOwnedAudioOutput`, `PlannedKeyReleaseRestoresAuthoredPoseAndMotion`, `AdapterRecoversAfterDestroyedCoordinatorReplacement`, and `EachSubscribedEventMovesFromPublisherAToBExactlyOnce`. Root Unity executed these eleven leaves and reported 11/11 green. Its latest EditMode execution is the real 21/24 RED recorded above; only the three fixture/assertion leaves changed here remain pending root rerun. The tests use real `Application.isPlaying` CLR subscriptions, public event counters, test-only read-only per-event diagnostics, distinct A/B outputs, real runtime audio, dynamic/kinematic Rigidbody outcomes, and destroyed Unity-object replacement rather than private subscribed flags or event backing fields.

Static audit result:

```text
W7_CS_COUNT=23
PHASE_ADAPTER_CS_COUNT=18
RELEVANT_UNITY_ASSET_COUNT=24
W7_MISSING_META_COUNT=0
ALL_ASSET_GUID_RECORD_COUNT=476
DUPLICATE_GUID_GROUPS=0
EVENT_ADD_COUNT=18
EVENT_REMOVE_COUNT=18
UNGUARDED_EVENT_ADD_COUNT=0
UNGATED_CONFIGURE_UNBIND_COUNT=0
FIXTURE_STRIPS_BEFORE_VALIDATE=True
FIXTURE_ASSERTS_BYTES_AND_HASH=True
FIXTURE_USES_TEMP_ASSET_COPY=True
FIXTURE_EXACT_32_HEX_PATH_AND_MARKER=True
TEMP_SCENE_ACTIVE_SWITCH_CALLS=5
TEMP_COPY_ACTIVE_BEFORE_MARKER=True
TEMP_COPY_ACTIVE_BEFORE_SETUP=True
EDITOR_MARKER_ACTIVE_FAIL_CLOSED=True
INACTIVE_MARKER_MUTATION_NEGATIVE=True
EDITOR_MARKER_BORN_IN_TARGET=True
EDITOR_MARKER_CREATE_THEN_MOVE_COUNT=0
TEST_OWNERSHIP_NEGATIVE_CASES=5
EDIT_FIXTURE_ADDITIVE_COUNT=4
EDIT_FIXTURE_SINGLE_COUNT=0
EDIT_FIXTURE_RESTORE_SCENE_SETUP_COUNT=0
FIXTURE_ASSERT_IGNORE_COUNT=0
DIRTY_SCENE_REGRESSION_COUNT=2
CLEAN_SAVED_ACTIVE_SCENE_REGRESSION_COUNT=1
CLEAN_SOURCE_SUCCESS_FAILURE_GUARD_ASSERTS=True
INJECTED_CREATION_FAILURE_CLEANUP=True
DIRTY_SCENE_VALUE_LOADED_DIRTY_ASSERTS=True
INDEPENDENT_FIXTURE_CLEANUP_CALLS=6
CLEANUP_FAILURE_BEHAVIOR_TESTS=1
FUZZY_W7_PREFIX_OWNERSHIP_COUNT=0
USER_PREFIX_SURVIVAL_ASSERTS=True
COMPONENT_FREE_GENERATED_STATE_COUNT_ASSERT=True
PUBLIC_RUNTIME_TEARDOWN_SEAM_COUNT=0
PRIVATE_ONDESTROY_RESTORE_PATH_COUNT=1
EDITOR_ONLY_TEARDOWN_INTERFACE_COUNT=1
EDITOR_ONLY_TEARDOWN_IMPLEMENTERS=9
EDITOR_STRIP_PREDESTROY_RELEASE_CALLS=1
PLAYER_EDITOR_TEARDOWN_TYPE_COUNT=0
PLAYER_EDITOR_TEARDOWN_METHOD_COUNT=0
SETUP_RELAY_PREWRITE_COUNT=0
SETUP_PLACEMENT_OWNER_WRITE_COUNT=1
RELAY_FALSE_AUTHORED_BASELINE_ASSERTS=1
RELAY_REPEAT_CONFIGURE_ASSERTS=1
RELAY_REPEAT_TEARDOWN_ASSERTS=1
PLAYER_RELAY_CONFIGURE_SET_ISTRIGGER_CALLS=1
EDITOR_SETUP_DIRECT_SET_ISTRIGGER_CALLS=1
AUTHORED_BEHAVIOUR_COLLIDER_BODY_POSE_ASSERTS=True
ROLLBACK_PUBLIC_DIAGNOSTIC_AND_COLLIDER_TEST=True
EDITMODE_PRIVATE_SUBSCRIPTION_FLAG_REFERENCES=0
EDITMODE_EVENT_BACKING_FIELD_REFERENCES=0
EDITMODE_PUBLIC_RESULT_ZERO_COMPLETION=True
EDITMODE_PUBLIC_RESULT_ZERO_GIVEUP=True
DIRECT_COMPLETION_GIVEUP_TARGET_REFRESH_CALLS=2
DIRECT_TARGET_REFRESH_PREPROCESSOR_GUARD=UNITY_EDITOR||UNITY_INCLUDE_TESTS
PLAYER_COORDINATOR_SCAN_SEAM_PRESENT=False
PLAYER_BINDING_REFRESH_SEAM_PRESENT=False
PLAYER_IL_FIND_OBJECTS_OF_TYPE_ALL_CALLS=0
PLAYER_IL_BINDING_REFRESH_CALLS=0
PLAYER_IL_COORDINATOR_REFRESH_CALLS=0
UNITY_TEST_COORDINATOR_SCAN_SEAM_PRESENT=True
UNITY_TEST_BINDING_REFRESH_SEAM_PRESENT=True
NORMAL_RUNTIME_SESSION_SUBSCRIPTION_GATE_UNCHANGED=True
ADAPTER_ENABLE_FAIL_CLOSED_CONJUNCTION=True
ADAPTER_STANDALONE_USES_CLR_REFERENCE_EQUALS=True
DESTROYED_COORDINATOR_REQUIRES_UNITY_LIVE_OBJECT=True
PLAYER_ENABLE_IL_RAW_CLR_NULL_BRANCH=True
PLAYER_ENABLE_IL_UNITY_LIVE_CHECKS=1
ADAPTER_ENABLE_COUNTEREXAMPLE_TESTS=1
FAIL_OPEN_NONCURRENT_BRANCHES=0
FAIL_OPEN_NO_SNAPSHOT_BRANCHES=0
AUTHORITY_TEST_USES_ADDITIVE_FIXTURE=True
AUTHORITY_TEST_DIRECT_CALLER_SCENE_ROOT_CREATE=False
AUTHORITY_TEST_EXPLICIT_ROOT_DESTROY=True
CLEAN_SAVED_ACTIVE_SCENE_REGRESSION_REUSES_FIXTURE=True
DIRECT_ENABLE_LOCK_ASSERTS=Coordinator+Adapter+Binding
MENU_SCENE_SETUP_RESTORE=True
CANONICAL_SCENE_SAVE_CALLS_IN_TEST=0
TEST_OWNED_SOURCE_INITIAL_SAVE_CALLS=1
CHEST_EXACT_PATH_COUNT=1
FINAL_EXACT_PATH_COUNT=1
PRESENTATION_BINDING_FUZZY_FALLBACKS=0
EXACT_PATH_VALIDATOR_CALLS=3
UNDO_RECORD_OBJECT_CALLS=1
UNDO_ROLLBACK_CALLS=1
PREFLIGHT_BEFORE_UNDO=True
HINT_AUTHORITY_SNAPSHOT_FIELDS=2
HINT_DISABLE_RECONFIGURE_RECREATE_TERMINAL_PLAYMODE_SOURCE=True
CONFIGURE_INVALID_ROLLBACK_AND_DISTINCT_A_B_PLAYMODE_SOURCE=True
CURRENT_OUTPUT_ONDESTROY_RESTORE_SOURCE=True
TARGET_AND_KEYPAD_DISABLE_COLLIDER_CHECKS=4/4
TARGET_AND_KEYPAD_DIRECT_ENTRY_GUARDS=4/4
TARGET_AND_KEYPAD_ACTIVE_CONFIGURE_GUARDS=4/4
PLACEMENT_DISABLED_PUBLIC_OVERLOAD_GUARDS=2/2
PLACEMENT_UNCONFIGURED_PUBLIC_OVERLOAD_GUARDS=2/2
PLACEMENT_DISABLED_TRANSFORM_PHYSICS_UNCHANGED=True
TEST_ONLY_SUBSCRIPTION_DIAGNOSTICS=8
EXACT_SUBSCRIBER_HANDLER_COUNT_COVERAGE=8/8
PUBLISHER_REPLACEMENT_COVERAGE=8 subscribers plus Adapter
NAMED_DIAGNOSTIC_COUNTERS=5/5
TARGET_PLACEMENT_RESET_EVENT_A_TO_B_COVERAGE=2/2
PRESENTER_RUNCONFIGURED_RUNRESET_RESULTPRODUCED_A_TO_B_COVERAGE=3 presenters x 3 events
PLAYER_DIAGNOSTIC_PREPROCESSOR_GUARD=True
PLAYER_HAS_SUBSCRIPTION_DIAGNOSTIC=False
PLAYER_HAS_TEST_CLIP_SEAM=False
UNITY_TEST_BUILD_HAS_SUBSCRIPTION_DIAGNOSTIC=True
UNITY_TEST_BUILD_HAS_TEST_CLIP_SEAM=True
AUDIO_SOURCE_STOP_CALLS=1
PLAYMODE_AUDIOCLIP_CREATE_CALLS=1
PLAYMODE_AUDIO_ISPLAYING_ASSERT_REFERENCES=11
KEY_AUTHORED_POSE_VELOCITY_FIELDS=6/6
STOP_AND_LOCK_CALLS=2
AUTHORED_KINEMATIC_BRANCHES=1
PLAYMODE_AUTHORED_KINEMATIC_FIXTURES=1
PLAYMODE_KINEMATIC_NONCONVEX_FIXTURE_ASSERTS=10
PLAYMODE_LOGASSERT_EXPECT_CALLS=0
PLAYMODE_NO_UNEXPECTED_LOG_GATES=4
DESTROYED_COORDINATOR_REPLACEMENT_TESTS=1
DESTROYED_COORDINATOR_FAKE_NULL_WRAPPER_ASSERTS=2
DESTROYED_COORDINATOR_FAKE_NULL_WINDOW_ASSERTS=Adapter+Binding+Collider+AcceptInput
UNITY_PUBLISHER_FLAG_CLEAR_OUTSIDE_NULL_GUARD=9/9
INPUT_METADATA_FIELDS=4
RESULT_PRODUCED_EVENT_DECLARATIONS=1
PUBLISH_INPUT_CALLS=8
CORE_ASMDEF_AUTO_REFERENCED=True
CORE_ASMDEF_NO_ENGINE_REFERENCES=True
CORE_ASMDEF_REFERENCE_COUNT=0
PHASE_ADAPTER_ASMDEF_COUNT=0
PLAYMODE_TEST_ASMDEF_COUNT=1
CORE_FORBIDDEN_REFERENCE_MATCHES=0
W7_FORBIDDEN_SCOPE_REFERENCE_MATCHES=0
CORE_TEST_METHODS=24
UNITY_EDITMODE_TEST_METHODS=24
UNITY_PLAYMODE_TEST_METHODS=11
PLAYMODE_APPLICATION_ISPLAYING_ASSERTS=1
INTERACTION_SCENE_BYTES_BEFORE=408990
INTERACTION_SCENE_BYTES_AFTER=408990
INTERACTION_SCENE_SHA256_BEFORE=A394EABE4D72739C625BA5A261F36DF86A87B15C201CCB02F53A134B1DF027B5
INTERACTION_SCENE_SHA256_AFTER=A394EABE4D72739C625BA5A261F36DF86A87B15C201CCB02F53A134B1DF027B5
NON_GIT_TRAILING_WHITESPACE_MATCHES=0
NON_GIT_MERGE_MARKER_MATCHES=0
NON_GIT_TEXT_FILES_CHECKED=49
GIT_COMMANDS_RUN=0
GIT_DIFF_CHECK=NOT_RUN_EXPLICITLY_PROHIBITED
ALL_ASSETS_MISSING_META_EXCLUDING_UNITY_IGNORED_TILDE_PATHS=0
PREEXISTING_UNITY_IGNORED_APILAYERS_TILDE_META_GAPS=5
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
- Editor non-play setup/configuration intentionally creates no CLR subscriptions. The EditMode source verifies that invariant through zero calls on the public coordinator `ResultProduced` event plus public adapter/binding/collider behavior; the PlayMode assembly covers the real runtime subscribe/unsubscribe/reconfigure/publisher-replacement chain, including the fake-null interval after a destroyed Unity publisher, atomic failure behavior, distinct output/audio/key ownership, per-event A-to-B detachment, terminal-snapshot input closure, and authority reconstruction with exact observable call counts. Root Unity has supplied 11/11 green PlayMode evidence, including the kinematic/non-convex planned-key leaf. This final change touches only EditMode fixture/assertion source. `InteractionSubscriptionDiagnostic`, named event counters, the clip-configuration test seam, the direct target-binding refresh/global scan, and the Editor teardown contract are fully guarded by `UNITY_INCLUDE_TESTS` and/or `UNITY_EDITOR` and absent from Study Player compilation.
- The EditMode fixture no longer depends on canonical InteractionLab load state or on `NewScene(Additive)`. Main setup tests and both outer sentinel fixtures work on unique temporary copies of the canonical scene asset, make each copy active before creating anything, and leave already loaded/dirty, untitled dirty, and active clean saved caller scenes untouched. The dirty sentinel case marks only its loaded temporary copy dirty; the clean case saves only its temporary copy. The adapter-authority counterexample also uses this fixture rather than creating in the caller's active scene. Test-only mutation APIs reject an inactive target scene. Unity Test Runner must still execute the three latest changed leaves after integration to validate AssetDatabase copy/delete, active-scene switching, dirty-memory preservation, and NUnit array assertions in the authoritative Editor. Independent cleanup preserves the primary failure and attempts close, active-scene restoration, temporary asset deletion, and canonical hash verification even when an earlier recovery action fails.
- Deterministic visual feedback is implemented; accepted/error audio clips are optional serialized references, which accounts for the two benign Player-only `CS0649` warnings. Real audio playback/`isPlaying`, Unity's no-kinematic-velocity-warning behavior, multi-Rigidbody key restoration, and destroyed-Coordinator recovery are source-covered but remain Unity Test Runner/device checks because Unity execution was prohibited here.
- No setup was applied to the saved `InteractionLab.unity` in this worktree, so authoritative scene wiring remains an Orchestrator integration step.
- Per the narrow review scope, this follow-up did not alter the separately noted `InteractionTargetBinding` dynamic restoration order or make test-only Strip transactional; both are explicitly outside this change.
- Recorded follow-up debt, intentionally not refactored in this targeted review: `W7InteractionPhaseAdaptersSetup.cs` is too large and should later be split by preflight/setup/validation responsibility; the digit/backspace/submit keypad bindings repeat availability/subscription code and should later share an internal helper after behavior is stable.

## 6. Recommended Orchestrator review steps

1. Review/selectively integrate the listed files, hydrate LFS assets, and retain the two exact chest-lid/final-door path constants. No W7 scene YAML is expected from this worker.
2. In the authoritative Unity 6000 editor, run W7 Setup on `InteractionLab`, then W7 Validate. Confirm the caller's prior scene setup and unsaved-scene prompt behavior are preserved. If automatic hand discovery cannot resolve the project hierarchy, explicitly assign the coordinator's two allowed roots to the left/right hand-only interactor roots and rerun validation; do not weaken the validator.
3. Wire lifecycle in this order: `Configure(machine.Plan)`; enable the coordinator; call `Synchronize(machine.CurrentPhase)` on Run start and every FirstPlayback/Active/ReplayPlayback/phase transition; on W7 task completion call W1 `CompletePhase`, then synchronize W7 with W1's new snapshot. For GiveUp, pass the current W1 snapshot to W7, invoke W1 `GiveUpPhase`, then synchronize the resulting snapshot. Abort/reset W1 separately and call W7's local `Abort`/`Reset`; configure a new immutable plan for a new Run.
4. For W8, attach one handler to `ResultProduced` and serialize the four nullable input fields into the detail JSON; do not add an alternate interaction event stream.
5. Rerun the three leaves that remained RED in the latest 21/24 root result: `CleanActiveSavedSceneSurvivesSuccessfulAndFailedFixtures`, `IsolatedSetupPreservesDirtyUnsavedUserScene`, and `StripUsesExactOwnershipAndRestoresAuthoredRuntimeState`. For the first two, begin with the Test Runner's normal untitled scene present and confirm the temporary asset copies open `Additive`, the clean/dirty sentinel state survives, cleanup removes every transient asset/meta, and canonical InteractionLab bytes/hash remain unchanged. For the strip leaf, confirm the final `W7Notes` and `W7UserContent` array-length assertions pass in addition to the already-green repeated relay Configure/teardown `isTrigger=false` restoration. The other 21 EditMode leaves were green in the latest run; a final full 24-leaf regression is still preferred. Root already recorded PlayMode 11/11 green; this test-only change does not alter runtime code.
6. On Quest, verify both naked-hand collider roots, all `0`–`9`/`*`/`#` trigger proxies, coin placement deduplication, phase-4 GiveUp key release, phase-5 visual reset, presentation rehydration, and final-door animation. Record the build identity; no device validation is claimed here.
