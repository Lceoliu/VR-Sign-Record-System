# Quest First-Person Video Capture Assessment

Updated: 2026-08-27

## Scope and project facts

This note evaluates recording the player's first-person view during one
standalone Interaction Run on Quest 3. It assumes the current project baseline:

- Unity `6000.5.6f1`
- Meta XR SDK `205.0.0`
- URP `17.5.0`
- Android `minSdk 32`, `targetSdk 34`, IL2CPP/arm64
- one Run contains six phases
- the 180-second phase timeout is log-only and does not end or advance a phase

The last point means storage planning must use an expected Run duration plus a
safety margin; the current contract does not impose a hard maximum recording
duration.

## Executive assessment

Recording is feasible, but the right implementation route matters much more
than the codec settings.

For an unattended study build that must start and stop with the Run, the best
candidate is an Android plugin using Quest/Horizon OS `MediaProjection` to feed
an Android hardware video encoder and write H.264/MP4. This captures the user's
composited point of view without asking Unity to render the scene a second
time. Its expected runtime cost is **low to moderate**, storage is predictable,
and production implementation difficulty is **medium-high**.

For an immediate pilot, Meta Quest Developer Hub (MQDH) recording/casting is
the lowest-effort option. It is useful for measuring real device cost and image
quality before building the plugin, but it is operator-driven and does not
naturally become an atomic artifact in the existing Run directory.

Do not plan on Unity Recorder for the installed Quest application: Unity states
that Recorder is Editor-only and unavailable in standalone players/builds.
Avoid a Unity `RenderTexture -> AsyncGPUReadback -> managed byte array ->
encoder` pipeline for full-duration study capture unless profiling disproves
the expected bandwidth and frame-time risk.

## Sourced platform facts

This section contains facts stated by first-party documentation, not project
performance predictions.

### Unity capture facilities

