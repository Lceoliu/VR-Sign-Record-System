# W3 Host Interaction Mode

Date: 2026-08-25
Worker baseline supplied by Orchestrator: `35fcc93`

Per the worker policy, this task ran no Git command, created no commit, did not edit `docs/interaction/PROGRESS.md`, and made no Unity, OpenXR, Recorder-data, or existing `data/recordings` semantic change.

## 1. Scope completed

- Added an Interaction-specific backend module with separate models, repository, service interface, and HTTP routes. An Interaction Run is never represented as a Recording Take.
- Implemented the Contract V1 minimum endpoints:
  - `GET /api/interaction/readiness`
  - `POST /api/interaction/runs`
  - `POST /api/interaction/runs/{run_id}/complete`
  - `POST /api/interaction/runs/{run_id}/abort`
  - `PUT /api/interaction/runs/{run_id}/artifacts/{artifact_type}`
  - `GET /api/interaction/runs/{run_id}/ack`
- Added the additive `PUT /api/interaction/readiness/camera` heartbeat so Quest-visible readiness cannot claim that webcam capture is ready merely because the backend process is alive. A positive camera report expires after five seconds without a browser heartbeat.
- The Study browser now recomputes every heartbeat from the current `MediaStream.active` value and its current live video tracks instead of replaying cached React state. Track `ended` and stream `inactive` events report the loss immediately. Stream identity and camera-request generation guards prevent callbacks or late `getUserMedia` results from an older camera selection from marking the replacement camera unavailable.
- `POST /api/interaction/runs` now fails closed with `409` unless Interaction storage is writable, a Quest is both selected and paired, and the browser camera heartbeat is fresh. A rejected Quest-authored request creates no Run directory, so W6 can retry the identical immutable plan; accepted bytes are still preserved exactly.
- Run registration is exclusive while any Run is `Scheduled` or `Running`. The route performs an early conflict check and repository creation repeats the active-state check while holding the same write lock, so simultaneous distinct registrations cannot both persist. `Completed` and `Aborted` history does not block the next Run.
- `active_run` now means active literally: readiness returns only a `Scheduled`/`Running` snapshot and returns `null` after completion or abort instead of presenting the most recent terminal Run as the current round.
- Added the additive read-only `GET /api/interaction/runs/{run_id}` snapshot endpoint. Unlike readiness `active_run`, it returns the complete Scheduled/Running/Completed/Aborted state of the requested persisted Run, allowing capture recovery by stable Run ID.
- Strictly validates top-level/nested Run Plan fields, duplicate JSON keys, schema version types, safe storage identifiers, normalized relative source paths, SHA-256 text, UTC timestamps, four unique safe digits, the chest-button permutation, the three allowed assistance conditions, six ordered phases, Contract V1 sentence ranges, and pilot signer `wang`.
- Validates path/body/artifact `run_id` consistency. JSONL capture rows and summary JSON must declare `schema_version: 1` and the path Run ID. Events additionally validate the complete Contract V1 event field shape and increasing `event_seq`.
- Stores the exact accepted Quest request bytes as `run.manifest.json`; it is not reconstructed from the Pydantic model.
- Stores Runs at `data/interaction-tests/{batch_id}/{participant_id}/{run_id}/`. Run creation is staged then atomically renamed. Artifact uploads are staged, flushed, validated, and published with a non-overwriting same-volume hard link. Duplicate Run IDs are rejected globally (case-insensitively), and existing artifacts return `409` without replacement.
- Persists additive Host state in `run.status.json`, including scheduled start, terminal state/time, and abort reason. ACK becomes true only after the Run is `Completed` or `Aborted` and all five required artifacts exist durably.
- Serializes every state-machine transition through one repository primitive that reads `run.status.json`, decides the transition, and atomically replaces the file while holding the same lock. Concurrent Complete/Abort calls therefore choose exactly one terminal state, and a delayed Scheduled-to-Running request can never overwrite `Completed` or `Aborted`.
- Corrupt existing Interaction storage does not prevent the Recording application from starting. Interaction readiness reports `storage_ready: false`, and new Interaction writes fail closed until the bad Run is repaired.
- Reused the existing `RealtimeHub` to publish scheduled/running/completed/aborted/artifact/ACK events. The synchronized start uses the existing two-second Host start convention and returns `start_at_utc`.
- Added a separate Study UI at `http://127.0.0.1:8011/?mode=interaction`, with a discoverable link from and back to the unchanged default Recording UI.
- Study UI provides local pseudonymous participant-ID verification, backend/storage/Quest/camera readiness, current Run ID and Run state, scheduled countdown, real webcam capture/upload state, abort-with-reason, and the authoritative missing-artifact/ACK list. It does not display or generate a condition, password, sentence, Task Variant, signer, or Take.
- Study webcam capture uses `getUserMedia({audio: false})`, starts against `start_at_utc`, stops when the bound Run becomes Completed/Aborted, and uploads one `webcam.webm`. WebSocket events provide the fast path; every readiness refresh polls the bound `captureRunId` snapshot as the reliability fallback, so losing the terminal WebSocket event cannot leave MediaRecorder running indefinitely.
- Readiness refreshes use monotonic request sequence watermarks. A newer HTTP observation, WebSocket Run event, or local Abort response invalidates older in-flight refreshes so an early Scheduled/Running response cannot overwrite a newer terminal state.
- Before any webcam PUT starts, the completed `Blob` is retained in a Run-keyed in-memory recovery table. Failed uploads and inconclusive ACK checks keep the exact Blob reachable; it is removed only after the PUT succeeds or `GET .../ack` proves that Host no longer lists `webcam` as missing.
- Webcam retry is serialized by a synchronous per-Run transition gate. Repeated clicks cannot start a concurrent request, attempt/blob identity prevents stale completion from releasing the wrong entry, and a later Run receives its own entry without replacing an earlier pending Run. Host's existing non-overwriting artifact endpoint remains the server-side safeguard for uncertain-response retries.
- The Study UI exposes every pending Run with an explicit **重试上传** action and a **保存到本地** fallback. The download name is `{run_id}.webcam.webm`, and downloading does not falsely mark Host upload as successful or release the in-memory Blob.

