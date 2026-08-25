# W4 Dual Build Entry and Clean Interaction Scene

Date: 2026-08-25

## Scope completed

- Added explicit Android build entry points:
  - `SignVR.Editor.CommandLineBuild.BuildRecorderAndroid`
  - `SignVR.Editor.CommandLineBuild.BuildInteractionAndroid`
- Kept `BuildAndroid` as a Recorder-only compatibility alias for existing
  automation.
- Recorder builds use only `Assets/Scenes/VRroom.unity`, product name
  `SignVR Recorder`, and application identifier `com.signvr.recorder`.
- Interaction builds use only `Assets/Scenes/InteractionLab.unity`, product
  name `SignVR Interaction`, and application identifier
  `com.signvr.interaction`.
- Build scenes are passed directly through `BuildPlayerOptions.scenes`; neither
  product reads enabled scenes from global Editor Build Settings.
- Build execution snapshots and restores, on both success and exception:
  company/product/version identity, Android application identifier/version
  code, scripting backend, target architecture, insecure HTTP setting, Editor
  Build Settings scenes, and the previously open scene setup.
- Added an idempotent Unity Editor scene tool. On first run it uses
  `AssetDatabase.CopyAsset` to create InteractionLab from VRroom. On later runs
  it updates the existing InteractionLab in place so unknown future
  Interaction Core components and children are preserved.
- Scene sanitation removes all `SignVR.Recording` scene components, the
  Recorder-only roots/UI/ray origins, `MetaBodyMotionRecorder`,
  `MetaBodyMotionStreamer`, `MetaMotionRecorderUI`, and streamer components
  serialized with legacy port `5005`.
- Added a name-based integration seam that does not reference W1 types:
  - `InteractionSceneRoot/RuntimeSystemsAnchor`
  - `InteractionSceneRoot/Anchors/ParticipantSpawnAnchor`
  - `InteractionSceneRoot/Anchors/InstructionSignerAnchor`
  - `InteractionSceneRoot/Anchors/InstructionBubbleAnchor`
  - `InteractionSceneRoot/Anchors/PhaseContentAnchor`
  - `InteractionSceneRoot/Anchors/InteractionUiAnchor`
  - `InteractionSceneRoot/Anchors/ExperimentCaptureAnchor`
- Added validation for build entry signatures, one-scene profiles, independent
  product/package/APK identities, scene path/GUID, required room/XR/hand
  content, anchors, missing scripts, forbidden recording components/objects,
  and serialized port `5005`.
- Added EditMode tests that invoke the build-contract and saved-scene
  validators.
- Updated the existing Quest PowerShell entry to call the explicit Recorder
  method and added an Interaction Quest PowerShell entry.

## Scope deliberately not completed

- Did not change Host code, Interaction Core code, Recording Take/data code,
  `docs/interaction/PROGRESS.md`, Editor Build Settings, or
  `OpenXRPackageSettings.asset`.
- Did not generate `Assets/Scenes/InteractionLab.unity` in this worktree. The
  project requires Unity `6000.5.6f1`; the only local Editor discovered here is
  `2022.3.62f3`. Per Orchestrator direction, generation and authoritative Unity
  validation are deferred to the main worktree rather than opening the project
  in an incompatible Editor.
- Did not run an Android player build or claim Quest/device validation.

## Files changed

Modified:

- `signvr_unity/Assets/Editor/CommandLineBuild.cs`
- `signvr_unity/Assets/Editor/SignVRReleaseSettings.cs`
- `signvr_unity/Tools/Build-Quest.ps1`

Created:

- `signvr_unity/Assets/Editor/InteractionLabContract.cs`
- `signvr_unity/Assets/Editor/InteractionLabContract.cs.meta`
- `signvr_unity/Assets/Editor/InteractionLabSceneTool.cs`
- `signvr_unity/Assets/Editor/InteractionLabSceneTool.cs.meta`
- `signvr_unity/Assets/Editor/InteractionLabValidator.cs`
- `signvr_unity/Assets/Editor/InteractionLabValidator.cs.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/SignVR.Interaction.Editor.Tests.asmdef`
- `signvr_unity/Assets/Tests/EditMode/Interaction/SignVR.Interaction.Editor.Tests.asmdef.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/DualBuildAndInteractionLabTests.cs`
- `signvr_unity/Assets/Tests/EditMode/Interaction/DualBuildAndInteractionLabTests.cs.meta`
- `signvr_unity/Tools/Build-InteractionQuest.ps1`
- `docs/interaction/reports/W4-dual-build-clean-scene.md`

## Commands and exact results

No Git command was run.

1. Read `ProjectSettings/ProjectVersion.txt` and Unity Hub editor inventory.
   Result: project is `6000.5.6f1`; this worktree can access only Unity
   `2022.3.62f3`.
2. Inspected `VRroom.unity` with read-only PowerShell/`rg` queries. Result:
   one `RecordingViewpoints` root with sentence/viewpoint components; one
   `RecordingSource` with `MetaBodyMotionRecorder`,
   `MetaBodyMotionStreamer`, and `remotePort: 5005`; reusable `VRPlayer`,
   `PlayerSpawnPoint`, `VRFloorCollision`, and `MetaBodyTrackingSource` content
   is present.
3. Parsed all changed/new C# with Roslyn `CSharpSyntaxTree`. Result:
   `syntax_errors=0` for every Editor and EditMode test source.
4. Ran an in-memory Roslyn semantic compilation of all W4 Editor C# against
   the installed Unity 2022 managed API assemblies, with stubs only for the two
   existing Recorder validator classes. Result: `editor_semantic_errors=0`.
   This is a useful compatibility/static check, not a substitute for compiling
   in project Unity `6000.5.6f1` with project packages.