- [Unity Recorder 5.1](https://docs.unity3d.com/Packages/com.unity.recorder@5.1/manual/index.html)
  captures in Play mode in the Editor. Unity's Recorder documentation states
  that the package is Editor-only and cannot be used in a standalone player or
  standalone build. Unity 6 lists Recorder 5.1.3 as the released package for
  the 6000.0 editor in the
  [Unity 6 package manual](https://docs.unity3d.com/6000.0/Documentation/Manual/com.unity.recorder.html).
- [`ScreenCapture.CaptureScreenshotIntoRenderTexture`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ScreenCapture.CaptureScreenshotIntoRenderTexture.html)
  copies the Game view into a `RenderTexture`. Unity explicitly presents it as
  a way to combine screen capture with asynchronous GPU readback so less time is
  spent on the main thread.
- [`AsyncGPUReadback.Request`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.Request.html)
  retrieves a GPU resource asynchronously. Unity also warns that forcing a
  request with
  [`WaitForCompletion`](https://docs.unity3d.com/2023.2/Documentation/ScriptReference/Rendering.AsyncGPUReadbackRequest.WaitForCompletion.html)
  significantly degrades CPU and GPU performance.

These Unity APIs provide frames, not a supported runtime MP4 recording pipeline.
The application would still need to encode, timestamp, mux, finalize, and
recover the output.

### Quest system recording and casting

- Meta says MQDH can record a monoscopic or stereoscopic MP4 directly from a
  connected Quest. The direct Device Action recording is limited to three
  minutes; its settings include examples such as 1024p/36 fps and 1080p/36 fps,
  and a selectable 5-40 Mbps bitrate. This direct mode does not record audio.
- MQDH Cast can record at the source resolution, optionally including system
  and gameplay audio but not microphone audio. Meta says higher target frame
  rates may affect in-headset performance, lowering maximum resolution can
  improve performance, and higher bitrate increases file size. Meta's `MAX`
  capture estimate is about 600 MB/min for VR and 1.2 GB/min for MR.
- Meta recommends recording from Cast's record control rather than screen-
  grabbing the desktop cast window with OBS, because the former encodes at the
  source resolution and provides smoother frame pacing and higher bitrate.

Source: [Meta Quest Developer Hub debugging tools](https://developers.meta.com/horizon/documentation/unreal/ts-mqdh-media/).

Android's ADB `screenrecord` is another developer-only operator route. The
official documentation says it writes MPEG-4, defaults to 20 Mbps, has a
three-minute maximum, and records no audio. It supports requested resolution
and bitrate when the device AVC encoder supports them. Source:
[Android Debug Bridge: record a video](https://developer.android.com/tools/adb#screenrecord).

### In-app MediaProjection and hardware encoding

- Meta documents Android's Media Projection API as supported on Horizon OS for
  capturing the user's whole view or a single panel. Meta distinguishes it from
  the Passthrough Camera API: Media Projection is the appropriate API for
  casting the user's POV including application UI, whereas Passthrough Camera
  provides raw forward-facing RGB camera imagery for CV/ML. Sources:
  [AOSP features on Horizon OS](https://developers.meta.com/horizon/documentation/android-apps/features-overview/)
  and
  [Passthrough Camera API overview](https://developers.meta.com/horizon/documentation/unity/unity-pca-overview/).
- Android `MediaProjection` creates a `VirtualDisplay` whose output is rendered
  to a supplied `Surface`. The capture size is explicitly configurable, so the
  app can trade quality for load. Source:
  [Android media projection guide](https://developer.android.com/media/grow/media-projection).
- A `MediaCodec` video encoder can accept an input `Surface`; frames submitted
  to that surface pass automatically to the codec rather than through
  application-accessible input buffers. Encoded samples can be written to MP4
  with `MediaMuxer`. Sources:
  [`MediaCodec`](https://developer.android.com/reference/android/media/MediaCodec)
  and [`MediaMuxer`](https://developer.android.com/reference/android/media/MediaMuxer).
- `MediaRecorder` is a higher-level alternative that can also use a surface as
  its video source. Its documentation notes that requested frame rate is not
  guaranteed when limited by hardware/encoder capacity. Source:
  [`MediaRecorder`](https://developer.android.com/reference/android/media/MediaRecorder).
- Because this project targets Android 14/API 34, each MediaProjection capture
  session requires fresh user consent. A projection instance may create only
  one recording virtual display. The app must register a stop callback and
  release its virtual display and surfaces. Android 14 also requires a
  foreground service declared with type `mediaProjection` and the corresponding
  foreground-service permissions. Sources:
  [Android 14 MediaProjection behavior changes](https://developer.android.com/about/versions/14/behavior-changes-14#media-projection)
  and
  [MediaProjection foreground-service requirements](https://developer.android.com/develop/background-work/services/fgs/service-types#media-projection).
- The screen-capture consent token grants screen capture, not system-audio
  capture. Audio requires a separate design and permission path. Source:
  [`MediaProjection` API](https://developer.android.com/reference/android/media/projection/MediaProjection).

Meta's newer
[Virtual Camera Publisher](https://developers.meta.com/horizon/documentation/unity/virtual-camera-publisher-sdk-unity/)
is available on Horizon OS v81+ and lets a Unity app publish up to three custom
cameras for system recording, casting, and livestreaming. It is valuable for a
spectator view, but is not necessary when the desired output is simply the
headset user's existing POV.

## Route comparison

| Route | Captures | Automation with Run | Expected Quest cost | Implementation | Suitability |
| --- | --- | --- | --- | --- | --- |
| MQDH direct record / ADB `screenrecord` | System headset view | External/manual; three-minute limit | Low-moderate, hardware-encoded, but must be measured | Very low | Short diagnostic clips only |
| MQDH Cast + source recording | System headset view; Cast supports game/system audio | External/operator-driven | Low-moderate plus stream/cast work | Very low | Best first experiment and supervised pilot |
| In-app `MediaProjection -> MediaRecorder` | Composited user POV | Yes | Low-moderate expected | Medium | Fastest in-app prototype |
| In-app `MediaProjection -> MediaCodec -> MediaMuxer` | Composited user POV | Yes; precise metadata/lifecycle control | Low-moderate expected | Medium-high | Best production control |
| Existing-eye texture -> native encoder surface | Application eye image | Yes | Potentially low if genuinely zero-copy | High/very high | Only if MediaProjection cannot meet requirements |
| Extra Unity camera -> RenderTexture -> native surface | Custom mono/spectator view | Yes | High: extra scene render plus encode | High | Use only when a custom viewpoint is required |
| RenderTexture -> AsyncGPUReadback -> CPU encoder/input | Unity-rendered view | Yes | High bandwidth and CPU/GPU synchronization risk | Medium-high | Poor default for on-device VR |
| Unity Recorder | Editor Game view | No runtime support | Not applicable | Not applicable | Reject for Quest APK |

`MediaRecorder` is simpler, while `MediaCodec + MediaMuxer` gives better control
over timestamps, keyframes, encoder errors, file segmentation, and the metadata
needed to align video with `events.jsonl`. Both require a Unity Android bridge
and Android lifecycle work.

## Storage estimates

### Exact calculation

For a fixed video bitrate, decimal file size is approximately:

```text
MB per minute = bitrate_Mbps * 60 / 8 = bitrate_Mbps * 7.5
GB per Run    = bitrate_Mbps * duration_minutes * 7.5 / 1000
```

MP4 container overhead is small relative to the video stream. Audio, if added
later, adds its own bitrate. The table below excludes audio and reserves no
filesystem safety margin.

| Video bitrate | MB/min | 10-minute Run | 20-minute Run | 30-minute Run | 100 x 20-minute Runs |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 4 Mbps | 30 MB | 0.30 GB | 0.60 GB | 0.90 GB | 60 GB |
| 6 Mbps | 45 MB | 0.45 GB | 0.90 GB | 1.35 GB | 90 GB |
| 8 Mbps | 60 MB | 0.60 GB | 1.20 GB | 1.80 GB | 120 GB |
| 10 Mbps | 75 MB | 0.75 GB | 1.50 GB | 2.25 GB | 150 GB |
| 20 Mbps | 150 MB | 1.50 GB | 3.00 GB | 4.50 GB | 300 GB |
| 40 Mbps | 300 MB | 3.00 GB | 6.00 GB | 9.00 GB | 600 GB |

Meta's documented `MAX` VR estimate, 600 MB/min, is roughly equivalent to an
80 Mbps aggregate rate and is excessive for routine study capture.

### Engineering recommendation

Start the experiment at **1280x720 or approximately 1024-square, 30 fps,
H.264, 6 Mbps**. Also test an 8 Mbps variant. At a 20-minute planning duration,
this is approximately **0.9-1.2 GB per Run**. Keep at least 20% extra free space
beyond the calculated batch requirement, and transfer then delete verified
videos between study blocks.

This bitrate/resolution recommendation is an engineering starting point, not a
Quest guarantee. The sign-language target in this application makes hand-shape
and finger-motion legibility more important than in ordinary gameplay footage;
the final bitrate must be selected from real captured samples, not screenshots.

### Why an uncompressed Unity path is unattractive

These are arithmetic estimates for RGBA32 frame traffic before encoding:

| Frame size and rate | Raw bytes per second | Raw bytes per minute |
| --- | ---: | ---: |
| 1280 x 720 at 30 fps | 110.6 MB/s | 6.64 GB/min |
| 1920 x 1080 at 30 fps | 248.8 MB/s | 14.93 GB/min |
| 1024 x 1024 at 30 fps | 125.8 MB/s | 7.55 GB/min |

The output MP4 would still be small after compression, but a managed readback
pipeline must move and possibly color-convert this raw data first. A surface-fed
hardware encoder avoids routing every RGBA frame through managed memory.

## Performance assessment

No first-party document gives a transferable “Quest 3 recording costs X ms”
number for this application. The following are engineering judgments that must
be verified on the actual build:

- **MediaProjection plus surface-fed hardware H.264:** low-moderate risk. It
  avoids a second Unity scene render and avoids CPU RGBA readback. Remaining
  costs include compositor copy/scale, hardware encoding, memory bandwidth,
  file I/O, and thermal/power load.
- **MQDH/system recording:** low-moderate risk and the best proxy for the above.
  Meta explicitly warns that higher capture frame rates may affect in-headset
  performance and that lower resolution may improve it.
- **Extra Unity camera:** high GPU risk. It introduces another cull/render pass
  for the scene in addition to the XR view, then still incurs encoding cost.
- **AsyncGPUReadback to CPU:** high CPU/memory-bandwidth risk for continuous
  video. Asynchronous completion reduces main-thread blocking but does not
  eliminate the bytes transferred or encoding work. Any synchronous wait is a
  particular red flag.
- **Storage writes:** generally smaller than raw-frame movement at 6-8 Mbps, but
  long sequential writes still add power/thermal load and need low-space/error
  handling.

Measure a full representative Run, not a short empty-scene clip. Meta's
[OVR Metrics Tool](https://developers.meta.com/horizon/documentation/unity/ts-ovrmetricstool/)
can record FPS, stale frames, CPU/GPU utilization, app GPU time, memory, and
thermal/throttling metrics to CSV. Compare recording-off and recording-on Runs
with identical content and fixed device performance settings.

Suggested acceptance gates:

- no regression in the headset's required application frame rate
- no sustained increase in stale frames or thermal throttling
- no lost/duplicated Run start-stop transitions
- video timestamps align with `events.jsonl` within the chosen tolerance
- output remains playable after normal completion, abort, pause/resume, and
  headset removal
- low-storage and encoder-init failures abort video cleanly without losing the
  existing JSONL/summary capture
- sign and hand details remain readable at the selected crop, eye, resolution,
  bitrate, and frame rate

## Implementation difficulty and schedule estimates

These are engineering estimates for one developer already familiar with the
project; they are not commitments from Unity, Meta, or Android.

| Deliverable | Estimate | Main work |
| --- | ---: | --- |
| MQDH capture A/B performance and quality experiment | 0.5-1 day | Record matched Runs at 5/8/10 Mbps and two resolutions; export OVR Metrics |
| `MediaProjection + MediaRecorder` proof of concept | 3-5 development days | Android plugin, consent activity result, foreground service, surface, MP4 start/stop |
| Production integration using `MediaCodec + MediaMuxer` | 1.5-3 weeks | Run lifecycle, timestamps, errors, app pause/stop, file placement, manifest/summary fields, device tests |
| Crash-tolerant rolling/phase segments and recovery | +3-7 days | Segment policy, finalization, indexing, recovery and merge/analysis workflow |
| Custom zero-copy Unity texture to encoder surface | 3-6 weeks | Vulkan/OpenGL interop, XR texture/crop correctness, native plugin lifecycle, device-specific validation |

The main complexity is not starting H.264. It is producing a trustworthy Run
artifact under real Android lifecycle conditions. A normal MP4 must be finalized
on stop; power loss or process death can leave the current file unusable. If
partial video is scientifically important, use bounded segments (for example,
per phase or rolling time windows), retain already finalized segments, and
record segment start/end monotonic timestamps in the Run metadata.

## Recommended design for this project

1. **Run a no-code device experiment first.** Use MQDH source recording at
   30/36 fps, one eye, approximately 720p/1024p, at 6 and 8 Mbps. Capture the
   same representative Run without recording and with each setting. Review hand
   legibility and OVR Metrics CSVs.
2. **Prototype in-app MediaProjection with MediaRecorder.** Request consent on
   the Start surface before the Run begins, so the system dialog cannot appear
   during phase 1. Record one monoscopic user-POV stream without audio.
3. **Move to MediaCodec/MediaMuxer only if needed.** Prefer it when precise
   timestamps, segmentation, explicit keyframes, or robust encoder diagnostics
   justify the extra code.
4. **Integrate video as an optional artifact.** A video failure must emit an
   event/summary integrity status but must not invalidate `events.jsonl`,
   `poses.jsonl`, `objects.jsonl`, or the Run summary.
5. **Store capture metadata next to the Run.** At minimum record filename or
   segment list, codec, width, height, nominal fps, configured bitrate, selected
   eye/crop, capture start/end monotonic time, dropped-frame/encoder errors,
   byte count, and SHA-256 after finalization.
6. **Use a temporary suffix while recording.** Rename to the final Run artifact
   only after encoder drain and muxer finalization. Preserve finalized segments
   during abort/recovery.
7. **Do not add audio in the first version.** Screen capture does not
   automatically grant system audio, and microphone recording adds permissions,
   consent, privacy, synchronization, and storage considerations.

## Risks requiring a physical Quest check

- exact monoscopic eye, crop, aspect ratio, and stabilization of MediaProjection
  output on the installed Horizon OS version
- whether all compositor layers, overlays, and any passthrough content required
  by the study appear in the captured output
- consent-dialog behavior in immersive mode and after headset/app resume
- interaction between capture, current 20 Hz pose/object logging, file flushes,
  thermal throttling, and long Runs
- MP4 finalization when the application is paused, loses focus, is aborted, or
  is killed
- available encoder profiles/resolutions on the exact Quest firmware; Android
  APIs expose device codec capabilities and implementations must query/fallback
  rather than assume a single configuration

## Bottom line

The feature is practical. For **6 Mbps, 30 fps mono capture**, budget about
**45 MB/minute**, or **0.9 GB for a 20-minute Run**. The likely production route
has manageable runtime overhead because it can use the system compositor and a
surface-fed hardware encoder, but it is not a one-script Unity feature: consent,
foreground-service, Android plugin, finalization, and recovery work make the
implementation **medium-high difficulty**. A measured MQDH pilot should precede
implementation and will answer the most important unknown: whether Quest 3 has
enough frame-time and thermal headroom in the real six-phase scene.
