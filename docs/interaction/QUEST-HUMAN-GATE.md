# Interaction Quest Human Gate — Local Study Mode

Updated: 2026-08-28

This is the physical acceptance checklist for the Quest-authoritative local
Interaction flow. Chrome, an Interaction Host, LAN readiness, and webcam
capture are not prerequisites. Unity's Editor-only QA Console may accelerate
engineering checks, but normal bare-hand Quest operation remains the final
truth.

## Fill in the build under test

Do not reuse an earlier pilot hash or commit value. Fill this table from the
exact APK and release record used for this run.

| Item | Runtime value |
| --- | --- |
| APK path | `<fill at run time>` |
| APK SHA-256 | `<fill at run time>` |
| Source commit/build identity | `<fill at run time>` |
| Package | `com.signvr.interaction` |
| Quest serial | `<fill at run time>` |
| Test UTC/local time | `<fill at run time>` |
| Operator | `<fill at run time>` |
| Batch / participant ID | `<fill at run time>` |

Record the APK hash immediately before installation:

```powershell
$apk = '<absolute-path-to-SignVR-Interaction.apk>'
Get-FileHash -LiteralPath $apk -Algorithm SHA256
```

## Device preparation and ADB evidence

Use Unity's bundled Android SDK or another known ADB of the same device:

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
$apk = '<absolute-path-to-SignVR-Interaction.apk>'
& $adb devices -l
& $adb install -r $apk
& $adb shell dumpsys package com.signvr.interaction |
    Select-String 'versionCode|versionName'
& $adb shell am force-stop com.signvr.interaction
& $adb shell am start -n com.signvr.interaction/com.unity3d.player.UnityPlayerGameActivity
```

In a second terminal, retain a timestamped Unity/Android log for the complete
gate. Start it before the Run and stop it after the files have sealed:

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
& $adb logcat -c
& $adb logcat -v threadtime 'Unity:I' 'AndroidRuntime:E' '*:S' |
    Tee-Object -FilePath ".\interaction-quest-$stamp.log"
```

Do not delete partial local data. If the headset is kept awake by an ADB or
proximity override, supervise charging and temperature and restore normal
sleep behavior after the gate.

## A. Cold start and PreStart

- [ ] The application reaches the Interaction scene without a crash or
  missing-scene screen.
- [ ] Naked hands, not controllers or QA injection, are the active participant
  input source.
- [ ] Startup partial-Run recovery finishes or reports an actionable failure.
- [ ] A pseudonymous Participant Session ID is visible/configured without a
  Host page.
- [ ] Start creates exactly one Run ID and one immutable six-phase Run Plan.
- [ ] The plan assigns only `TextAndPointing`, `TextOnly`, or `SignOnly` for the
  complete Run; ray-only never appears.
- [ ] Starting, retrying a blocked action, Replay, or wrong input does not
  replace the Run ID or rerandomize targets.

Optional Editor preparation: open
`Tools/SignVR/Interaction/Open QA Console` in Play Mode to inspect public state
and Ghost diagnostics. Do not count a QA-injected success as evidence for any
bare-hand checkbox below.

## B. Six-phase bare-hand Run

For every phase verify first playback, optional Replay, task feedback, wrong
input, reset behavior, and exactly-once advance. Record the planned target IDs
before manipulating the scene.

### Phase 1 — planned box

- [ ] All relevant boxes are visible and reachable.
- [ ] Touching a wrong box produces error feedback without advancing.
- [ ] Touching the planned box produces safe-door feedback and advances once.

### Phase 2 — coin and plate stability

- [ ] All three coins and three plates are visible with correct logical IDs.
- [ ] A grabbed coin remains stable in the participant's hand; it does not
  jitter, teleport, fall through the hand, or retain an old grab owner.
- [ ] Releasing the planned coin on the planned plate yields one accepted
  coin-plate pair and stable snapped presentation.
- [ ] A wrong coin or wrong plate is rejected without moving to Phase 3.
- [ ] Repeated contact/release does not produce duplicate completion.

### Phase 3 — picture frame

- [ ] The signer instruction and planned frame agree with the frozen plan.
- [ ] A wrong frame produces feedback without advancing.
- [ ] The planned frame completes exactly once and exposes the next
  deterministic presentation state.

### Phase 4 — three direct key choices

- [ ] All three candidate keys remain visible; there is no chest-button input
  sequence before key selection.
- [ ] Each key has a nearby participant-visible Touch Target Proxy with the
  same semantic target identity.
- [ ] One physical proxy contact produces at most one interaction step.
- [ ] A wrong key gives feedback but leaves Phase 4 active.
- [ ] The planned key completes Phase 4 and the released-key/chest presentation
  matches that planned key.

### Phase 5 — direct cabinet buttons

- [ ] Phase 5 begins without a key prerequisite.
- [ ] The cabinet doors and button feedback are in their authored/reset start
  state on phase entry.
