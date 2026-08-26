# W6 Quest Capture and Host Client completion report

Date: 2026-08-26
Worker: W6 targeted review remediation
Worktree: `C:\Users\woshica\.codex\worktrees\13c9\VR-Sign-Record-System`
Original requested baseline: `be28ad6`
Final test-boundary baseline supplied by Orchestrator: `fdc2cb4`

The baseline was supplied by the Orchestrator but was not queried: every Git
command was prohibited. No Git command was run, no commit was created, Unity
Editor was not started, and no other worktree was read or changed.

## 1. Scope completed and scope deliberately not completed

### Completed

The original W6 contract remains intact: W1
`InteractionRunStateMachine` is the only Run/Phase lifecycle authority; the
frozen manifest remains byte-compatible and contains no `quest_device_id`;
Quest data stays under the independent `interaction-tests` tree; presentation
must ACK its real first frame; Study requires exact fresh Host/participant/
Quest identities plus HMD, both hand sources, and at least one object probe;
and ACK state remains in `.upload-state.json`, never in a sealed artifact.

The two-axis review items were closed as follows:

1. Artifact integrity is background-only. Initial frozen length/SHA creation
   uses `InteractionFrozenArtifactSet.BeginReadOnce`; every PUT first polls
   `InteractionFrozenArtifact.BeginVerifyUnchanged`. Both operations hash a
   file stream on a worker. Oversized files are rejected before hashing. The
   transport remains `UploadHandlerFile`; no Quest artifact is passed through
   `ReadAllBytes` or `UploadHandlerRaw`.
2. Capture persistence has one serial background I/O scheduler per Run.
   Manifest publication is created by `BeginCreateNew`; phase checkpoints are
   queued by `BeginPhaseCheckpoint`; terminal seal is queued after all earlier
   checkpoints by `BeginSeal`. `Monitor.Wait`, `Thread.Join`, `Flush(true)`,
   promotion, and summary atomic publication occur only on background workers.
   Controller coroutines poll operations and advance W1/presentation/upload
   only after success. Upload-state atomic fsync is also queued on a serial
   background scheduler and exposed through `PendingPersistence`.
3. Capture budgets fail closed at four levels: queue item count, one-line UTF-8
   bytes, pending UTF-8 bytes, and total artifact bytes. JSONL writers force
   canonical `\n`, so accounting is platform-independent. A shared disk guard
   enforces a configurable minimum-free-space watermark before creation,
   checkpoints, seal, and every byte reservation. Budget/disk failure leaves
   partial evidence and prevents formal completion/upload.
4. `InteractionRunController` now has one idempotent lifecycle shutdown gate.
   Pause, disable, quit, and destroy cancel active requests, stop all controller
   coroutines, disable sampling, transition a consumed W1 Run to Aborting when
   applicable, and queue local abort/partial terminalization. Repeated lifecycle
   callbacks cannot queue a second seal. An independently owned lifecycle job
   waits for initialization and publishes one immutable terminalization result;
   its worker never mutates controller fields or W1. An interrupted process
   still leaves manifest/partial evidence discoverable by recovery. A later
   main-thread reconciliation may publish the complete result back into W1.
5. Editor setup is split. `ValidateLoadedSceneStructure` checks only the three
   components, references, Study-safe defaults, unique scene ownership, and
   Host URL. `ValidateLoadedSceneStudyReadiness` separately requires real HMD,
   both hands, and probes. Structural setup therefore succeeds before W8
   injection. All component additions and existing-object changes are inside
   one named Undo group; validation failure calls `Undo.RevertAllDownToGroup`.
6. Completed seal has a duplicated fail-closed guard: the controller checks and
   the background writer rechecks that poses and objects each contain at least
   one accepted row. Missing data becomes the explicit abort reason
   `completed_capture_missing_required_streams` (or a retained partial if seal
   itself fails), never Completed/uploaded. Abort and restart recovery may
   truthfully publish empty pose/object streams without fabricating samples.
7. Heartbeat generation no longer derives from wall-clock milliseconds. A
   PlayerPrefs-backed `IInteractionHeartbeatGenerationStore` persists a
   non-negative Int64 and allocates `previous + 1` once per new application
   session; corrupt or overflowed state fails closed. Sequence starts at 1 and
   stays monotonic for that generation. Every 2xx heartbeat parses
   `accepted=true` and exact ordinal `quest_device_id`, generation, and sequence
   echoes. `LastHeartbeatReady` remains false on any mismatch. The one serial
   loop uses fixed monotonic deadlines, skipping missed slots after a slow
   response instead of drifting by two seconds from response completion.
8. Every active `UnityWebRequest` is registered before send and removed in a
   `finally`. Lifecycle cancellation explicitly calls idempotent `Abort` and
   disposes the request, including `UploadHandlerFile`; `Send` owns final
   disposal and callers no longer wrap the same request in a second `using`.
   Stopping a coroutine is not the cancellation guarantee.

The follow-up review items were closed as follows:

1. Terminal Abort has explicit single-owner arbitration. Public
   `TryAbortRun(reason, out error)` reserves Abort before changing W1. During a
   phase checkpoint it stops only the coroutine waiting for that checkpoint,
   leaves the background checkpoint running, enters W1 Aborting once, and
   queues exactly one Aborted seal after the checkpoint. Once a Completed seal
   has been queued, Abort is rejected with an error while W1 remains Completing;
   no second seal, terminal event, or summary can be written. The legacy void
   `AbortRun` safely delegates and records the rejection in `LastError`.
2. Lifecycle shutdown is heartbeat-symmetric. It always calls both
   `DisableQuestHeartbeat` and `CancelActiveRequests`. Resume/enable resets the
   shutdown gate, re-enables sampling, and refreshes heartbeat only while W1 is
   PreStart. A Run aborted by pause therefore cannot let the Host client resume
   `ready:true`; `ResetToPreStart` is the only path that re-arms it.
3. The prior completion callback was removed. `InteractionBackgroundHandoff<T>`
   atomically exposes a single immutable
   `InteractionLifecycleTerminalizationResult`. The background lifecycle job
   owns only the writer/initialization operation and a detached summary copy;
   only the Controller's main-thread reconciliation path consumes the complete
   result and updates W1, `LastError`, and pending discovery. Queue-start
   failures are reconciled synchronously inside the lifecycle callback.
4. W6 Editor setup now preflights scene validity, unique anchors, whole-scene
   component uniqueness, existing component placement, the exact default Study
   configuration, and existing Host URL before opening its Undo transaction.
   The transaction uses only `Undo.AddComponent`, `Undo.RecordObject`, and
   `SerializedObject` reference writes. It never calls runtime configure APIs.
   Host client, controller, and sampler lifecycle entry points all fail inertly
   outside Play Mode, so setup cannot allocate heartbeat PlayerPrefs or start
   coroutines.
