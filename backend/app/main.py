from __future__ import annotations

import asyncio
import json
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
    SentenceImportRequest,
    TakeQuality,
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
    recordings = RecordingService()
    repository = RecordingRepository(config.data_root)
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
        yield
        if start_udp:
            await udp.stop()

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

    @app.put("/api/sentences")
    async def import_sentences(body: SentenceImportRequest):
        try:
            state = await recordings.import_sentences(body.sentences)
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
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
    async def start_recording():
        device_id = await selected_device_id()
        previous = await recordings.snapshot()
        try:
            state, packet, cmd_id, start_at = await recordings.start()
        except ValueError as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        if start_udp:
            try:
                await _send_and_require_ack(udp, device_id, packet)
            except HTTPException:
                await recordings.restore(previous)
                raise
        await hub.publish_event({"type": "state_changed", "payload": state.model_dump(mode="json")})
        asyncio.create_task(_mark_started(recordings, hub, start_at))
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
        directory = repository.take_directory(session_id, sentence_id, take_id)
        pose_path = await repository.save_upload(pose_file, directory / f"{safe_segment(take_id)}.pose.jsonl")
        meta_path = await repository.save_upload(meta_file, directory / f"{safe_segment(take_id)}.meta.json")
        state = await recordings.attach_files(
            take_id,
            pose_file=str(pose_path.relative_to(repository.root)),
            meta_file=str(meta_path.relative_to(repository.root)),
        )
        quality = _read_quality(meta_path)
        if quality is not None:
            state = await recordings.attach_quality(take_id, quality)
        await hub.publish_event({"type": "take_uploaded", "payload": state.model_dump(mode="json")})
        return {"pose_file": str(pose_path), "meta_file": str(meta_path)}

    @app.post("/api/takes/{take_id}/camera-upload")
    async def upload_camera(
        take_id: str,
        session_id: str = Form(...),
        sentence_id: str = Form(...),
        video_file: UploadFile = File(...),
    ) -> dict:
        directory = repository.take_directory(session_id, sentence_id, take_id)
        video_path = await repository.save_upload(video_file, directory / f"{safe_segment(take_id)}.camera.webm")
        state = await recordings.attach_files(
            take_id,
            video_file=str(video_path.relative_to(repository.root)),
        )
        await hub.publish_event({"type": "camera_uploaded", "payload": state.model_dump(mode="json")})
        return {"video_file": str(video_path)}

    @app.websocket("/ws/events")
    async def event_socket(websocket: WebSocket) -> None:
        await hub.add_events(websocket)
        await websocket.send_json({"type": "state_changed", "payload": (await recordings.snapshot()).model_dump(mode="json")})
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


def _read_quality(meta_path: Path) -> TakeQuality | None:
    """Lift the hand tracking summary the Quest wrote into the take metadata."""
    try:
        payload = json.loads(meta_path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    summary = payload.get("hand_capture_quality")
    if not isinstance(summary, dict) or not summary.get("frames"):
        return None
    return TakeQuality(
        frames=int(summary.get("frames", 0)),
        clean_ratio=float(summary.get("clean_ratio", 0.0)),
        left_tracked_ratio=float(summary.get("left_tracked_ratio", 0.0)),
        right_tracked_ratio=float(summary.get("right_tracked_ratio", 0.0)),
        left_inside_ratio=float(summary.get("left_inside_ratio", 0.0)),
        right_inside_ratio=float(summary.get("right_inside_ratio", 0.0)),
        guidance_enabled=bool(summary.get("guidance_enabled", True)),
    )


async def _mark_started(recordings: RecordingService, hub: RealtimeHub, start_at_unix_ms: int) -> None:
    from .protocol import unix_ms

    await asyncio.sleep(max(0, start_at_unix_ms - unix_ms()) / 1000)
    state = await recordings.mark_recording_started()
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
