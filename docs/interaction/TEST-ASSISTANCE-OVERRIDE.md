# Test assistance override

## Purpose and boundary

`InteractionRunController.forceTextAndPointingForTesting` is an explicit test aid for the generated `InteractionLab` scene. The canonical test scene enables it by default together with:

- `runMode = EngineeringLocal`
- `debugOverridesActive = true`
- `engineeringLocalExplicitlyArmed = true`

The start policy additionally requires a debug build. `StandaloneStudy` rejects the flag even if a scene or serialized asset accidentally carries it. This keeps formal Study runs on the randomized three-condition allocation.

The override is applied before the immutable Run Plan is generated. Every affected Plan therefore contains `TextAndPointing`; manifest `assistance_condition`, the `run_created` capture event, orchestration, and presentation all consume that same value. The capture event also records `assignment_mode: "ForcedTextAndPointing"`. The Contract V1 manifest schema is unchanged because its existing canonical `assistance_condition` already represents the effective condition; no incompatible schema revision is needed.

The override changes selection only. It does not synthesize a pointing hit, expand eligible phase targets, alter the immediate hit-on display rule, or change the approximately 150 ms hit-loss grace period.

## Enable or restore Study behavior

The generated test scene is enabled through:

`Tools > SignVR > Interaction > Assistance > Enable Test Text + Pointing`

To prepare a formal Study scene, use:

`Tools > SignVR > Interaction > Assistance > Disable Override for Study`

That command atomically selects `StandaloneStudy` and clears the debug, arm, and force flags. Save the scene afterward. New Run Plans then return to shuffled three-Run blocks containing `TextAndPointing`, `TextOnly`, and `SignOnly` exactly once. The canonical scene generator gives a newly created controller the test default, but preserves either complete valid configuration on later runs. A saved Study switch therefore survives setup and command-line build regeneration.

## Transcript timing

For `TextAndPointing` and `TextOnly`, the Instruction Bubble is shown at the real playback-start boundary, after the first pose has been applied and the signer has been made visible. It remains until phase exit. First-play completion only unlocks Replay; Replay leaves the bubble unchanged. `SignOnly` never shows it.

## Compatibility and validation

The manifest and capture schema version remains 1. Existing Study manifests remain byte-shape compatible; randomized allocation is unchanged when the flag is off. Tests cover forced consecutive Runs, restored randomized blocks, Plan/manifest/capture agreement, both text-bearing conditions, `SignOnly`, Replay idempotence, phase cleanup, start-policy fail-closed behavior, and the generated scene configuration.

Validation on 2026-08-27 with Unity `6000.5.6f1`:

- project and Editor assemblies compiled successfully, with only pre-existing warnings;
- related EditMode selection passed 45/45;
- the focused randomized/forced Plan-manifest-capture selection passed 2/2;
- the new PlayMode assistance/transcript scenario passed 1/1;
- the broader W8 PlayMode class passed 14/18. Two failures require `WaitForEndOfFrame`, which Unity does not drive in batch mode, and two saved-scene cases could not deserialize missing third-party prefabs in this sparse worktree;
- full EditMode passed 849/880, with 5 skipped. Twenty-five project failures were rooted in the same unavailable prefab-backed scene hierarchy, and one installed Unity Skills package test exceeded its own documentation byte budget.

The canonical generator and full saved-scene validator were attempted and failed closed because this worktree does not contain the third-party prefab assets required to reconstruct the `room` and phase targets. The supported W6 automation still saved and validated the four serialized test-mode fields on the existing canonical scene. Unity's automatic OpenXR and ProjectSettings import rewrites were restored; they are not part of this change.
