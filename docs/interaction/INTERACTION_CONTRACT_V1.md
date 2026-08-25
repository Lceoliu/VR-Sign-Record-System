# Interaction Contract V1

Status: frozen for the 2026-08-26 internal pilot. Additive implementation detail is allowed; incompatible changes require an Orchestrator decision and an explicit schema-version change.

## Canonical serialized values

### Assistance condition

- `TextAndPointing`
- `TextOnly`
- `SignOnly`

`PointingOnly` is invalid. One value is assigned per Run and applies to all six phases. A Start consumes one slot from a session-local shuffled block containing each value once; abort does not return the slot, and application restart creates a new block.

### Run and phase states

```text
RunState: PreStart, AwaitingHost, Scheduled, Running, Completing, Completed, Aborting, Aborted, Faulted
PhaseState: Inactive, FirstPlayback, Active, ReplayPlayback, Completed, Stuck
PhaseResult: Completed, Stuck
```

### Phase ranges

| Phase | Inclusive sentence IDs |
| --- | --- |
| 1 | `001–003` |
| 2 | `004–012` |
| 3 | `013–015` |
| 4 | `016–018` |
| 5 | `019–025` |
| 6 | `026–031` |

Every Run samples one sentence independently and uniformly from each row. Pilot signer is `wang` for all phases. The exact resolved Take is the latest completed Take for the sentence; completion timestamp wins, then Take index is the stable tie-breaker.

## Run Plan rules

- The Quest creates the globally unique Run ID and the complete plan when Start is accepted locally.
- The plan contains the resolved values, not only a seed: condition, six sentence IDs, per-phase signer/Take/artifact, task targets, safe password, and chest-button order.
- The safe password is four unique digits from 0–9.
- The chest password is an independent permutation of the four physical button IDs.
- The plan is immutable after creation. Error, replay, phase reset, Stuck, upload retry, and abort cannot rerandomize it.
- Development builds may use an explicit debug override. A Study start must reject any active override.

Minimum JSON shape:

```json
{
  "schema_version": 1,
  "batch_id": "pilot-20260826",
  "participant_id": "P001",
  "run_id": "run_20260826T101530Z_<unique>",
  "app_session_id": "app_<unique>",
  "created_utc": "2026-08-26T10:15:30Z",
  "app_version": "<version>",
  "git_commit": "<commit>",
  "seed": 123456789,
  "assistance_condition": "TextAndPointing",
  "condition_assignment": {
    "block_index": 0,
    "slot_index": 0
  },
  "safe_password": [7, 1, 4, 9],
  "chest_button_order": ["blue", "red", "yellow", "green"],
  "phases": [
    {
      "phase_id": 1,
      "sentence_id": "001",
      "signer_id": "wang",
      "take_id": "<take>",
      "artifact_path": "wang/sentence_001/<pose-file>",
      "artifact_sha256": "<sha256>",
      "task_variant": { "target_id": "box_a" }
    }
  ]
}
```

The actual manifest contains exactly six ordered phase entries.

## Instruction content manifest

The build-time staging tool emits `instruction-content-manifest.json` with:

```json
{
  "schema_version": 1,
  "generated_utc": "<utc>",
  "signer_id": "wang",
  "entries": [
    {
      "phase_id": 1,
      "sentence_id": "001",
      "signer_id": "wang",
      "take_id": "<take>",
      "completed_utc": "<utc>",
      "take_index": 1,
      "pose_path": "wang/sentence_001/<pose-file>",
      "metadata_path": "wang/sentence_001/<metadata-file>",
      "pose_bytes": 0,
      "pose_sha256": "<sha256>"
    }
  ]
}
```

Pilot validation requires one entry for every sentence `001` through `031`. Generated pose artifacts are local build inputs and must not be silently substituted with `test_game` data.

## Phase behavior

- Current-phase interactions are enabled throughout the phase, including playback.
- First playback start is the timer origin.
- After first playback completes, Replay becomes available and may be used at most once.
- In text-bearing conditions, the existing Chinese bubble appears one second after first playback completes and stays until phase exit.
- Pointing display is derived from the ghost fingertip ray against current legal targets. A hit shows ray and highlight immediately; there is no entry dwell. Loss of hit hides them after an approximately 150 ms grace period.
- A phase timeout at 180 seconds logs an event but does not auto-advance.
- `GiveUpPhase` is available after the allowed replay completes and records `Stuck` before advancing.
- `AbortRun` is a separate always-available safety action and preserves partial data.
- Wrong safe submission, wrong chest-button input, and wrong breaker order clear all progress for that task without changing the Run Plan.

## Event contract

Each JSONL event contains:

```text
schema_version, run_id, phase_id, event_seq,
monotonic_time_s, utc_time, frame,
event_type, actor_id, target_id, payload
```

Required event names:

```text
run_created, host_ready, run_started, run_completed, run_aborted,
phase_entered, phase_completed, phase_stuck, phase_timeout,
instruction_play_started, instruction_play_completed,
replay_available, replay_used,
bubble_shown, bubble_hidden,
pointing_hit_started, pointing_hit_ended,
interaction_attempt, interaction_error, task_progress_reset,
capture_gap, upload_started, upload_acknowledged
```

## Host API and ownership

Quest owns the plan. Host validates IDs/schema, reports readiness, schedules synchronized start, records webcam, accepts artifacts, and acknowledges durable storage.

Minimum endpoints:

```text
GET  /api/interaction/readiness
POST /api/interaction/runs
POST /api/interaction/runs/{run_id}/complete
POST /api/interaction/runs/{run_id}/abort
PUT  /api/interaction/runs/{run_id}/artifacts/{artifact_type}
GET  /api/interaction/runs/{run_id}/ack
```

`POST /api/interaction/runs` accepts the complete Run Plan and returns at least:

```json
{
  "accepted": true,
  "run_id": "<same-id>",
  "start_at_utc": "<short-future-utc>",
  "missing_artifacts": ["events", "poses", "objects", "summary", "webcam"]
}
```

Host stores each Run under:

```text
data/interaction-tests/{batch_id}/{participant_id}/{run_id}/
```

Required final filenames are `run.manifest.json`, `events.jsonl`, `poses.jsonl`, `objects.jsonl`, `summary.json`, and `webcam.webm`. Existing Recording Take routes and `data/recordings` semantics must remain unchanged.
