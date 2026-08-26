# W9 Local Hardening and Android Build Gate

Updated: 2026-08-26

## Integrated runtime changes

- `1ae5c30`: readiness cancellation is isolated from terminal Run cleanup.
- `9a29f59`: suspended, aborting, and terminal cleanup is bounded, retryable,
  and fail-safe without replaying completed side effects.
- `99790bf`: runtime pointing-ray and target-highlight `LineRenderer` creation
  uses Unity-aware explicit null checks instead of C# `??`.
- `1c1344a`: progress state before the post-hardening Android build.

## Authoritative local validation

```text
W8_EDITMODE=35/35 (a9659beb)
W5_FOCUSED_EDITMODE=19/19
W8_PLAYMODE=10/10 (95fe63cb)
W6_PLAYMODE=8/8 (8bb619d6)
W7_PLAYMODE=11/11 (66c988f6)
W7_EDITMODE=24/24 (ab6697b0)
UNITY_CONSOLE_ERRORS_AFTER_CLEAR=0
INTERACTIONLAB_SHA256=AEF55496BF8E05B9FF417832FDE553DEB4E815D5659229E4EC2786BD3D5B64DE
```

The minimal PlayMode reproduction for runtime pointing cleanup first failed
with `MissingComponentException`, then passed 1/1 (`a0aa4e34`) after the
explicit Unity null checks. The independent injected-`OnDisable` cleanup case
passed 1/1 (`3676e7f0`) after its two expected log messages were matched
exactly; unexpected logs remain test failures.

## Frozen Android artifact

The project-owned `SignVR/Build/Interaction/Android APK` entry completed with
`BuildResult.Succeeded` and restored its temporary build state.

```text
BUILD_SOURCE_HEAD=1c1344a
BUILD_DURATION=00:09:20.6898122
APK=signvr_unity/Builds/SignVR_Interaction_Local.apk
APK_BYTES=504626328
APK_SHA256=7EA5B1A4DEB208D98526D9F54FC180EAA2594FEC79DFE2C5BB865BB7CB7518DF
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

The APK manifest, signature, alignment, ABI, and package identity were read
back with Unity's bundled Android build tools. The packaged instruction
manifest was read directly from the APK; all 31 referenced pose and metadata
entries exist, and every pose byte length and SHA-256 matches its manifest.

All protected files retained their exact pre-build SHA-256 values:

- `Assets/Scenes/InteractionLab.unity`
- `Assets/Settings/Mobile_RPAsset.asset`
- `Assets/XR/Settings/OpenXRPackageSettings.asset`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/URPProjectSettings.asset`

The BuildReport counted 247 errors; all 247 were the known `UnitySkills CJK`
editor UI font-initialization message. No other error was present, and the
Console returned zero errors after clearing build output.

## Remaining gate

No device, webcam-capture, or participant result is claimed here. The exact
physical batch is frozen in [`../QUEST-HUMAN-GATE.md`](../QUEST-HUMAN-GATE.md)
and must use the APK hash and Build Identity above.
