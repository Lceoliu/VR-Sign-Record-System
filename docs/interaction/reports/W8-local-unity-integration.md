# W8 — Local Unity Study Flow integration

Date: 2026-08-26

Workspace: `C:\Users\woshica\.codex\worktrees\b1cb\VR-Sign-Record-System`

> The APK identity in this historical W8 report has been superseded by the
> post-hardening artifact in
> [`W9-local-hardening-build.md`](W9-local-hardening-build.md). Physical testing
> must use [`../QUEST-HUMAN-GATE.md`](../QUEST-HUMAN-GATE.md).

## 1. Outcome and authority boundaries

W8 now provides a thin local Unity orchestration layer that connects the
already-owned W1/W5/W6/W7 surfaces into one participant-visible Study flow:
PreStart identity and readiness, Start, six continuous phases, the one-time
Replay, Give Up/Stuck, Abort, capture, Host readiness, terminal cleanup, and
return to PreStart.

The implementation deliberately does not create another Run state, phase
index, random plan, timer, task-progress model, or capture owner:

- W1 `InteractionRunStateMachine` remains the only Run/phase lifecycle and
  random-plan authority.
- W6 `InteractionRunController` remains the Unity/Host/capture facade and the
  owner of readiness parsing, participant matching, presentation tokens,
  checkpoint/seal/upload, and reset eligibility.
- W5 remains the owner of first/replay presentation, actual-first-frame and
  completion observations, bubble/sign/pointing/highlight behavior, and
  one-time playback rendering.
- W7 remains the owner of all six task rules and `ValidationResult.Progress`.
- W8 owns only command routing, subscriptions, epochs/tokens, ordering, and
  lifecycle-safe teardown.

The W8 worker itself did not edit scene YAML, Host source, ProjectSettings,
XR/URP configuration, Recorder workflow, W6/W7 reports, or the progress log.
Subsequent Orchestrator integration saved the fully wired canonical
`InteractionLab.unity` and committed it as `31d6369`. This report records that
later main-workspace verification separately; no Quest, participant, webcam,
or Host/device end-to-end result is claimed.

## 2. Event ordering

### PreStart and Start

1. The operator enters the exact Host-selected participant ID and an integrated
   commit/build identity, then applies them while W1/W6 is in `PreStart`.
2. W8 validates the values and calls W6 `ConfigureIdentity`; it does not infer
   identity from an unauthenticated or stale readiness response.
3. W8 loads the staged instruction manifest into W6. Start remains unavailable
   until manifest, explicit identity, W6 Host readiness, and real Study capture
   readiness all pass.
4. Start calls W8 `TryStart`, which delegates Run creation and frozen plan
   ownership to W6/W1, then configures W7 from W1's resulting `RunPlan`.
5. W8 waits for W6 `PresentationRequested`, asks W5 to present exactly once,
   and does not ACK the request yet.
6. Only W5's actual first-presented-frame event is ACKed to W6/W1. W8 then
   synchronizes W7 from the fresh authoritative W1 phase snapshot.

### Task input and phase transition

1. W8 observes W7 `ResultProduced`, copies its `Progress` and
   `RequiredProgress` for UI, and records the validation observation through
   W6.
2. Because validation recording can change W1 `InteractionErrorCount` and
   `TaskProgress`, W8 immediately resynchronizes W7 from W6/W1's fresh
   `CurrentPhase`. It never derives progress from scene animation.
3. On completion or Give Up, W8 ends W5 first, asks W6/W1 to finish the phase,
   disables W7 input, and waits through checkpoint/seal work.
4. W8 never starts the next phase itself. It reacts only when W6 later publishes
   the next `PresentationRequested` token.

This ordering also defines capture attribution: W6/W1 keeps the old phase
current during checkpoint, next-presentation request, and loading. Samples
remain attributed to that old phase until the next phase's real W5 first-frame
ACK advances the authoritative snapshot. No capture/schema contract was
changed to manufacture an earlier transition.

### Replay and Give Up

- The real `InteractionInstructionControls` now supports an explicit W8 command
  sink. In W8 mode, Replay/GiveUp/Abort fail closed if the sink is absent; the
  standalone W5 direct-Replay fallback remains available outside W8 mode.
