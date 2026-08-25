# W5 Instruction Presentation Completion Report

Date: 2026-08-25
Worker: W5
Baseline supplied by Orchestrator: `335befa`

## 1. Scope completed and deliberately not completed

Completed:

- Added an independent `InstructionGhostPlayer` that accepts the exact
  `InstructionContentReference` already frozen into a W1 `RunPhasePlan`. It
  exposes explicit `Load`, `Play`, `Completed`, `Stop`, and `Replay` surfaces
  and has no `RecordingCoordinator`, `LastArtifact`, Take scan, or recording
  state dependency.
- The player reads desktop and Android `StreamingAssets` through
  `UnityWebRequest`, verifies the Run Plan SHA-256, strictly decodes UTF-8
  Pose JSONL, validates every record's finite/non-negative monotonic timestamp,
  array lengths, fixed positive joint count, usable-frame presence, and
  successful EOF, and verifies the Pose joint count against the independent
  signer's source skeleton.
- First play and replay share one loaded in-memory artifact and one persistent,
  preallocated `NativeArray<MSDKUtility.NativeTransform>`. The per-frame path
  reuses that buffer and performs no per-frame container allocation. The pure
  playback guard rejects repeated Play, automatic/third playback, and a second
  Replay.
- Added a deterministic phase-presentation policy driven only by W1's single
  `AssistanceCondition` enum. Its only projections are the frozen matrix:
  `TextAndPointing = text + pointing`, `TextOnly = text`, and
  `SignOnly = neither`; there is no `PointingOnly` or independently serialized
  pair of capability booleans.
- Added an Interaction-only prompt presenter that obtains all 31 Chinese
  strings from the existing
  `RecordingPointingSentenceCatalog.CreateSentences()` table. It mirrors the
  existing rounded surface, font, shader, and color treatment, contains only
  the canonical prompt text, follows the stable `InstructionSignerAnchor`
  root, and billboards toward the participant HMD. It contains no countdown,
  Take progress, or Recorder state.
- Text-bearing conditions reveal the bubble exactly 1.0 second after natural
  first-play completion, keep it visible through replay, and hide it on phase
  exit. An early phase exit prevents a delayed reveal.
- Replay becomes available only after natural first-play completion and is
  permanently consumed before the replay start request. `GiveUpPhase` is gated
  until that allowed replay completes. `AbortRun` is a separate hold-to-confirm
  control and event with no Replay/Give Up gate. Presentation state never gates
  the current phase's real interactions.
- Added ghost pointing inference from both left and right index distal-to-tip
  directions. It tests only the current W1 `TaskVariant.TargetIds`, shows the
  inferred ray and actual hit target immediately with zero entry dwell, keeps
  both through a 150 ms loss grace, and clears them on playback stop,
  completion, failure, component disable, or phase exit. It exposes hit-start,
  hit-end, current hit, and cumulative phase exposure without writing data.
- Added an Interaction-native bounds outline and rounded UI graphic. They reuse
  Recorder visual resources/parameters without placing any serialized
  `SignVR.Recording` component into the W4 clean scene.
- Added one uniquely named, idempotent W5 Editor setup/validator. It wires the
  W5 components to W4's existing anchors, instantiates an independent Meta
  Movement signer rig, derives logical-target scene bindings from W1's catalog
  plus the existing Recorder sentence catalog, marks the open scene dirty, and
  deliberately does not save the scene or assets.
- Added deterministic EditMode tests for all requested pure state/timing
  contracts plus W5 source and saved-scene validators.

Deliberately not completed:

- Did not edit or save `InteractionLab.unity` or any other Unity scene YAML.
- Did not edit `docs/interaction/PROGRESS.md`, any shared asmdef, Core code,
  W4's `InteractionLabSceneTool`, Host, Recorder/Take/upload flows, W6 capture,
  W7 adapters, Editor Build Settings, OpenXR settings, or URP settings.
- Did not stage or version the large 31-artifact instruction-content tree.
- Did not launch a Unity Editor in this isolated worktree and did not claim an
  authoritative Unity import, EditMode/PlayMode run, Android build, Quest
  performance check, or device validation.
