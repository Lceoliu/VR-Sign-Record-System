# W8a Host Study Readiness

Date: 2026-08-26

This worker used no Git command, created no commit, did not start Unity, and did
not edit `docs/interaction/PROGRESS.md`. Recorder UDP ports, packets, routes, and
the default Recorder frontend workflow remain unchanged.

## 1. Scope completed

- Replaced the Interaction Study readiness dependency on the Recorder
  `DeviceRegistry` with two Interaction-only HTTP heartbeats on TCP 8011.
- Added explicit, independently reported Quest, camera, and Participant
  freshness. The single process-local TTL is
  `INTERACTION_READINESS_TTL_SECONDS = 5.0`; age `<= 5.0` seconds is fresh and
  age `> 5.0` seconds is stale.
- Freshness uses Host `time.monotonic()` only. UTC fields are display/audit
  timestamps and changing wall-clock UTC cannot extend presence.
- Added strict int64 heartbeat watermarks. A stale/equal
  `(heartbeat_generation, heartbeat_sequence)` returns the current state with
  `accepted: false` and does not change readiness or `*_last_seen_utc`.
- Serialized Quest/camera/Participant updates, readiness snapshots, and Run
  admission through one `asyncio.Lock`. Admission holds that lock through the
  repository create operation, so a concurrent Participant change cannot land
  between identity validation and persistence.
- Host restart intentionally restores no presence state: all three presences
  start stale and must heartbeat again.
- Extended the Study page's existing localStorage-backed anonymous Participant
  input into the camera heartbeat. Invalid input immediately advertises
  `ready: false` with `participant_id: null` and cannot reuse a previous valid
  identity.
- The Study page does not display camera/Participant READY until the Host
  accepts and echoes the exact latest generation, sequence, and Participant.
  Sending every new heartbeat first clears any previous READY while the request
  is pending; only its exact accepted echo restores READY. Older or failed
  responses cannot retain or relight READY.
- Allocates camera heartbeat generations from one module-level page-session
  allocator. Every component mount receives a strictly larger generation even
  when `performance.timeOrigin` is unchanged, so a remount that restarts its
  sequence at 1 cannot be trapped below the Host's previous watermark.
- Binds camera/Participant retirement to `pagehide` and sends the canonical
  `ready: false, participant_id: null` PUT with `fetch(..., {keepalive: true})`.
  React effect cleanup invokes the same idempotent retirement seam, so
  pagehide plus cleanup never emits duplicate retirement watermarks.
- Hardened `POST /api/interaction/runs` to bind the caller's
  `X-SignVR-Quest-Id` to the fresh Quest heartbeat and the frozen manifest's
  `participant_id` to the fresh Host Participant heartbeat before any Run is
  created.

## 2. Exact additive HTTP contract

All bodies require `Content-Type: application/json`, reject unknown fields, and
use strict JSON types (for example, `1` is not accepted as a boolean and
`true` is not accepted as an integer).

### Quest heartbeat

`PUT /api/interaction/readiness/quest`

```json
{
  "schema_version": 1,
  "quest_device_id": "quest-interaction",
  "ready": true,
  "heartbeat_generation": 1724600000000,
  "heartbeat_sequence": 1
}
```

- `quest_device_id` uses the existing safe Interaction identifier rules.
- `heartbeat_generation` is an integer in `0..9223372036854775807`.
- `heartbeat_sequence` is an integer in `1..9223372036854775807`.
- Watermarks are compared lexicographically for the same Quest owner.
- A heartbeat from a different Quest returns `409` while the current Quest
  presence is fresh. Once it is older than five seconds, a different Quest may
  take ownership with its own watermark.
- A successful response contains exactly the current `accepted`,
  `quest_fresh`, `quest_ready`, `quest_device_id`, `quest_last_seen_utc`, and
  echoed watermark fields plus `schema_version`.

### Host camera and Participant heartbeat

`PUT /api/interaction/readiness/camera`

```json
{
  "schema_version": 1,
  "ready": true,
  "participant_id": "P001",
  "heartbeat_generation": 1724600000000,
  "heartbeat_sequence": 1
}
```

- The generation/sequence ranges and ordering are the same as the Quest
  heartbeat, with an independent camera-channel watermark.
- `ready: true` requires a valid non-null `participant_id`.
- `ready: false` accepts either a valid Participant ID or `null`; the Study page
  sends `null` when its input is invalid or when it clears presence on unload.
  Unload/navigation clearing uses `pagehide` plus a keepalive PUT instead of
  depending only on an ordinary asynchronous React-cleanup fetch.
- A successful response contains `accepted`, `camera_fresh`, `camera_ready`,
  `camera_last_seen_utc`, `participant_fresh`, `participant_ready`,
  `participant_id`, `participant_last_seen_utc`, and the current watermark.

### Readiness snapshot

`GET /api/interaction/readiness` retains the W3 compatibility fields and adds:

- `quest_fresh`, `quest_last_seen_utc`
- `camera_fresh`, `camera_last_seen_utc`
- `participant_fresh`, `participant_ready`, `participant_id`,
  `participant_last_seen_utc`

`ready` is true only when storage is writable and the current HTTP Quest,
camera, and Participant assertions are all ready and fresh. Recorder UDP
selection/pairing does not contribute to this result.

### Run admission

`POST /api/interaction/runs` now also requires:

```http
X-SignVR-Quest-Id: quest-interaction
```