### Deliberately not completed

- No Unity/Quest client, scene, OpenXR, Recorder protocol, or device build work.
- No real Quest or physical webcam end-to-end run was claimed. Browser visual QA intentionally did not accept a camera permission prompt, so it verified the honest NOT READY path rather than hardware capture.
- No live browser network-fault test forcibly dropped the WebSocket during an actual MediaRecorder session. The no-WebSocket Scheduled-to-Running-to-terminal path and stale-response ordering are covered deterministically at the pure frontend state seam plus the real backend HTTP API seam.
- Webcam recovery is intentionally memory-only in this minimal fix. Refreshing, closing, or navigating away from the Study page destroys any Blob that has not already reached Host or been manually downloaded; no IndexedDB/File System Access persistence is claimed.
- Backend verifies `video/webm` content type and the WebM EBML header, but does not demux the container to prove that it has no audio track. The browser source constraint is the video-only guarantee at capture time.
- No new LAN authentication scheme was added; this preserves the Host's existing trusted-local-network deployment model.

## 2. Files created or changed

Created:

- `vr-sign-host/backend/app/interaction_models.py`
- `vr-sign-host/backend/app/interaction_repository.py`
- `vr-sign-host/backend/app/interaction_service.py`
- `vr-sign-host/backend/app/interaction_routes.py`
- `vr-sign-host/backend/tests/test_interaction_api.py`
- `vr-sign-host/backend/tests/test_interaction_service.py`
- `vr-sign-host/backend/tests/test_interaction_concurrency.py`
- `vr-sign-host/frontend/src/InteractionStudyApp.tsx`
- `vr-sign-host/frontend/src/interaction-study.css`
- `vr-sign-host/frontend/src/interactionCapture.ts`
- `vr-sign-host/frontend/src/interactionCapture.test.ts`
- `docs/interaction/reports/W3-host-interaction.md`

Changed:

- `vr-sign-host/backend/app/main.py`
- `vr-sign-host/frontend/src/App.tsx`
- `vr-sign-host/frontend/src/api.ts`
- `vr-sign-host/frontend/src/main.tsx`
- `vr-sign-host/frontend/src/styles.css`
- `vr-sign-host/frontend/src/types.ts`
- `vr-sign-host/frontend/package.json`
- `vr-sign-host/frontend/pnpm-lock.yaml`

## 3. Commands and exact results

Baseline before implementation:

- `cd vr-sign-host/backend; python -m pytest -q`
  - 30 tests collected: 29 passed, 1 failed.
  - The sole baseline failure was `test_frontend_es_module_uses_javascript_mime_type`, because ignored `frontend/dist` did not exist yet and `/` returned 404.

