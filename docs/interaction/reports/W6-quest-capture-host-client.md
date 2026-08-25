# W6 Quest Capture and Host Client completion report

Date: 2026-08-26  
Worker: W6  
Worktree: `C:\Users\woshica\.codex\worktrees\13c9\VR-Sign-Record-System`  
W3 source identified by the Orchestrator: `4f9629d`

The exact worker baseline was intentionally not queried because every Git
command was prohibited. No Git command was run and no commit was created.

## 1. Scope completed and scope deliberately not completed

### Completed

- Added `InteractionRunController`; the W1 `InteractionRunStateMachine` remains
  the sole Run/Phase authority. Start consumes one W1 condition slot, creates
  one immutable `RunPlan` and unique `run_id`, serializes it once, and atomically
  publishes the exact bytes locally before any Host request. Transport failure,
  not-ready, and 409 retries retain the same plan, seed, condition, ID, and
  manifest bytes.
- Added an explicit Contract V1 manifest serializer rather than relying on
  `JsonUtility`. It emits the frozen snake_case schema, all identities/UTC/build/
  seed/condition fields, four unique password digits, four-color order, and
  exactly six ordered phases. Each phase contains exact signer/Take/artifact/
  hash data and a non-empty `task_variant` with `variant_id`, `target_ids`, and
  `ordered_target_ids`. The frozen manifest contains no `quest_device_id`.
- Added strict Host contracts and a client for readiness, registration, exact
  Run snapshot lookup, Complete, Abort, the four Quest artifact PUTs, ACK, and
  the W8a Quest heartbeat seam. Registration sends
  `X-SignVR-Quest-Id`; a lost successful POST followed by 409 proceeds only
  after `GET /api/interaction/runs/{run_id}` returns the same safe
  batch/participant/run snapshot. An unrelated active-run 409 remains blocked.
- Added the canonical W8a heartbeat body:
  `{schema_version:1,quest_device_id,ready,heartbeat_generation,heartbeat_sequence}`.
  Generation is a non-negative app-session Unix-millisecond `long`; sequence is
  numeric, begins at 1, and remains monotonic across pause/resume. The client
  owns one serial routine: enable/resume sends immediately, then every two
  seconds; pause/disable cancels it without creating a second routine.
- Readiness now fails closed unless backend, storage, paired Quest, explicit
  `quest_fresh`, ready/fresh camera, and ready/fresh participant are all true.
  Host participant and Quest identities must match the local identities with
  `StringComparison.Ordinal`. In particular, `quest_alpha` and `QUEST_ALPHA`
  are different devices. A newest-issued-response watermark prevents an older
  concurrent readiness callback from replacing newer state or freshness.
- Formal Study also rejects debug overrides, a missing participant, a disabled
  Host requirement, an unintegrated build identity, a stale readiness response,
  any unsealed consumed Run, missing HMD/left-hand/right-hand data sources, and
  zero valid object probes. `EngineeringLocal` remains explicit, armed, and
  debug-build-only.
- Added an explicit presentation handshake. Reaching `start_at_utc`, completing
  a phase, or requesting replay only creates a presentation request; it does
  not fabricate `instruction_play_started` or advance W1. W5/W8 must report the
  actual first presented frame through `NotifyInstructionPlaybackStarted`.
  That timestamp is the W1 first-playback/phase origin and is when initial
  `RunStarted` and capture begin.
- Added Quest-local Experiment Capture under
  `Application.persistentDataPath/interaction-tests/{batch}/{participant}/{run}`
  with `run.manifest.json`, `events.jsonl`, `poses.jsonl`, `objects.jsonl`, and
  `summary.json`. No Recording Take path or semantics are reused.
- Added bounded asynchronous JSONL writers, strictly increasing `event_seq`,
  nondecreasing real monotonic time/frame, the complete frozen event envelope,
  required event names, and a low-coupling `IInteractionEventSink`. Critical
  enqueue/drain/close waits have finite 250/500/500 ms limits; overload and
  detected sample-time gaps emit `capture_gap` rather than silently dropping
  data. Every phase flushes all streams; Complete and Abort seal them.
- Added per-Unity-`Update` HMD and bilateral hand/joint sampling plus separate
  object-state rows. The sampler does not assume 72 Hz and periodically
  re-resolves a late-instantiated XR rig instead of treating an empty `Awake`
  lookup as permanent.