- Replay UI calls W8, W8 requests replay from W6/W1, and W5 starts replay only
  after W6 publishes the authoritative replay `PresentationRequested` token.
  Request sequence, run ID, active/pending playback, epoch, and completion
  guards make duplicate or stale callbacks inert.
- Replay remains unavailable before first playback completes. Give Up uses the
  W1 replay gate and the fresh W1 snapshot supplied to W7, so the result remains
  `Stuck` and cannot bypass lifecycle drift checks.

### Abort, disable, pause, and terminal cleanup

Abort ordering is:

`W5 EndPhase -> W6 TryAbortRun -> wait for true Aborted/Faulted terminal -> W7 Abort/Reset -> W6 reset to PreStart`

Application pause, component disable, destroy, and disposal use the same safe
abort/suspend path for an owned Run. Subscription detach happens even if a
presentation/task publisher or suspend/dispose call throws. Re-enable/resume
attaches once with a new epoch; callbacks from prior publishers or generations
remain stale. The participant Start surface becomes the visible PreStart
control again only after W6 reports real terminal cleanup and accepts reset.

## 3. Review gates closed and regression coverage

The final nine-item review is covered as follows:

1. **Identity display/armed consistency.** Armed participant/build inputs are
   locked. A defensive `onValueChanged` divergence immediately disarms Start,
   invalidates readiness, and restores the displayed values to W6's actual
   configured identity. Tests:
   `IdentityIsPreStartArmedAndLocksAfterConsumption` and
   `ArmedIdentityInputsCannotDivergeFromW6Identity`.
2. **One-at-a-time readiness polling.** A readiness request remains in flight
   until its W6 completion/timeout callback; the two-second interval begins at
   completion. Disable/reconfigure invalidates the generation and in-flight
   state, making late completions inert. Test:
   `SlowReadinessResponseCannotBeStaledByPolling`.
3. **Transactional Configure/Reconfigure.** All replacement dependencies and
   ports are constructed before mutation. Flow replacement rolls back partial
   subscriptions and restores publisher A if publisher B fails; an active Run
   rejects replacement without changing controller fields, subscriptions, or
   readiness ownership. Tests:
   `ReconfigureDetachesOldPublishersAndRejectsActiveReplacement` and
   `ControllerReconfigureFailurePreservesDependenciesAndSubscriptions`.
4. **Manifest coroutine ownership.** Disable/destroy/reconfigure stops and
   invalidates the coroutine; all manifest/parser exceptions are caught; a
   `finally` block clears the current operation so Retry is possible. Flow
   suspend/dispose failures cannot prevent later teardown. Tests:
   `ManifestFailureRetryAndDisableCancelAreSafe` and PlayMode driver
   `ControllerPauseAndDisableUseTheSafeAbortPath`.
5. **Real controls lifecycle.** The W8 controls install the W5 command sink only
   when ownership can be acquired, subscribe once, and clear the sink only if
   they still own it on disable/destroy/reconfigure. Destroyed Unity interface
   values fail closed. Test:
   `RealInstructionControlsRouteReplayThroughW8Sink`; the source validator also
   proves the real W8 path cannot call W5 Replay directly.
6. **Actual setup Undo.** Every mutation of an existing Canvas, CanvasScaler,
   CanvasGroup, poke canvas, RectTransform, Image, TMP input, Button, TMP text,
   component reference, array, routing flag, skeleton, probe, and layer records
   Undo first. Created objects/components use object Undo. Test:
   `SetupSceneCopyIsTransactionalValidatedAndIdempotent` exercises the real W8
   setup against a canonical asset copy, injects `afterWiring` failures before
   and after existing-UI mutation, verifies full rollback, validates a
   successful setup, reruns idempotently, independently closes/deletes the
   copy/folder/meta, preserves loaded/dirty/active scenes, and checks the
   canonical byte hash.
7. **Meta/GUID completeness.** Runtime, Editor, EditMode, PlayMode, asmdef,
   files, and directory metas are present. The final read-only audit found zero
   W8 missing metas and zero duplicate GUID groups across all Assets.
8. **Real-scene prerequisites and setup order.** The production integrated
   facade validates the saved W5 baseline, then calls W6 setup, W7 setup, and W8
   wiring in one outer Undo transaction without saving. The W8-only setup also
   gives exact W6/W7 prerequisite menu errors. The copy fixture uses the exact
   W7-owned path and ownership API required by W7. Exact operator menus are in
   section 6.