Dependency/build preparation:

- `cd vr-sign-host/frontend; pnpm install`
  - Exit 0; 177 packages added locally; `pnpm-lock.yaml` updated for Vitest.

Final automated verification:

- Before the concurrency fix, `cd vr-sign-host/backend; python -m pytest -q tests/test_interaction_concurrency.py`
  - Exit 1; **2 failed in 0.32s**.
  - The deterministic asyncio reproductions showed both concurrent Complete and Abort calls succeeding, and a delayed Scheduled-to-Running write replacing an already persisted `Aborted` state.
- Initial camera-helper red run, `cd vr-sign-host/frontend; pnpm test -- interactionCapture.test.ts`
  - Exit 1; **1 test file failed; 2 failed and 5 passed**, duration 180ms.
  - The missing helper failed the exact live/ended/no-track/inactive and stale-stream identity cases.
- Camera-helper green run with the pure helper implemented:
  - Exit 0; **1 test file passed; 7 passed**, duration 166ms. The same suite remained green after wiring the helper into heartbeat and event handling.
- Initial readiness-gate red run, `cd vr-sign-host/backend; python -m pytest -q tests/test_interaction_api.py::test_run_registration_rejects_each_missing_readiness_without_persisting_plan`
  - Exit 1; **3 failed in 0.76s**. Missing storage returned 400, while missing Quest and missing camera each incorrectly returned 200.
- Readiness-gate green rerun:
  - Exit 0; **3 passed in 0.72s**; every missing readiness component returns 409 and the proposed Run directory is absent.
- Initial active-Run red run, `cd vr-sign-host/backend; python -m pytest -q tests/test_interaction_api.py::test_only_active_run_blocks_registration_and_terminal_history_is_not_active`
  - Exit 1; **1 failed in 0.71s** because a second plan was incorrectly accepted with HTTP 200 while the first was active.
- Active-Run green rerun:
  - Exit 0; **1 passed in 0.71s**; the same raw second plan is rejected without persistence, then accepted byte-for-byte after the first reaches a terminal state. The test also proves both Completed and Aborted Runs yield `active_run: null` and permit the next Run.
- The first post-wiring TypeScript build correctly caught a missing explicit null narrowing (`TS2345` at `InteractionStudyApp.tsx`); after restoring the explicit stream guard, the final build below is green.
- Initial terminal-snapshot red run, `cd vr-sign-host/backend; python -m pytest -q tests/test_interaction_api.py::test_run_snapshot_remains_pollable_after_it_is_no_longer_active`
  - Exit 1; **2 failed in 0.77s**. Both Completed and Aborted Runs correctly disappeared from readiness `active_run`, but the requested stable Run snapshot returned 404.
- Terminal-snapshot green rerun after adding the read-only endpoint:
  - Exit 0; **2 passed in 0.66s**. Scheduled/Running snapshots remain queryable, and both terminal states remain queryable after `active_run` becomes null.
- Initial HTTP-fallback reducer red run, `cd vr-sign-host/frontend; pnpm test -- interactionCapture.test.ts`
  - Exit 1; **1 test file failed; 2 failed and 7 passed**, duration 180ms. The missing reducer could neither turn `active_run: null` plus a bound terminal HTTP snapshot into Stop nor reject an older readiness response.
- HTTP-fallback reducer green rerun:
  - Exit 0; **1 test file passed; 9 passed**, duration 168ms. The retained test now drives Scheduled, Running, and Completed without any WebSocket event and proves the bound recorder receives Stop; the terminal predicate also covers Aborted.
- Initial webcam-recovery state red run, `cd vr-sign-host/frontend; pnpm test -- interactionCapture.test.ts`
  - Exit 1; **1 test file failed; 2 failed and 9 passed**, duration 182ms. Both new tests failed with `TypeError: transitionWebcamRecovery is not a function`, proving that no Run-keyed Blob retention/concurrent-retry seam existed yet.
- Webcam-recovery state green rerun after implementing the pure transition seam:
  - Exit 0; **1 test file passed; 11 passed**, duration 188ms. The tests retain the exact Blob after failure, reject a concurrent retry, preserve an older Run when a new Run arrives, refuse same-Run replacement, ignore mismatched/stale confirmation, release only the matching confirmed entry, and verify the Run-bearing download filename.
