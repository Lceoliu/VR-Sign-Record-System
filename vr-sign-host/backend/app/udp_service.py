from __future__ import annotations

import asyncio
import ipaddress
import socket
from collections.abc import Awaitable, Callable
from contextlib import suppress
from typing import Any

import psutil

from .config import Settings
from .device_registry import DeviceRegistry
from .protocol import (
    LEGACY_COMPATIBILITY_TOKEN,
    command_id,
    decode_packet,
    discovery_packet,
    encode_packet,
    pair_packet,
)
from .realtime import RealtimeHub


class SignVrDatagramProtocol(asyncio.DatagramProtocol):
    def __init__(self, service: "UdpService") -> None:
        self.service = service

    def connection_made(self, transport: asyncio.BaseTransport) -> None:
        self.service.transport = transport  # type: ignore[assignment]

    def datagram_received(self, data: bytes, addr: tuple[str, int]) -> None:
        self.service.enqueue_packet(data, addr)

    def error_received(self, exc: Exception) -> None:
        self.service.publish_udp_error(exc)


class UdpService:
    def __init__(self, settings: Settings, registry: DeviceRegistry, hub: RealtimeHub) -> None:
        self.settings = settings
        self.registry = registry
        self.hub = hub
        self.transport: asyncio.DatagramTransport | None = None
        self._pending_acks: dict[str, asyncio.Future[dict[str, Any]]] = {}
        self._realtime_packets: asyncio.Queue[tuple[dict[str, Any], tuple[str, int]]] = asyncio.Queue(maxsize=256)
        self._realtime_worker: asyncio.Task[None] | None = None
        self._discovery_worker: asyncio.Task[None] | None = None
        self._control_tasks: set[asyncio.Task[None]] = set()
        self._pair_refreshing_devices: set[str] = set()
        self._pair_refreshed_devices: set[str] = set()
        self.on_signal: Callable[[dict[str, Any]], Awaitable[None]] | None = None

    async def start(self) -> None:
        loop = asyncio.get_running_loop()
        transport, _ = await loop.create_datagram_endpoint(
            lambda: SignVrDatagramProtocol(self),
            local_addr=(self.settings.udp_host, self.settings.udp_port),
            allow_broadcast=True,
        )
        self.transport = transport  # type: ignore[assignment]
        self._realtime_worker = asyncio.create_task(self._process_realtime_packets())
        self._discovery_worker = asyncio.create_task(self._scan_periodically())

    async def stop(self) -> None:
        if self.transport:
            self.transport.close()
            self.transport = None
        if self._realtime_worker is not None:
            self._realtime_worker.cancel()
            with suppress(asyncio.CancelledError):
                await self._realtime_worker
            self._realtime_worker = None
        if self._discovery_worker is not None:
            self._discovery_worker.cancel()
            with suppress(asyncio.CancelledError):
                await self._discovery_worker
            self._discovery_worker = None
        tasks = tuple(self._control_tasks)
        for task in tasks:
            task.cancel()
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)

    async def scan(self) -> None:
        self._send_discovery()
        await self.hub.publish_event({"type": "device_scan_started", "payload": {}})

    async def _scan_periodically(self) -> None:
        while True:
            self._send_discovery()
            await asyncio.sleep(3)

    def _send_discovery(self) -> None:
        packet = discovery_packet(self.settings.udp_port)
        for target in self._discovery_targets():
            self._send(packet, (target, self.settings.quest_control_port))

    def _discovery_targets(self) -> list[str]:
        targets = {self.settings.discovery_broadcast, "255.255.255.255"}
        stats = psutil.net_if_stats()
        for interface, addresses in psutil.net_if_addrs().items():
            if interface in stats and not stats[interface].isup:
                continue
            for address in addresses:
                if address.family != socket.AF_INET or not address.netmask:
                    continue
                ip = ipaddress.IPv4Address(address.address)
                if ip.is_loopback or ip.is_link_local:
                    continue
                network = ipaddress.IPv4Network(
                    f"{address.address}/{address.netmask}",
                    strict=False,
                )
                if network.prefixlen < 31:
                    targets.add(str(network.broadcast_address))
        return sorted(targets)

    async def send_to_device(self, device_id: str, packet: dict[str, Any]) -> None:
        device = await self.registry.get(device_id)
        if device is None:
            raise KeyError(device_id)
        outbound = packet
        if packet.get("type") == "command":
            token = await self.registry.session_token(device_id)
            outbound = {
                **packet,
                "station_id": device.paired_station_id or self.settings.station_id,
                "session_token": token or LEGACY_COMPATIBILITY_TOKEN,
            }
        self._send(outbound, (device.ip, device.control_port))

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
        await self._handle_decoded_packet(packet, addr)

    def enqueue_packet(self, data: bytes, addr: tuple[str, int]) -> None:
        try:
            packet = decode_packet(data)
        except (UnicodeDecodeError, ValueError):
            return
        if packet.get("type") in {"status", "skeleton", "frame"}:
            if self._realtime_packets.full():
                self._realtime_packets.get_nowait()
            self._realtime_packets.put_nowait((packet, addr))
            return
        self._spawn_control_task(self._handle_decoded_packet(packet, addr))

    def publish_udp_error(self, exc: Exception) -> None:
        self._spawn_control_task(
            self.hub.publish_event({"type": "udp_error", "payload": {"message": str(exc)}})
        )

    async def _process_realtime_packets(self) -> None:
        while True:
            packet, addr = await self._realtime_packets.get()
            await self._handle_decoded_packet(packet, addr)

    async def _handle_decoded_packet(self, packet: dict[str, Any], addr: tuple[str, int]) -> None:
        packet_type = str(packet.get("type", ""))
        if packet_type == "announce":
            device = await self.registry.upsert_announcement(packet, addr[0])
            if device is None:
                return
            await self.hub.publish_event({"type": "device_updated", "payload": device.model_dump()})
            if (
                device.selected
                and device.device_id not in self._pair_refreshing_devices
                and device.device_id not in self._pair_refreshed_devices
            ):
                # Refresh once for every Host process, even when Quest reports
                # itself as paired. A migrated Host can keep the same station ID
                # while having a different IP address.
                self._pair_refreshing_devices.add(device.device_id)
                self._spawn_control_task(self._refresh_pairing(device.device_id))
            return
        if packet_type == "ack":
            device_id = str(packet.get("device_id") or "")
            accepted_source = bool(device_id) and await self.registry.mark_ack(
                device_id,
                str(packet.get("state") or "available"),
                addr[0],
            )
            if not accepted_source:
                return
            cmd_id = str(packet.get("command_id") or "")
            future = self._pending_acks.get(cmd_id)
            if future and not future.done():
                future.set_result(packet)
            await self.hub.publish_event({"type": "command_ack", "payload": packet})
            return
        if packet_type == "signal":
            if not await self.registry.is_selected_source(
                str(packet.get("device_id") or "") or None,
                addr[0],
            ):
                return
            # The teacher pressed the help button inside the headset. They cannot
            # call out, so this has to surface on the console immediately.
            await self.hub.publish_event({"type": "device_signal", "payload": packet})
            if self.on_signal is not None:
                await self.on_signal(packet)
            return
        if packet_type in {"status", "skeleton", "frame"}:
            device_id = await self.registry.mark_pose_packet(packet.get("device_id"), addr[0])
            if device_id:
                await self.hub.publish_pose(device_id, packet)

    def _spawn_control_task(self, operation: Awaitable[None]) -> None:
        task = asyncio.create_task(operation)
        self._control_tasks.add(task)
        task.add_done_callback(self._control_task_finished)

    async def _refresh_pairing(self, device_id: str) -> None:
        try:
            device = await self.registry.get(device_id)
            token = await self.registry.session_token(device_id)
            if device is None or not token:
                return
            packet = pair_packet(
                cmd_id=command_id(),
                device_id=device_id,
                host_ip=self.host_ip_for(device.ip),
                http_port=self.settings.http_port,
                pose_port=self.settings.udp_port,
                station_id=device.paired_station_id or self.settings.station_id,
                session_token=token,
                pairing_key=self.settings.pairing_key,
            )
            ack = await self.send_to_device_and_wait(device_id, packet)
            if bool(ack.get("accepted")):
                await self.registry.mark_pairing_result(
                    device_id,
                    True,
                    self.settings.station_id,
                )
                self._pair_refreshed_devices.add(device_id)
        except (KeyError, OSError, TimeoutError):
            # The next Quest announcement retries the refresh.
            return
        finally:
            self._pair_refreshing_devices.discard(device_id)

    def _control_task_finished(self, task: asyncio.Task[None]) -> None:
        self._control_tasks.discard(task)
        if task.cancelled():
            return
        exception = task.exception()
        if exception is not None:
            asyncio.get_running_loop().call_exception_handler(
                {"message": "SignVR UDP control packet failed", "exception": exception, "task": task}
            )

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