5. Parsed `Build-Quest.ps1` and `Build-InteractionQuest.ps1` with
   `System.Management.Automation.Language.Parser`. Result: `parse_errors=0`
   for both scripts.
6. Parsed the new test asmdef as JSON and checked all six new Unity meta GUIDs
   under `Assets`. Result: valid JSON; each new GUID occurs exactly once.
7. Read `ProjectSettings/EditorBuildSettings.asset` after implementation.
   Result: it remains Recorder-only with the sole enabled scene
   `Assets/Scenes/VRroom.unity`; the new product build entries do not depend on
   that global list.

Not run in this worktree:

- Unity project compilation
- InteractionLab generation
- InteractionLab Unity validator
- EditMode tests
- Recorder or Interaction Android build
- Quest hand-tracking/device validation

## Main-worktree Unity steps

Run these after integrating the W4 files into the main worktree. Paths below
are relative to `signvr_unity`.

1. Generate/update and save the clean scene using either the Editor menu
   `Tools > SignVR > Interaction > Generate or Update InteractionLab` or:

   ```powershell
   & $Unity6000 `
     -batchmode -quit `
     -projectPath $ProjectRoot `
     -executeMethod SignVR.Editor.Interaction.InteractionLabSceneTool.GenerateOrUpdateSceneForAutomation `
     -logFile Logs/W4-GenerateInteractionLab.log
   ```

   Integrate the Unity-created `Assets/Scenes/InteractionLab.unity` and its
   `.meta`; they are intended versioned scene assets, not ignored local output.

2. Run the complete validator:

   ```powershell
   & $Unity6000 `
     -batchmode -quit `
     -projectPath $ProjectRoot `
     -executeMethod SignVR.Editor.Interaction.InteractionLabValidator.ValidateAllForAutomation `
     -logFile Logs/W4-ValidateInteractionLab.log
   ```

3. Run W4 EditMode tests:

   ```powershell
   & $Unity6000 `
     -batchmode -quit `
     -projectPath $ProjectRoot `
     -runTests `
     -testPlatform EditMode `
     -testFilter SignVR.Interaction.Editor.Tests `
     -testResults Logs/W4-EditMode-results.xml `
     -logFile Logs/W4-EditMode.log
   ```

4. Proportionally smoke both build paths. The wrappers are:

   ```powershell
   .\Tools\Build-Quest.ps1
   .\Tools\Build-InteractionQuest.ps1
   ```

   Direct automation may instead execute
   `SignVR.Editor.CommandLineBuild.BuildRecorderAndroid` or
   `SignVR.Editor.CommandLineBuild.BuildInteractionAndroid` and pass
   `-buildPath`, `-recorderBuildPath`, or `-interactionBuildPath`.

5. After both a successful and an intentionally failed validation/build
   attempt, confirm the original Player Settings, Editor Build Settings, and
   open scene setup were restored. The code restores each setting independently
   and reports aggregate restore failures in the Unity log.

## Generated/local prerequisites

- Pending versioned output: `Assets/Scenes/InteractionLab.unity` plus its Unity
  generated `.meta`, created in the main worktree by the method above.
- Expected ignored outputs: `Builds/`, `Logs/`, EditMode result XML, and Android
  APKs.
- No generated scene or APK was produced locally by W4.

## Contract deviations and integration assumptions

- No frozen Interaction Contract V1 deviation.
- `RecordingRuntimeBootstrap` is a static runtime initializer, not a serialized
  scene component. Its existing implementation opts in only when the loaded
  scene path/name is `VRroom`. InteractionLab has a distinct enforced path/name
  and contains no `MetaBodyMotionRecorder`, so the bootstrap cannot install the
  Recorder stack there. The validator also forbids any future serialized
  component with that type name.
- W1 and later workers should attach their runtime objects/components beneath
  the name-based anchors above. The update tool preserves unknown components
  and children and therefore remains safe to rerun after those types exist.
- The source VRroom scene is copied only when InteractionLab does not yet exist.
  Later runs sanitize and repair anchors in place rather than overwriting
  integrated Interaction work with a fresh Recorder scene.

## Risks

- OpenXR and Meta project settings remain shared by both products. W4 did not
  switch global OpenXR settings. If Interaction naked-hand requirements differ
  from Recorder settings, this cannot be isolated by the two build identities
  alone and needs an explicit Orchestrator-level configuration decision plus
  regression validation of both products.
- Static compilation used Unity 2022 reference assemblies because project Unity
  was unavailable here. Only the main-worktree Unity 6000 compile/test result is
  authoritative.
- Naked-hand interaction behavior, Android package coexistence, file I/O, and
  performance still require Quest validation; none is claimed here.

## Recommended Orchestrator review

1. Review `CommandLineBuild` state restoration, especially failure paths and
   the intentional Recorder compatibility alias.
2. Generate InteractionLab before running the scene EditMode test; inspect the
   saved hierarchy and confirm W1 content attached to anchors is preserved on a
   second generator run.
3. Review the sanitation allowlist/denylist against any W1/W2 components added
   during integration. The namespace rule removes every serialized
   `SignVR.Recording` component by design.
4. Verify Recorder still builds as `com.signvr.recorder` and Interaction installs
   alongside it as `com.signvr.interaction`.
5. Confirm `EditorBuildSettings.asset`, `OpenXRPackageSettings.asset`, existing
   Recording Take data/code, Host, and `PROGRESS.md` remain outside the W4
   integration change.