- Final `cd vr-sign-host/backend; python -m pytest -q tests/test_interaction_concurrency.py`
  - Exit 0; **3 passed in 0.19s**.
  - The asyncio barrier tests cover Complete versus Abort, delayed Running versus terminal, and two simultaneous distinct Run registrations choosing exactly one persisted active Run.
- Final `cd vr-sign-host/backend; python -m pytest -q tests/test_interaction_api.py`
  - Exit 0; **28 passed in 1.78s**.
- `cd vr-sign-host/backend; python -m pytest -q tests/test_interaction_api.py tests/test_interaction_service.py tests/test_interaction_concurrency.py`
  - Exit 0; **32 passed in 1.74s**.
- `cd vr-sign-host/backend; python -m pytest -q`
  - Exit 0; **62 passed in 2.31s**.
  - Includes all pre-existing API/service/config/UDP tests plus Interaction normal flow, exact manifest retention and retry, readiness fail-closed behavior, selected-but-unpaired Quest rejection, active-Run exclusivity/terminal release, stable Run snapshot polling, duplicate rejection, invalid ID/path/schema/condition/six-phase/sentence cases, camera heartbeat expiry, deterministic concurrent state/registration transitions, atomic/non-overwriting artifact upload, missing list/ACK, restart recovery, and corrupt-Interaction-storage Recorder regression.
- `cd vr-sign-host/frontend; pnpm test`
  - Exit 0; **1 test file passed, 11 tests passed**, duration 164ms.
- `cd vr-sign-host/frontend; pnpm lint`
  - Exit 0; no warnings or errors.
- `cd vr-sign-host/frontend; pnpm build`
  - Exit 0; TypeScript project build and Vite production build succeeded; 1812 modules transformed. Final generated bundle reported `dist/index.html` 0.48 kB (gzip 0.33 kB), CSS 22.67 kB (gzip 5.19 kB), JS 241.05 kB (gzip 76.02 kB); Vite build phase completed in 170ms.

Local browser verification against the compiled frontend and a `start_udp=False` FastAPI instance:

- Study mode rendered the four live readiness cards, showed Backend READY and unpaired Quest/unavailable camera as NOT READY, accepted local `P001` verification, showed all five missing artifacts, kept Abort disabled without a Run, and showed no fabricated capture success.
- The `返回 Recording Take` link returned to the existing 31-sentence Recorder console with its original controls and disabled-state behavior.
- Final browser console inspection returned zero warnings/errors.

## 4. Generated/local prerequisites not versioned

- `vr-sign-host/frontend/node_modules/` from `pnpm install` (ignored).
- `vr-sign-host/frontend/dist/` from `pnpm build` (ignored; portable packaging must rebuild/include it).
- Python `__pycache__/`, `.pytest_cache/`, and `.pyc` test artifacts (ignored).
- Empty local default `vr-sign-host/data/recordings/` and `vr-sign-host/data/interaction-tests/` directories created when importing the default app (the entire Host `data/` directory is ignored). No existing recording file was written or changed.
- Browser QA used `%TEMP%/signvr-w3-browser-b57e` as its separate temporary `SIGNVR_DATA_ROOT`; it is not a repository prerequisite.
- Runtime prerequisites remain Python backend requirements plus a built frontend. Frontend development/testing now additionally uses Vitest through the checked-in lockfile.

## 5. Migration, startup, contract details, and assumptions

### Migration/start

- There is no Recording data migration. Existing `data/recordings` layout and endpoints remain unchanged.
- Install/build as before: install `vr-sign-host/backend/requirements-lock.txt`, then run `pnpm install` and `pnpm build` in `vr-sign-host/frontend` when producing a new frontend bundle.
- Start the normal Host with `vr-sign-host/scripts/start-local.ps1` (or the portable launcher). Default `/` remains Recording; open `/?mode=interaction` for Study mode.
- The first accepted Run creates the new sibling tree `SIGNVR_DATA_ROOT/interaction-tests/...` automatically.

### Request assumptions added where Contract V1 does not freeze a body shape

