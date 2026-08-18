from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, File, Form, Header, HTTPException, Request, UploadFile, WebSocket, WebSocketDisconnect
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


def create_app(*, settings: Settings | None = None, start_udp: bool = True) -> FastAPI:
    config = settings or Settings.from_environment()
    registry = DeviceRegistry()
    hub = RealtimeHub()
    repository = RecordingRepository(config.data_root)
    recordings = RecordingService(repository)
    udp = UdpService(config, registry, hub)

    async def handle_signal(packet: dict) -> None:
        if str(packet.get("signal")) != "help":
            return
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

    app = FastAPI(title="VR Sign Host", version="0.1.0", lifespan=lifespan)
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
        return {"status": "ok", "udp_port": config.udp_port, "control_port": config.quest_control_port}

    @app.get("/api/state")
    async def get_state():
        return await recordings.snapshot()

    @app.get("/api/recording/batches")
    async def list_recording_batches() -> dict:
        return {"root": "data/recordings", "batches": repository.list_batches()}

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
        return state

    @app.put("/api/recording/current-sentence")
    async def select_current_sentence(body: SentenceSelectRequest):
        try:
            state = await recordings.select_sentence(body.sentence_index)
        except ValueError as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})
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
        selected, token = await registry.select(device_id)
        cmd_id = command_id()
        packet = pair_packet(
            cmd_id=cmd_id,
            host_ip=udp.host_ip_for(selected.ip),
            http_port=config.http_port,
            pose_port=config.udp_port,
            session_token=token,
        )
        if start_udp:
            try:
                await _send_and_require_ack(udp, device_id, packet)
            except HTTPException:
                await registry.mark_pairing_result(device_id, False)
                raise
        selected = await registry.mark_pairing_result(device_id, True)
        await recordings.set_selected_device(device_id)
        await hub.publish_event({"type": "device_selected", "payload": selected.model_dump()})
        return DeviceSelectResponse(selected=selected, command_id=cmd_id)

    async def selected_device_id() -> str:
        device = await registry.selected()
        if device is None:
            raise HTTPException(status_code=409, detail="请先选择一台 Quest 设备")
        return device.device_id

    @app.post("/api/recording/start", response_model=RecordingCommandResponse)
    async def start_recording(body: StartRecordingRequest):
        device_id = await selected_device_id()
        previous = await recordings.snapshot()
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
                await recordings.restore(previous)
                raise
        await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})
        asyncio.create_task(_mark_started(recordings, hub, cmd_id, start_at))
        return RecordingCommandResponse(
            action="start_take", state=state, command_id=cmd_id, start_at_unix_ms=start_at
        )

    @app.post("/api/recording/stop", response_model=RecordingCommandResponse)
    async def stop_recording():
        device_id = await selected_device_id()
        previous = await recordings.snapshot()
        try:
            state, packet, cmd_id = await recordings.stop()
        except ValueError as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        if start_udp:
            try:
                await _send_and_require_ack(udp, device_id, packet)
            except HTTPException:
                await recordings.restore(previous)
                raise
        await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})
        return RecordingCommandResponse(action="stop_take", state=state, command_id=cmd_id)

    @app.post("/api/recording/reset", response_model=RecordingCommandResponse)
    async def reset_recording():
        device_id = await selected_device_id()
        previous = await recordings.snapshot()
        state, packet, cmd_id = await recordings.reset()
        if start_udp:
            try:
                await _send_and_require_ack(udp, device_id, packet)
            except HTTPException:
                await recordings.restore(previous)
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
        x_signvr_token: str = Header(default=""),
    ) -> dict:
        if not await registry.validate_token(device_id, x_signvr_token):
            raise HTTPException(status_code=401, detail="Invalid device token")
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
        x_signvr_token: str = Header(default=""),
    ) -> dict:
        if not await registry.validate_token(device_id, x_signvr_token):
            raise HTTPException(status_code=401, detail="Invalid device token")
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