- Added summaries that independently retain first-attempt correctness,
  completion, stuck outcome, error count, replay use, first action/completion
  times, text/pointing exposure flags and actual seconds, and timeout. Run
  summaries contain six phase results, terminal status, total duration and
  totals, abort reason, and data completeness.
- Added restart discovery and explicit partial terminalization. A consumed
  AwaitingHost/Scheduled/Running/Completing Run is locally aborted on lifecycle
  exit when possible; otherwise its partial files remain discoverable.
  `RecoverAllPartialRunsAsAborted` streams valid rows into a sealed aborted Run,
  appends at most one explicit `run_aborted`, does not invent pose/object rows,
  and is restart-idempotent.
- Added recoverable atomic replacement. Discovery and reads restore the sole
  valid `.replace-backup` when a two-rename fallback was interrupted; a present
  destination wins and stale backups are removed. Tests cover the backup-only
  crash window.
- Added path/length/SHA frozen artifact descriptors and `UploadHandlerFile`
  streaming. The four artifacts are not loaded into byte arrays. W3 byte caps
  are enforced before upload and the path, length, and SHA are revalidated for
  every retry.
- ACK never mutates sealed capture artifacts. Upload progress, accepted PUTs,
  Host stored/missing sets, `acknowledged_utc`, and cleanup eligibility live in
  the atomic `.upload-state.json` sidecar. Cleanup is eligible only for a
  terminal ACK with all five artifacts, including Host-only webcam; W6 still
  never deletes the local Run automatically.
- Added a W6-only idempotent Editor setup/validator for W4's
  `RuntimeSystemsAnchor` and `ExperimentCaptureAnchor`. It wires the controller,
  Host client, and sampler, enforces Study-safe defaults and capture sources,
  and deliberately does not save the scene.

### Deliberately not completed

- No Host file was edited. W8a owns the HTTP heartbeat/readiness/admission
  bridge; W6 implements only its centralized client seam.
- No webcam is captured or fabricated on Quest. No Host-selected random value,
  Showcase Replay, Zhao-teacher package, Addressables, authentication system,
  or Quest visual polish was added.
- W5 presentation and W7 phase adapters were not edited. Their required W6
  seams are public but must be wired during integration.
- No scene YAML, build/settings asset, W4 generator, Core asmdef, Recording
  Take/uploader, Host backend/frontend, W5/W7 source, or
  `docs/interaction/PROGRESS.md` was edited.
- No automatic local deletion was implemented.
- Unity Editor was not started. Unity Test Runner, PlayMode, Android/Quest,
  camera, real Meta hand/joint, durability, memory, thermal, and performance
  validation remain Orchestrator/device gates.

## 2. Files created or changed

W6 runtime files are confined to
`signvr_unity/Assets/Scripts/Interaction/CaptureHost/`:

- `InteractionCaptureModels.cs`
- `InteractionCaptureSampler.cs`
- `InteractionCaptureWriter.cs`
- `InteractionHostClient.cs`
- `InteractionHostContracts.cs`
- `InteractionJson.cs`
- `InteractionPartialRunRecovery.cs`
- `InteractionPendingRunDiscovery.cs`
- `InteractionPresentationHandshake.cs`
- `InteractionRunController.cs`
- `InteractionRunManifestContractV1.cs`
- `InteractionRunPolicy.cs`
- `InteractionStoragePaths.cs`
- `InteractionSummaryTracker.cs`
- `InteractionUploadState.cs`
- `W6InteractionCaptureHostTestDriver.cs`

Each runtime source has its matching `.cs.meta`; the directory has
`CaptureHost.meta`.

Editor setup:

- `signvr_unity/Assets/Editor/W6InteractionCaptureHostSetup.cs`
- `signvr_unity/Assets/Editor/W6InteractionCaptureHostSetup.cs.meta`

EditMode tests:

- `signvr_unity/Assets/Tests/EditMode/Interaction/CaptureHost.meta`
- `signvr_unity/Assets/Tests/EditMode/Interaction/CaptureHost/W6InteractionCaptureHostTests.cs`
- `signvr_unity/Assets/Tests/EditMode/Interaction/CaptureHost/W6InteractionCaptureHostTests.cs.meta`

Completion report:

