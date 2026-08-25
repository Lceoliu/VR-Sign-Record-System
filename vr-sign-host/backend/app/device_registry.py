from __future__ import annotations

import asyncio
from dataclasses import dataclass

from .models import DeviceInfo
from .protocol import LEGACY_COMPATIBILITY_TOKEN, unix_ms


@dataclass(slots=True)
class DeviceRecord:
    info: DeviceInfo
    session_token: str | None = None


class DeviceRegistry:
    def __init__(
        self,
        local_station_id: str | None = None,
        allowed_device_ids: frozenset[str] | None = None,
    ) -> None:
        self._devices: dict[str, DeviceRecord] = {}
        self._selected_id: str | None = None
        self._local_station_id = local_station_id
        self._allowed_device_ids = frozenset(
            device_id.casefold() for device_id in (allowed_device_ids or ())
        )
        self._lock = asyncio.Lock()

    async def upsert_announcement(self, packet: dict, ip: str) -> DeviceInfo | None:
        device_id = str(packet["device_id"])
        if (
            self._allowed_device_ids
            and device_id.casefold() not in self._allowed_device_ids
        ):
            return None
        async with self._lock:
            current = self._devices.get(device_id)
            reported_paired = packet.get("paired")
            paired_station_id = str(packet.get("paired_station_id") or "").strip() or None
            if (
                self._selected_id is None
                and self._local_station_id
                and (
                    paired_station_id == self._local_station_id
                    or bool(self._allowed_device_ids)
                )
            ):
                self._selected_id = device_id
            info = DeviceInfo(
                device_id=device_id,
                name=str(packet.get("name") or "Quest 3"),
                model=str(packet.get("model") or "Quest"),
                app_version=str(packet.get("app_version") or "unknown"),
                ip=ip,
                control_port=int(packet.get("control_port") or 5012),
                capabilities=[str(value) for value in packet.get("capabilities", [])],
                state=str(packet.get("state") or "available"),
                selected=device_id == self._selected_id,
                paired=(
                    bool(reported_paired)
                    if reported_paired is not None
                    else current.info.paired if current else False
                ),
                paired_station_id=paired_station_id,
                last_seen_unix_ms=unix_ms(),
                preview_frames=current.info.preview_frames if current else 0,
                pose_packets=current.info.pose_packets if current else 0,
            )
            self._devices[device_id] = DeviceRecord(
                info=info,
                session_token=LEGACY_COMPATIBILITY_TOKEN,
            )
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
            for candidate_id, candidate in self._devices.items():
                candidate.info.selected = candidate_id == device_id
            record.info.paired = False
            record.session_token = LEGACY_COMPATIBILITY_TOKEN
            return record.info.model_copy(), LEGACY_COMPATIBILITY_TOKEN

    async def mark_pairing_result(
        self,
        device_id: str,
        paired: bool,
        station_id: str | None = None,
    ) -> DeviceInfo:
        async with self._lock:
            record = self._devices[device_id]
            record.info.paired = paired
            if paired and station_id:
                record.info.paired_station_id = station_id
            return record.info.model_copy()

    async def selected(self) -> DeviceInfo | None:
        async with self._lock:
            if self._selected_id is None:
                return None
            return self._devices[self._selected_id].info.model_copy()

    async def session_token(self, device_id: str) -> str | None:
        async with self._lock:
            record = self._devices.get(device_id)
            return LEGACY_COMPATIBILITY_TOKEN if record else None

    async def mark_pose_packet(self, device_id: str | None, ip: str) -> str | None:
        async with self._lock:
            record = self._resolve_record(device_id, ip)
            if record is None or record.info.device_id != self._selected_id:
                return None
            record.info.pose_packets += 1
            record.info.last_seen_unix_ms = unix_ms()
            return record.info.device_id

    async def mark_preview_frame(self, device_id: str) -> bool:
        async with self._lock:
            record = self._devices.get(device_id)
            if record is None or record.info.device_id != self._selected_id:
                return False
            record.info.preview_frames += 1
            record.info.last_seen_unix_ms = unix_ms()
            return True

    async def mark_ack(self, device_id: str, state: str, ip: str) -> bool:
        async with self._lock:
            record = self._resolve_record(device_id, ip)
            if record is None or record.info.device_id != self._selected_id:
                return False
            record.info.state = state
            record.info.last_seen_unix_ms = unix_ms()
            return True

    async def is_selected_source(self, device_id: str | None, ip: str) -> bool:
        async with self._lock:
            record = self._resolve_record(device_id, ip)
            return record is not None and record.info.device_id == self._selected_id

    async def is_selected(self, device_id: str) -> bool:
        async with self._lock:
            return device_id == self._selected_id and device_id in self._devices

    def _resolve_record(self, device_id: str | None, ip: str) -> DeviceRecord | None:
        if device_id:
            return self._devices.get(device_id)
        return next((record for record in self._devices.values() if record.info.ip == ip), None)