5. The real setup rollback NUnit test is isolated from user scene memory. It
   creates only test-owned additive guard/target scenes, temporarily changes the
   active scene, injects failure after W6 wiring, and restores the original
   active scene in `finally`. The empty guard is first saved into a unique
   temporary `Assets` folder, then made dirty with its sentinel; the target may
   therefore remain the sole untitled scene. It never opens, reloads, replaces,
   closes, or saves a pre-existing scene. The target starts with an existing Controller,
   so rollback must both remove the newly added Host/Sampler and restore that
   Controller's original null references. It also asserts every original scene
   remains loaded with the same dirty flag and object-value signature, the
   dirty guard sentinel remains unchanged after target cleanup, heartbeat
   PlayerPrefs are byte-for-byte equivalent through the string seam, and the
   on-disk InteractionLab SHA-256 remains unchanged. Both test-owned scenes are
   closed independently and the temporary guard scene asset/folder are deleted.

The final-review items were closed as follows:

1. Lifecycle initialization and seal no longer have a five-second abandonment
   path. `InteractionLifecycleTerminalizationJob` is the single detached owner
   until the initialization operation actually completes and, if it yields a
   writer, until the queued Abort seal actually completes. Both waits use short
   50 ms slices on the job worker; five seconds is now a read-only diagnostic
   watermark (`InitializationDelayObserved`), not a terminal result. The job
   publishes one immutable result and never writes Controller fields or W1.
   `ResetToPreStart` remains fail-closed while that owner is outstanding. A late
   writer is therefore sealed/closed and restart discovery sees one diagnostic,
   uploadable Aborted Run rather than an ownerless partial.
2. Controller behavior now has an Editor-only test seam and driver, both removed
   from Player compilation by `#if UNITY_EDITOR`. The driver constructs the real
   `InteractionRunController`, `InteractionHostClient`, W1 state machine, capture
   writer, summary, and files. Lifecycle-dependent wrappers now live in a
   non-Editor-only PlayMode test assembly, assert `Application.isPlaying`, finish
   component `Awake`/`OnEnable` before installing deterministic state, and then
   trigger actual `OnDisable`/`OnDestroy`. The driver arms its EditMode-only seam
   only when `Application.isPlaying` is false. Test observations remain
   internal/read-only; Study Player APIs and the frozen schema are unchanged.
3. The setup rollback test restores heartbeat PlayerPrefs in the outermost
   cleanup `finally`: it restores the original existence/value and always calls
   `PlayerPrefs.Save()` after scene cleanup, even when an assertion or cleanup
   operation fails. The existing in-test assertions still detect any setup-time
   mutation before restoration.

The final Standards follow-up was closed as follows:

1. `InteractionArtifactOperationRegistry` is now the explicit component-level
   owner for Controller artifact freezes and Host per-artifact verification.
   A canonical Run/artifact key rejects overlap. The registry remains reachable
   after a waiting coroutine is stopped, removes a completed lease atomically,
   and lets a retry start only after the previous lease has completed and been
   reaped. Its worker completion callback touches only the pure registry; it
   never touches a MonoBehaviour, W1, or another Unity API.
2. Artifact SHA-256 now reads in 64 KiB chunks. Controller/Host shutdown requests
   cancellation before `StopAllCoroutines`; the worker checks cancellation
   before opening, immediately after each observed chunk, and before
   finalization. `using` closes the file stream and hash object on cancellation.
   Shutdown only flips cancellation state and never waits on the Unity thread.
   Host `PutArtifact` uses the same owner, so stopping its outer request
   coroutine cannot orphan verification.
3. PlayMode lifecycle regressions drive real Controller disable/destroy and
   public Host `PutArtifact` with a controlled chunk observer. They assert
   cancellation observation, no overlapping retry, automatic reap, exclusive
   file reopen, and no background W1 transition. They were source-built in this
   worktree but still require the Orchestrator's Unity rerun.
4. Scene cleanup now attempts active-scene restoration, target close, guard
   close, PlayerPrefs restoration, and `PlayerPrefs.Save` independently. It
   preserves a primary exception and attaches cleanup failures, or aggregates
   cleanup failures when no primary exists. Test-driver cleanup also checks every
   bounded wait result; an active owner causes the temporary path to be retained
   and reported instead of being disposed/deleted underneath the worker.

The last TDD/Standards closeout was completed as follows:

1. The controlled read observer is now passive: it records chunk entry, waits
   until cancellation has been requested, waits for explicit test release, and
   returns normally. It never throws cancellation. Freeze and verify fixtures
   are deterministically larger than two 64 KiB chunks, and both scenarios
   require exactly one observed chunk. The only cancellation exception in that
   path therefore comes from the production hasher's post-observer guard.
2. Both artifact-owner scenarios preserve their primary assertion exception and
   independently aggregate cleanup diagnostics. Observer disposal and fixture
   deletion are permitted only after the operation completed, registry
   `ActiveCount` reached zero, and the lease reports `IsReaped`; otherwise the
   temporary path is retained and attached to the diagnostic.
3. Controller freeze and Host verify snapshot the canonical directory/frozen
   artifact and observer into pure managed locals before `TryStart`. Their
   worker delegates no longer capture a MonoBehaviour or re-read an instance
   observer. The three real lifecycle regressions now hold the worker in a
   controlled queue, replace the component observer, release the worker, and
   require only the startup observer to receive chunks before Disable/Destroy
   cancels and reaps the operation.
4. `IInteractionBackgroundWorkQueue` is one narrow internal seam with the
   ThreadPool as the Player adapter. A rejected enqueue completes
   `InteractionBackgroundOperation`/handoff immediately with a clear failure;
   artifact ownership likewise returns an already-failed, already-reaped lease.
   A closing `InteractionSerialBackgroundScheduler` continues to reject
   synchronously, so no `Start`/`Enqueue` path can expose a permanently pending
   operation after queue refusal.

The final lifecycle-lease/cadence closeout was completed as follows:

1. Lifecycle terminalization now publishes queue rejection, queue exception,
   and worker failure as one immutable, once-consumable result. The Controller
   reconciles either success or failure, clears the detached job, closes or
   transfers writer ownership, marks the terminal arbiter, and moves W1 to the
   explicit Faulted state on failure. A queue refusal during disable/destroy can
   no longer leave W1 permanently Aborting or block `ResetToPreStart` forever.
2. `InteractionHostClient` owns one accepting flag plus monotonically advancing
   request epoch. Every public request coroutine captures a lease and validates
   it at entry, after every local/background boundary, atomically before request
   construction/registration, before `SendWebRequest`, and before publishing a
   response. Disable, destroy, and pause cancellation close the epoch before
   cancelling owned operations and requests. A stale artifact iterator therefore
   cannot construct or send a request after verification; callbacks publish
   exactly once. PreStart restoration opens a new epoch without reviving a stale
   heartbeat or iterator.
