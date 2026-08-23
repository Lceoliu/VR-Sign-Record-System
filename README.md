# VR Sign Record System

VR Sign Record System is a Quest 3 research prototype for recording sign-language motion and synchronized camera video on local workstations. The repository also serves as the foundation for a separate real-time ASR + SLT communication demo.

## Repository structure

- `signvr_unity/` - Unity/Quest application, VR scenes, hand tracking, recording, preview streaming, and take upload.
- `vr-sign-host/` - FastAPI + React local recording workstation, device pairing, sentence/round management, preview, and local file storage.
- `POINTING_RECORDING.dev` - pointing-dataset architecture, extension contract, local validation, and Quest acceptance checklist.

The two directories retain the histories of their original repositories. Local recordings, workstation-specific configuration, Unity build outputs, package caches, and virtual environments are excluded from version control.

## Development boundary

The deployed recording workflow is treated as a stable baseline. New ASR + SLT work should be developed in a separate Unity demo scene and separate runtime modules without changing the recording protocol or take format unless explicitly required.