9. **Real Meta capture discovery.** The EditMode wrapper and PlayMode wrapper
   both discover `CaptureBindingRequiresRealMatchedMetaSourcesAndProbes`. Setup
   installs exactly one genuine `OVRSkeleton` on each distinct scene OVRHand,
   matches left/right provider skeleton types, and adds one stable unique object
   probe per W7 target binding. Strict Start still fails closed without a real
   tracked XR HMD, `OVRHand.IsTracked`, `OVRHand.IsDataValid`, and nonempty live
   skeleton bones.

Additional frozen seams have direct regressions:

- Six stages and actual-first-frame gating:
  `SixPhaseHappyPathAdvancesOnlyOnActualFirstFrames`.
- One W6 replay token and no double W5 playback:
  `ReplayUsesOneW6TokenAndCannotStartTwice`.
- W1/W7 validation drift, including reset and non-reset errors followed by
  replay then successful Give Up/Stuck:
  `ValidationErrorsResynchronizeBeforeReplayGiveUp`.
- Give Up replay gate and stuck retention:
  `GiveUpRequiresCompletedReplayAndRetainsStuckResult`.
- Abort order and true-terminal wait:
  `AbortEndsW5BeforeW6AndWaitsForTerminalBeforeW7Reset`.
- Terminal adapter failures cannot strand W6 or throw every frame:
  `TerminalAdapterFailuresStillConvergeToPreStart`.
- Start/Host/initialization failures:
  `StartAndInitializationFailuresDoNotInventAnotherRunPlan`.
- Duplicate/stale callbacks and resubscription:
  `DuplicateAndStalePresentationCallbacksAreExactlyOnce` and
  `SuspendUsesTheSameSafeAbortAndResumeResubscribesOnce`.
- W5 faults and legal task completion during first/replay playback:
  `PresentationFaultUsesAbortPathWithoutAcknowledgingARequest` and
  `SuccessfulTaskMayEndDuringFirstOrReplayPlayback`.
- Runtime-driver assembly boundary:
  `SetupSourceContractProvesRealRoutingAndRuntimeNUnitBoundary`. The driver is
  compiled only under `UNITY_EDITOR || UNITY_INCLUDE_TESTS`, uses custom
  throwing assertions, and imports no NUnit namespace.

## 4. Files created or changed

Existing runtime integration seams changed:

- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionPresentationHandshake.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionRunController.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InteractionInstructionControls.cs`

New W8 runtime directory and files, each with its matching `.meta`:

- `signvr_unity/Assets/Scripts/Interaction/Orchestration.meta`
- `signvr_unity/Assets/Scripts/Interaction/Orchestration/InteractionStudyAsyncGates.cs`
- `signvr_unity/Assets/Scripts/Interaction/Orchestration/InteractionStudyCaptureBinding.cs`
- `signvr_unity/Assets/Scripts/Interaction/Orchestration/InteractionStudyFlow.cs`
- `signvr_unity/Assets/Scripts/Interaction/Orchestration/InteractionStudyFlowController.cs`
- `signvr_unity/Assets/Scripts/Interaction/Orchestration/InteractionStudyFlowControls.cs`
- `signvr_unity/Assets/Scripts/Interaction/Orchestration/InteractionStudyFlowPorts.cs`
- `signvr_unity/Assets/Scripts/Interaction/Orchestration/InteractionStudyIdentityPolicy.cs`
- `signvr_unity/Assets/Scripts/Interaction/Orchestration/UnityInteractionStudyFlowPorts.cs`
- `signvr_unity/Assets/Scripts/Interaction/Orchestration/W8InteractionStudyFlowTestDriver.cs`

New Editor setup/validator, with `.meta`:

- `signvr_unity/Assets/Editor/W8LocalUnityIntegrationSetup.cs`

New EditMode test directory/file, each with `.meta`:

- `signvr_unity/Assets/Tests/EditMode/Interaction/Orchestration.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Orchestration/W8LocalUnityIntegrationTests.cs`

New PlayMode test directory/files, each with `.meta`:

- `signvr_unity/Assets/Tests/PlayMode/Interaction/Orchestration.meta`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/Orchestration/SignVR.Interaction.Orchestration.PlayMode.Tests.asmdef`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/Orchestration/W8LocalUnityIntegrationPlayModeTests.cs`

Documentation:

- `docs/interaction/reports/W8-local-unity-integration.md`

## 5. Static compilation and executed tests

Unity 6000.5.6f1 managed references and the current Unity Library assembly graph
were used without starting Unity:

```text
PLAYER_UNITY_EDITOR_AND_TESTS: exit 0, 0 errors, 2 unrelated existing warnings
PLAYER_NON_EDITOR_NO_TEST_DEFINE_NO_NUNIT_REF: exit 0, 0 errors, 5 unrelated existing warnings
EDITOR: exit 0, 0 errors, 5 unrelated existing warnings
EDITMODE_TEST_ASSEMBLY_SOURCE: exit 0, 0 diagnostics
PLAYMODE_TEST_ASSEMBLY_SOURCE: exit 0, 0 diagnostics
```

The warnings are existing Recording/UI/Editor deprecation or unused-field
warnings outside W8; no W8 compiler diagnostic was emitted.

A pure external reflection runner executed the coordinator scenarios that do
not require Unity lifecycle/scene objects:

```text
PASS Abort
PASS GiveUp
PASS PresentationFault
PASS Reconfigure
PASS Replay
PASS SixPhase
PASS SlowReadiness
PASS StaleCallbacks
PASS StartFailures
PASS SuspendResume
PASS TaskDuringPlayback
PASS ValidationDrift
PASS WrongInput
TOTAL=13 PASSED=13 FAILED=0
```

This external runner was not a Unity Test Runner result. After integration in
the main workspace, Unity Test Runner passed W8 EditMode 22/22 and W8 PlayMode
5/5. Adjacent authority regression also passed W7 EditMode 24/24, W6 PlayMode
8/8, and W7 PlayMode 11/11. The Console contained zero errors after the runs;
the scene returned clean with 34 roots and no temporary test scene left loaded.
These local results do not establish physical Quest tracking, webcam capture,
or participant end-to-end readiness. Subsequent main-workspace checks did
establish the local Host runtime and Android artifact gates described below.

## 6. Exact setup and operator procedure

### Preferred single production facade

With the saved canonical `InteractionLab.unity` active and its existing W5
presentation present:

1. Run `Tools/SignVR/Interaction/W8 Configure Complete Integration (Unsaved)`.
   It validates W5, then performs W6 -> W7 -> W8 in one outer Undo transaction.
2. Run `Tools/SignVR/Interaction/W8 Validate Local Study Structure`.
3. Review W6/W7/W8 references, the two matched hand skeletons, probes, and the
   participant Start surface. The setup intentionally has not saved anything.
4. The Orchestrator saves the canonical scene exactly once after review.
5. Run `Tools/SignVR/Interaction/W8 Validate Saved Integrated Study Flow`.

The Orchestrator completed this production path and committed the resulting
canonical scene as `31d6369`. The saved validator passed. Repeating the complete
wiring operation at the fixed point produced consecutive identical scene
hashes, with SHA-256
`AEF55496BF8E05B9FF417832FDE553DEB4E815D5659229E4EC2786BD3D5B64DE`.

### Equivalent explicit staged path

If the Orchestrator prefers the already-authoritative setup chain, run exactly:

1. `Tools/SignVR/Interaction/W6 Configure Capture and Host (Unsaved)`
2. `Tools/SignVR/Interaction/W7 Setup Phase Adapters`
3. `Tools/SignVR/Interaction/W8 Configure Local Study Flow (Unsaved)`
4. `Tools/SignVR/Interaction/W8 Validate Local Study Structure`
5. Review, save once, then run
   `Tools/SignVR/Interaction/W8 Validate Saved Integrated Study Flow`

If W5 is absent or invalid, first run
`Tools/SignVR/Interaction/W5 Configure Instruction Presentation (Unsaved)`.
The current authoritative scene inspection reports one valid W5 presentation,
so W5 is normally validation-only in the W8 facade.

### Participant identity and strict readiness

1. Stage the W2 31-entry instruction manifest at
   `StreamingAssets/InstructionContent/instruction-content-manifest.json`.
2. Select the participant in the Host frontend and wait for its heartbeat.
3. In W8 PreStart UI, enter that exact participant ID and the integrated
   Git/build identity. `UNCONFIGURED`, `unintegrated`, `unknown`, and build
   identities shorter than seven characters are rejected.
4. Apply identity. Inputs lock and Start remains disabled until W6 observes a
   fresh Host readiness response for the same participant and the real capture
   gate passes. The same button becomes `修改参与者与构建身份`, allowing a typo
   to be corrected safely in PreStart; changing a draft disarms rather than
   silently changing an already-configured identity. The status surface shows
   W6's current Start-readiness reason while Start is disabled.
5. In real PlayMode/device context, run
   `Tools/SignVR/Interaction/W8 Validate Strict Study Readiness` and then use the
   participant-visible Start button. There is no tutorial stage.
6. Completed, Aborted, and Faulted cleanup returns to PreStart and requires an
   explicit identity reconfirmation for the next Run.

## 7. Unity Test Runner handoff

Run these exact W8 class filters:

```text
SignVR.Interaction.Editor.Tests.Orchestration.W8LocalUnityIntegrationTests
SignVR.Interaction.PlayMode.Tests.Orchestration.W8LocalUnityIntegrationPlayModeTests
```

The main task should also rerun these adjacent authority filters after setup:

```text
SignVR.Interaction.Core.Tests.InteractionRunStateMachineTests
SignVR.Interaction.Core.Tests.PhaseInteractionRulesTests
SignVR.Interaction.Editor.Tests.Presentation.W5InstructionPresentationStateTests
SignVR.Interaction.Editor.Tests.Presentation.W5InstructionPresentationEditorTests
SignVR.Interaction.Editor.Tests.W6InteractionCaptureHostTests
SignVR.Interaction.PlayMode.Tests.W6InteractionCaptureHostPlayModeTests
SignVR.Interaction.Editor.Tests.W7InteractionPhaseAdaptersTests
SignVR.Interaction.PhaseAdapters.PlayMode.Tests.W7InteractionPhaseAdaptersPlayModeTests
```

The copied-scene W8 test uses the required path
`Assets/__W7InteractionPhaseAdaptersTests_<32hex>/InteractionLab_W7Test.unity`,
makes it active, configures W6 with `requireCanonical=false`, invokes W7
`MarkTestOwnedSceneForAutomation`, then invokes
`SetupAndValidateTestOwnedSceneWithoutSaving` before exercising W8. It never
calls `NewScene`; it saves only that uniquely named test-owned copy to prove a
clean-scene failed transaction restores the dirty flag and that W8 skeletons,
probes, references, and UI survive close/reopen. Independent cleanup removes
the copy even when the test body fails.

## 8. Final read-only audit

```text
ALL_ASSETS_MISSING_META_EXCLUDING_UNITY_TILDE_PATHS=0
W8_MISSING_META=0
ASSET_GUID_RECORDS=538
DUPLICATE_GUID_GROUPS=0
W8_MERGE_MARKERS=0
W8_TRAILING_WHITESPACE=0
LEFTOVER_W7_TEST_FIXTURE_FOLDERS=0
RUNTIME_NUNIT_MATCHES=0
W8_IGNORE_EXPLICIT_MATCHES=0
W8_PRODUCTION_SAVE_NEW_SCENE_MATCHES=0
W8_TEST_OWNED_SAVE_SCENE_MATCHES=2
POINTING_ONLY_MATCHES=0
INTERACTIONLAB_BYTES=774784
INTERACTIONLAB_SHA256=AEF55496BF8E05B9FF417832FDE553DEB4E815D5659229E4EC2786BD3D5B64DE
INTERACTIONLAB_ROOTS=34
INTERACTION_RUN_CONTROLLERS=1
INTERACTION_CAPTURE_SAMPLERS=1
INTERACTION_PHASE_COORDINATORS=1
INTERACTION_TARGET_BINDINGS=25
INTERACTION_OBJECT_STATE_PROBES=25
OVR_SKELETONS=2
W8_FLOW_CONTROLLERS=1
W8_CAPTURE_BINDINGS=1
W8_FLOW_CONTROLS=1
W8_START_SURFACES=1
HOST_BASE_URL=http://192.168.1.114:8011
SAVED_INTEGRATION_VALIDATOR=PASS
CONSECUTIVE_FIXED_POINT_HASHES=IDENTICAL
UNITY_CONSOLE_ERRORS=0
TEMPORARY_TEST_SCENES_LOADED=0
```

### Main-workspace Host and Android artifact gate

The Orchestrator subsequently started the production Host on `0.0.0.0:8011`.
Both `http://127.0.0.1:8011/api/health` and
`http://192.168.1.114:8011/api/health` returned healthy station state, while
Interaction readiness reported backend and storage ready. The browser Study
page connected and sent a fresh camera heartbeat but correctly remained camera
`NOT READY` without granted webcam access; no Quest or Participant readiness is
claimed.