- Did not run the required real-content 31-clip pointing geometry precheck;
  this worktree contains no staged real Pose set or saved W5 scene wiring.

## 2. Files created or changed

No pre-existing shared source file was modified. Created runtime sources under
the Orchestrator-assigned exclusive directory, each with a matching `.meta`:

- `signvr_unity/Assets/Scripts/Interaction/Presentation/GhostPointingDetector.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/GhostPointingState.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/GhostPointingTargetBinding.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InstructionGhostPlaybackState.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InstructionGhostPlayer.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InstructionPhasePresentationState.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InstructionPoseArtifactValidator.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InstructionPresentationController.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InteractionHoldToConfirm.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InteractionInstructionControls.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InteractionPromptPresenter.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InteractionRoundedRectangleGraphic.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation/InteractionTargetHighlightVisual.cs`
- `signvr_unity/Assets/Scripts/Interaction/Presentation.meta`

Created the unique Editor tool and matching `.meta`:

- `signvr_unity/Assets/Editor/W5InstructionPresentationSetup.cs`

Created tests under the assigned exclusive directory, each with a matching
`.meta`:

- `signvr_unity/Assets/Tests/EditMode/Interaction/Presentation/W5InstructionPresentationStateTests.cs`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Presentation/W5InstructionPresentationEditorTests.cs`
- `signvr_unity/Assets/Tests/EditMode/Interaction/Presentation.meta`

Created this report:

- `docs/interaction/reports/W5-instruction-presentation.md`

No W5 asmdef was created, and
`Assets/Scripts/Interaction/Core/SignVR.Interaction.Core.asmdef` was not
changed.

## 3. Commands/tests executed and exact results

No Git command was run. No Unity Editor process was started.

1. Read `signvr_unity/ProjectSettings/ProjectVersion.txt`.
   Result: `6000.5.6f1 (0e0577a1a2ac)`.
2. Compiled W1 Core, the four pure W5 state/validation classes, and
   `W5InstructionPresentationStateTests` into isolated temporary assemblies,
   then executed NUnit test/test-case methods through reflection without
   Unity. Final result:

   ```text
   PURE_STATE_CSC_EXIT=0
   PURE_STATE_NUNIT_PASSED=18
   PURE_STATE_NUNIT_FAILED=0
   ```

   Covered: both text-bearing 1-second bubble cases, early exit cancellation,
   `SignOnly`, exact three-condition matrix, Replay prerequisite and one-use
   consumption, Give Up gate, non-blocking interactions, zero-dwell entry,
   exact 150 ms loss grace and exposure, target allowlist, stop cleanup, phase
   reset, frozen first/replay artifact, third-play rejection, repeated Load and
   Play cleanup, Stop semantics, joint/array/time/EOF Pose validation.
   The preceding TDD RED run compiled the reflection-based tests but supplied
   no `Assembly-CSharp`: `RED_TEST_CSC_EXIT=0`,
   `RED_REFLECTION_PASSED=0`, `RED_REFLECTION_FAILED=18`, with the expected
   missing-assembly failure for every case. Adding the pure policies changed
   that result to the 18/0 GREEN result above.
3. Two compiler-harness probes initially used the stale
   `1900b0aEDbg.dag/Assembly-CSharp.rsp`. The direct probe exited `1` with
   `CS2001` for two source files no longer present in either worktree
   (`RecordingHandSkeletonVisualizer.cs` and
   `RecordingIndexFingerRays.cs`). A probe that skipped those stale entries
   also exited `1` because that older response predates the W1 Core reference
   and `AsyncPoseFileWriter` source. These were response-cache selection
   failures, not W5 source failures; the current `1300b0aEDbg.dag` response has
   every baseline source and the W1 Core reference.
4. Ran Roslyn `csc.dll` from the installed Unity `6000.5.6f1` toolchain using
   the main project's Unity-generated Bee response/reference set, substituting
   this worktree's baseline sources and adding all 13 W5 runtime sources. This
   was a compiler/reference check only; it did not open Unity. Final result:

   ```text
   W5_RUNTIME_UNITY_CSC_EXIT=0
   W5_RUNTIME_SOURCE_COUNT=98
   ```

   The only warnings were two pre-existing Recorder warnings:
   `RecordingTargetVisualCues.cs` `CS0618` and
   `RecordingPromptBubble.cs` `CS0414`. No warning originated in W5 source.
5. Compiled the 19 existing Editor sources plus the W5 setup/validator against
   the just-built W5 runtime reference assembly. Final result:

   ```text
   W5_EDITOR_STATIC_CSC_EXIT=0
   W5_EDITOR_SOURCE_COUNT=20
   ```

   The five warnings all originated in pre-existing
   `SignVRAssistControlsSetup.cs` / `SignVRDaylightExteriorSetup.cs` uses of
   deprecated Meta/Unity APIs. No warning originated in the W5 Editor tool.
6. Compiled the existing Interaction Editor test source plus both W5 test
   sources using the existing `SignVR.Interaction.Editor.Tests` Bee reference
   set. Final result:

   ```text
   W5_TEST_STATIC_CSC_EXIT=0
   W5_TEST_SOURCE_COUNT=3
   ```

7. Checked every W5 source/folder `.meta` and compared its GUID with all Unity
   asset metadata in this worktree. Final result:

   ```text
   W5_META_FILES_CHECKED=18
   W5_META_MISSING=0
   W5_META_GUID_ISSUES=0
   ```

8. Scanned W5 runtime/Editor sources for scene/asset save calls, forbidden
   product-setting APIs, Recorder state dependencies, `LastArtifact`, new
   asmdefs, `PointingOnly`, and temporary per-frame `NativeArray` allocation.
   Result: zero actionable occurrence. The only textual matches were negative
   validator assertions/comments; the prompt's sole sentence-data source is
   `RecordingPointingSentenceCatalog.CreateSentences()`.

Not run here: Unity import/domain reload, the two Unity Editor test methods,
saved-scene validation, PlayMode, Android build, real-content playback, the
31-clip geometry precheck, or Quest testing.

## 4. Generated files and local prerequisites intentionally not versioned

- Static compiler/test artifacts were produced only below the user's temporary
  directory, including:
  `C:/Users/woshica/AppData/Local/Temp/signvr-w5-pure-final-2c96f0e557ef46afa3e04335f75d2f8e`
  and
  `C:/Users/woshica/AppData/Local/Temp/signvr-w5-final-static-0912d983fccd45479c13332eed59923e`.
  These DLL/PDB/response files are disposable and must not be versioned.
- W2's generated instruction-content tree remains an external build input. By
  default W5 resolves a Run Plan relative artifact path beneath
  `Application.streamingAssetsPath/InstructionContent`; the build/integration
  pipeline must stage the matching Pose bytes there or deliberately configure
  another content root. Every loaded artifact must match the Run Plan SHA-256.
- The W5 scene objects are intentionally absent from this worktree's saved
  scene. After integration, the Orchestrator must run the unsaved setup in
  Unity `6000.5.6f1`, inspect it, and explicitly save the reviewed
  `InteractionLab.unity` change.
- The setup expects Meta Movement's
  `Packages/com.meta.xr.sdk.movement/Shared/Prefabs/Character/StylizedCharacter.prefab`
  and the existing SignVR Chinese font/UI/overlay shader Resources.
- The ignored roadmap file was absent from this worktree; it was read-only from
  the supplied main-workspace copy. It was not copied or changed.

## 5. Contract deviations, integration assumptions, and remaining risks

No incompatible frozen Contract V1 deviation was introduced.

Integration assumptions/interpretations:

- W7 owns Run/phase/task authority and calls `BeginPhase`/`EndPhase`; W5 only
  presents the exact `RunPhasePlan` and emits separate Replay, Give Up, Abort,
  playback, bubble, and pointing events. W5 never disables real task input.
- W6 subscribes to `HitStarted`, `HitEnded`, playback/bubble events, and reads
  `PointingExposureSeconds`; W5 intentionally performs no disk write. Exposure
  includes the approximately 150 ms interval during which the ray/highlight is
  still visibly retained after the last physical hit.
- If both ghost hands simultaneously hit legal targets, W5 displays the nearer
  inferred ray/actual target as the single current hit. This follows the
  contract's singular actual-hit presentation and avoids highlighting all legal
  targets; the Orchestrator should confirm that interpretation during the
  geometry review.
- Logical-to-scene target bindings are derived, not copied, by pairing W1's
  frozen `TaskVariantCatalog` entries with the corresponding target paths in
  the one existing 31-sentence Recorder catalog. The validator rejects catalog
  ID/arity disagreement and incomplete scene bindings.
- Reusing the existing canonical catalog is a data-only Assembly-CSharp
  compatibility dependency on `SignVR.Recording`; no Recorder component or
  state reference is serialized. The prompt/highlight graphics are
  Interaction-native because W4 correctly rejects serialized
  `SignVR.Recording` components from InteractionLab.

Remaining risks:

- Static Roslyn checks are not authoritative Unity import or Test Runner
  results. The saved-scene Editor test is expected to fail until the
  Orchestrator runs W5 setup, reviews, and saves the integrated scene.
- Meta Movement native-handle initialization, the prefab's exact humanoid
  distal/tip endpoint resolution, recorded joint order, final ghost placement,
  and overlay appearance require Unity PlayMode and Quest review.
- No real frozen Pose artifact was loaded here. Android `jar:`
  `UnityWebRequest`, memory use for the largest selected clip, retargeting
  performance, and cleanup on Quest remain unvalidated.
- The required automated geometry precheck over all 31 selected Wang Takes is
  still pending. Any Take that cannot stably infer a legal target must be
  excluded by the upstream candidate/precheck workflow rather than silently
  replaced with full-clip highlighting; all six phase pools must remain
  non-empty.
- Bubble height, HMD billboard readability, minimal control reachability, and
  the independent signer rig's visual treatment need in-headset inspection.

## 6. Recommended Orchestrator review steps

1. Review/integrate only the W5 files listed above, preserving all concurrent
   W6/W7 work and confirming no shared asmdef/Core/scene/settings file entered
   the W5 change set.
2. Let Unity `6000.5.6f1` import and compile the integrated project. Open the
   saved W4 InteractionLab, run
   `Tools > SignVR > Interaction > W5 Configure Instruction Presentation (Unsaved)`,
   inspect the independent signer prefab, both distal-tip pairs, HMD reference,
   all logical target bindings, bubble root, and three distinct controls, then
   explicitly save the reviewed scene. Run the setup a second time and confirm
   it is idempotent.
3. Run
   `Tools > SignVR > Interaction > W5 Validate Instruction Presentation`, the
   complete W4 clean-scene validator, and EditMode tests filtered to
   `SignVR.Interaction.Editor.Tests.Presentation`. Expected W5 count is 20:
   18 pure state cases plus 2 source/saved-scene validator tests.
4. Stage W2's exact generated Wang content into the chosen build content root,
   construct a real W1 Run Plan, and verify successful SHA/joint/EOF Load,
   natural first completion, the 1.0-second bubble, one frozen-artifact Replay,
   and cleanup on early phase completion.
5. Wire W7 to `BeginPhase`, `EndPhase`, `GiveUpPhaseRequested`, and
   `AbortRunRequested`; verify task input remains live before/during/after both
   playbacks. Wire W6 only as an event consumer and confirm no W5 code writes
   capture files.
6. Exercise all three assistance conditions and the independent Give Up/Abort
   semantics in PlayMode, including a replay-start failure and phase exit while
   loading/playing/awaiting the delayed bubble.
7. Run the 31-clip pointing geometry precheck against the saved scene, review
   the per-Take failure list and six non-empty candidate pools, then perform a
   Quest 3 naked-hand smoke for zero-dwell hit, approximately 150 ms loss grace,
   HMD-facing UI, Android content I/O, memory, and frame-time behavior.
8. Re-run Recorder compilation/scene smoke and W4 cleanliness validation to
   confirm the data-only catalog/resource reuse did not install Recorder state
   in InteractionLab or regress the Recorder product.