- [ ] Each planned button visibly acknowledges one accepted press.
- [ ] An incorrect or repeated button resets current button progress and all
  Phase 5 button feedback, without reintroducing a key task.
- [ ] Re-entering the correct one-, two-, or three-button set completes once;
  cabinet-door feedback matches completion.

### Phase 6 — signer pointing and downward breakers

- [ ] The three breaker targets are reachable and their authored activation is
  a clear downward operation.
- [ ] The planned breaker order in the Run Plan matches the instruction.
- [ ] An out-of-order breaker resets progress and visible breaker feedback.
- [ ] Under `TextAndPointing`, `GhostPointingDetector` reports
  `PhaseConfigured`, a complete index-finger rig, and the current real hit.
- [ ] Ray and highlight appear only when the signer fingertip ray actually
  intersects a current eligible target; no hit produces no fabricated visual.
- [ ] Under `TextOnly` or `SignOnly`, pointing visibility follows the assigned
  condition.
- [ ] The correct downward breaker order completes the Run exactly once.

## C. Replay, Give Up, Abort, and result review

- [ ] Replay is unavailable before first playback completion, available once
  afterward, and cannot be consumed twice in one phase.
- [ ] Give Up is unavailable until the allowed Replay completes; then it
  records Stuck and advances without rewriting the Run Plan.
- [ ] Abort can be requested for safety from an active Run and retains partial
  evidence.
- [ ] A Completed Run reaches a participant-visible result review only after
  its five local artifacts have sealed.
- [ ] Result Review visibly shows the sealed `Pass`/`Warning`/`Fail` grade,
  capture diagnostics, flow counters, duration, and `5/5` local-file status;
  it never reports missing statistics as a successful measurement.
- [ ] A person wearing the headset explicitly confirms the Completed review
  before the application returns to PreStart.
- [ ] Repeat with a disposable Run: Aborted review shows the abort outcome and
  is explicitly confirmed before returning to PreStart.

The Editor QA Console's **Confirm Result** action must use the same public
`TryAcknowledgeResult` authority and must never reset lifecycle state directly.
It is engineering evidence only: the two physical checks above still require a
visible Result Review and normal bare-hand confirmation by the headset wearer.

## D. Pull the Quest-local Run artifacts

The Android external persistent-data root for the Interaction package is:

```text
/storage/emulated/0/Android/data/com.signvr.interaction/files/
```

List and pull the complete local Interaction tree after the Run seals:

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
$deviceRoot = '/storage/emulated/0/Android/data/com.signvr.interaction/files/interaction-tests'
$pullRoot = Join-Path (Get-Location) ('quest-interaction-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
& $adb shell ls -la $deviceRoot
& $adb pull $deviceRoot $pullRoot
```

For each accepted Completed or Aborted Run, locate:

```text
interaction-tests/<batch_id>/<participant_id>/<run_id>/
```

and verify exactly the authoritative five-file set:

- [ ] `run.manifest.json` exists, parses, and contains the frozen six-phase
  plan and the observed Run ID.
- [ ] `events.jsonl` exists, every non-empty line parses, and terminal plus
  phase events agree with the observed Run.
- [ ] `poses.jsonl` exists and contains actual HMD and distinct left/right hand
  samples rather than synthetic fallbacks.
- [ ] `objects.jsonl` exists and covers required planned probes.
- [ ] `summary.json` exists, parses, and agrees with the manifest/events.
- [ ] No Host ACK or `webcam.webm` is required or expected.

Do not remove the Quest copy until the pulled copy has been parsed and backed
up under the lab's retention policy.

## E. Summary semantics and quality acceptance

For every phase in `summary.json`:

- [ ] `first_attempt_correct_semantics` equals
  `legacy_alias_of_first_action_correct`.
- [ ] `first_attempt_correct` equals `first_action_correct`; the former is a
  compatibility alias, not whole-phase success.
- [ ] `first_attempt_success` is true only when the phase completed without a
  wrong input or task reset.

Check `data_completeness` separately from `capture_quality`:

- [ ] `data_completeness.quest_artifacts_complete` is true for the sealed
  five-file set.
- [ ] `capture_quality.measurement_available` is true for a normally captured
  Run.
- [ ] `capture_quality.sample_rate.actual_hz` and status are plausible.
- [ ] HMD, left-hand, and right-hand validity rates and combined status are
  recorded.
- [ ] Required-probe count, coverage rate, and status are recorded.
- [ ] Gap count/status agree with `capture_gap` evidence.
- [ ] Overall `Pass`, `Warning`, or `Fail` equals the worst quality component.
- [ ] A Warning/Fail quality grade did not delete files or prevent safe sealing;
  retain the Run and record the analytical exclusion decision separately.

Record the Run ID, artifact directory, five file sizes, terminal outcome,
quality grade, APK SHA-256, source/build identity, Quest serial, log filename,
and every failed checkbox in the gate report.