The fully wired scene then built through
`SignVR/Build/Interaction/Android APK`:

```text
BUILD_RESULT=Succeeded
BUILD_DURATION=00:18:51.0549154
APK=Builds/SignVR_Interaction_Local.apk
APK_BYTES=471228297
APK_SHA256=1021260185B7F66A5F8277A6086954A64CAA0892945712D311C0BB66CF598AAE
PACKAGE=com.signvr.interaction
PRODUCT=SignVR Interaction
VERSION_NAME=1.0.0
VERSION_CODE=1
MIN_SDK=32
TARGET_SDK=34
ABI=arm64-v8a
IL2CPP=True
DEVELOPMENT_DEBUGGABLE=True
APK_SIGNATURE_V2=True
APK_SIGNERS=1
ZIPALIGN=PASS
BUILT_SCENES=Assets/Scenes/InteractionLab.unity
RECORDER_SCENE_PRESENT=False
INSTRUCTION_CONTENT_FILES=63
INSTRUCTION_MANIFEST_ENTRIES=31
INSTRUCTION_SIGNER=wang
INSTRUCTION_PHASE_COUNTS=3,9,3,3,7,6
INSTRUCTION_SENTENCE_RANGE=001-031
INSTRUCTION_POSE_INTEGRITY_ISSUES=0
SENTENCE_001_SELECTED_TAKE=take_004
SENTENCE_016_SELECTED_TAKE=take_002
POST_BUILD_SCENE_SHA256=AEF55496BF8E05B9FF417832FDE553DEB4E815D5659229E4EC2786BD3D5B64DE
POST_BUILD_SCENE_DIRTY=False
POST_BUILD_SCENE_ROOTS=34
POST_BUILD_CONSOLE_ERRORS=0
```

