# Recording integration

The Unity recording client from `VR-Sign-Record-System/signvr_unity` is
integrated into the main project under `Assets/Scripts/Recording`.

## Runtime behavior

- `VRroom` is the sole enabled build scene. The legacy
  `SignTrackingRecorder` scene is not loaded or included in the player build.
- `VRroom/VRPlayer/MetaBodyTrackingSource` supplies full-body tracking data.
  The scene-root `RecordingSource` owns `MetaBodyMotionRecorder` and
  `MetaBodyMotionStreamer`, both wired to that provider.
- `RecordingRuntimeBootstrap` only activates for that explicit `VRroom`
  `RecordingSource` and creates one `_Recording` manager when the scene loads.
  It does not create, load, or replace a scene or XR camera rig.
- The coordinator owns start/stop timing. Legacy automatic recording is
  disabled and coordinated takes have no fixed duration.
- Pose samples are written at 30 Hz to:

  `Application.persistentDataPath/Recordings/{session}/{sentence}/`

  A take contains `{stem}.pose.jsonl` and `{stem}.meta.json`. A successful
  upload adds `{stem}.uploaded`.
- The existing `motion_viewer.py` remains compatible with the UDP v1
  skeleton/frame/status stream.
- `VRroom` has no operator canvas; its production start/stop/reset path is the
  paired host gateway (the coordinator methods remain public for a future
  in-headset control surface).
- Local-network HTTP is enabled in Player Settings because the workstation
  service uses `http://<host>:8000` on the trusted recording LAN.

## Host service

The host remains a separate process in
`VR-Sign-Record-System/vr-sign-host`. Its network contract is:

| Port | Protocol | Purpose |
| --- | --- | --- |
| 8000 | HTTP / WebSocket | Operator UI, take uploads, preview |
| 5005 | UDP | Device announcements, ACKs, live pose |
| 5006 | UDP | Quest discovery and recording commands |

First-time setup:

```powershell
cd E:\SignVR_Unity\VR-Sign-Record-System\vr-sign-host\backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt

cd ..\frontend
pnpm install
pnpm build

cd ..
.\scripts\configure-station.ps1 -StationId Station-01 -DataRoot E:\SignVRData\Station-01
```

Run the workstation:

```powershell
cd E:\SignVR_Unity\VR-Sign-Record-System\vr-sign-host
.\scripts\start-local.ps1
```

Then open `http://127.0.0.1:8000`. Quest and workstation must be on the same
trusted local network. Run `scripts/setup-firewall.ps1` from an elevated
PowerShell once before device testing.

## Validation

Unity validation commands:

```powershell
Unity.exe -batchmode -nographics -projectPath E:\SignVR_Unity `
  -executeMethod ConfigureVRRoomPlayer.ValidateSceneForAutomation `
  -quit -logFile validate-vrroom.log
```

Host validation after installing its locked dependencies:

```powershell
cd E:\SignVR_Unity\VR-Sign-Record-System\vr-sign-host\backend
.\.venv\Scripts\python.exe -m pytest -q
```

Replay and JPEG preview components are imported but are not automatically
enabled in `VRroom`; they require a visible retargeted character and a preview
camera. Raw body recording, local metadata, UDP pairing/control, streaming,
and take upload are active without those optional visuals.