- Complete JSON body: `{"schema_version":1,"run_id":"<same path ID>","completed_utc":"<optional UTC>"}`.
- Abort JSON body: `{"schema_version":1,"run_id":"<same path ID>","abort_reason":"<required>","aborted_utc":"<optional UTC>"}`.
- JSONL uploads accept `application/x-ndjson`, `application/jsonl`, `application/json`, `text/plain`, or `application/octet-stream`. Summary accepts `application/json` or octet-stream. Webcam accepts `video/webm` or octet-stream.
- Events must contain at least one row. Empty `poses.jsonl` or `objects.jsonl` remains valid for an immediately aborted partial capture, while any present row must carry matching schema/run identity.
- `task_variant` stays a required non-empty JSON object because Contract V1 intentionally does not freeze the six phase-specific inner shapes. Pose/object rows similarly validate JSON-object and identity safety, not an invented capture schema.
- Lower-snake-case additive event names are accepted; the Contract V1 required names are not incorrectly treated as an exclusive enum.

### Additive implementation details

- `PUT /api/interaction/readiness/camera` and the readiness fields `camera_ready`, `camera_last_seen_utc`, `storage_error`, and `active_run` are additive.
- `GET /api/interaction/runs/{run_id}` is additive and read-only. It uses the same strict Run ID validation and persisted status transition logic as the existing snapshot service, returning 400 for unsafe IDs and 404 for unknown Runs.
- `POST /api/interaction/runs` returns 409 before parsing or persisting a Run Plan when Host readiness is false or an active Run exists. This ordering is intentional: Quest retains and retries the same already-frozen plan after the readiness conflict clears; it must not rerandomize.
- `active_run` is `null` unless a persisted Run is currently `Scheduled` or `Running`. Terminal history is still durably available through its Run files and ACK endpoint but is not mislabeled as the current Run by readiness.
- `run.status.json` is additive Host metadata; it does not replace or modify the exact Quest-authored `run.manifest.json`.
- Duplicate Run/artifact requests return conflict rather than silently treating a retry as success. A client that loses an upload response must query ACK/missing artifacts before deciding whether to retry.

## 6. Risks and recommended Orchestrator review

1. Compare W1's actual serialized Run Plan against these strict models, especially `task_variant`, UTC formatting, safe `take_id`, `artifact_path` prefix, and the complete/abort request bodies.
2. Confirm the Quest uploader sends a matching `run_id` and `schema_version: 1` on every JSONL pose/object row and summary. Confirm its event rows include every Contract field, including nullable `phase_id`, `actor_id`, and `target_id`.
3. Run a real integrated cycle: pair/select Quest, grant browser camera permission, observe the five-second camera readiness heartbeat, submit a Quest-authored plan, verify synchronized camera start, complete and abort separate Runs, upload all artifacts, confirm Quest retains local files until ACK, and byte-compare `run.manifest.json` with the sent body.
4. Test a physical camera WebM to confirm the chosen browser/codec writes the expected EBML header and no audio track. Physically unplug and hot-switch cameras to confirm the tested `ended`/`inactive`/stale-stream logic matches the target browser and hardware. Exercise late camera permission and browser refresh during a Run; these intentionally remain visible failure/late-start cases.
5. Validate `os.link` atomic publication on the actual station data volume, especially if `SIGNVR_DATA_ROOT` is a NAS/SMB path. Failure is non-destructive and leaves no published destination, but the target filesystem must support same-volume hard links or the repository adapter needs an alternative exclusive-publish primitive.
6. Re-run the full backend suite after integration and manually smoke-test both `/` and `/?mode=interaction`; the default page must remain the Recording console.
7. Review whether the trusted-LAN deployment is sufficient for Study data. Authentication was deliberately not broadened in this worker because it would change the existing Host network contract.
8. Ensure portable packaging rebuilds/includes the new frontend `dist`; it is intentionally ignored in this worktree.
9. The state-transition and active-registration lock is process-local, matching the current single Host repository/service instance. If deployment later runs multiple backend worker processes against one `SIGNVR_DATA_ROOT`, add an inter-process lock or a transactional store before treating cross-process transitions or Run exclusivity as serialized.
10. During integration, explicitly disconnect or firewall the event WebSocket while leaving HTTP available, then complete and abort physical webcam Runs. The deterministic reducer/API tests cover this ordering, but a real browser MediaRecorder/network-fault cycle remains a required station validation.
11. Force a physical webcam PUT failure, verify the pending Run remains visible while a new Run is captured, exercise rapid repeated retry clicks, and confirm the Host file is not replaced. Also download the fallback and confirm its `{run_id}.webcam.webm` name/content before deliberately refreshing the page; refresh loss is expected because recovery is not durably browser-persisted.
