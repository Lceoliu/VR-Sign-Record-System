# W11 Quest Poke Routing and Frozen APK Gate

Updated: 2026-08-26

## Device finding

The first physical Quest cold start exposed a production-only initialization
assertion on `InstructionControlCanvas`:

```text
ISDK_PokeCanvasInteraction
PointableCanvas requires Canvas GraphicRaycaster
```

The Start surface already had a `GraphicRaycaster`; the Replay, GiveUp, and
whole-Run Abort canvas did not. This prevented a truthful naked-hand control
gate even though the local presentation state tests were green.

## Integrated repair

Commit `ed00636` makes both the runtime compatibility path and the saved scene
own the missing prerequisite:

- `WorldSpacePokeCanvas.EnsurePokeInteraction()` first ensures a
  `GraphicRaycaster`, so existing/older canvases self-repair before Meta poke
  configuration runs.
- `InteractionInstructionControls.EnsureVisuals()` ensures the same component
  when it creates or repairs the instruction controls.
- The W5 setup validator now rejects an instruction-controls canvas without a
  `GraphicRaycaster`.
- `InteractionLab.unity` serializes the component on
  `InstructionControlCanvas`.
- An EditMode regression constructs the old canvas shape and proves that the
  runtime repair adds the prerequisite.

No unrelated scene normalization was retained. Two temporary TMP serialization
changes produced during Unity save were restored before integration.

## Authoritative local validation

```text
POKE_REPAIR_REGRESSION=1/1
W5_EDITOR_SETUP=4/4
W5_PRESENTATION_STATE=19/19
W8_EDITMODE=35/35
W8_PLAYMODE=10/10
UNITY_CONSOLE_ERRORS_AFTER_CLEAR=0
INTERACTIONLAB_SHA256=C0823BFA555E45453C721B50CDE4793F140C6E11DF6B1E621A2789BCC19D4362
```

## Frozen Android artifact

The project-owned `SignVR/Build/Interaction/Android APK` entry completed with
`BuildResult.Succeeded` and restored the prior Player Settings, build-scene
list, and scene setup.

```text
BUILD_SOURCE_HEAD=ed00636
BUILD_DURATION=00:08:48.7906449
BUILD_WARNINGS=11
APK=signvr_unity/Builds/SignVR_Interaction_Local.apk
APK_BYTES=476594573
APK_SHA256=94B8D66592BCC9DC5BC849CABE9880C8DFCCAD7947D1335BF508F59119AC8619
PACKAGE=com.signvr.interaction
PRODUCT=SignVR Interaction
VERSION_NAME=1.0.0
VERSION_CODE=1
MIN_SDK=32
TARGET_SDK=34
COMPILE_SDK=34
ABI=arm64-v8a
IL2CPP=True
DEVELOPMENT_DEBUGGABLE=True
APK_SIGNATURE_V2=True
APK_SIGNERS=1
SIGNER_CERT_SHA256=00DDFE275CD3685F60824BFBB9B274A447D8F839761527157D095E11F1136160
ZIPALIGN=PASS
BUILT_SCENE=Assets/Scenes/InteractionLab.unity
INSTRUCTION_CONTENT_FILES=63
INSTRUCTION_MANIFEST_ENTRIES=31
INSTRUCTION_SIGNER=wang
INSTRUCTION_PHASE_COUNTS=3,9,3,3,7,6
INSTRUCTION_SENTENCE_RANGE=001-031
INSTRUCTION_POSE_INTEGRITY_ISSUES=0
SENTENCE_001_SELECTED_TAKE=take_004
SENTENCE_016_SELECTED_TAKE=take_002
```

The APK hash was unchanged before and after the independent read-only audit.
`aapt`, `apksigner`, `zipalign`, and streamed ZIP inspection confirmed package,
SDK, ABI, signature, alignment, single-scene, and all 31 instruction entries.
The 247 BuildReport errors remain the known UnitySkills CJK editor-font
messages; after clearing build output, the Unity Console reported zero errors
and zero warnings.

All six protected files retained their exact pre-build SHA-256 values:

- `Assets/Scenes/InteractionLab.unity`
- `Assets/Settings/Mobile_RPAsset.asset`
- `Assets/XR/Settings/OpenXRPackageSettings.asset`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/URPProjectSettings.asset`

## Quest installation and cold-start evidence

The artifact was installed with `adb install -r` on Quest 3 serial
`2G0YC5ZF84043B`. The package reports version `1.0.0` (`versionCode=1`) and a
new `lastUpdateTime`. A clean logcat capture then showed:

```text
APP_PROCESS_ALIVE=True
RUNTIME_VIEW_READY=2
ASSERTION_EXCEPTION=0
GRAPHIC_RAYCASTER_ERROR=0
POINTABLE_CANVAS_ERROR=0
ISDK_POKE_CANVAS_ERROR=0
FATAL_EXCEPTION=0
APP_CRASH=0
```

The process paused when the unworn headset returned to sleep. Six certificate
verification failures were also present because the Quest wall clock is still
`2025-06-23`; this must be corrected with operator approval before experiment
timestamps are accepted.

## Remaining human gate

Cold start proves that the previous initialization assertion is gone; it does
not yet prove physical naked-hand activation of Replay, GiveUp, Abort, or the
complete six-phase capture chain. Continue with
[`../QUEST-HUMAN-GATE.md`](../QUEST-HUMAN-GATE.md) using only the APK hash and
Build Identity above.
