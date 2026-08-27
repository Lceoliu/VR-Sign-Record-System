# Interaction Contract V2

Status: current integrated Quest-local engineering contract as of 2026-08-28.
Physical-device observations remain acceptance gates even when the supporting
public API and Editor tooling are present.

## Ownership and execution mode

- Quest is the sole authority for the immutable Run Plan, six-phase lifecycle,
  Experiment Capture, startup recovery, and retained Run artifacts.
- Standalone Study does not require Chrome, an Interaction Host, LAN readiness,
  or webcam capture.
- Start creates one Run ID and freezes the condition, six Task Variants,
  instruction sources, and other resolved plan values. Replay, wrong input,
  reset, Give Up, Abort, and recovery do not rerandomize that plan.
- `InteractionStudyFlowController` is the public orchestration surface.
  `InteractionPhaseCoordinator` validates task input but never replaces or
  directly advances the W1 lifecycle authority.

## Current six-phase tasks

| Phase | Sentences | Current task |
| --- | --- | --- |
| 1 | `001–003` | Select the planned box. |
| 2 | `004–012` | Place the planned coin on the planned plate as one coin-plate pair. |
| 3 | `013–015` | Select the planned picture frame. |
| 4 | `016–018` | Select one of three visible keys through its Touch Target Proxy; the planned key completes the phase. |
| 5 | `019–025` | Press the planned one-, two-, or three-button set directly. An incorrect or repeated button resets only the current set; there is no key prerequisite. |
| 6 | `026–031` | Operate the three breakers in the planned order. |

Current-phase manipulation remains available during instruction playback. One
Replay becomes available after first playback completes. Give Up is available
after that Replay completes and records a Stuck phase. Abort is a separate
whole-Run safety action.

## Pointing and deterministic presentation

For `TextAndPointing`, `GhostPointingDetector` derives the ray from the
Instruction Signer's index-finger bones and exposes a ray/highlight only while
that inferred ray hits a current eligible target, subject to the short loss
grace. It does not synthesize a hit. Public diagnostics include
`PhaseConfigured`, finger-rig completeness, pointing visibility, and the
current hit target ID.

`InteractionDeterministicPresentation` derives doors, buttons, keys, and
breaker visuals from the current task authority. Its public rebuild/reset
operations affect presentation only and do not create another phase or Run
authority.

## Editor-only QA Console

Open `Tools/SignVR/Interaction/Open QA Console` while the Unity Editor is in
Play Mode. The console is compiled in `Assembly-CSharp-Editor` and is excluded
from Quest builds. It shows RunState, current phase, progress, planned targets,
accepted targets, the latest validation result, and public Ghost pointing
diagnostics. Its actions delegate only to the existing public Study Flow,
Phase Coordinator, and deterministic-presentation APIs.

The console can request Start, Replay, Give Up, Abort, correct/wrong
current-phase input (including Phase 2 coin-plate pairs), presentation
rebuild/reset, and a Game View PNG under the ignored `Assets/Screenshots`
directory. These injections are engineering aids; normal bare-hand operation
on Quest remains the acceptance truth.

Completed and Aborted remain in Result Review after safe local sealing until a
headset wearer confirms. The public orchestration command is
`TryAcknowledgeResult`; the console's **Confirm Result** action delegates to
that command. Neither the console nor another adapter may call
`ResetToPreStart` directly. Editor confirmation is useful engineering evidence,
but visible review and normal bare-hand confirmation on Quest remain the
physical acceptance truth.

Once sealing succeeds, Result Review displays the terminal outcome plus the
sealed aggregate summary: exact `Pass`/`Warning`/`Fail` grade, actual sample
rate, minimum HMD/hand tracking validity, required-probe coverage, gap count,
completed phases, interaction errors, replays, stuck phases, duration, and
whether the authoritative five-file set is complete. A missing aggregate is
shown as unavailable rather than silently replaced with a success message.
These review values are a projection of `summary.json`, not a second source of
truth.

## Quest-local sealed artifacts

Completed and Aborted Runs seal exactly five authoritative files beneath:

```text
Application.persistentDataPath/
  interaction-tests/<batch_id>/<participant_id>/<run_id>/
    run.manifest.json
    events.jsonl
    poses.jsonl
    objects.jsonl
    summary.json
```

No upload acknowledgement or webcam file is required to complete the local
Run. Low analytical quality does not discard evidence or prevent an otherwise
structurally valid Run from sealing.

## Summary correctness fields

Each phase summary contains:

- `first_attempt_correct`: compatibility field preserving the original
  first-action-correct meaning.
- `first_attempt_correct_semantics`:
  `legacy_alias_of_first_action_correct`.
- `first_action_correct`: whether the first task input matches the frozen plan
  at that point.
- `first_attempt_success`: whether the phase completes with a correct first
  action and no recorded interaction error/reset.

Consumers must not reinterpret `first_attempt_correct` as whole-phase success.

## Structural completeness and capture quality

`data_completeness` is structural. It records the presence of the five files,
capture gaps, and `quest_artifacts_complete`.

`capture_quality` is a separate analytical assessment:

```json
{
  "measurement_available": true,
  "overall": "Pass",
  "sample_rate": {
    "status": "Pass",
    "target_hz": 20.0,
    "actual_hz": 19.8
  },
  "tracking_validity": {
    "status": "Pass",
    "hmd_rate": 1.0,
    "left_hand_rate": 0.98,
    "right_hand_rate": 0.99
  },
  "required_probe_coverage": {
    "status": "Pass",
    "required_probe_count": 12,
    "rate": 1.0
  },
  "gaps": {
    "status": "Pass",
    "count": 0
  }
}
```

The configured defaults are:

| Metric | Pass | Warning | Fail |
| --- | --- | --- | --- |
| Actual sample rate | `>= 18 Hz` | `>= 15 Hz` | `< 15 Hz` |
| Each of HMD/left/right validity | `>= 0.95` | `>= 0.80` | `< 0.80` |
| Required-probe coverage | `>= 0.99` | `>= 0.90` | `< 0.90` |
| Capture gaps | `0` | `1–2` | `> 2` |

The overall grade is the worst component grade. A recovered or otherwise
unmeasured summary can report `measurement_available: false`; consumers must
check that flag before interpreting the numeric rates.
