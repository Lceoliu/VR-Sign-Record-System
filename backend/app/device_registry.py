from __future__ import annotations

import asyncio
import secrets
from dataclasses import dataclass

from .models import DeviceInfo
from .protocol import unix_ms


@dataclass(slots=True)
class DeviceRecord:
    info: DeviceInfo
    session_token: str | None = None


class DeviceRegistry:
    def __init__(self) -> None:
        self._devices: dict[str, DeviceRecord] = {}
        self._selected_id: str | None = None
        self._lock = asyncio.Lock()

    async def upsert_announcement(self, packet: dict, ip: str) -> DeviceInfo:
        device_id = str(packet["device_id"])
        async with self._lock:
            current = self._devices.get(device_id)
            info = DeviceInfo(
                device_id=device_id,
                name=str(packet.get("name") or "Quest 3"),
                model=str(packet.get("model") or "Quest"),
                app_version=str(packet.get("app_version") or "unknown"),
                ip=ip,
                control_port=int(packet.get("control_port") or 5006),
                capabilities=[str(value) for value in packet.get("capabilities", [])],
                state=str(packet.get("state") or "available"),
                selected=device_id == self._selected_id,
                paired=current.info.paired if current else False,
                last_seen_unix_ms=unix_ms(),
                preview_frames=current.info.preview_frames if current else 0,
                pose_packets=current.info.pose_packets if current else 0,
            )
            self._devices[device_id] = DeviceRecord(info=info, session_token=current.session_token if current else None)
            return info.model_copy()

    async def list(self) -> list[DeviceInfo]:
        async with self._lock:
            return [record.info.model_copy() for record in self._devices.values()]

    async def get(self, device_id: str) -> DeviceInfo | None:
        async with self._lock:
            record = self._devices.get(device_id)
            return record.info.model_copy() if record else None

    async def select(self, device_id: str) -> tuple[DeviceInfo, str]:
        async with self._lock:
            record = self._devices[device_id]
            self._selected_id = device_id
            token = secrets.token_urlsafe(32)
            for candidate_id, candidate in self._devices.items():
                candidate.info.selected = candidate_id == device_id
            record.info.paired = False
            record.session_token = token
            return record.info.model_copy(), token

    async def mark_pairing_result(self, device_id: str, paired: bool) -> DeviceInfo:
        async with self._lock:
            record = self._devices[device_id]
            record.info.paired = paired
            return record.info.model_copy()

    async def selected(self) -> DeviceInfo | None:
        async with self._lock:
            if self._selected_id is None:
                return None
            return self._devices[self._selected_id].info.model_copy()

    async def validate_token(self, device_id: str, token: str) -> bool:
        async with self._lock:
            record = self._devices.get(device_id)
            return bool(record and record.session_token and secrets.compare_digest(record.session_token, token))

    async def mark_pose_packet(self, device_id: str | None, ip: str) -> str | None:
        async with self._lock:
            record = self._resolve_record(device_id, ip)
            if record is None:
                return None
            record.info.pose_packets += 1
            record.info.last_seen_unix_ms = unix_ms()
            return record.info.device_id

    async def mark_preview_frame(self, device_id: str) -> None:
        async with self._lock:
            record = self._devices.get(device_id)
            if record:
                record.info.preview_frames += 1
                record.info.last_seen_unix_ms = unix_ms()

    async def mark_ack(self, device_id: str, state: str) -> None:
        async with self._lock:
            record = self._devices.get(device_id)
            if record:
                record.info.state = state
                record.info.last_seen_unix_ms = unix_ms()

    def _resolve_record(self, device_id: str | None, ip: str) -> DeviceRecord | None:
        if device_id and device_id in self._devices:
            return self._devices[device_id]
        return next((record for record in self._devices.values() if record.info.ip == ip), None)
