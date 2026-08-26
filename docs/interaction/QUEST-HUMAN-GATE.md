# Interaction Quest Human Gate

Updated: 2026-08-26

Use this checklist for the first physical Quest + webcam validation batch. A
check is evidence of device behavior only when the exact APK below was used and
the corresponding Host/device artifact was inspected.

## Frozen build identity

| Item | Required value |
| --- | --- |
| APK | `signvr_unity/Builds/SignVR_Interaction_Local.apk` |
| APK SHA-256 | `94B8D66592BCC9DC5BC849CABE9880C8DFCCAD7947D1335BF508F59119AC8619` |
| Package | `com.signvr.interaction` |
| Quest Build Identity field | `ed00636` |
| Host URL | `http://192.168.1.114:8011` |
| Instruction signer | `wang` |
| Instruction entries | 31, grouped `3/9/3/3/7/6` |

## Human prerequisites

- Connect a real webcam. Windows must show a present Camera device.
- Open `http://127.0.0.1:8011/?mode=interaction` in Chrome, allow camera
  access, select the intended camera, and keep the page open.
- Connect the Quest by USB, wear it once, and accept the USB debugging prompt.
- Keep the Quest connected to this signed-in Host PC so the station always-on
  watcher can reapply the proximity override after a headset reboot.
- Keep the Quest and Host PC on the same LAN as `192.168.1.114`.
- Before collecting evidence, verify that the Quest wall clock agrees with the
  Host PC. Do not accept a Run whose device timestamps are materially wrong.
- Choose one pseudonymous participant ID such as `P001`. Enter the exact same
  value in the Host page and the Quest Start surface.
- Do not delete partial local or Host data during the gate.

The Orchestrator should not proceed until all four Host cards show READY:
Backend, Quest, Camera, and Participant.

## Orchestrator setup

