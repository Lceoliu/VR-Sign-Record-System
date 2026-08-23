from __future__ import annotations

import io

from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app


def build_app(tmp_path, *, start_udp: bool = False):
    settings = Settings(
        data_root=tmp_path,
        station_id="station-test",
        http_port=8000,
        udp_port=5005,
        quest_control_port=5006,
    )
    return create_app(settings=settings, start_udp=start_udp)


def stub_udp_lifecycle(app) -> None:
    async def noop() -> None:
        return None

    app.state.udp.start = noop
    app.state.udp.scan = noop
    app.state.udp.stop = noop


def register_and_select(client: TestClient, app) -> tuple[str, str]:
    device_id = "quest-test"
    with client.websocket_connect("/ws/events") as websocket:
        websocket.receive_json()
        import anyio

        anyio.run(
            app.state.registry.upsert_announcement,
            {
                "device_id": device_id,
                "name": "Quest 3 Test",
                "control_port": 5006,
                "capabilities": ["pose", "preview", "take_upload"],
            },
            "127.0.0.1",
        )
    response = client.post(f"/api/devices/{device_id}/select")
    assert response.status_code == 200
    record = app.state.registry._devices[device_id]
    return device_id, record.session_token


def create_round(
    client: TestClient,
    batch_id: str = "测试批次",
    round_id: str = "round_001",
) -> dict:
    response = client.post(
        f"/api/recording/batches/{batch_id}/rounds",
        json={"round_id": round_id},
    )
    assert response.status_code == 200
    return response.json()