The build restored Player Settings, Editor Build Settings, InteractionLab,
mobile URP, and OpenXR files to their exact pre-build hashes. Unity's
BuildReport counted 247 repeated `UnitySkills CJK` editor UI font-initialization
messages as errors even though the player and Gradle result succeeded; clearing
the build output left the Console at zero errors. This is editor tooling noise,
not a claim that physical device execution has passed.

The packaged content manifest was also read back from the APK rather than
trusted from the source tree. It contains 31 Wang-signer entries across the
required phase ranges `001–003`, `004–012`, `013–015`, `016–018`, `019–025`,
and `026–031`. Every referenced pose and metadata entry exists; all pose byte
lengths and SHA-256 values match. The selected latest completed re-recordings
include `sentence_001/take_004` and `sentence_016/take_002`.

Unity's intentionally ignored `Assets/XR/APILayers~` subtree was excluded from
the meta-presence count; all other Assets, including every W8 file and folder,
have metas. No duplicate GUID was found anywhere under Assets.

## 9. Remaining main-task gates and device risks

The local Host runtime and Android artifact gates now pass. The remaining
batched human gate starts by attaching and authorizing the Quest (none was
visible to `adb devices -l` during this check), granting webcam access in the
Chrome Study page, and entering the anonymous Participant ID. One physical
six-phase Run must then confirm the W2 manifest on device, distinct live
left/right OVRHand skeleton data, tracked HMD state, stable W7 object probes,
exact Host/Participant identity matching, webcam capture,
checkpoint/upload/terminal recovery, and phase attribution around the next
first-frame ACK. The validation batch must also exercise wrong input, the
single Replay, Give Up/Stuck, and whole-Run Abort. No Quest, Participant,
webcam-capture, or complete Host/device end-to-end result is claimed by this
report.
