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