- `docs/interaction/reports/W6-quest-capture-host-client.md`

No asmdef was created or changed.

## 3. Commands/tests executed and exact results

No Git command was run. Unity Editor was not started.

### Final standalone C# and Unity-reference gate

Unity `6000.5.6f1`'s bundled Roslyn compiler and .NET Standard 2.1 reference
pack were used without launching the Editor. Output directory:

`C:\Users\woshica\AppData\Local\Temp\signvr-w6-final-61305c1839d0404dab5e20caed0c5724`

Exact results:

- `W6_FINAL_CORE_CSC_EXIT=0`
- `W6_FINAL_PURE_CSC_EXIT=0`
- `W6_FINAL_PLAYER_CSC_EXIT=0`
- `W6_FINAL_EDITOR_RUNTIME_CSC_EXIT=0`
- `W6_FINAL_EDITOR_SETUP_CSC_EXIT=0`
- `W6_FINAL_TEST_CSC_EXIT=0`
- `W6_FINAL_SCENARIOS_DISCOVERED=33`
- `W6_FINAL_SCENARIOS_PASSED=33`
- `W6_FINAL_SCENARIOS_FAILED=0`

The 33 deterministic scenarios all passed:

1. `ManifestMatchesFrozenContract`
2. `FrozenRegistrationRetriesExactBytesAfter409`
3. `ConflictRecoveryRequiresMatchingSnapshot`
4. `HostNotReadyAndConflictNeverRedrawPlan`
5. `StudyReadinessRequiresMatchingFreshParticipant`
6. `StudyReadinessRejectsQuestIdCaseMismatch`
7. `ScheduledStartGateBlocksPreStartCapture`
8. `PresentationHandshakeUsesActualPlaybackOrigin`
9. `StudyCapturePrerequisitesFailClosed`
10. `CompleteAndAbortSealAllLocalFiles`
11. `EventSequenceAndMonotonicTimeAreStrict`
12. `EventSinkSeamWorksWithFakeAdapter`
13. `BufferOverflowEmitsCaptureGap`
14. `EveryPhaseFlushFlushesAllStreams`
15. `RestartDiscoversPendingRun`
16. `RestartFlagsAbnormalPartialRunForRecovery`
17. `PartialRunCanBeTerminalizedAfterRestart`
18. `AtomicBackupRecoversBeforeRead`
19. `AckRequiresWebcamAndAllFiveArtifacts`
20. `AckStateNeverMutatesSealedArtifacts`
21. `ArtifactRetrySnapshotStaysByteIdentical`
22. `FrozenArtifactsEnforceHostByteLimits`
23. `W3AccurateResponsesAndTerminalBodiesMatch`
24. `QuestHeartbeatContractIsExplicit`
25. `HeartbeatLifecycleHasOneRoutineAcrossPauseResume`
26. `ReadinessGenerationRejectsStaleCallbacks`
27. `WriterWaitsHaveShortFiniteLimits`
28. `LifecyclePolicyTerminalizesConsumedRuns`
29. `GeneratedFixtureCarriesW3StrictIdentity`
30. `PathsFailClosedAgainstTraversalAndIllegalIds`
31. `StudyRejectsDebugOverrides`
32. `SummaryKeepsAccuracyAndStuckSeparate`
33. `InstructionManifestReaderMapsExactArtifacts`

These are direct deterministic invocations of the same Editor-only driver used
by the NUnit wrapper. They are not a claim that Unity Test Runner ran.

### Integrated W3 strict fixture gate

The integrated W3 source at
`D:\work\Unity6\VR-Sign-Record-System\vr-sign-host\backend` was read-only.
Its own `.venv`, FastAPI `TestClient`, `start_udp=False`, and a temporary Host
data root were used. The manifest and all four Quest artifacts came from the
compiled C# serializer/writer, not a Python facsimile.

Latest exact results:

- `W6_W3_MANIFEST_POST_STATUS=200`
- `W6_W3_RETRY_STATUS=409`
- `W6_W3_SNAPSHOT_IDENTITY=exact`
- `W6_W3_ARTIFACT_PUT_STATUSES=events:200,poses:200,objects:200,summary:200`
- `W6_W3_ACK=false;missing=webcam`
- `W6_W3_ACTUAL_REGISTRATION_PARSE=pass`
- `W6_W3_ACTUAL_SNAPSHOT_PARSE=pass`
- `W6_W3_ACTUAL_409_RECOVERY_POLICY=pass`

