from __future__ import annotations

import asyncio

import anyio

from app.config import Settings
from app.device_registry import DeviceRegistry
from app.protocol import encode_packet, pair_packet
from app.realtime import RealtimeHub
from app.udp_service import UdpService


class FakeDatagramTransport:
    def __init__(self) -> None:
        self.sent: list[tuple[bytes, tuple[str, int]]] = []

    def sendto(self, data: bytes, address: tuple[str, int]) -> None:
        self.sent.append((data, address))


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
        transport = FakeDatagramTransport()
        service.transport = transport  # type: ignore[assignment]
        packet = pair_packet(
            cmd_id="pair-command",
            host_ip="192.168.1.10",
            http_port=8000,
            pose_port=5005,
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

    anyio.run(scenario)
