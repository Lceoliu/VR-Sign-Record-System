from __future__ import annotations

import io

from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app


def build_app(tmp_path):
    settings = Settings(data_root=tmp_path, http_port=8000, udp_port=5005, quest_control_port=5006)
    return create_app(settings=settings, start_udp=False)


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
    import anyio

    record = app.state.registry._devices[device_id]
    return device_id, record.session_token


def test_health_and_initial_state(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        assert client.get("/api/health").json()["status"] == "ok"
        state = client.get("/api/state").json()
        assert state["recording_status"] == "ready"
        assert state["countdown_seconds"] == 2.0


def test_recording_commands_require_selected_device(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        response = client.post("/api/recording/start")
        assert response.status_code == 409


def test_preview_and_take_upload(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        device_id, token = register_and_select(client, app)
        start = client.post("/api/recording/start")
        assert start.status_code == 200
        state = start.json()["state"]
        assert state["recording_status"] == "countdown"
        take_id = state["current_take"]["take_id"]

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
            data={"session_id": state["session_id"], "sentence_id": "sentence_001", "take_id": take_id},
            files={
                "pose_file": ("pose.jsonl", io.BytesIO(b"{\"frame\":1}\n"), "application/x-ndjson"),
                "meta_file": ("meta.json", io.BytesIO(b"{}"), "application/json"),
            },
        )
        assert upload.status_code == 200
        assert (tmp_path / "recordings" / state["session_id"] / "sentence_001" / take_id / f"{take_id}.pose.jsonl").exists()


def test_sentence_import(tmp_path):
    app = build_app(tmp_path)
    with TestClient(app) as client:
        response = client.put("/api/sentences", json={"sentences": ["第一句", "第二句"]})
        assert response.status_code == 200
        body = response.json()
        assert len(body["sentences"]) == 2
        assert body["sentences"][0]["status"] == "current"
