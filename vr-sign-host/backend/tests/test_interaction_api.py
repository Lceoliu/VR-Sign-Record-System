from __future__ import annotations

import json
from copy import deepcopy

import anyio
import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app


PHASE_SENTENCES = ["001", "004", "013", "016", "019", "026"]


def build_app(tmp_path):
    return create_app(
        settings=Settings(
            data_root=tmp_path,
            station_id="station-interaction-test",
            http_port=8011,
            udp_port=5005,
            quest_control_port=5006,
        ),
        start_udp=False,
    )


def run_plan(run_id: str = "run_20260826T101530Z_alpha") -> dict:
    return {
        "schema_version": 1,
        "batch_id": "pilot-20260826",
        "participant_id": "P001",
        "run_id": run_id,
        "app_session_id": "app_alpha",
        "created_utc": "2026-08-25T10:15:30Z",
        "app_version": "interaction-pilot-1",
        "git_commit": "35fcc93",
        "seed": 123456789,
        "assistance_condition": "TextAndPointing",
        "condition_assignment": {"block_index": 0, "slot_index": 0},
        "safe_password": [7, 1, 4, 9],
        "chest_button_order": ["blue", "red", "yellow", "green"],
        "phases": [
            {
                "phase_id": phase_id,
                "sentence_id": sentence_id,
                "signer_id": "wang",
                "take_id": f"take_{phase_id:03d}",
                "artifact_path": (
                    f"wang/sentence_{sentence_id}/take_{phase_id:03d}.pose.jsonl"
                ),
                "artifact_sha256": f"{phase_id:x}" * 64,
                "task_variant": {"target_id": f"target_{phase_id}"},
            }
            for phase_id, sentence_id in enumerate(PHASE_SENTENCES, start=1)
        ],
    }


def post_plan(client: TestClient, plan: dict, *, raw: bytes | None = None):
    return client.post(
        "/api/interaction/runs",
        content=raw if raw is not None else json.dumps(plan).encode("utf-8"),
        headers={"content-type": "application/json"},
    )


def announce_paired_quest(app, device_id: str = "quest-interaction") -> None:
    device = anyio.run(
        app.state.registry.upsert_announcement,
        {
            "device_id": device_id,
            "control_port": 5006,
            "capabilities": ["interaction_capture"],
            "paired": True,
            "paired_station_id": "station-interaction-test",
        },
        "127.0.0.1",
    )
    assert device is not None
    assert device.selected is True
    assert device.paired is True


def report_camera_ready(client: TestClient) -> None:
    response = client.put(
        "/api/interaction/readiness/camera",
        json={"schema_version": 1, "ready": True},
    )
    assert response.status_code == 200


def make_interaction_ready(client: TestClient, app) -> None:
    announce_paired_quest(app)
    report_camera_ready(client)


def run_directory(tmp_path, plan: dict):
    return (
        tmp_path
        / "interaction-tests"
        / plan["batch_id"]
        / plan["participant_id"]
        / plan["run_id"]
    )


def completion_body(run_id: str) -> dict:
    return {"schema_version": 1, "run_id": run_id}


def abort_body(run_id: str, reason: str = "operator safety stop") -> dict:
    return {
        "schema_version": 1,
        "run_id": run_id,
        "abort_reason": reason,
    }


def event_bytes(run_id: str) -> bytes:
    event = {
        "schema_version": 1,
        "run_id": run_id,
        "phase_id": None,
        "event_seq": 0,
        "monotonic_time_s": 0.0,
        "utc_time": "2026-08-25T10:15:30Z",
        "frame": 0,
        "event_type": "run_created",
        "actor_id": "quest",
        "target_id": None,
        "payload": {},
    }
    return (json.dumps(event, separators=(",", ":")) + "\n").encode("utf-8")


def capture_row_bytes(run_id: str, record_type: str) -> bytes:
    return (
        json.dumps(
            {
                "schema_version": 1,
                "run_id": run_id,
                "record_type": record_type,
            },
            separators=(",", ":"),
        )
        + "\n"
    ).encode("utf-8")


def summary_bytes(run_id: str) -> bytes:
    return json.dumps(
        {"schema_version": 1, "run_id": run_id, "phase_results": []},
        separators=(",", ":"),
    ).encode("utf-8")