Latest fixture path:

`C:\Users\woshica\AppData\Local\Temp\signvr-w6-tests-d0b0fd0ff7084e47a2dd3aaa44c9209b\interaction-tests\pilot-20260826\P901\run_20260826T101530Z_000000012153013f94355c7500000000`

An earlier full W3 fixture sequence in this W6 task also returned Complete
200/retry 200, Abort 200/retry 200, all four artifact PUTs 200, duplicate PUT
409, ACK false with only webcam missing, and byte-identical Host storage. The
latest rerun above specifically adds actual W3 response parsing and the lost
registration response -> 409 -> exact GET recovery gate.

One preliminary latest-rerun harness attempted to Complete the deterministic
fixture immediately and received `400` with
`Terminal timestamp cannot precede Run Plan creation`; its fixed
`created_utc=2026-08-26T10:15:30Z` was ahead of the Host wall clock at that
moment. The requested strict manifest/four-artifact gate was rerun without that
clock-invalid terminal step and passed as listed. Terminal request shape and
Complete/Abort local sealing remain covered by scenarios 10 and 23.

The only Host output besides results was its existing
`StarletteDeprecationWarning` about `httpx2`.

### Final metadata and boundary audit

- `W6_CS_FILES=16`
- `W6_TEST_CS_FILES=1`
- `W6_MISSING_CS_META=0`
- `W6_DUPLICATE_GUIDS_WITHIN_SCOPE=0`
- `W6_GUID_COLLISIONS_IN_ASSETS=0`
- `W6_MANIFEST_QUEST_ID_REFERENCES=0`
- `W6_UPLOAD_READALLBYTES_REFERENCES=0`
- `W6_POST_SEAL_ACK_EVENT_REFERENCES=0`
- `W6_RECORDING_TAKE_REFERENCES=0`
- `W6_INSTRUCTION_STARTED_REFERENCES=1` (the actual playback ACK path only)
- `W6_UPLOAD_HANDLER_FILE_REFERENCES=1`
- `W6_CASE_MISMATCH_TEST_ASSERTIONS=3`

Not run in this worktree: Unity import/Test Runner, PlayMode, Editor scene setup,
live W8a heartbeat/readiness, live browser camera, LAN Quest/Host three-process
sync, Android build/install, headset tracking, power-loss, memory, thermal,
performance, and Recorder regression.

## 4. Generated files or local prerequisites intentionally not versioned

- Roslyn DLLs and C# fixture Runs under `%TEMP%` are validation output only and
  must not be versioned.
- Real Quest output and `.upload-state.json` remain under
  `Application.persistentDataPath/interaction-tests`. Partial, deferred,
  unacknowledged, and cleanup-eligible Runs are intentionally retained.
- Study requires configured safe `batch_id`, non-placeholder `participant_id`,
  integrated build identity, W2 instruction manifest/catalog, the existing
  `SignVR.DeviceId`, and a LAN HTTP Host URL from Inspector or
  `-interactionHostUrl`.
- W8a must provide the Quest heartbeat endpoint and explicit fresh Quest,
  camera, and participant readiness fields. Until then, the formal Study gate
  is intentionally unreachable.
- W5/W8 must subscribe to `PresentationRequested`, start the requested real
  clip, and ACK its actual first frame. A missing subscriber leaves the Run
  safely pending rather than inventing playback.
- The integrated scene must inject a real HMD, left/right hand data sources,
  and at least one valid key-object probe. Zero-probe Study is rejected.
- The W6 setup operation leaves `InteractionLab.unity` dirty but unsaved; only
  the Orchestrator owns the reviewed save.

## 5. Contract deviations or integration assumptions

### Frozen contracts

- No known manifest/artifact Contract V1 deviation. The C# manifest and four
  C# artifacts were accepted by W3 strict validation.
- Quest identity is deliberately absent from the frozen manifest. It exists
  only in the canonical heartbeat and `X-SignVR-Quest-Id` header.
- ACK state is deliberately outside sealed artifacts. `upload_started` is
  captured before sealing; post-ACK state and time are atomic sidecar data.
  This implements the review decision that Host and Quest artifact bytes must
  never diverge after upload.

