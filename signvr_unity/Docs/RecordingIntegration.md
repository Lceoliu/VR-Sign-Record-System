# Recording integration

The cloned recording stack is integrated directly into `VRroom`; no secondary
recording scene or XR rig is loaded.

## Runtime

- `VRroom/VRPlayer/MetaBodyTrackingSource` provides body/hand data.
- Scene-root `RecordingSource` owns `MetaBodyMotionRecorder` and
  `MetaBodyMotionStreamer`.
- `RecordingRuntimeBootstrap` creates one `_Recording` manager for this scene.
- `RecordingCoordinator` is the only capture state machine.
- `RecordingSentenceSequence` contains 31 target-specific entries across six
  fixed viewpoints. The host is authoritative after pairing.
- The upper-left HMD prompt, target outlines, breaker order labels, hand skeleton,
  and two index-tip rays are presentation-only and never alter recorded Pose.
- All catalog targets are fixed at their authored poses. State 2 opens the safe;
  states 5 and 6 open the closet; other states restore closed poses.
- No Touch-controller or in-headset sentence navigation is installed. A host USB
  HID foot pedal/keyboard maps short `Space` to start/stop and a 1.2-second hold
  to retake. The host webpage owns previous/next/jump/scene selection.
- `select_sentence` received while Quest is finalizing/resetting is deferred until
  an idle state.

Pose output:

```text
Application.persistentDataPath/Recordings/{session}/{sentence}/
  {stem}.pose.jsonl
  {stem}.meta.json
  {stem}.meta.json.uploaded
```

Only completed real-pose artifacts enter the upload queue. Editor simulation is
explicitly marked and remains local.

## Host

The host is `VR-Sign-Record-System/vr-sign-host`.

| Port | Protocol | Purpose |
| --- | --- | --- |
| 8000 | HTTP / WebSocket | Operator UI, Take upload, preview |
| 5005 | UDP | Device announcement, ACK, live Pose |
| 5006 | UDP | Quest discovery and recording commands |

`scripts/start-local.ps1` loads the 31-entry
`backend/app/pointing_sentence_catalog.json`. The React console displays the HMD
prompt plus `target_label`, so repeated pointing sentences are distinguishable.
The old 300-entry catalog remains available for the separate dataset.

Host output:

```text
{data_root}/{batch_id}/{round_id}/{sentence_id}/{take_id}/
  {take_id}.pose.jsonl
  {take_id}.meta.json
  {take_id}.camera.webm
```

## Run

```powershell
cd E:\SignVR_Unity\VR-Sign-Record-System\vr-sign-host\backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt

cd ..\frontend
pnpm install
pnpm build

cd ..
.\scripts\configure-station.ps1 -StationId Station-01 -DataRoot E:\SignVRData\Station-01
.\scripts\start-local.ps1
```

Open `http://127.0.0.1:8000`. Quest and workstation must be on the same trusted
LAN. Run `scripts/setup-firewall.ps1` once from elevated PowerShell before device
testing.

## Validate

Unity:

```text
Tools/SignVR/Validate VRroom Player and Physics
Tools/SignVR/Validate Pointing Recording
```

Host:

```powershell
cd vr-sign-host/backend
.\.venv\Scripts\python.exe -m pytest -q

cd ../frontend
pnpm lint
pnpm build
```

The full component ownership and extension contract is in
`POINTING_RECORDING.dev`; the 31-row target table is in
`POINTING_SENTENCE_CATALOG.md`.
