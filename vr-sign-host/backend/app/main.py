from __future__ import annotations

import asyncio
import json
import mimetypes
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, File, Form, HTTPException, Request, UploadFile, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import Response
from fastapi.staticfiles import StaticFiles

from .config import Settings
from .device_registry import DeviceRegistry
from .models import (
    DeviceSelectResponse,
    RecordingCommandResponse,
    RoundCreateRequest,
    SentenceSelectRequest,
    StartRecordingRequest,
)
from .protocol import command_id, pair_packet, pedal_packet
from .realtime import RealtimeHub
from .recording_service import RecordingService
from .repository import RecordingRepository, safe_segment
from .udp_service import UdpService


# Windows can inherit a text/plain .js mapping from the registry. Browsers
# reject Vite's ES modules unless they are served with a JavaScript MIME type.
mimetypes.add_type("application/javascript", ".js", strict=True)
mimetypes.add_type("application/javascript", ".mjs", strict=True)


def create_app(*, settings: Settings | None = None, start_udp: bool = True) -> FastAPI:
    config = settings or Settings.from_environment()
    registry = DeviceRegistry(config.station_id, config.allowed_device_ids)
    hub = RealtimeHub()
    repository = RecordingRepository(config.data_root, config.station_id)
    recordings = RecordingService(repository)
    udp = UdpService(config, registry, hub)
    sentence_sync_lock = asyncio.Lock()

    async def select_sentence_and_sync(sentence_index: int):
        async with sentence_sync_lock:
            checkpoint = await recordings.checkpoint()
            state, packet, _ = await recordings.select_sentence_command(
                sentence_index
            )
            device = await registry.selected()
            if device is not None and start_udp:
                try:
                    await _send_and_require_ack(udp, device.device_id, packet)
                except HTTPException:
                    await recordings.restore(checkpoint)
                    raise
        await hub.publish_event(
            {"type": "state_changed", "payload": state.model_dump(mode="json")}
        )
        return state

    async def sync_current_sentence_to_quest(device_id: str | None = None) -> None:
        """Best-effort mirror after pairing or changing the active round."""
        if not start_udp:
            return

        async with sentence_sync_lock:
            state = await recordings.snapshot()
            if state.batch_id is None or state.round_id is None:
                return
            if device_id is None:
                device = await registry.selected()
                if device is None:
                    return
                device_id = device.device_id
            packet, _ = await recordings.current_sentence_command()
            try:
                await _send_and_require_ack(udp, device_id, packet)
            except HTTPException as exc:
                await hub.publish_event(
                    {
                        "type": "command_sync_failed",
                        "payload": {
                            "action": "select_sentence",
                            "message": (
                                "主机已经切换句子，但 Quest 未确认同步；"
                                f"请检查设备连接后重新选择当前句。{exc.detail}"
                            ),
                        },
                    }
                )

    async def handle_signal(packet: dict) -> None:
        signal = str(packet.get("signal") or "")
        if signal == "help":
            state = await recordings.set_help_requested(bool(packet.get("active")))
            await hub.publish_event(
                {"type": "state_changed", "payload": state.model_dump(mode="json")}
            )

    udp.on_signal = handle_signal

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        if start_udp:
            await udp.start()
            await udp.scan()
        try:
            yield
        finally:
            if start_udp:
                await udp.stop()
            await hub.close()

    app = FastAPI(title="SignVR Recorder Host", version="1.0.0", lifespan=lifespan)
    app.add_middleware(
        CORSMiddleware,
        allow_origins=[config.frontend_origin, "http://127.0.0.1:5174", "http://localhost:5173"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )
    app.state.settings = config
    app.state.registry = registry
    app.state.hub = hub
    app.state.recordings = recordings
    app.state.repository = repository
    app.state.udp = udp

    @app.get("/api/health")
    async def health() -> dict:
        return {
            "status": "ok",
            "station_id": config.station_id,
            "udp_port": config.udp_port,
            "control_port": config.quest_control_port,
            "device_filter_enabled": bool(config.allowed_device_ids),
        }

    @app.get("/api/state")
    async def get_state():
        return await recordings.snapshot()

    @app.get("/api/recording/batches")
    async def list_recording_batches() -> dict:
        return {"root": str(repository.recordings_root), "batches": repository.list_batches()}

    @app.get("/api/recording/batches/{batch_id}/rounds")
    async def list_recording_rounds(batch_id: str) -> dict:
        try:
            safe_batch = safe_segment(batch_id)
            rounds = repository.list_rounds(safe_batch)
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        return {
            "batch_id": safe_batch,
            "rounds": [item.model_dump(mode="json") for item in rounds],
            "suggested_round_id": repository.suggested_round_id(safe_batch),
        }

    @app.post("/api/recording/batches/{batch_id}/rounds")
    async def create_recording_round(batch_id: str, body: RoundCreateRequest):
        try:
            state = await recordings.create_round(
                safe_segment(batch_id),
                safe_segment(body.round_id),
            )
        except FileExistsError as exc:
            raise HTTPException(status_code=409, detail="该轮次已经存在") from exc
        except ValueError as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})
        await sync_current_sentence_to_quest()
        return state

    @app.post("/api/recording/batches/{batch_id}/rounds/{round_id}/select")
    async def select_recording_round(batch_id: str, round_id: str):
        try:
            state = await recordings.select_round(
                safe_segment(batch_id),
                safe_segment(round_id),
            )
        except FileNotFoundError as exc:
            raise HTTPException(status_code=404, detail="未找到该录制轮次") from exc
        except ValueError as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})
        await sync_current_sentence_to_quest()
        return state

    @app.put("/api/recording/current-sentence")
    async def select_current_sentence(body: SentenceSelectRequest):
        try:
            state = await select_sentence_and_sync(body.sentence_index)
        except ValueError as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        return state

    @app.get("/api/devices")
    async def list_devices():
        return await registry.list()

    @app.post("/api/devices/scan", status_code=202)
    async def scan_devices() -> dict:
        if not start_udp:
            return {"status": "disabled"}
        await udp.scan()
        return {"status": "scanning"}

    @app.post("/api/devices/{device_id}/select", response_model=DeviceSelectResponse)
    async def select_device(device_id: str):
        device = await registry.get(device_id)
        if device is None:
            raise HTTPException(status_code=404, detail="未找到这台 Quest 设备")
        quest_station_id = config.station_id
        selected, token = await registry.select(device_id)
        cmd_id = command_id()
        packet = pair_packet(
            cmd_id=cmd_id,
            device_id=device_id,
            host_ip=udp.host_ip_for(selected.ip),
            http_port=config.http_port,
            pose_port=config.udp_port,
            station_id=quest_station_id,
            session_token=token,
            pairing_key=config.pairing_key,
        )
        if start_udp:
            try:
                await _send_and_require_ack(udp, device_id, packet)
            except HTTPException:
                await registry.mark_pairing_result(device_id, False)
                raise
        selected = await registry.mark_pairing_result(
            device_id,
            True,
            config.station_id,
        )
        await recordings.set_selected_device(device_id)
        await hub.publish_event({"type": "device_selected", "payload": selected.model_dump()})
        await sync_current_sentence_to_quest(device_id)
        return DeviceSelectResponse(selected=selected, command_id=cmd_id)

    async def selected_device_id() -> str:
        device = await registry.selected()
        if device is None:
            raise HTTPException(status_code=409, detail="请先选择一台 Quest 设备")
        state = await recordings.snapshot()
        if state.selected_device_id != device.device_id:
            await recordings.set_selected_device(device.device_id)
        return device.device_id

    @app.post("/api/recording/start", response_model=RecordingCommandResponse)
    async def start_recording(body: StartRecordingRequest):
        device_id = await selected_device_id()
        checkpoint = await recordings.checkpoint()
        previous = checkpoint.state
        if previous.recording_status.value != "ready":
            raise HTTPException(status_code=409, detail="当前状态不能开始录制")
        try:
            batch_id = safe_segment(body.batch_id)
            round_id = safe_segment(body.round_id)
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        if previous.batch_id != batch_id or previous.round_id != round_id:
            raise HTTPException(status_code=409, detail="请先在网页端打开要录制的批次和轮次")
        sentence = previous.sentences[previous.current_sentence_index]
        try:
            take_id, take_index, _ = repository.reserve_take(
                batch_id,
                round_id,
                sentence.sentence_id,
            )
        except (ValueError, FileNotFoundError) as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        try:
            state, packet, cmd_id, start_at = await recordings.start(
                batch_id,
                round_id,
                take_id,
                take_index,
            )
        except ValueError as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        if start_udp:
            try:
                await _send_and_require_ack(udp, device_id, packet)
            except HTTPException:
                await recordings.restore(checkpoint)
                raise
        await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})
        asyncio.create_task(_mark_started(recordings, hub, cmd_id, start_at))
        return RecordingCommandResponse(
            action="start_take", state=state, command_id=cmd_id, start_at_unix_ms=start_at
        )

    @app.post("/api/recording/stop", response_model=RecordingCommandResponse)
    async def stop_recording():
        device_id = await selected_device_id()
        checkpoint = await recordings.checkpoint()
        previous = checkpoint.state
        sentence_sync_error: HTTPException | None = None
        try:
            state, packet, cmd_id = await recordings.stop()
        except ValueError as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        if start_udp:
            try:
                await _send_and_require_ack(udp, device_id, packet)
            except HTTPException:
                await recordings.restore(checkpoint)
                raise
            if state.current_sentence_index != previous.current_sentence_index:
                sentence_packet, _ = await recordings.current_sentence_command()
                try:
                    await _send_and_require_ack(udp, device_id, sentence_packet)
                except HTTPException as exc:
                    # The take is already stopped and may already be persisted on
                    # Quest. Keep the authoritative host cursor instead of making
                    # a successful stop appear to have failed or rolling it back.
                    sentence_sync_error = exc
        await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})
        if sentence_sync_error is not None:
            await hub.publish_event(
                {
                    "type": "command_sync_failed",
                    "payload": {
                        "action": "select_sentence",
                        "message": (
                            "录制已结束并保存，但 Quest 未确认切换到下一句；"
                            "请确认设备在线后用左右键重新切换。"
                        ),
                    },
                }
            )
        return RecordingCommandResponse(action="stop_take", state=state, command_id=cmd_id)

    @app.post("/api/recording/reset", response_model=RecordingCommandResponse)
    async def reset_recording():
        device_id = await selected_device_id()
        checkpoint = await recordings.checkpoint()
        state, packet, cmd_id = await recordings.reset()
        if start_udp:
            try:
                await _send_and_require_ack(udp, device_id, packet)
            except HTTPException:
                await recordings.restore(checkpoint)
                raise
        await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})
        return RecordingCommandResponse(action="reset_take", state=state, command_id=cmd_id)

    @app.post("/api/pedal/{phase}", status_code=202)
    async def pedal_event(phase: str, progress: float = 0.0) -> dict:
        """Mirror the pedal into the headset.

        Sent on every press regardless of whether it changes the recording state:
        a deaf teacher has no other way to tell that the pedal registered.
        """
        if phase not in {"down", "hold", "up"}:
            raise HTTPException(status_code=400, detail="Unknown pedal phase")
        device = await registry.selected()
        if device is None or not start_udp:
            return {"status": "ignored"}
        packet = pedal_packet(
            cmd_id=command_id(), phase=phase, progress=max(0.0, min(1.0, progress))
        )
        try:
            await udp.send_to_device(device.device_id, packet)
        except KeyError:
            return {"status": "ignored"}
        return {"status": "sent", "phase": phase}

    @app.post("/api/guidance/{enabled}")
    async def set_guidance(enabled: bool):
        """Toggle the in-headset boundary guidance for the A/B recording passes."""
        state, packet, cmd_id = await recordings.set_guidance_enabled(enabled)
        device = await registry.selected()
        if device is not None and start_udp:
            try:
                await _send_and_require_ack(udp, device.device_id, packet)
            except HTTPException:
                await recordings.set_guidance_enabled(not enabled)
                raise
        await hub.publish_event(
            {"type": "state_changed", "payload": state.model_dump(mode="json")}
        )
        return {"guidance_enabled": enabled, "command_id": cmd_id}

    @app.post("/api/help/{acknowledged}")
    async def acknowledge_help(acknowledged: bool):
        state = await recordings.set_help_requested(not acknowledged)
        await hub.publish_event(
            {"type": "state_changed", "payload": state.model_dump(mode="json")}
        )
        return {"help_requested": state.help_requested}

    @app.post("/api/devices/{device_id}/preview-frame", status_code=202)
    async def upload_preview(
        device_id: str,
        request: Request,
    ) -> dict:
        if not await registry.is_selected(device_id):
            raise HTTPException(status_code=409, detail="该 Quest 未被本工作站选中")
        if request.headers.get("content-type", "").split(";", 1)[0] != "image/jpeg":
            raise HTTPException(status_code=415, detail="Expected image/jpeg")
        jpeg = await request.body()
        await hub.publish_preview(device_id, jpeg)
        await registry.mark_preview_frame(device_id)
        return {"bytes": len(jpeg)}

    @app.get("/api/devices/{device_id}/preview.jpg")
    async def latest_preview(device_id: str) -> Response:
        jpeg = await hub.latest_preview(device_id)
        if jpeg is None:
            raise HTTPException(status_code=404, detail="No preview frame received")
        return Response(content=jpeg, media_type="image/jpeg", headers={"Cache-Control": "no-store"})

    @app.post("/api/devices/{device_id}/takes/upload")
    async def upload_take(
        device_id: str,
        session_id: str = Form(...),
        sentence_id: str = Form(...),
        take_id: str = Form(...),
        pose_file: UploadFile = File(...),
        meta_file: UploadFile = File(...),
    ) -> dict:
        if not await registry.is_selected(device_id):
            raise HTTPException(status_code=409, detail="该 Quest 未被本工作站选中")
        # New Unity Takes carry an explicit quality contract. Keep accepting
        # legacy metadata without those fields, but never persist a Take that
        # explicitly identifies itself as simulated or interrupted.
        try:
            metadata_bytes = await meta_file.read()
            await meta_file.seek(0)
            metadata = json.loads(metadata_bytes.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise HTTPException(status_code=400, detail="元数据不是有效 JSON") from exc

        if not isinstance(metadata, dict):
            raise HTTPException(status_code=400, detail="元数据必须是 JSON 对象")

        if metadata.get("editor_simulation") or metadata.get("pose_source_simulated"):
            raise HTTPException(status_code=400, detail="编辑器模拟 Take 不能上传")

        capture_status = metadata.get("capture_status")
        if capture_status is not None and capture_status != "completed":
            raise HTTPException(status_code=400, detail="未完成或质量不合格的 Take 不能上传")

        quality_fields = {
            "pose_frame_count",
            "valid_pose_frame_count",
            "valid_pose_ratio",
        }
        if quality_fields.intersection(metadata):
            try:
                frame_count = int(metadata["pose_frame_count"])
                valid_frame_count = int(metadata["valid_pose_frame_count"])
                valid_ratio = float(metadata["valid_pose_ratio"])
            except (KeyError, TypeError, ValueError) as exc:
                raise HTTPException(status_code=400, detail="姿态质量元数据不完整") from exc
            if (
                frame_count <= 0
                or valid_frame_count <= 0
                or valid_frame_count > frame_count
                or not 0.0 < valid_ratio <= 1.0
            ):
                raise HTTPException(status_code=400, detail="姿态质量未通过门槛")

        metadata_session_id = metadata.get("session_id")
        metadata_sentence_id = metadata.get("sentence_id")
        metadata_take_id = metadata.get("take_id")
        if (
            metadata_session_id is not None
            and metadata_session_id != session_id
            or metadata_sentence_id is not None
            and metadata_sentence_id != sentence_id
            or metadata_take_id is not None
            and metadata_take_id != take_id
        ):
            raise HTTPException(status_code=400, detail="元数据与上传字段不一致")

        try:
            directory = repository.take_directory(session_id, sentence_id, take_id)
            safe_take_id = safe_segment(take_id)
            pose_path, meta_path = await repository.save_uploads(
                [
                    (pose_file, directory / f"{safe_take_id}.pose.jsonl"),
                    (meta_file, directory / f"{safe_take_id}.meta.json"),
                ]
            )
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        except FileExistsError as exc:
            raise HTTPException(status_code=409, detail="该 Take 的动作文件已经存在，服务器拒绝覆盖") from exc
        state = await recordings.refresh_after_upload(session_id, sentence_id)
        await hub.publish_event({"type": "take_uploaded", "payload": state.model_dump(mode="json")})
        return {"pose_file": str(pose_path), "meta_file": str(meta_path)}

    @app.post("/api/takes/{take_id}/camera-upload")
    async def upload_camera(
        take_id: str,
        session_id: str = Form(...),
        sentence_id: str = Form(...),
        video_file: UploadFile = File(...),
    ) -> dict:
        try:
            directory = repository.take_directory(session_id, sentence_id, take_id)
            video_path = await repository.save_upload(
                video_file,
                directory / f"{safe_segment(take_id)}.camera.webm",
            )
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        except FileExistsError as exc:
            raise HTTPException(status_code=409, detail="该 Take 的相机视频已经存在，服务器拒绝覆盖") from exc
        state = await recordings.refresh_after_upload(session_id, sentence_id)
        await hub.publish_event({"type": "camera_uploaded", "payload": state.model_dump(mode="json")})
        return {"video_file": str(video_path)}

    @app.websocket("/ws/events")
    async def event_socket(websocket: WebSocket) -> None:
        await hub.add_events(
            websocket,
            {"type": "state_changed", "payload": (await recordings.snapshot()).model_dump(mode="json")},
        )
        try:
            while True:
                await websocket.receive_text()
        except WebSocketDisconnect:
            await hub.remove_events(websocket)

    @app.websocket("/ws/preview/{device_id}")
    async def preview_socket(websocket: WebSocket, device_id: str) -> None:
        await hub.add_preview(device_id, websocket)
        try:
            while True:
                await websocket.receive_text()
        except WebSocketDisconnect:
            await hub.remove_preview(device_id, websocket)

    @app.websocket("/ws/pose/{device_id}")
    async def pose_socket(websocket: WebSocket, device_id: str) -> None:
        await hub.add_pose(device_id, websocket)
        try:
            while True:
                await websocket.receive_text()
        except WebSocketDisconnect:
            await hub.remove_pose(device_id, websocket)

    frontend_dist = Path(__file__).resolve().parents[2] / "frontend" / "dist"
    if frontend_dist.exists():
        app.mount("/", StaticFiles(directory=frontend_dist, html=True), name="frontend")

    return app


async def _mark_started(
    recordings: RecordingService,
    hub: RealtimeHub,
    command_id: str,
    start_at_unix_ms: int,
) -> None:
    from .protocol import unix_ms

    await asyncio.sleep(max(0, start_at_unix_ms - unix_ms()) / 1000)
    state = await recordings.mark_recording_started(command_id)
    if state is None:
        return
    await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})


async def _send_and_require_ack(
    udp: UdpService,
    device_id: str,
    packet: dict,
) -> dict:
    try:
        ack = await udp.send_to_device_and_wait(device_id, packet)
    except TimeoutError as exc:
        raise HTTPException(
            status_code=504,
            detail="Quest 未确认命令，请检查设备是否在线且录制程序正在运行",
        ) from exc

    if not bool(ack.get("accepted")):
        raise HTTPException(
            status_code=409,
            detail=str(ack.get("message") or "Quest 拒绝了该命令"),
        )

    return ack


app = create_app()