Run these from the repository root using Unity's bundled Android SDK:

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
$apk = 'D:\work\Unity6\VR-Sign-Record-System\signvr_unity\Builds\SignVR_Interaction_Local.apk'
& $adb devices -l
& $adb install -r $apk
& $adb shell am force-stop com.signvr.interaction
& $adb shell am start -n com.signvr.interaction/com.unity3d.player.UnityPlayerGameActivity
```

Record the reported Quest serial and verify the installed package before
starting the participant:

```powershell
& $adb shell dumpsys package com.signvr.interaction | Select-String 'versionCode|versionName'
```

The lab station has a persistent always-on watcher for Quest 3
`2G0YC5ZF84043B`. It sets Android's plugged-in stay-awake value to `7`, applies
Meta's virtual proximity `CLOSE` override, and wakes the headset whenever it
reconnects over ADB. Inspect it without changing state:

```powershell
& .\vr-sign-host\scripts\configure-quest-always-on.ps1 -Mode Status
```

The expected result is `Task=Running`, `proximity=CLOSE`, and
`plugged-in=7`. The watcher runs every 30 seconds and was verified by rebooting
the Quest: the firmware first restored normal proximity behavior, then the
watcher reapplied `CLOSE` after ADB returned. To deliberately restore normal
wear/sleep behavior and remove the scheduled task, connect the Quest and run:

```powershell
& .\vr-sign-host\scripts\configure-quest-always-on.ps1 -Mode Uninstall
```

Do not leave the always-on headset enclosed, covered, or charging unattended;
the override increases battery use and heat.

## Batch A: readiness and PreStart

- [ ] The Quest launches without a crash or missing-scene screen.
- [ ] Naked hands, not controllers, are the active interaction source.
- [ ] The Host Quest card becomes READY and stays fresh.
- [ ] The Host Camera card becomes READY with a visible 1280×720 preview.
- [ ] Enter the same participant ID on Host and Quest.
- [ ] Enter `ed00636` as the Quest Build Identity.
- [ ] Start remains blocked on any readiness or identity mismatch.
- [ ] Pressing Start once creates exactly one Run ID and freezes one Run Plan.
- [ ] The Run Plan records one of only three conditions: text+ray, text-only,
  or neither. Ray-only must never appear.
- [ ] The Run Plan contains one sentence from each of the six phase groups, one
  signer/take per sentence, and one four-digit password with no repeated digit.

Capture a Host screenshot containing the four READY cards and the Run ID.

## Batch B: one six-phase participant Run

For every phase, record pass/fail and any observed deviation:

| Phase | Allowed sentences | Required task evidence |
| --- | --- | --- |
| 1 | 001–003 | Task accepts the correct simplified interaction |
| 2 | 004–012 | Password/button ordering is correct; one wrong input resets the whole current task |
| 3 | 013–015 | Task completion advances exactly once |
| 4 | 016–018 | Task completion or explicit stuck/give-up is recorded correctly |
| 5 | 019–025 | Task state resets cleanly before/after the phase |
| 6 | 026–031 | Final task completes the Run exactly once |

Common checks across all six phases:

- [ ] The signer ghost starts the instruction and the response timer together.
- [ ] The participant can interact at any time; interaction is not locked to
  animation playback.
- [ ] The first instruction plays once automatically.
- [ ] The text bubble appears about one second after the first playback ends,
  remains visible thereafter, and is above the signer ghost's head. Its text
  and visual style match the prior recording UI.
- [ ] The Replay button is disabled until playback completes, can be used at
  most once, and the Run records whether it was used.
- [ ] In ray-enabled condition, a valid hit shows ray/highlight immediately,
  with no dwell. No hit means no ray/highlight.
- [ ] Text and ray/highlight obey the frozen condition for the whole Run.
- [ ] Success, wrong input, stuck/give-up, replay count, and phase duration are
  attributed to the correct phase and sentence.
- [ ] The next phase begins only after the previous presentation/task/capture
  acknowledgement chain completes.

During this internal gate it is acceptable to use simplified object motions;
the task order, validation, reset, timing, and data semantics must remain exact.

## Batch C: terminal and recovery checks

- [ ] Complete one normal Run and observe Completed on both Quest and Host.
- [ ] Start a second disposable Run, press whole-Run Abort, and confirm the
  Quest returns to PreStart without retaining the frozen plan.
- [ ] Host releases the active Run after Completed and after Abort.
- [ ] If Replay has already been used and the participant is still stuck, the
  stuck/give-up path terminates the task as designed and is recorded.
- [ ] A temporary Host/readiness loss does not rerandomize an already frozen
  plan; retry uses the same Run ID and manifest bytes.

Pause/resume and hot reconfiguration soak are follow-up hardening checks, not a
blocker for the first supervised participant Run.

## Artifact acceptance

For every accepted Run, locate the directory under
`vr-sign-host/data/interaction-tests` and verify:

- [ ] `run.manifest.json` exists and matches the Quest-authored frozen plan.
- [ ] `events.jsonl` exists, parses, and contains all six phase transitions.
- [ ] `poses.jsonl` exists and contains tracked HMD plus distinct left/right
  hand samples rather than synthetic fallbacks.
- [ ] `objects.jsonl` exists and contains stable state for the active targets.
- [ ] `summary.json` exists and agrees with events for condition, sentence,
  signer/take, password, success/stuck, Replay, and duration.
- [ ] `webcam.webm` exists, is non-empty, and covers the participant Run.
- [ ] Host ACK reports no missing artifact before Quest local retention may be
  cleared.

Record the Run ID, participant ID, condition, APK SHA-256, Host directory, file
sizes, and final ACK in the W8 report. Do not mark the physical gate complete
from screenshots alone.

## Current physical-gate state

The Quest 3 (`2G0YC5ZF84043B`) is authorized over USB and the frozen APK above
is installed. A cold start reached `InteractionLab` without the prior
`PointableCanvas` / missing-`GraphicRaycaster` assertion or an app crash. The
HIK 1080P Camera is selected in the Chrome Study page and its live 1280x720
preview has been observed.

An accepted participant Run is still open. The Quest clock was corrected to
`2026-08-26`, automatic time remains enabled, `PILOT01` is the selected
anonymous participant ID, and Quest 3 `2G0YC5ZF84043B` is again authorized over
ADB. The `SignVR Quest Always On` scheduled task is running: virtual proximity
is `CLOSE`, plugged-in stay-awake is `7`, and a real reboot test confirmed that
the watcher reapplies the override after ADB reconnects. It depends on this Host
PC remaining signed in and the Quest being connected over USB.

The Host runtime has been restarted and its Interaction storage is writable.
`e265c23` repaired the Study page so a failed default camera still exposes the
available camera selector and the last successful device is remembered. The
HIK camera currently enumerates in Windows, DirectShow, and Chrome, but both
Chrome and an independent FFmpeg capture probe fail to open it. Physically
replug or otherwise reset the HIK device, select it again, and require a live
1280x720 preview before accepting Camera READY. Then wear the headset, enter
`PILOT01` and `ed00636` on Quest, wait for all four Host cards to remain READY,
and execute the physical batches above. Cold-start evidence alone does not
prove naked-hand button activation or artifact completeness.