def test_health_and_fixed_sentence_catalog(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        assert client.get("/api/health").json()["status"] == "ok"
        state = client.get("/api/state").json()
        assert state["station_id"] == "station-test"

    assert state["recording_status"] == "ready"
    assert state["countdown_seconds"] == 2.0
    assert state["batch_id"] is None
    assert len(state["sentences"]) == 31
    assert [
        sum(sentence["viewpoint_id"] == f"state_{viewpoint:02d}" for sentence in state["sentences"])
        for viewpoint in range(1, 7)
    ] == [3, 9, 3, 3, 7, 6]
    assert state["sentences"][18]["highlight_target_ids"] == ["industrial_button"]
    assert state["sentences"][24]["highlight_target_ids"] == [
        "industrial_button",
        "red_button",
        "alarm_button",
    ]
    assert state["sentences"][25]["sequence_numbers"] == [1, 2, 3]
    assert state["sentences"][30]["sequence_numbers"] == [3, 2, 1]


def test_recording_commands_require_selected_device(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        create_round(client)
        response = client.post(
            "/api/recording/start",
            json={"batch_id": "测试批次", "round_id": "round_001"},
        )
        assert response.status_code == 409


def test_nested_round_recording_upload_and_completion(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        device_id, token = register_and_select(client, app)
        created = create_round(client)
        assert created["station_id"] == "station-test"
        session_id = created["session_id"]
        assert session_id.startswith("session_")
        assert session_id != "测试批次"

        batches = client.get("/api/recording/batches")
        assert batches.status_code == 200
        assert batches.json()["batches"] == ["测试批次"]

        start = client.post(
            "/api/recording/start",
            json={"batch_id": "测试批次", "round_id": "round_001"},
        )
        assert start.status_code == 200
        state = start.json()["state"]
        assert state["recording_status"] == "countdown"
        take_id = state["current_take"]["take_id"]
        assert take_id == "take_001"

        stop = client.post("/api/recording/stop")
        assert stop.status_code == 200
        assert stop.json()["state"]["current_sentence_index"] == 0

        jpeg = b"\xff\xd8fake-jpeg\xff\xd9"
        preview = client.post(
            f"/api/devices/{device_id}/preview-frame",
            headers={"x-signvr-token": token, "content-type": "image/jpeg"},
            content=jpeg,
        )
        assert preview.status_code == 202
        assert client.get(f"/api/devices/{device_id}/preview.jpg").content == jpeg

        upload = client.post(
            f"/api/devices/{device_id}/takes/upload",
            headers={"x-signvr-token": token},
            data={
                "session_id": session_id,
                "sentence_id": "sentence_001",
                "take_id": take_id,
            },
            files={
                "pose_file": (
                    "pose.jsonl",
                    io.BytesIO(b'{"frame":1}\n'),
                    "application/x-ndjson",
                ),
                "meta_file": ("meta.json", io.BytesIO(b"{}"), "application/json"),
            },
        )
        assert upload.status_code == 200

        camera = client.post(
            f"/api/takes/{take_id}/camera-upload",
            data={
                "session_id": session_id,
                "sentence_id": "sentence_001",
            },
            files={
                "video_file": (
                    "camera.webm",
                    io.BytesIO(b"webm"),
                    "video/webm",
                )
            },
        )
        assert camera.status_code == 200

        take_directory = (
            tmp_path
            / "recordings"
            / "测试批次"
            / "round_001"
            / "sentence_001"
            / take_id
        )
        pose_path = take_directory / f"{take_id}.pose.jsonl"
        assert pose_path.read_bytes() == b'{"frame":1}\n'
        assert (take_directory / f"{take_id}.meta.json").is_file()
        assert (take_directory / f"{take_id}.camera.webm").is_file()

        current = client.get("/api/state").json()
        # The stop above cancelled the host countdown, so a late upload does
        # not make the cancelled command advance the active sentence.
        assert current["current_sentence_index"] == 0
        assert current["sentences"][0]["completed"] is True
        assert current["sentences"][0]["take_count"] == 1

        rounds = client.get("/api/recording/batches/测试批次/rounds").json()
        assert rounds["rounds"][0]["completed_sentences"] == 1
        assert rounds["suggested_round_id"] == "round_002"

        duplicate = client.post(
            f"/api/devices/{device_id}/takes/upload",
            headers={"x-signvr-token": token},
            data={
                "session_id": session_id,
                "sentence_id": "sentence_001",
                "take_id": take_id,
            },
            files={
                "pose_file": (
                    "pose.jsonl",
                    io.BytesIO(b"replacement\n"),
                    "application/x-ndjson",
                ),
                "meta_file": (
                    "meta.json",
                    io.BytesIO(b'{"replacement":true}'),
                    "application/json",
                ),
            },
        )
        assert duplicate.status_code == 409
        assert pose_path.read_bytes() == b'{"frame":1}\n'


def test_round_switching_preserves_independent_cursor(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        create_round(client, round_id="round_001")
        jump = client.put(
            "/api/recording/current-sentence",
            json={"sentence_index": 20},
        )
        assert jump.status_code == 200
        assert jump.json()["current_sentence_index"] == 20

        second = create_round(client, round_id="round_002")
        assert second["current_sentence_index"] == 0

        first_again = client.post(
            "/api/recording/batches/测试批次/rounds/round_001/select"
        )
        assert first_again.status_code == 200
        assert first_again.json()["current_sentence_index"] == 20

        duplicate = client.post(
            "/api/recording/batches/测试批次/rounds",
            json={"round_id": "round_001"},
        )
        assert duplicate.status_code == 409

    restarted = build_app(tmp_path)
    with TestClient(restarted) as client:
        initial = client.get("/api/state").json()
        assert initial["round_id"] is None
        restored = client.post(
            "/api/recording/batches/测试批次/rounds/round_001/select"
        )
        assert restored.status_code == 200
        assert restored.json()["current_sentence_index"] == 20
        rounds = client.get("/api/recording/batches/测试批次/rounds").json()
        assert [item["round_id"] for item in rounds["rounds"]] == [
            "round_001",
            "round_002",
        ]


def test_pairing_syncs_the_active_round_sentence_to_quest(tmp_path):
    app = build_app(tmp_path, start_udp=True)
    stub_udp_lifecycle(app)
    sent_packets: list[dict] = []

    async def acknowledge(
        device_id: str,
        packet: dict,
        timeout_seconds: float = 2.5,
    ) -> dict:
        del device_id, timeout_seconds
        sent_packets.append(packet)
        return {"accepted": True, "command_id": packet["command_id"]}

    app.state.udp.send_to_device_and_wait = acknowledge

    with TestClient(app) as client:
        create_round(client)
        selected = client.put(
            "/api/recording/current-sentence",
            json={"sentence_index": 20},
        )
        assert selected.status_code == 200
        register_and_select(client, app)

    assert [packet.get("action") or packet["type"] for packet in sent_packets] == [
        "pair",
        "select_sentence",
    ]
    assert sent_packets[-1]["sentence_id"] == "sentence_021"
    assert sent_packets[-1]["sentence_index"] == 20
    assert sent_packets[-1]["viewpoint_id"] == "state_05"


def test_failed_sentence_sync_preserves_previous_sentence_retake(tmp_path):
    app = build_app(tmp_path, start_udp=True)
    stub_udp_lifecycle(app)

    async def reject_sentence_three(
        device_id: str,
        packet: dict,
        timeout_seconds: float = 2.5,
    ) -> dict:
        del device_id, timeout_seconds
        if packet.get("action") == "select_sentence" and packet.get("sentence_index") == 2:
            raise TimeoutError
        return {"accepted": True, "command_id": packet["command_id"]}

    app.state.udp.send_to_device_and_wait = reject_sentence_three

    with TestClient(app) as client:
        register_and_select(client, app)
        create_round(client)
        started = client.post(
            "/api/recording/start",
            json={"batch_id": "测试批次", "round_id": "round_001"},
        )
        assert started.status_code == 200

        import anyio

        anyio.run(
            app.state.recordings.mark_recording_started,
            started.json()["command_id"],
        )
        stopped = client.post("/api/recording/stop")
        assert stopped.status_code == 200
        assert stopped.json()["state"]["current_sentence_index"] == 1

        failed_jump = client.put(
            "/api/recording/current-sentence",
            json={"sentence_index": 2},
        )
        assert failed_jump.status_code == 504
        assert client.get("/api/state").json()["current_sentence_index"] == 1
        rounds = client.get(
            "/api/recording/batches/测试批次/rounds"
        ).json()["rounds"]
        assert rounds[0]["current_sentence_index"] == 1

        retake = client.post("/api/recording/reset")
        assert retake.status_code == 200
        assert retake.json()["state"]["current_sentence_index"] == 0
        assert retake.json()["action"] == "reset_take"


def test_stop_syncs_advanced_sentence_and_sync_failure_keeps_take_state(tmp_path):
    app = build_app(tmp_path, start_udp=True)
    stub_udp_lifecycle(app)
    sent_actions: list[str] = []

    async def acknowledge_except_sentence_sync(
        device_id: str,
        packet: dict,
        timeout_seconds: float = 2.5,
    ) -> dict:
        del device_id, timeout_seconds
        action = str(packet.get("action") or packet.get("type"))
        sent_actions.append(action)
        if action == "select_sentence":
            raise TimeoutError
        return {"accepted": True, "command_id": packet["command_id"]}

    app.state.udp.send_to_device_and_wait = acknowledge_except_sentence_sync

    with TestClient(app) as client:
        register_and_select(client, app)
        create_round(client)
        assert sent_actions[-1] == "select_sentence"
        started = client.post(
            "/api/recording/start",
            json={"batch_id": "测试批次", "round_id": "round_001"},
        )
        assert started.status_code == 200

        import anyio

        anyio.run(
            app.state.recordings.mark_recording_started,
            started.json()["command_id"],
        )
        stopped = client.post("/api/recording/stop")

        assert stopped.status_code == 200
        assert stopped.json()["state"]["current_sentence_index"] == 1
        assert sent_actions[-2:] == ["stop_take", "select_sentence"]
        assert client.get("/api/state").json()["current_sentence_index"] == 1

        reset = client.post("/api/recording/reset")
        assert reset.status_code == 200
        assert reset.json()["state"]["current_sentence_index"] == 0
        assert sent_actions[-1] == "reset_take"


def test_device_reported_by_another_station_cannot_be_selected(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        import anyio

        anyio.run(
            app.state.registry.upsert_announcement,
            {
                "device_id": "quest-other",
                "control_port": 5006,
                "paired_station_id": "station-other",
            },
            "127.0.0.1",
        )

        response = client.post("/api/devices/quest-other/select")
        assert response.status_code == 409
        assert "station-other" in response.json()["detail"]