### Integration assumptions and risks

1. W8a was still parallel/not present in this isolated worktree during the
   final gate. W6 centralizes its assumed path as
   `/api/interaction/readiness/quest` and the canonical field names in Host
   contracts, so any final W8a naming adjustment should remain a small local
   adapter. Live heartbeat TTL, header ownership admission, participant
   matching, and freshness behavior still require the integrated Host test.
2. The exact Quest comparison is now `StringComparison.Ordinal`; case variants
   fail closed. Participant comparison is also ordinal.
3. W5/W8 must call `NotifyInstructionPlaybackStarted` on the actual first
   presented frame. Until that happens, W1 stays Scheduled/current-phase-old
   and interaction input is rejected. This is intentional truth preservation,
   but the final presentation wiring must be exercised around `start_at_utc`.
4. W8 must inject actual tracking sources and object probes. Late rig discovery
   is supported, but an SDK-specific Meta hand/bone shape and joint coverage
   were not verified on hardware.
5. `UploadHandlerFile`, path/length/SHA verification, and byte caps compile
   against Unity 6; actual Quest streaming memory and retry behavior still need
   device profiling.
6. Managed waits are bounded, but OS `Flush(true)` latency cannot be forcibly
   bounded by this API. A timeout/error retains partial data and may leave a
   recoverable Run rather than claiming completion.
7. Partial recovery preserves valid complete rows and drops only an invalid
   tail. It does not reconstruct missing task semantics; unfinished phase
   results stay explicit defaults and data completeness records empty pose/
   object streams.
8. Host and Quest synchronized clocks remain operational prerequisites. W6
   derives a monotonic deadline from `start_at_utc`, while actual presentation
   start is authoritative for W1/capture timing.
9. Cleanup remains manual even when `.upload-state.json` marks the Run eligible.

## 6. Recommended Orchestrator review steps

1. Integrate the W6-owned files as a unit. Do not add a CaptureHost asmdef or
   modify `SignVR.Interaction.Core`; predefined `Assembly-CSharp` intentionally
   references the auto-referenced Core assembly.
2. Compare the final W8a report with the centralized W6 heartbeat/readiness
   seam. Verify the exact PUT path/body, numeric generation/sequence, two-second
   serial loop, pause/resume cancellation, `X-SignVR-Quest-Id`, five-second TTL,
   and ordinal participant/Quest admission.
3. Import the main Unity `6000.5.6f1` project, require a clean Player/Editor
   compile, then run
   `SignVR.Interaction.Editor.Tests.W6InteractionCaptureHostTests` and expect
   33/33.
4. Load `Assets/Scenes/InteractionLab.unity`, run the W6 unsaved setup and
   validator, inspect the three component references, inject the real XR rig
   and at least one object probe, then let the Orchestrator own the reviewed
   scene save.
5. Wire W5/W8 to `PresentationRequested` and
   `NotifyInstructionPlaybackStarted(request.RequestSequence,
   request.PhaseId, request.PlaybackKind)`, plus first/replay playback-complete
   callbacks. Confirm no start event, W1 transition, or capture occurs before
   the actual-start ACK.
6. Wire W7/W5 domain actions through the controller's attempt/error/progress/
   completion, bubble, pointing, replay, and event-sink APIs. Confirm they do
   not own a second Run/Phase state.
7. Run live W8a/W3 integration for: missing freshness fields, participant and
   Quest case/identity mismatch, stale/out-of-order readiness, heartbeat pause/
   resume, response-loss registration 409 with exact GET recovery, unrelated
   active-run 409 rejection, start scheduling, Complete, Abort, PUT response
   loss/409, restart upload, and partial terminalization.
8. Verify four Quest artifacts leave ACK false/missing webcam; only a terminal
   five-artifact ACK marks `.upload-state.json` cleanup-eligible. Compare hashes
   before/after ACK and confirm no capture artifact changed or directory was
   deleted.
9. On Quest, profile large file streaming, queue pressure and `capture_gap`,
   per-phase fsync, pause/quit/kill/reboot recovery, HMD/hands/joints/object
   validity, frame time, memory, thermal behavior, and Android power-loss
   windows.
10. Re-run Recorder compile/build/smoke regression and confirm all Recording
    Take paths, uploader behavior, Host Recorder routes, scenes, and settings
    remain unchanged.
