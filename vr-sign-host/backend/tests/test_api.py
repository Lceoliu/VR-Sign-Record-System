from __future__ import annotations

import io
import json

from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app


def build_app(tmp_path):
    settings = Settings(
        data_root=tmp_path,
        station_id="station-test",
        http_port=8000,
        udp_port=5005,
        quest_control_port=5006,
    )
    return create_app(settings=settings, start_udp=False)


def register_and_select(client: TestClient, app) -> str:
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
    return device_id


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


def test_health_and_extended_sentence_catalog(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        assert client.get("/api/health").json()["status"] == "ok"
        state = client.get("/api/state").json()
        assert state["station_id"] == "station-test"

    assert state["recording_status"] == "ready"
    assert state["countdown_seconds"] == 2.0
    assert state["batch_id"] is None
    assert len(state["sentences"]) == 339
    assert len({sentence["text"] for sentence in state["sentences"]}) == 339
    assert state["sentences"][0]["text"] == "你叫什么名字？"
    assert state["sentences"][99]["category"] == "social"
    assert state["sentences"][100]["text"] == "你先打开门，我去找钥匙。"
    assert state["sentences"][299]["category"] == "stress"
    assert state["sentences"][300]["text"] == "今天星期几？"
    assert state["sentences"][338]["text"] == "我已经上线了，你什么时候加入游戏？"
    assert not any(sentence["text"].startswith("大纲") for sentence in state["sentences"])


def test_sentence_reorder_and_delete_endpoints(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        state = create_round(client)
        sentence_ids = [sentence["sentence_id"] for sentence in state["sentences"]]
        moved = [*sentence_ids[1:], sentence_ids[0]]

        response = client.put(
            "/api/recording/sentences/order",
            json={"sentence_ids": moved},
        )
        assert response.status_code == 200
        assert response.json()["sentences"][-1]["sentence_id"] == "sentence_001"
        assert response.json()["current_sentence_index"] == 338

        response = client.delete("/api/recording/sentences/sentence_002")
        assert response.status_code == 200
        assert len(response.json()["sentences"]) == 338
        assert not any(
            sentence["sentence_id"] == "sentence_002"
            for sentence in response.json()["sentences"]
        )


def test_review_page_lists_media_and_persists_only_issue_labels(tmp_path):
    take_directory = (
        tmp_path
        / "0819"
        / "lin"
        / "round_001"
        / "sentence_001"
        / "take_001"
    )
    take_directory.mkdir(parents=True)
    meta = {
        "sentence_id": "sentence_001",
        "sentence_text": "你叫什么名字？",
        "take_id": "take_001",
        "capture_status": "completed",
        "utc_started": "2026-08-19T05:25:15Z",
        "utc_stopped": "2026-08-19T05:25:21Z",
        "pose_frame_count": 1,
        "hand_capture_quality": {"clean_ratio": 1.0},
    }
    (take_directory / "take_001.meta.json").write_text(
        json.dumps(meta, ensure_ascii=False),
        encoding="utf-8",
    )
    (take_directory / "take_001.pose.jsonl").write_text(
        '{"recording_time":0,"pose_valid":true,"joint_count":1,"positions":[{"x":0,"y":0,"z":0}]}\n',
        encoding="utf-8",
    )
    (take_directory / "take_001.camera.webm").write_bytes(b"webm-review")

    app = build_app(tmp_path)
    with TestClient(app) as client:
        assert client.get("/api/review/datasets").json()["datasets"] == ["0819"]
        response = client.get("/api/review/items", params={"dataset": "0819"})
        assert response.status_code == 200
        item = response.json()["items"][0]
        assert item["sentence_text"] == "你叫什么名字？"
        assert item["video_issue"] is False
        assert client.get(f"/api/review/files/{item['video_file']}").content == b"webm-review"

        marked = client.put(
            "/api/review/labels",
            json={
                "dataset": "0819",
                "item_id": item["id"],
                "video_issue": True,
                "sentence_issue": False,
            },
        )
        assert marked.status_code == 200
        label_path = tmp_path / "0819" / "review_labels.json"
        assert json.loads(label_path.read_text(encoding="utf-8"))["items"][item["id"]][
            "video_issue"
        ] is True

        cleared = client.put(
            "/api/review/labels",
            json={
                "dataset": "0819",
                "item_id": item["id"],
                "video_issue": False,
                "sentence_issue": False,
            },
        )
        assert cleared.status_code == 200
        assert json.loads(label_path.read_text(encoding="utf-8"))["items"] == {}


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
        device_id = register_and_select(client, app)
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
        assert stop.json()["state"]["current_sentence_index"] == 1

        jpeg = b"\xff\xd8fake-jpeg\xff\xd9"
        preview = client.post(
            f"/api/devices/{device_id}/preview-frame",
            headers={"content-type": "image/jpeg"},
            content=jpeg,
        )
        assert preview.status_code == 202
        assert client.get(f"/api/devices/{device_id}/preview.jpg").content == jpeg

        upload = client.post(
            f"/api/devices/{device_id}/takes/upload",
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
                "started_at_unix_ms": "1000",
                "first_chunk_at_unix_ms": "1100",
                "stopped_at_unix_ms": "4500",
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
        camera_meta = json.loads(
            (take_directory / f"{take_id}.camera.meta.json").read_text(encoding="utf-8")
        )
        assert camera_meta["duration_ms"] == 3500
        assert camera_meta["first_chunk_at_unix_ms"] == 1100

        retry = client.post(
            f"/api/takes/{take_id}/camera-upload",
            data={
                "session_id": session_id,
                "sentence_id": "sentence_001",
                "started_at_unix_ms": "1000",
                "first_chunk_at_unix_ms": "1100",
                "stopped_at_unix_ms": "4500",
            },
            files={"video_file": ("camera.webm", io.BytesIO(b"retry"), "video/webm")},
        )
        assert retry.status_code == 200
        assert (take_directory / f"{take_id}.camera.webm").read_bytes() == b"webm"

        current = client.get("/api/state").json()
        assert current["current_sentence_index"] == 1
        assert current["sentences"][0]["completed"] is True
        assert current["sentences"][0]["take_count"] == 1

        rounds = client.get("/api/recording/batches/测试批次/rounds").json()
        assert rounds["rounds"][0]["completed_sentences"] == 1
        assert rounds["suggested_round_id"] == "round_003"
        assert [item["signing_mode"] for item in rounds["rounds"]] == ["rough", "precise"]

        duplicate = client.post(
            f"/api/devices/{device_id}/takes/upload",
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
            json={"sentence_index": 50},
        )
        assert jump.status_code == 200
        assert jump.json()["current_sentence_index"] == 50

        second_response = client.post(
            "/api/recording/batches/测试批次/rounds/round_002/select"
        )
        assert second_response.status_code == 200
        second = second_response.json()
        assert second["current_sentence_index"] == 0

        first_again = client.post(
            "/api/recording/batches/测试批次/rounds/round_001/select"
        )
        assert first_again.status_code == 200
        assert first_again.json()["current_sentence_index"] == 50

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
        assert restored.json()["current_sentence_index"] == 50
        rounds = client.get("/api/recording/batches/测试批次/rounds").json()
        assert [item["round_id"] for item in rounds["rounds"]] == [
            "round_001",
            "round_002",
        ]


def test_device_can_be_rebound_by_the_active_trusted_lan_host(tmp_path):
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
        assert response.status_code == 200
        assert response.json()["selected"]["device_id"] == "quest-other"
