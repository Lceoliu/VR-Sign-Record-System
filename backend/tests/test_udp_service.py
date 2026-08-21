from __future__ import annotations

import asyncio

import anyio
import pytest
from starlette.websockets import WebSocketDisconnect

import app.realtime as realtime_module
from app.config import Settings
from app.device_registry import DeviceRegistry
from app.protocol import LEGACY_COMPATIBILITY_TOKEN, decode_packet, encode_packet, pair_packet
from app.realtime import RealtimeHub
from app.udp_service import UdpService


class FakeDatagramTransport:
    def __init__(self) -> None:
        self.sent: list[tuple[bytes, tuple[str, int]]] = []

    def sendto(self, data: bytes, address: tuple[str, int]) -> None:
        self.sent.append((data, address))


class SlowWebSocket:
    def __init__(self) -> None:
        self.accepted = False
        self.release = asyncio.Event()

    async def accept(self) -> None:
        self.accepted = True

    async def send_json(self, message: dict) -> None:
        await self.release.wait()

    async def send_bytes(self, message: bytes) -> None:
        await self.release.wait()


class DisconnectedWebSocket:
    async def accept(self) -> None:
        pass

    async def send_json(self, message: dict) -> None:
        raise WebSocketDisconnect(code=1006)

    async def send_bytes(self, message: bytes) -> None:
        raise WebSocketDisconnect(code=1006)


def test_pose_packets_are_forwarded_to_console(tmp_path):
    async def scenario() -> None:
        registry = DeviceRegistry()
        await registry.upsert_announcement(
            {"device_id": "quest-test", "name": "Quest 3 Test", "control_port": 5006},
            "192.168.1.42",
        )
        hub = RealtimeHub()
        forwarded: list[tuple[str, dict]] = []
        hub.publish_pose = lambda device_id, packet: _record(forwarded, device_id, packet)  # type: ignore[method-assign]
        service = UdpService(Settings(data_root=tmp_path), registry, hub)

        # The Quest omits device_id on pose packets, so the source IP resolves it.
        await service.handle_packet(
            encode_packet({"type": "skeleton", "joint_names": ["Root"], "parent_indices": [-1]}),
            ("192.168.1.42", 5005),
        )
        await service.handle_packet(
            encode_packet({"type": "frame", "sequence": 1, "joint_count": 1, "positions": [0.0, 1.0, 2.0]}),
            ("192.168.1.42", 5005),
        )
        await service.handle_packet(
            encode_packet({"type": "frame", "sequence": 2}),
            ("10.0.0.9", 5005),
        )

        assert [device_id for device_id, _ in forwarded] == ["quest-test", "quest-test"]
        assert [packet["type"] for _, packet in forwarded] == ["skeleton", "frame"]
        assert (await registry.get("quest-test")).pose_packets == 2

    anyio.run(scenario)


async def _record(sink: list, device_id: str, packet: dict) -> None:
    sink.append((device_id, packet))


def test_only_devices_owned_by_this_station_are_auto_selected():
    async def scenario() -> None:
        registry = DeviceRegistry("station-a")
        unclaimed = await registry.upsert_announcement(
            {"device_id": "quest-new", "control_port": 5006},
            "192.168.1.40",
        )
        foreign = await registry.upsert_announcement(
            {
                "device_id": "quest-b",
                "control_port": 5006,
                "paired_station_id": "station-b",
            },
            "192.168.1.41",
        )
        owned = await registry.upsert_announcement(
            {
                "device_id": "quest-a",
                "control_port": 5006,
                "paired_station_id": "station-a",
            },
            "192.168.1.42",
        )

        assert unclaimed.selected is False
        assert foreign.selected is False
        assert owned.selected is True
        assert (await registry.selected()).device_id == "quest-a"

    anyio.run(scenario)


def test_command_wait_resolves_matching_ack(tmp_path):
    async def scenario() -> None:
        registry = DeviceRegistry()
        await registry.upsert_announcement(
            {
                "device_id": "quest-test",
                "name": "Quest 3 Test",
                "control_port": 5006,
            },
            "192.168.1.42",
        )
        service = UdpService(Settings(data_root=tmp_path), registry, RealtimeHub())
        observed_acks: list[dict] = []

        async def observe_ack(packet: dict) -> None:
            observed_acks.append(packet)

        service.on_ack = observe_ack
        transport = FakeDatagramTransport()
        service.transport = transport  # type: ignore[assignment]
        packet = pair_packet(
            cmd_id="pair-command",
            host_ip="192.168.1.10",
            http_port=8000,
            pose_port=5005,
            station_id="station-test",
            session_token="token",
        )

        pending = asyncio.create_task(
            service.send_to_device_and_wait("quest-test", packet)
        )
        await asyncio.sleep(0)
        assert transport.sent[0][1] == ("192.168.1.42", 5006)

        await service.handle_packet(
            encode_packet(
                {
                    "type": "ack",
                    "command_id": "pair-command",
                    "device_id": "quest-test",
                    "accepted": True,
                    "state": "ready",
                }
            ),
            ("192.168.1.42", 5006),
        )

        ack = await pending
        assert ack["accepted"] is True
        assert observed_acks == [ack]

    anyio.run(scenario)