While holding the readiness lock, Host requires all of the following before
calling repository creation:

1. storage is ready;
2. Quest, camera, and Participant HTTP presences are ready and fresh;
3. `X-SignVR-Quest-Id` exactly equals the fresh heartbeat's
   `quest_device_id`;
4. the Contract V1 manifest's `participant_id` exactly equals the fresh Host
   `participant_id`.

Missing/stale presence, a missing/wrong Quest header, or a wrong Participant
returns `409`; identity rejection leaves no Run manifest or Run directory.

The frozen `INTERACTION_CONTRACT_V1` Run Plan was deliberately **not changed**.
In particular, `quest_device_id` is not a V1 manifest field and remains rejected
by the model's `extra="forbid"`. Quest identity is carried only by the request
header and fresh heartbeat.

## 3. Files changed

- `vr-sign-host/backend/app/interaction_models.py`
- `vr-sign-host/backend/app/interaction_service.py`
- `vr-sign-host/backend/app/interaction_routes.py`
- `vr-sign-host/backend/app/main.py`
- `vr-sign-host/backend/tests/test_interaction_api.py`
- `vr-sign-host/backend/tests/test_interaction_service.py`
- `vr-sign-host/backend/tests/test_interaction_concurrency.py`
- `vr-sign-host/frontend/src/types.ts`
- `vr-sign-host/frontend/src/api.ts`
- `vr-sign-host/frontend/src/interactionCapture.ts`
- `vr-sign-host/frontend/src/interactionCapture.test.ts`
- `vr-sign-host/frontend/src/InteractionStudyApp.tsx`
- `docs/interaction/reports/W8a-host-study-readiness.md`

No Unity file, Recorder UDP/protocol/service implementation, Recorder frontend
workflow file, or `docs/interaction/PROGRESS.md` was changed. The only shared
Host bootstrap edit is the Interaction router constructor call in `main.py`.

## 4. Automated verification

Final results:

- `cd vr-sign-host/backend; python -m pytest -q`
  - Exit 0: **85 passed in 2.85s**.
  - Includes strict heartbeat API bodies/int64 bounds, five-second inclusive
    TTL and post-boundary expiry, wall-clock rollback immunity, stale generation
    and sequence rejection without timestamp refresh, fresh Quest conflict,
    expired Quest takeover, Host-restart staleness, wrong/missing Quest header,
    wrong Participant and zero-persistence rejection, frozen-manifest rejection
    of `quest_device_id`, concurrent Participant heartbeat versus admission,
    HTTP-only `start_udp=False` readiness, Recorder DeviceRegistry isolation,
    and all existing Recorder API/config/service/UDP regressions.
- `cd vr-sign-host/frontend; pnpm test`
  - Exit 0: **1 test file passed, 21 tests passed**.
  - Covers canonical heartbeat construction, invalid Participant fail-closed
    payloads, exact Host-echo gating, old-READY/pending/failure races,
    same-time-origin remount generations, and pagehide keepalive payload/options
    with idempotent cleanup, in addition to existing capture/polling and
    webcam-recovery behavior.
- `cd vr-sign-host/frontend; pnpm lint`
  - Exit 0: no warnings or errors.
- `cd vr-sign-host/frontend; pnpm build`
  - Exit 0: TypeScript and Vite production build succeeded; **1812 modules
    transformed**. Generated sizes were HTML 0.48 kB (gzip 0.33 kB), CSS
    22.67 kB (gzip 5.19 kB), and JS 244.08 kB (gzip 76.97 kB).

Ignored local outputs are `frontend/node_modules/`, `frontend/dist/`, Python
`__pycache__/`, and `.pytest_cache/`; they are not source changes or deployment
inputs checked in by this task.

## 5. W6 Unity client seam still required

W8a intentionally does not wire Unity. W6 must implement the Quest side as
follows:

1. Send the canonical Quest PUT to the Host's TCP 8011 endpoint at an interval
   comfortably below five seconds (the Host page uses two seconds).
2. Start `heartbeat_sequence` at 1 and increment it for every request. Use a
   nonnegative int64 generation that increases when a new Quest heartbeat
   session supersedes an older session. Client generation/sequence only orders
   messages; it must never be used to calculate freshness.
3. Treat `accepted: false` as a non-refreshing stale request. Treat a `409`
   different-owner response as a real ownership conflict; do not silently
   overwrite the fresh Quest.
4. Use the same stable `quest_device_id` in the heartbeat and
   `X-SignVR-Quest-Id` when posting the Run Plan. Do not add it to the frozen V1
   manifest.
5. Continue to author and freeze the Run Plan on Quest. On a `409` readiness or
   identity rejection, retain and retry the identical plan after readiness is
   restored; do not rerandomize it.
6. Do not require Recorder UDP 5011/5012 announce, select, or pair for the
   Interaction application. The existing UDP path remains Recorder-only.

## 6. Remaining risks and integration checks

- Run one physical Quest + webcam cycle to verify the two-second heartbeat
  cadence, camera permission/unplug behavior, header identity, synchronized
  start, terminal upload, and ACK on the station network.
- `X-SignVR-Quest-Id` is an identity binding inside the existing trusted LAN,
  not cryptographic authentication.
- Readiness serialization is process-local, matching the current single Host
  process. A future multi-worker Host would require an inter-process lock or
  transactional state store.
- A Host restart intentionally blocks new Runs until both Quest and Host page
  have re-established fresh HTTP presence.
