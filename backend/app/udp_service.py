from __future__ import annotations

import asyncio
import socket
from collections.abc import Awaitable, Callable
from typing import Any

from .config import Settings
from .device_registry import DeviceRegistry
from .protocol import decode_packet, discovery_packet, encode_packet
from .realtime import RealtimeHub


class SignVrDatagramProtocol(asyncio.DatagramProtocol):
    def __init__(self, service: "UdpService") -> None:
        self.service = service

    def connection_made(self, transport: asyncio.BaseTransport) -> None:
        self.service.transport = transport  # type: ignore[assignment]

    def datagram_received(self, data: bytes, addr: tuple[str, int]) -> None:
        asyncio.create_task(self.service.handle_packet(data, addr))

    def error_received(self, exc: Exception) -> None:
        asyncio.create_task(self.service.hub.publish_event({"type": "udp_error", "payload": {"message": str(exc)}}))


class UdpService:
    def __init__(self, settings: Settings, registry: DeviceRegistry, hub: RealtimeHub) -> None:
        self.settings = settings
        self.registry = registry
        self.hub = hub
        self.transport: asyncio.DatagramTransport | None = None
        self._pending_acks: dict[str, asyncio.Future[dict[str, Any]]] = {}

    async def start(self) -> None:
        loop = asyncio.get_running_loop()
        transport, _ = await loop.create_datagram_endpoint(
            lambda: SignVrDatagramProtocol(self),
            local_addr=(self.settings.udp_host, self.settings.udp_port),
            allow_broadcast=True,
        )
        self.transport = transport  # type: ignore[assignment]

    async def stop(self) -> None:
        if self.transport:
            self.transport.close()
            self.transport = None

    async def scan(self) -> None:
        self._send(
            discovery_packet(self.settings.udp_port),
            (self.settings.discovery_broadcast, self.settings.quest_control_port),
        )
        await self.hub.publish_event({"type": "device_scan_started", "payload": {}})

    async def send_to_device(self, device_id: str, packet: dict[str, Any]) -> None:
        device = await self.registry.get(device_id)
        if device is None:
            raise KeyError(device_id)
        self._send(packet, (device.ip, device.control_port))

    async def send_to_device_and_wait(
        self,
        device_id: str,
        packet: dict[str, Any],
        timeout_seconds: float = 2.5,
    ) -> dict[str, Any]:
        cmd_id = str(packet["command_id"])
        future = asyncio.get_running_loop().create_future()
        self._pending_acks[cmd_id] = future
        await self.send_to_device(device_id, packet)

        try:
            return await asyncio.wait_for(future, timeout_seconds)
        finally:
            self._pending_acks.pop(cmd_id, None)

    async def handle_packet(self, data: bytes, addr: tuple[str, int]) -> None:
        try:
            packet = decode_packet(data)
        except (UnicodeDecodeError, ValueError):
            return
        packet_type = str(packet.get("type", ""))
        if packet_type == "announce":
            device = await self.registry.upsert_announcement(packet, addr[0])
            await self.hub.publish_event({"type": "device_updated", "payload": device.model_dump()})
            return
        if packet_type == "ack":
            device_id = str(packet.get("device_id") or "")
            if device_id:
                await self.registry.mark_ack(device_id, str(packet.get("state") or "available"))
            cmd_id = str(packet.get("command_id") or "")
            future = self._pending_acks.get(cmd_id)
            if future and not future.done():
                future.set_result(packet)
            await self.hub.publish_event({"type": "command_ack", "payload": packet})
            return
        if packet_type in {"status", "skeleton", "frame"}:
            device_id = await self.registry.mark_pose_packet(packet.get("device_id"), addr[0])
            if device_id:
                await self.hub.publish_pose(device_id, packet)

    def host_ip_for(self, remote_ip: str) -> str:
        probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            probe.connect((remote_ip, 9))
            return str(probe.getsockname()[0])
        finally:
            probe.close()

    def _send(self, packet: dict[str, Any], address: tuple[str, int]) -> None:
        if self.transport is None:
            raise RuntimeError("UDP service is not running")
        self.transport.sendto(encode_packet(packet), address)