def test_commands_use_stable_legacy_fields_for_old_quest_builds(tmp_path):
    async def scenario() -> None:
        registry = DeviceRegistry()
        await registry.upsert_announcement(
            {"device_id": "quest-test", "control_port": 5006},
            "192.168.1.42",
        )
        await registry.select("quest-test")
        service = UdpService(
            Settings(data_root=tmp_path, station_id="station-test"),
            registry,
            RealtimeHub(),
        )
        transport = FakeDatagramTransport()
        service.transport = transport  # type: ignore[assignment]

        await service.send_to_device(
            "quest-test",
            {"type": "command", "command_id": "start", "action": "start_take"},
        )

        packet = decode_packet(transport.sent[0][0])
        assert packet["station_id"] == "station-test"
        assert packet["session_token"] == LEGACY_COMPATIBILITY_TOKEN

    anyio.run(scenario)


def test_command_wait_retries_until_timeout(tmp_path):
    async def scenario() -> None:
        registry = DeviceRegistry()
        await registry.upsert_announcement(
            {"device_id": "quest-test", "control_port": 5006},
            "192.168.1.42",
        )
        service = UdpService(Settings(data_root=tmp_path), registry, RealtimeHub())
        transport = FakeDatagramTransport()
        service.transport = transport  # type: ignore[assignment]
        packet = pair_packet(
            cmd_id="retry-command",
            host_ip="192.168.1.10",
            http_port=8000,
            pose_port=5005,
            station_id="station-test",
            session_token="token",
        )

        with pytest.raises(TimeoutError):
            await service.send_to_device_and_wait(
                "quest-test",
                packet,
                timeout_seconds=0.08,
                retry_interval_seconds=0.01,
            )
        assert len(transport.sent) >= 2

    anyio.run(scenario)


def test_realtime_pose_queue_keeps_only_bounded_latest_frames():
    async def scenario() -> None:
        hub = RealtimeHub()
        websocket = SlowWebSocket()
        await hub.add_pose("quest-test", websocket)  # type: ignore[arg-type]

        for sequence in range(100):
            await hub.publish_pose("quest-test", {"type": "frame", "sequence": sequence})

        client = hub._pose_clients["quest-test"][websocket]  # type: ignore[index]
        assert websocket.accepted is True
        assert client.queue.qsize() <= 2
        assert client.queue._queue[-1][1]["sequence"] == 99
        await hub.remove_pose("quest-test", websocket)  # type: ignore[arg-type]

    anyio.run(scenario)


def test_udp_realtime_input_queue_is_bounded(tmp_path):
    service = UdpService(Settings(data_root=tmp_path), DeviceRegistry(), RealtimeHub())

    for sequence in range(1000):
        service.enqueue_packet(
            encode_packet({"type": "frame", "sequence": sequence}),
            ("192.168.1.42", 5005),
        )

    assert service._realtime_packets.qsize() == 256
    assert service._realtime_packets._queue[-1][0]["sequence"] == 999


def test_disconnected_websocket_is_removed_without_breaking_publish():
    async def scenario() -> None:
        hub = RealtimeHub()
        websocket = DisconnectedWebSocket()
        await hub.add_events(websocket)  # type: ignore[arg-type]
        await hub.publish_event({"type": "state_changed", "payload": {}})
        await asyncio.sleep(0)
        await asyncio.sleep(0)

        assert websocket not in hub._event_clients

    anyio.run(scenario)


def test_cached_preview_expires(monkeypatch):
    async def scenario() -> None:
        hub = RealtimeHub()
        await hub.publish_preview("quest-test", b"jpeg")
        captured_at = realtime_module.time.monotonic()
        assert await hub.latest_preview("quest-test") == b"jpeg"

        monkeypatch.setattr(realtime_module.time, "monotonic", lambda: captured_at + 6.0)
        assert await hub.latest_preview("quest-test") is None

    anyio.run(scenario)