@pytest.mark.parametrize("missing", ["storage", "quest", "camera"])
def test_run_registration_rejects_each_missing_readiness_without_persisting_plan(
    tmp_path,
    missing,
):
    if missing == "storage":
        corrupt = (
            tmp_path
            / "interaction-tests"
            / "pilot-corrupt"
            / "P999"
            / "run_corrupt"
        )
        corrupt.mkdir(parents=True)
        (corrupt / "run.manifest.json").write_text("{not-json", encoding="utf-8")

    app = build_app(tmp_path)
    plan = run_plan(f"run_missing_{missing}")
    with TestClient(app) as client:
        if missing != "quest":
            announce_paired_quest(app)
        if missing != "camera":
            report_camera_ready(client)

        readiness = client.get("/api/interaction/readiness")
        rejected = post_plan(client, plan)

    assert readiness.status_code == 200
    assert readiness.json()[f"{missing}_ready"] is False
    assert rejected.status_code == 409
    assert not run_directory(tmp_path, plan).exists()


def test_identical_quest_plan_can_be_retried_after_camera_becomes_ready(tmp_path):
    app = build_app(tmp_path)
    plan = run_plan("run_retry_after_camera_ready")
    raw = (json.dumps(plan, ensure_ascii=False, indent=4) + "\n").encode("utf-8")

    with TestClient(app) as client:
        announce_paired_quest(app)
        rejected = post_plan(client, plan, raw=raw)
        assert rejected.status_code == 409
        assert not run_directory(tmp_path, plan).exists()

        report_camera_ready(client)
        accepted = post_plan(client, plan, raw=raw)

    assert accepted.status_code == 200
    assert accepted.json()["run_id"] == plan["run_id"]
    assert (run_directory(tmp_path, plan) / "run.manifest.json").read_bytes() == raw


def test_selected_but_unpaired_quest_cannot_register_a_run(tmp_path):
    app = build_app(tmp_path)
    plan = run_plan("run_unpaired_quest")
    with TestClient(app) as client:
        device = anyio.run(
            app.state.registry.upsert_announcement,
            {
                "device_id": "quest-unpaired",
                "control_port": 5006,
                "capabilities": ["interaction_capture"],
            },
            "127.0.0.1",
        )
        assert device is not None
        selected, _token = anyio.run(app.state.registry.select, "quest-unpaired")
        assert selected.selected is True
        assert selected.paired is False
        report_camera_ready(client)

        readiness = client.get("/api/interaction/readiness")
        rejected = post_plan(client, plan)

    assert readiness.json()["quest_ready"] is False
    assert rejected.status_code == 409
    assert not run_directory(tmp_path, plan).exists()