3. `InteractionCaptureSampler` now uses an absolute monotonic 0.05-second
   cadence. One due Update captures at most one pose/object group; a long frame
   advances the deadline without burst catch-up. Inactive, disable, and reset
   clear the cadence; the Controller also resets it explicitly on
   `ResetToPreStart` and the real first-playback transition, so even a same-frame
   new Run can sample immediately. Deterministic rule coverage proves the same
   20 groups per second at both 72 Hz and 90 Hz.
4. Artifact completion has atomic cancellation-versus-publication arbitration.
   Cancellation that wins before publication converts a computed hash into a
   cancelled result; success that was already atomically published makes a later
   cancel return false. A deterministic post-hash/pre-publish observer makes both
   Controller freeze and Host verify mutation-sensitive without throwing the
   cancellation from test code.
5. Controller freeze and Host verify still snapshot canonical path/artifact and
   observer into pure managed locals before queueing. Their workers do not capture
   a MonoBehaviour. Lifecycle cancellation remains cooperative at chunk
   boundaries, the registry rejects overlap, and completed operations are reaped
   without blocking the main thread.
6. All three real artifact-lifecycle driver cleanups now require the operation to
   be completed, the owning registry to report `ActiveCount == 0`, and the lease
   to report `IsReaped` before disposing observers or deleting fixtures. Cleanup
   actions remain independent and preserve the primary test exception; otherwise
   the diagnostic path is retained.

The final pending-initialization queue-failure review was closed as follows:

1. A rejecting or throwing primary lifecycle queue now becomes a completed
   failure result synchronously, even when capture initialization is still
   pending. `StartLifecycleTerminalizationJob` immediately reconciles that result
   in the same Disable/Destroy callback, clears the job, faults W1, and marks the
   terminal arbiter. It no longer depends on Update, OnEnable, or another Unity
   callback after destruction.
2. Pending initialization is transferred to an
   `InteractionDetachedInitializationOwner` registered directly on the pure
   `InteractionBackgroundOperation` completion boundary. If initialization later
   returns a writer, that owner only disposes it and retains the partial; it never
   seals, writes summary/terminal events, references a MonoBehaviour/W1, or calls
   a Unity API.
3. The secondary dedicated fallback queue was removed. Queue-failure convergence
   therefore cannot itself be stranded by failure to start another thread. The
   outward failure remains immutable and consumable exactly once, while the
   existing initialization worker roots its detached completion owner until
   eventual completion.
4. Pure coverage now exercises pending initialization with both rejecting and
   throwing queues. The real Controller test covers Disable and Destroy crossed
   with both queue behaviors, requires Faulted/Terminal before the callback
   returns, then releases initialization and requires the writer to close with no
   summary or terminal event.

The final queue-failure concurrency review was closed as follows:

1. Both immediate queue rejection and the defensive `TryConsume` failure path
   now call the same `AdoptInitializationAfterQueueFailure` helper. When no writer
   is already owned, it always delegates to
   `InteractionDetachedInitializationOwner.Adopt`; `ObserveCompletion` decides
   under one lock whether to register or invoke immediately. There is no longer
   a separate `IsCompleted`/`Succeeded`/`!IsCompleted` snapshot window.
2. A ready writer remains the direct owner and is never adopted a second time.
   Completed success, completed failure, pending success, and pending failure all
   publish one immutable lifecycle failure while detached ownership completes
   exactly once. The matrix runs against both rejecting and throwing queues.
3. Completion-observer fanout is fail-contained. Each observer invocation,
   including late immediate invocation, is isolated so one diagnostic observer
   cannot prevent a later ownership observer or escape the worker completion
   boundary. Operation success/failure remains immutable before observers run.

Additional review behavior retained or strengthened:

- Manifest persistence still completes before the first POST; registration
  retry is rejected while local initialization is pending. Host 409 recovery
  still requires an active GET snapshot with exact ordinal batch/participant/
  run identity and reuses the identical plan/bytes.
- Study start now also requires the current application session's heartbeat to
  have received a valid exact ACK, in addition to Host readiness freshness.
- The sampler continues periodically re-resolving late XR/HMD/hand sources and
  Study rejects zero probes. Final object/pose row checks prevent a configured
  but silent source from producing a false Completed Run.
- `BeginRecoverAllPartialRunsAsAborted` moves controller-initiated recovery
  writes off the Unity thread. Callers poll the returned operation, then call
  `RefreshPendingRuns`.
- The acknowledged controller-size smell is recorded below. This remediation
  intentionally did not split it or create another Run/Phase authority.

### Deliberately not completed

- No Host backend/frontend, W5 presentation, W7 adapter, W4 generator, frozen
  schema, Core asmdef, Recording Take/uploader, scene YAML, build/OpenXR/URP
  setting, or `docs/interaction/PROGRESS.md` file was changed.
- No webcam is captured or fabricated on Quest. No Host random selection,
  Showcase Replay, Zhao-teacher mixed package, Addressables, authentication
  system, automatic local deletion, or Quest visual feature was added.
- No live Unity, Quest, W8a Host, browser-camera, or three-process run was
  executed in this worktree because this task explicitly prohibited starting
  Unity and reading another worktree. The Orchestrator-provided pre-fix Unity
  `53/62` result is recorded separately below.

## 2. Files created or changed

New runtime files, each with its matching `.meta`:

- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionAsyncWork.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionAsyncWork.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionCaptureBudgets.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionCaptureBudgets.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionArtifactOperations.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionArtifactOperations.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionRunControllerTestSeam.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionRunControllerTestSeam.cs.meta`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/W6InteractionRunControllerTestDriver.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/W6InteractionRunControllerTestDriver.cs.meta`

Modified runtime files:

- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionAsyncWork.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionCaptureSampler.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionCaptureWriter.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionHostClient.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionHostContracts.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionPartialRunRecovery.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionRunController.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionRunPolicy.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionSummaryTracker.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/InteractionUploadState.cs`
- `signvr_unity/Assets/Scripts/Interaction/CaptureHost/W6InteractionCaptureHostTestDriver.cs`

Modified Editor/test/report files:

- `signvr_unity/Assets/Editor/W6InteractionCaptureHostSetup.cs`
- `signvr_unity/Assets/Tests/EditMode/Interaction/CaptureHost/W6InteractionCaptureHostTests.cs`
- `docs/interaction/reports/W6-quest-capture-host-client.md`

New PlayMode test assets:

- `signvr_unity/Assets/Tests/PlayMode.meta`
- `signvr_unity/Assets/Tests/PlayMode/Interaction.meta`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/CaptureHost.meta`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/CaptureHost/SignVR.Interaction.CaptureHost.PlayMode.Tests.asmdef`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/CaptureHost/SignVR.Interaction.CaptureHost.PlayMode.Tests.asmdef.meta`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/CaptureHost/W6InteractionCaptureHostPlayModeTests.cs`
- `signvr_unity/Assets/Tests/PlayMode/Interaction/CaptureHost/W6InteractionCaptureHostPlayModeTests.cs.meta`

The final closeout pass specifically modified:

- `InteractionAsyncWork.cs` (once-consumable lifecycle queue/worker failure
  convergence, synchronous queue-start failure publication, and detached
  eventual-initialization ownership without a secondary queue);
- `InteractionArtifactOperations.cs` (atomic cancel/publish arbitration and a
  deterministic post-hash completion seam);
- `InteractionRunPolicy.cs` and `InteractionCaptureSampler.cs` (absolute 20 Hz
  cadence plus pure lease/callback policies);
- `InteractionRunController.cs` and `InteractionHostClient.cs` (Controller
  same-callback lifecycle reconciliation and a component-wide accepting/epoch
  request lease);
- `InteractionRunControllerTestSeam.cs`,
  `W6InteractionCaptureHostTestDriver.cs`,
  `W6InteractionRunControllerTestDriver.cs`, and
  `W6InteractionCaptureHostTests.cs` (sensitive pure and real-Controller
  regressions plus safe cleanup);
- this report.

The final concurrency pass modified only `InteractionAsyncWork.cs`,
`W6InteractionCaptureHostTestDriver.cs`, and this report. No public Study API,
schema, scene, asmdef, or `.meta` file changed.

The final test-boundary pass modified
`W6InteractionRunControllerTestDriver.cs`, the EditMode wrapper, and this report;
it added the isolated PlayMode test assets listed above. No runtime asmdef,
scene/settings asset, product lifecycle method, public Study API, or user setting
was created or changed.

## 3. Commands/tests executed and exact results

No Git command was run. Unity Editor was not started.

### Test-first RED evidence

The 11 new deterministic scenario methods and NUnit wrappers were added before
their production seams. The isolated RED output directory was:

`C:\Users\woshica\AppData\Local\Temp\signvr-w6-review-red-c003945925b244a5a4b7c88265ed6150`

Exact RED results:

- `W6_REVIEW_RED_CORE_CSC_EXIT=0`
- `W6_REVIEW_RED_PURE_CSC_EXIT=1`

The expected failures were missing background operation, artifact budget,
disk guard, lifecycle cancellation, persistent generation, strict heartbeat
ACK, and fixed-deadline types.

The follow-up seams were also driven from RED compile failures before their
implementations:

- terminal arbitration: pure C# compile exit `1` for missing arbiter/enums;
- immutable lifecycle job handoff: pure C# compile exit `1` for missing
  detached-copy/job/result APIs;
- heartbeat resume policy: pure C# compile exit `1` for the missing PreStart
  policy seam.

Each was then made GREEN and included in the earlier 47-scenario rerun.

Final-review TDD added two more pure scenarios and three Controller behavior
wrappers. Exact RED evidence was:

- delayed initialization: pure compile `0`, then the targeted old behavior
  failed with `RED_EXPECTED_FAILURE=Lifecycle owner abandoned initialization at
  the old five-second threshold.`;
- delay diagnostics: `RED_DIAGNOSTIC_PURE_CSC_EXIT=1` for the missing job/result
  observation properties;
- slow seal: pure compile `0`, then the targeted old behavior failed with
  `RED_EXPECTED_FAILURE=Lifecycle job abandoned its still-running seal at five
  seconds.`;
- real Controller seam: `RED_CONTROLLER_EDITOR_RUNTIME_CSC_EXIT=1` for 14
  missing Controller/Host seam members;
- real Unity lifecycle arm: `RED_UNITY_LIFECYCLE_EDITOR_RUNTIME_CSC_EXIT=1` for
  the missing lifecycle arm and active-request seam.

The corresponding targeted GREEN results were
`GREEN_LATE_INITIALIZATION_DIAGNOSTIC_PASS=1`, `GREEN_SLOW_SEAL_PASS=1`,
`GREEN_CONTROLLER_EDITOR_RUNTIME_CSC_EXIT=0`, and
`GREEN_UNITY_LIFECYCLE_EDITOR_RUNTIME_CSC_EXIT=0`.

The artifact-owner slice was likewise written RED before production code. RED
output directory:

`C:\Users\woshica\AppData\Local\Temp\signvr-w6-artifact-owner-red-014f0ebccf834ef5b062e74e12b66271`

Exact RED results:

- `W6_ARTIFACT_OWNER_RED_CORE_CSC_EXIT=0`
- `W6_ARTIFACT_OWNER_RED_PURE_CSC_EXIT=1`
- `W6_ARTIFACT_OWNER_RED_EDITOR_RUNTIME_CSC_EXIT=1`

Both failures were the expected missing cancellation/operation/read-observer
contract errors. After implementation, targeted results were
`W6_ARTIFACT_OWNER_GREEN_PURE_CSC_EXIT=0`,
`W6_ARTIFACT_OWNER_GREEN_EDITOR_RUNTIME_CSC_EXIT=0`, and both
`ArtifactFreezeOwnerCancelsAndReapsWithoutOverlap` and
`ArtifactVerifyOwnerCancelsAndReapsWithoutOverlap` passed.

The final four-item closeout then produced fresh RED evidence:

- post-observer guard mutation output:
  `C:\Users\woshica\AppData\Local\Temp\signvr-w6-post-observer-final-mutant-red-6fb62d5f12c84edfb7ec0dd8430da8f8`;
  compile exit was `0`, then both real freeze and verify paths failed as
  expected with `Freeze cancellation was not thrown by the post-observer chunk
  guard.` and `Verify cancellation was not thrown by the post-observer chunk
  guard.` (`EXPECTED_RED_COUNT=2`). Only the production post-observer guard was
  temporarily removed; it was restored immediately afterward.
- queue seam compile RED:
  `C:\Users\woshica\AppData\Local\Temp\signvr-w6-queue-rejection-red-66ab1c373245462f8d2431120e94fb93`
  with `QUEUE_REJECTION_RED_COMPILE_EXIT=1` for the missing queue interface;
  handoff propagation separately failed compile with exit `1` in
  `signvr-w6-handoff-rejection-red-c0774b6d992b417f83f78e9529348f`
  for the missing two-argument `Start`.
- artifact-owner rejection semantics compiled, then failed against the old
  throwing behavior with
  `EXPECTED_RED=BackgroundOperationRunsOffCallingThread::System.InvalidOperationException::Artifact background operation could not be queued.`
  in
  `C:\Users\woshica\AppData\Local\Temp\signvr-w6-artifact-queue-convergence-red-b6821393b80b486b998801ee0f5c6cad`.
- worker-snapshot RED compiled with exit `0` in
  `C:\Users\woshica\AppData\Local\Temp\signvr-w6-observer-snapshot-red-8883f5e16d024ad49987ca3068d1819b`.
  Cecil showed `<>4__this` fields of exact types
  `InteractionRunController` and `InteractionHostClient` in the two worker
  display classes. After the fix, the corresponding GREEN assembly in
  `signvr-w6-observer-snapshot-green-5dfc3f67f0c242b4b350c1fbfbaa7bb0`
  contained only `canonicalDirectory`/`observerSnapshot` and
  `artifactSnapshot`/`observerSnapshot`; both MonoBehaviour-field counts were
  zero.

The last six-item slice was also test-first. Tests were compiled against the old
production surface in
`C:\Users\woshica\AppData\Local\Temp\signvr-w6-latest-red-e64f357ecb954b2199232959cfe8916f`:

- `RED_CORE_EXIT=0`;
- `RED_PURE_EXIT=1` for the missing deterministic artifact-completion contract;
- `RED_EDITOR_RUNTIME_EXIT=1` for the missing completion observer and Host
  request-factory/lifecycle seams.

The post-hash publication race was mutation-tested by temporarily bypassing only
the production atomic cancellation branch, compiling, running the two real
freeze/verify pure paths, and immediately restoring the branch. Output directory:
`C:\Users\woshica\AppData\Local\Temp\signvr-w6-posthash-mutant-red-e78d9d91966b40a9a970f35a92132af8`.
Exact result: `MUTANT_COMPILE_EXIT=0`, `EXPECTED_RED_COUNT=2`,
`UNEXPECTED_PASS_COUNT=0`. Both
`ArtifactFreezeOwnerCancelsAndReapsWithoutOverlap` and
`ArtifactVerifyOwnerCancelsAndReapsWithoutOverlap` incorrectly published success
under the mutant, then both passed after restoring atomic arbitration.

Final targeted GREEN used
`C:\Users\woshica\AppData\Local\Temp\signvr-w6-targets-final-green-50d80cabdcdd4bf2bcc2d8481c0e6586`:
pure compile exit `0`, and
`BackgroundOperationRunsOffCallingThread`,
`ArtifactFreezeOwnerCancelsAndReapsWithoutOverlap`, and
`ArtifactVerifyOwnerCancelsAndReapsWithoutOverlap` all passed.

The pending-initialization queue-failure counterexample was then added before
the production ownership change. RED output directory:

`C:\Users\woshica\AppData\Local\Temp\signvr-w6-pending-init-red-7775a21c2d9e4c9ea3eb3fc4b93651c8`

Exact RED results:

- `RED_CORE_EXIT=0`
- `RED_PURE_EXIT=0`
- `EXPECTED_RED=1`
- `EXPECTED_RED_MESSAGE=Pending initialization made lifecycle queue failure wait for a future Controller callback.`

After replacing the secondary fallback with direct detached completion
ownership, targeted GREEN used
`C:\Users\woshica\AppData\Local\Temp\signvr-w6-pending-init-green-af7ae9fddf0f43cab38ecefbb00e7360`:
`GREEN_CORE_EXIT=0`, `GREEN_PURE_EXIT=0`, and `TARGET_GREEN=1`.

The final atomic consume-or-observe slice was also test-first. RED output:

`C:\Users\woshica\AppData\Local\Temp\signvr-w6-atomic-owner-red-ecdde35dc6da4b94a7d41201525ffd93`

Exact RED results:

- `RED_PURE_COMPILE_EXIT=0`
- `EXPECTED_RED=BackgroundOperationRunsOffCallingThread::System.InvalidOperationException::A failing completion observer escaped or blocked another owner.`
- `EXPECTED_RED=LifecycleTerminalizationHandoffPublishesAtomically::System.InvalidOperationException::A completed initialization was lost between queue-failure ownership snapshots.`

After production changed, targeted GREEN output was
`C:\Users\woshica\AppData\Local\Temp\signvr-w6-atomic-owner-green-62cbb1e2ed2449c285327e69c8dd058e`:
`GREEN_PURE_COMPILE_EXIT=0`, both named scenarios passed, and
`TARGET_GREEN_COUNT=2`.

The Orchestrator then supplied the first authoritative Unity execution result:
`53 passed / 9 failed` across the old 62-method EditMode fixture. Eight failures
were the lifecycle wrappers listed below: an ordinary MonoBehaviour does not
guarantee EditMode `OnDisable`/`OnDestroy`, so those tests never crossed the
product wrapper. `SetupUndoGroupRollsBackOnFailure` was the ninth failure because
it attempted to create a second additive untitled scene while the guard scene was
still untitled and unsaved. This `53/62` result is retained as the real Unity RED;
it is not relabeled as a passing run.

Before the boundary change, a non-Unity source gate independently reported:

- `STRUCTURAL_RED_EDITMODE_TESTS=62`
- `STRUCTURAL_RED_MISPLACED_LIFECYCLE_WRAPPERS=8`
- `STRUCTURAL_RED_PLAYMODE_TESTS=0`
- `STRUCTURAL_RED_GUARD_SAVE_BEFORE_TARGET=0`

After the change, the same gate reported:

- `STRUCTURAL_GREEN_EDITMODE_TESTS=54`
- `STRUCTURAL_GREEN_PLAYMODE_TESTS=8`
- `STRUCTURAL_GREEN_EDITMODE_LIFECYCLE_WRAPPERS=0`
- `STRUCTURAL_GREEN_PLAYMODE_LIFECYCLE_WRAPPERS=8`
- `STRUCTURAL_GREEN_GUARD_SAVE_BEFORE_TARGET=1`
- `EXECUTE_ALWAYS_REFERENCES=0`

Unity execution was prohibited in this worktree, so the new 54+8 split and the
saved-dirty guard-scene transaction remain explicit Orchestrator rerun gates.

### Final standalone compilation gate

Unity `6000.5.6f1` bundled Roslyn and .NET Standard 2.1 references were used
without starting the Editor. Final output directory:

`C:\Users\woshica\AppData\Local\Temp\signvr-w6-playmode-boundary-final-b3de290a55134a6795ef20f922909757`

Exact final results:

- `FINAL_CORE_EXIT=0`
- `FINAL_PURE_EXIT=0`
- `FINAL_PLAYER_EXIT=0`
- `FINAL_EDITOR_RUNTIME_EXIT=0`
- `FINAL_EDITOR_SETUP_EXIT=0`
- `FINAL_EDITMODE_TESTS_EXIT=0`
- `FINAL_PLAYMODE_TESTS_EXIT=0`
- `EDITMODE_TEST_METHODS=54`
- `PLAYMODE_TEST_METHODS=8`
- `TOTAL_TEST_METHODS=62`

All seven requested source targets were recompiled from current source with Unity
`6000.5.6f1` references. Both NUnit wrapper gates use the already-validated
compile-only stub; authoritative execution remains the Orchestrator's Unity Test
Runner rerun after integration.

### Deterministic scenario execution

Exact results from the freshly compiled final pure assembly:

- `FINAL_SCENARIOS_DISCOVERED=51`
- `FINAL_SCENARIOS_PASSED=51`
- `FINAL_SCENARIOS_FAILED=0`

All original 33 scenarios passed. The 11 new passing scenarios are:

1. `BackgroundOperationRunsOffCallingThread`
2. `ArtifactIntegrityWorkRunsOffCallingThread`
3. `CheckpointAndSealRunSeriallyOffCallingThread`
4. `CaptureBudgetsFailClosed`
5. `LowDiskSealRetainsPartial`
6. `CompletedSealRejectsEmptyCaptureWhileAbortAllowsIt`
7. `LifecycleShutdownAndRequestCancellationAreIdempotent`
8. `SetupPolicySeparatesStructureAndStudyReadiness`
9. `HeartbeatGenerationPersistsAndStrictlyIncrements`
10. `HeartbeatAckRequiresExactFreshEcho`
11. `HeartbeatDeadlineDoesNotDriftAfterSlowResponse`

The three follow-up scenarios also passed:

12. `TerminalSealArbitrationPreventsDoubleSeal`
13. `LifecycleTerminalizationHandoffPublishesAtomically`
14. `HeartbeatLifecycleResumePolicyIsPreStartOnly`

The two final-review pure scenarios also passed after each reproduced the old
five-second abandonment behavior in RED:

15. `LifecycleTerminalizationOwnsLateInitialization`
16. `LifecycleTerminalizationOwnsSlowSeal`

The two artifact-owner scenarios also passed:

17. `ArtifactFreezeOwnerCancelsAndReapsWithoutOverlap`
18. `ArtifactVerifyOwnerCancelsAndReapsWithoutOverlap`

Those two scenarios now use a passive observer and a deterministic three-chunk
artifact, assert exactly one observed chunk, and gate test cleanup on completed
plus reaped ownership. `BackgroundOperationRunsOffCallingThread` now also
proves rejected background operations and handoffs complete as failures,
rejected artifact work returns a completed/reaped failed lease, and a closing
serial scheduler rejects synchronously. The method count therefore remains 51
while the queue-refusal coverage traverses every W6 background entry shape.

The same 51-scenario run additionally proves rejected and throwing lifecycle
queues publish one failure result and can be consumed only once. With capture
initialization still pending, that failure is immediately consumable, the late
writer is adopted by a pure detached owner and disposed without a seal, and no
secondary queue is required. The same scenario now also covers completed and
pending initialization crossed with success/failure and rejecting/throwing
queues, verifies ready-writer ownership is not duplicated, and proves observer
exceptions cannot block later completion owners. It also proves stale/fresh Host
request epochs and callback exactly-once behavior; atomic post-hash cancellation
for freeze and verify; and deterministic 20 Hz cadence at 72/90 Hz with no
long-frame burst and immediate first sample for a new Run. No scenario was added
merely to increase the count; the existing public seams were strengthened so the
original 51 names exercise the new invariants.

Existing scenarios additionally revalidated manifest exactness, byte-identical
409 retries, exact GET recovery, start scheduling, true presentation origin,
event sequencing, capture gaps, phase flush, restart/backup recovery, ACK
webcam policy, immutable sealed artifacts, upload caps, readiness freshness and
case matching, lifecycle policy, summary semantics, unsafe paths, Study debug
rejection, and W2 artifact mapping. `AckStateNeverMutatesSealedArtifacts` now
also asserts upload-state fsync ran off its calling thread.

The Unity wrappers are now partitioned by their real execution boundary:
EditMode contains the 51 deterministic scenarios plus
`CompletedSealRejectsAbortThroughController`,
`CaptureSamplerEnforcesTwentyHertzCadence`, and
`SetupUndoGroupRollsBackOnFailure` (`54` total). PlayMode contains exactly the
eight lifecycle wrappers (`8` total). All 62 compiled here; only the old all-
EditMode layout has an authoritative Unity result (`53/62`, with the eight
lifecycle wrappers and setup rollback failing as described above):

- `CompletedSealRejectsAbortThroughController` exercises public
  `TryAbortRun` after the production Completed seal is queued, then verifies W1
  and the actual terminal artifacts;
- `DisableOwnsLateInitializationThroughController` triggers real component
  disable, Host heartbeat/request cancellation, delayed initialization ownership,
  main-thread reconciliation, and one Aborted artifact set;
- `DestroyDoesNotDuplicateDetachedTerminalization` triggers disable followed by
  destroy and verifies the original detached job remains the sole seal owner;
- `ControllerDisableCancelsOwnedArtifactFreeze` and
  `ControllerDestroyCancelsOwnedArtifactFreeze` drive real lifecycle callbacks
  after a controlled queue delay, replace the Controller's observer before
  worker release, require the startup snapshot to receive the controlled chunk,
  and assert W1 stays Completed while the lease cancels and reaps;
- `HostDisableCancelsOwnedArtifactVerify` starts public `PutArtifact`, interrupts
  its outer iterator with real Host disable, replaces the Host observer before
  worker release, requires the startup snapshot, rejects an overlapping retry,
  and verifies the file handle closes;
- `LifecycleQueueFailureConvergesThroughController` injects both rejecting and
  throwing queues through the real Controller disable/destroy lifecycle and
  verifies one consumed failure, Faulted W1, terminal-arbiter convergence,
  released partial handles, and reset convergence;
- `PendingInitializationQueueFailureConvergesThroughController` crosses real
  Controller Disable and Destroy with both rejecting and throwing queues while
  initialization is pending. It requires Faulted W1, a Terminal arbiter, and no
  pending Controller job before the lifecycle callback returns, then releases
  initialization and requires the detached owner to close the writer without a
  summary, terminal event, second terminalization, or lingering file handle;
- `HostLifecycleEpochRejectsStaleArtifactIterator` completes real artifact
  verification, disables/destroys the Host, continues the stale public iterator,
  and requires no request construction/send plus one failure callback; a newly
  enabled epoch can construct a fresh owned request;
- `CaptureSamplerEnforcesTwentyHertzCadence` drives the real sampler cadence seam
  at 72 Hz, 90 Hz, across a long frame, and across disable/re-enable;
- `SetupUndoGroupRollsBackOnFailure` exercises the actual Undo transaction in
  one temporary saved-but-dirty guard scene and one untitled target scene. Its
  outermost `finally` independently restores the active scene and PlayerPrefs,
  closes both scenes, and deletes the temporary scene asset and folder.

### Strict W3-compatible fixture rerun

The final compiled C# serializer/writer fixture and response-policy subset was
rerun without accessing the Host worktree:

- `FINAL_W3_FIXTURE_DISCOVERED=6`
- `FINAL_W3_FIXTURE_PASSED=6`
- `FINAL_W3_FIXTURE_FAILED=0`

Passing gates:

1. `ManifestMatchesFrozenContract`
2. `FrozenRegistrationRetriesExactBytesAfter409`
3. `ConflictRecoveryRequiresMatchingSnapshot`
4. `GeneratedFixtureCarriesW3StrictIdentity`
5. `W3AccurateResponsesAndTerminalBodiesMatch`
6. `AckRequiresWebcamAndAllFiveArtifacts`

This validates a real serialized manifest, exact lost-response retry bytes,
409 GET identity policy, all four generated Quest artifact record identities,
Complete/Abort and W3 response DTO shapes, and terminal five-artifact ACK
policy. It is not a claim that the isolated W3 HTTP service ran; that remains an
Orchestrator integration step.

### Metadata and boundary audit

- `RUNTIME_CS=21`
- `TARGET_CS=24`
- `TARGET_META=28` (24 source metas plus the PlayMode asmdef and three folder metas)
- `MISSING_META=0`
- `TARGET_GUID_ROWS=28`
- `DUPLICATE_TARGET_GUIDS=0`
- `NON_UNIQUE_GLOBAL_TARGET_GUIDS=0`
- new PlayMode asset GUID global hit counts: all five are `1`
- new GUIDs: PlayMode folder `89d8f771e04447d28f034c4ae1d1adbc`,
  Interaction folder `57ebded3e82c4815be3a64411a859d48`, CaptureHost
  folder `1f4b07d671ab4c35bb3ffc812f6edd29`, test source
  `7251a52041a246fbbe7d1d398b518e1b`, and asmdef
  `1ced1fed00a048708cfb1b4f258d3cd6`
- new test-seam GUID global hit counts: `1` and `1`
- new artifact-operation GUID global hit count: `1`
- `RUNTIME_ONCOMPLETED_CALLS=0`
- `CONTROLLER_THREADPOOL_CALLS=0`
- `CONTROLLER_BLOCKING_CALLS=0`
- `HOST_ARTIFACT_READALLBYTES=0`
- `HOST_UPLOADHANDLERFILE_CALLS=1`
- `HOST_PUBLIC_IENUMERATOR=8`
- `HOST_LEASE_ACQUIRE_CALLS=7` (Complete/Abort share one terminal path)
- `HOST_TRY_CREATE_CALLS=7`
- `HOST_SENDWEBREQUEST_CALLS=1`
- `HOST_ACTIVE_REGISTER_CALLS=1`
- `CONTROLLER_LOCAL_FREEZE_CALLS=0`
- `HOST_LOCAL_VERIFY_CALLS=0`
- `CONTROLLER_TASK_OR_BLOCKING=0`
- `HOST_TASK_OR_BLOCKING=0`
- `RUNTIME_QUEUEUSERWORKITEM_CALLS=1` (the default queue adapter only)
- `DEDICATED_LIFECYCLE_FALLBACK_REFERENCES=0`
- `OBSERVE_COMPLETION_REFERENCES=2` (the operation seam and the detached owner)
- `QUEUE_FAILURE_SNAPSHOT_WINDOW_BRANCHES=0`
- `ATOMIC_QUEUE_FAILURE_HELPER_REFERENCES=3` (two call sites plus definition)
- `OBSERVER_SAFE_INVOCATION_REFERENCES=3` (two call sites plus definition)
- `HASHER_CANCELLATION_GUARD_CALLS=3` (before open, after each
  observer-returned chunk, before finalization)
- `CONTROLLED_OBSERVER_CANCEL_THROW_CALLS=0`
- Controller freeze closure fields:
  `canonicalDirectory`, `observerSnapshot`; MonoBehaviour/Unity fields `0`
- Host verify closure fields: `artifactSnapshot`, `observerSnapshot`;
  MonoBehaviour/Unity fields `0`
- `SNAPSHOT_BAD_FIELDS_TOTAL=0`
- `IGNORED_DRIVER_WAITS=0`
- `EDITMODE_TEST_METHODS=54`
- `PLAYMODE_TEST_METHODS=8`
- `TOTAL_TEST_METHODS=62`
- `NUNIT_IGNORE_OR_EXPLICIT=0`
- `EDITMODE_LIFECYCLE_WRAPPERS=0`
- `PLAYMODE_LIFECYCLE_WRAPPERS=8`
- `PLAYMODE_ASMDEF_INCLUDE_PLATFORMS=0`
- `PLAYMODE_ASMDEF_TEST_CONSTRAINT=1`
- `PLAYMODE_ISPLAYING_ASSERTS=1`
- compiled PlayMode wrapper UnityEditor assembly references: `0`
- `EXECUTE_ALWAYS_REFERENCES=0`
- `ARM_HELPER_REFERENCES=10` (eight call sites plus two overloads)
- direct `ArmUnityLifecycleForTests` calls: `2`, both inside guarded
  `!Application.isPlaying` helper overloads
- `MANIFEST_QUEST_DEVICE_FIELDS=0`
- `SETUP_RUNTIME_CONFIGURE_CALLS=0`
- `SETUP_PLAYERPREFS_CALLS=0`
- `SETUP_RUNTIME_SAVE_OR_OPEN_CALLS=0`
- `TEST_UNSAFE_SCENE_CALLS=0`
- `TEST_ADDITIVE_SCENE_CALLS=2`
- `GUARD_SAVE_BEFORE_TARGET=1`
- test cleanup asset calls: `CREATE_FOLDER=1`, `SAVE_SCENE=1`,
  `DELETE_ASSET=2`
- `LEFTOVER_W6_TEMP_ASSET_FOLDERS=0`
- outer restoration calls: `TEST_PREFS_SET=1`, `TEST_PREFS_DELETE=1`,
  `TEST_PREFS_SAVE=1`
- `TRAILING_WHITESPACE_LINES=0`
- `CONFLICT_MARKER_LINES=0`
- InteractionLab bytes: `408990`
- InteractionLab SHA-256 before/after:
  `A394EABE4D72739C625BA5A261F36DF86A87B15C201CCB02F53A134B1DF027B5`

Git `diff --check` was not run because every Git command was explicitly
prohibited. The two non-Git textual checks above found no whitespace errors or
merge markers.

## 4. Generated files or local prerequisites intentionally not versioned

- Roslyn outputs, the compile-only NUnit stub DLL, and deterministic fixture
  directories under `%TEMP%` are validation artifacts only.
- Quest capture, partials, and `.upload-state.json` remain under
  `Application.persistentDataPath/interaction-tests`; no cleanup is automatic.
- Study requires safe batch/participant/build identity, W2 content catalog,
  persistent `SignVR.DeviceId`, a LAN HTTP Host URL, one successfully echoed
  heartbeat in the current app session, and fresh exact readiness.
- W5/W8 must subscribe to `PresentationRequested` and call
  `NotifyInstructionPlaybackStarted` on the actual first presented frame.
- W8 must inject a real HMD, bilateral hand data sources, and one or more valid
  object probes before the strict Study validator/CanStart can pass.
- Structural Editor setup deliberately leaves the scene dirty and unsaved.

## 5. Contract deviations, risks, and unfinished items

### Frozen contracts

- No known frozen manifest/artifact deviation. The manifest remains exact
  Contract V1 snake_case with six phases and no Quest identity field.
- Quest identity exists only in the canonical heartbeat and
  `X-SignVR-Quest-Id` header.
- ACK state remains an atomic sidecar. No ACK-time event is appended to sealed
  JSONL, so Host and Quest upload bytes cannot diverge.

### Risks and unfinished validation

1. The Orchestrator ran the pre-fix all-EditMode fixture and reported `53/62`.
   This worktree did not start Unity, so neither the new PlayMode eight nor the
   repaired saved-dirty guard-scene EditMode test has a post-fix Unity result.
   Android/IL2CPP and Quest execution also remain unrun. Static compilation and
   source gates cannot replace that rerun.
2. Live W8a heartbeat ACK/readiness TTL, W3 POST/409/GET/PUT/ACK, browser camera,
   and participant admission were not available inside this worktree. Field
   names are centralized for a small integration adaptation if necessary.
3. `DriveInfo.AvailableFreeSpace`, `UploadHandlerFile` cancellation/disposal,
   worker threading, and file durability compile for Unity 6 but require Quest
   hardware validation for IL2CPP/platform behavior and peak memory.
4. OS fsync latency cannot be forcibly interrupted. It is no longer on the
   Unity thread; bounded drain/join failures retain partial data. A hard process
   kill may stop before background abort finishes, by design leaving recovery
   evidence rather than claiming Completed.
5. If lifecycle shutdown occurs after a Completed/Aborted seal is already
   queued, that terminal local seal is allowed to finish but upload is
   suppressed; re-enable reconciles the result into W1. Earlier consumed states
   become an explicit local abort.
6. `InteractionRunController` remains large. This is the registered smell the
   review explicitly excluded from this targeted remediation; splitting it now
   would increase integration risk and could accidentally create a second
   lifecycle authority.
7. `BeginRecoverAllPartialRunsAsAborted` is asynchronous. Integration callers
   must poll success/failure and refresh discovery afterward.
8. Recorder regression, thermal/performance, long-run queue pressure, low-disk,
   power-loss, and reboot recovery remain device/integrated gates.
9. For a normally accepted lifecycle job, the job remains the sole owner while
   initialization is pending. For a queue-start failure, the Controller faults
   and marks terminal immediately while a pure completion observer becomes the
   sole owner of the pending initialization; eventual success disposes the
   writer without sealing it. If initialization never returns, that observer
   deliberately remains attached instead of publishing false success. A process
   exit leaves the manifest/partial directory for restart recovery; a permanently
   hung platform I/O call remains a device fault-injection gate.
10. Artifact cancellation is cooperative at 64 KiB boundaries. It closes the
    stream promptly after a normal chunk read returns, but cannot preempt a
    platform `FileStream.Read` call that is itself permanently stuck. Device
    storage fault injection remains the authority for that platform behavior.

## 6. Recommended Orchestrator integration steps

1. Integrate the W6 files as a unit; do not add a CaptureHost/runtime asmdef or
   modify `SignVR.Interaction.Core`.
2. Import the main Unity `6000.5.6f1` project and require Player plus Editor
   compile. Run EditMode class
   `SignVR.Interaction.Editor.Tests.W6InteractionCaptureHostTests` and expect
   `54/54`. Then run PlayMode class
   `SignVR.Interaction.PlayMode.Tests.W6InteractionCaptureHostPlayModeTests` and
   expect `8/8`. The authoritative total remains `62/62`; do not run the eight
   lifecycle methods through their removed EditMode wrappers.
3. In a clean loaded `InteractionLab.unity`, run structural setup first and
   confirm it succeeds with no tracking/probes. Undo once and verify all three
   additions/reference changes roll back. Reapply, inject W8 tracking/probes,
   then run the separate strict Study readiness validator. Orchestrator alone
   should save the reviewed scene.
4. Compare W8a with the centralized heartbeat seam: exact body, persistent
   Int64 generation, sequence from 1, `accepted=true` exact ordinal echo,
   two-second fixed deadlines, one routine, five-second freshness, and request
   abort/disposal across pause/disable.
5. Run the live W3 sequence with a C# fixture: manifest POST, simulated lost
   response, 409 plus exact GET recovery, unrelated 409 rejection, Complete and
   Abort, all four streamed artifact PUTs including response-loss retries, and
   ACK false until Host webcam is present. Compare Quest/Host lengths and SHA.
6. Exercise lifecycle shutdown during initialization, each phase checkpoint,
   active artifact upload, the phase-6 Completing checkpoint window, and an
   already queued Completed seal. Verify checkpoint-window Abort produces one
   Aborted seal, late Abort is explicitly rejected while the original Completed
   seal wins, and pause/resume cannot re-enable heartbeat until PreStart. Reboot
   and verify pending discovery/async abort recovery with no duplicate summary,
   event, seal, upload, or false Completed status.
   Also queue a freeze/verify, replace the component observer before releasing
   the worker, then disable and destroy during the startup observer's blocked
   chunk; require only that startup snapshot to be called, one cancellation, no
   overlapping retry, zero owned operations after reap, and an exclusively
   reopenable artifact file.
   Repeat once after hash completion but before publication, and require the
   cancellation winner to prevent a success result. Inject rejecting/throwing
   lifecycle queues and require one Faulted reconciliation with released writer
   ownership. Complete verify, cross a Host lifecycle epoch, then continue the
   stale iterator and require no request creation/send and one callback.
7. On Quest, test one-line/pending/file caps, low-disk watermark, slow fsync,
   queue overflow/capture_gap, nonempty Completed pose/object enforcement,
   empty Abort streams, late XR rig resolution, and actual HMD/hand/joint/object
   rows while profiling frame time, memory, and thermal behavior.
8. Re-run Recorder compile/build/smoke regression and confirm Recording Take
   paths, upload semantics, Host Recorder routes, scenes, and settings remain
   unchanged.