def test_readiness_reports_backend_storage_and_selected_quest(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        initial = client.get("/api/interaction/readiness")
        assert initial.status_code == 200
        assert initial.json() == {
            "schema_version": 1,
            "backend_ready": True,
            "storage_ready": True,
            "storage_error": None,
            "quest_ready": False,
            "camera_ready": False,
            "ready": False,
            "quest_device_id": None,
            "camera_last_seen_utc": None,
            "server_utc": initial.json()["server_utc"],
            "interaction_root": str(tmp_path / "interaction-tests"),
            "active_run": None,
        }

        anyio.run(
            app.state.registry.upsert_announcement,
            {
                "device_id": "quest-interaction",
                "control_port": 5006,
                "capabilities": ["interaction_capture"],
            },
            "127.0.0.1",
        )
        selected = client.post("/api/devices/quest-interaction/select")
        assert selected.status_code == 200

        quest_only = client.get("/api/interaction/readiness").json()
        assert quest_only["quest_ready"] is True
        assert quest_only["camera_ready"] is False
        assert quest_only["ready"] is False

        invalid_camera = client.put(
            "/api/interaction/readiness/camera",
            json={"schema_version": 1, "ready": 1},
        )
        assert invalid_camera.status_code == 422

        camera = client.put(
            "/api/interaction/readiness/camera",
            json={"schema_version": 1, "ready": True},
        )
        assert camera.status_code == 200
        ready = client.get("/api/interaction/readiness").json()
        assert ready["backend_ready"] is True
        assert ready["storage_ready"] is True
        assert ready["quest_ready"] is True
        assert ready["camera_ready"] is True
        assert ready["ready"] is True
        assert ready["quest_device_id"] == "quest-interaction"
        assert ready["camera_last_seen_utc"] is not None


def test_only_active_run_blocks_registration_and_terminal_history_is_not_active(
    tmp_path,
):
    app = build_app(tmp_path)
    first = run_plan("run_active_first")
    second = run_plan("run_retry_after_completed")
    second_raw = (json.dumps(second, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    third = run_plan("run_after_aborted")

    with TestClient(app) as client:
        make_interaction_ready(client, app)

        assert post_plan(client, first).status_code == 200
        active = client.get("/api/interaction/readiness").json()["active_run"]
        assert active["run_id"] == first["run_id"]
        assert active["state"] in {"Scheduled", "Running"}

        rejected = post_plan(client, second, raw=second_raw)
        assert rejected.status_code == 409
        assert not run_directory(tmp_path, second).exists()

        completed = client.post(
            f"/api/interaction/runs/{first['run_id']}/complete",
            json=completion_body(first["run_id"]),
        )
        assert completed.status_code == 200
        assert client.get("/api/interaction/readiness").json()["active_run"] is None

        accepted_retry = post_plan(client, second, raw=second_raw)
        assert accepted_retry.status_code == 200
        assert (run_directory(tmp_path, second) / "run.manifest.json").read_bytes() == second_raw

        aborted = client.post(
            f"/api/interaction/runs/{second['run_id']}/abort",
            json=abort_body(second["run_id"]),
        )
        assert aborted.status_code == 200
        assert client.get("/api/interaction/readiness").json()["active_run"] is None
        assert post_plan(client, third).status_code == 200


@pytest.mark.parametrize(
    ("terminal_endpoint", "expected_state"),
    [("complete", "Completed"), ("abort", "Aborted")],
)
def test_run_snapshot_remains_pollable_after_it_is_no_longer_active(
    tmp_path,
    terminal_endpoint,
    expected_state,
):
    app = build_app(tmp_path)
    plan = run_plan(f"run_snapshot_{terminal_endpoint}")
    run_id = plan["run_id"]
    body = (
        completion_body(run_id)
        if terminal_endpoint == "complete"
        else abort_body(run_id)
    )

    with TestClient(app) as client:
        make_interaction_ready(client, app)
        assert post_plan(client, plan).status_code == 200
        active_snapshot = client.get(f"/api/interaction/runs/{run_id}")
        assert active_snapshot.status_code == 200
        assert active_snapshot.json()["state"] in {"Scheduled", "Running"}
        terminal = client.post(
            f"/api/interaction/runs/{run_id}/{terminal_endpoint}",
            json=body,
        )
        assert terminal.status_code == 200
        assert client.get("/api/interaction/readiness").json()["active_run"] is None

        polled = client.get(f"/api/interaction/runs/{run_id}")

    assert polled.status_code == 200
    assert polled.json()["run_id"] == run_id
    assert polled.json()["state"] == expected_state
    assert polled.json()["terminal_utc"] is not None


def test_run_plan_is_scheduled_broadcast_and_preserved_byte_for_byte(tmp_path):
    app = build_app(tmp_path)
    plan = run_plan()
    raw = (json.dumps(plan, ensure_ascii=False, indent=3) + "\n").encode("utf-8")

    with TestClient(app) as client:
        make_interaction_ready(client, app)
        with client.websocket_connect("/ws/events") as websocket:
            assert websocket.receive_json()["type"] == "state_changed"
            response = post_plan(client, plan, raw=raw)
            event = websocket.receive_json()

    assert response.status_code == 200
    payload = response.json()
    assert payload["accepted"] is True
    assert payload["run_id"] == plan["run_id"]
    assert payload["state"] == "Scheduled"
    assert payload["missing_artifacts"] == [
        "events",
        "poses",
        "objects",
        "summary",
        "webcam",
    ]
    assert event["type"] == "interaction_run_scheduled"
    assert event["payload"]["run_id"] == plan["run_id"]
    directory = run_directory(tmp_path, plan)
    assert (directory / "run.manifest.json").read_bytes() == raw
    assert not (tmp_path / "recordings" / plan["batch_id"]).exists()


def test_duplicate_global_run_id_never_overwrites_original_manifest(tmp_path):
    app = build_app(tmp_path)
    first = run_plan()
    first_raw = json.dumps(first, indent=2).encode("utf-8")
    duplicate = deepcopy(first)
    duplicate["batch_id"] = "pilot-other-batch"
    duplicate["participant_id"] = "P999"

    with TestClient(app) as client:
        make_interaction_ready(client, app)
        assert post_plan(client, first, raw=first_raw).status_code == 200
        assert client.post(
            f"/api/interaction/runs/{first['run_id']}/complete",
            json=completion_body(first["run_id"]),
        ).status_code == 200
        response = post_plan(client, duplicate)

    assert response.status_code == 409
    assert (run_directory(tmp_path, first) / "run.manifest.json").read_bytes() == first_raw
    assert not run_directory(tmp_path, duplicate).exists()


@pytest.mark.parametrize(
    "mutate",
    [
        lambda plan: plan.update(participant_id="../P001"),
        lambda plan: plan.update(schema_version=True),
        lambda plan: plan.update(assistance_condition="PointingOnly"),
        lambda plan: plan.update(safe_password=[1, 1, 2, 3]),
        lambda plan: plan.update(chest_button_order=["blue", "red", "yellow", "yellow"]),
        lambda plan: plan["phases"].pop(),
        lambda plan: plan["phases"][0].update(sentence_id="004"),
        lambda plan: plan["phases"][1].update(phase_id=3),
        lambda plan: plan["phases"][0].update(signer_id="other"),
        lambda plan: plan["phases"][0].update(artifact_path="../../outside.pose.jsonl"),
        lambda plan: plan.update(unexpected=True),
    ],
    ids=[
        "unsafe-participant",
        "boolean-schema-version",
        "invalid-condition",
        "duplicate-safe-digits",
        "invalid-button-permutation",
        "not-six-phases",
        "sentence-outside-phase",
        "unordered-phases",
        "non-pilot-signer",
        "artifact-path-traversal",
        "extra-schema-field",
    ],
)
def test_invalid_run_plans_are_rejected_without_creating_a_run(tmp_path, mutate):
    app = build_app(tmp_path)
    plan = run_plan()
    mutate(plan)

    with TestClient(app) as client:
        make_interaction_ready(client, app)
        response = post_plan(client, plan)

    assert response.status_code == 422
    manifests = list((tmp_path / "interaction-tests").rglob("run.manifest.json"))
    assert manifests == []


def test_duplicate_json_keys_and_non_json_media_type_are_rejected(tmp_path):
    app = build_app(tmp_path)
    plan = run_plan()
    raw = json.dumps(plan).replace(
        '"schema_version": 1,',
        '"schema_version": 1, "schema_version": 1,',
        1,
    ).encode("utf-8")
    with TestClient(app) as client:
        make_interaction_ready(client, app)
        duplicate_key = post_plan(client, plan, raw=raw)
        wrong_media = client.post(
            "/api/interaction/runs",
            content=json.dumps(plan),
            headers={"content-type": "text/plain"},
        )

    assert duplicate_key.status_code == 400
    assert wrong_media.status_code == 415


def test_complete_and_abort_validate_run_id_and_are_mutually_exclusive(tmp_path):
    app = build_app(tmp_path)
    completed_plan = run_plan("run_completed_alpha")
    aborted_plan = run_plan("run_aborted_alpha")
    with TestClient(app) as client:
        make_interaction_ready(client, app)
        assert post_plan(client, completed_plan).status_code == 200
        mismatch = client.post(
            f"/api/interaction/runs/{completed_plan['run_id']}/complete",
            json=completion_body("run_other"),
        )
        assert mismatch.status_code == 409

        completed = client.post(
            f"/api/interaction/runs/{completed_plan['run_id']}/complete",
            json=completion_body(completed_plan["run_id"]),
        )
        assert completed.status_code == 200
        assert completed.json()["state"] == "Completed"
        assert completed.json()["acknowledged"] is False
        assert client.post(
            f"/api/interaction/runs/{completed_plan['run_id']}/abort",
            json=abort_body(completed_plan["run_id"]),
        ).status_code == 409

        assert post_plan(client, aborted_plan).status_code == 200
        aborted = client.post(
            f"/api/interaction/runs/{aborted_plan['run_id']}/abort",
            json=abort_body(aborted_plan["run_id"]),
        )
        assert aborted.status_code == 200
        assert aborted.json()["state"] == "Aborted"
        assert aborted.json()["abort_reason"] == "operator safety stop"
        repeat = client.post(
            f"/api/interaction/runs/{aborted_plan['run_id']}/abort",
            json=abort_body(aborted_plan["run_id"]),
        )
        assert repeat.status_code == 200

    status = json.loads(
        (run_directory(tmp_path, aborted_plan) / "run.status.json").read_text("utf-8")
    )
    assert status["state"] == "Aborted"
    assert status["abort_reason"] == "operator safety stop"


def test_artifact_upload_is_validated_atomic_and_non_overwriting(tmp_path):
    app = build_app(tmp_path)
    plan = run_plan()
    directory = run_directory(tmp_path, plan)
    invalid_event = event_bytes("run_wrong")
    valid_event = event_bytes(plan["run_id"])
    with TestClient(app) as client:
        make_interaction_ready(client, app)
        assert post_plan(client, plan).status_code == 200
        invalid = client.put(
            f"/api/interaction/runs/{plan['run_id']}/artifacts/events",
            content=invalid_event,
            headers={"content-type": "application/x-ndjson"},
        )
        assert invalid.status_code == 422
        assert not (directory / "events.jsonl").exists()
        assert list(directory.glob(".*.uploading")) == []

        stored = client.put(
            f"/api/interaction/runs/{plan['run_id']}/artifacts/events",
            content=valid_event,
            headers={"content-type": "application/x-ndjson"},
        )
        assert stored.status_code == 200
        duplicate = client.put(
            f"/api/interaction/runs/{plan['run_id']}/artifacts/events",
            content=event_bytes(plan["run_id"]).replace(b"quest", b"other"),
            headers={"content-type": "application/x-ndjson"},
        )
        wrong_type = client.put(
            f"/api/interaction/runs/{plan['run_id']}/artifacts/not-a-contract-type",
            content=b"data",
            headers={"content-type": "application/octet-stream"},
        )

    assert duplicate.status_code == 409
    assert wrong_type.status_code == 400
    assert (directory / "events.jsonl").read_bytes() == valid_event


def test_ack_lists_missing_until_terminal_run_has_all_durable_artifacts(tmp_path):
    app = build_app(tmp_path)
    plan = run_plan()
    run_id = plan["run_id"]
    artifacts = {
        "events": (event_bytes(run_id), "application/x-ndjson"),
        "poses": (capture_row_bytes(run_id, "pose"), "application/x-ndjson"),
        "objects": (capture_row_bytes(run_id, "object"), "application/x-ndjson"),
        "summary": (summary_bytes(run_id), "application/json"),
        "webcam": (b"\x1aE\xdf\xa3webm-payload", "video/webm"),
    }
    with TestClient(app) as client:
        make_interaction_ready(client, app)
        assert post_plan(client, plan).status_code == 200
        completed = client.post(
            f"/api/interaction/runs/{run_id}/complete",
            json=completion_body(run_id),
        )
        assert completed.status_code == 200

        for index, (artifact_type, (content, content_type)) in enumerate(artifacts.items()):
            uploaded = client.put(
                f"/api/interaction/runs/{run_id}/artifacts/{artifact_type}",
                content=content,
                headers={"content-type": content_type},
            )
            assert uploaded.status_code == 200
            expected_missing = list(artifacts)[index + 1 :]
            assert uploaded.json()["missing_artifacts"] == expected_missing
            assert uploaded.json()["acknowledged"] is (not expected_missing)

        ack = client.get(f"/api/interaction/runs/{run_id}/ack")

    assert ack.status_code == 200
    assert ack.json() == {
        "run_id": run_id,
        "state": "Completed",
        "acknowledged": True,
        "missing_artifacts": [],
    }
    directory = run_directory(tmp_path, plan)
    assert sorted(path.name for path in directory.iterdir()) == [
        "events.jsonl",
        "objects.jsonl",
        "poses.jsonl",
        "run.manifest.json",
        "run.status.json",
        "summary.json",
        "webcam.webm",
    ]

    restarted = build_app(tmp_path)
    with TestClient(restarted) as client:
        recovered_ack = client.get(f"/api/interaction/runs/{run_id}/ack")
    assert recovered_ack.status_code == 200
    assert recovered_ack.json()["acknowledged"] is True
    assert recovered_ack.json()["missing_artifacts"] == []


def test_ack_rejects_unsafe_or_unknown_run_id(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        reserved = client.get("/api/interaction/runs/CON/ack")
        unknown = client.get("/api/interaction/runs/run_missing/ack")
        reserved_snapshot = client.get("/api/interaction/runs/CON")
        unknown_snapshot = client.get("/api/interaction/runs/run_missing")

    assert reserved.status_code == 400
    assert unknown.status_code == 404
    assert reserved_snapshot.status_code == 400
    assert unknown_snapshot.status_code == 404


def test_invalid_interaction_storage_does_not_block_recording_api_startup(tmp_path):
    corrupt_directory = (
        tmp_path
        / "interaction-tests"
        / "pilot-corrupt"
        / "P001"
        / "run_corrupt"
    )
    corrupt_directory.mkdir(parents=True)
    (corrupt_directory / "run.manifest.json").write_text("{not-json", encoding="utf-8")

    app = build_app(tmp_path)
    with TestClient(app) as client:
        make_interaction_ready(client, app)
        recording_state = client.get("/api/state")
        interaction_readiness = client.get("/api/interaction/readiness")
        rejected_write = post_plan(client, run_plan())

    assert recording_state.status_code == 200
    assert recording_state.json()["recording_status"] == "ready"
    assert interaction_readiness.status_code == 200
    assert interaction_readiness.json()["storage_ready"] is False
    assert interaction_readiness.json()["storage_error"] == (
        "1 invalid existing Interaction Run(s)"
    )
    assert rejected_write.status_code == 409
