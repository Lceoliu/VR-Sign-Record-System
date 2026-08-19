from __future__ import annotations

import json
import socket
import sys
import threading
import unittest
import urllib.request
from pathlib import Path


CLIENT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CLIENT_ROOT))

import spectator_viewer as viewer  # noqa: E402


def make_packets(
    jpeg: bytes,
    *,
    frame_id: int = 7,
    flags: int = viewer.HEADSET_POV_FLAG,
    payload_size: int = 11,
) -> list[bytes]:
    chunks = [
        jpeg[offset : offset + payload_size]
        for offset in range(0, len(jpeg), payload_size)
    ]
    packets: list[bytes] = []
    for chunk_index, chunk in enumerate(chunks):
        header = viewer.HEADER.pack(
            viewer.MAGIC,
            viewer.PROTOCOL_VERSION,
            flags,
            viewer.HEADER_SIZE,
            frame_id,
            chunk_index,
            len(chunks),
            640,
            360,
            len(jpeg),
        )
        packets.append(header + chunk)
    return packets


class FrameAssemblerTests(unittest.TestCase):
    def test_reassembles_out_of_order_chunks(self) -> None:
        jpeg = b"\xff\xd8" + bytes(range(64)) + b"\xff\xd9"
        packets = make_packets(jpeg)
        assembler = viewer.FrameAssembler(frame_timeout=0.5)
        sender = ("192.168.1.50", 43123)
        completed = None

        for step, index in enumerate(reversed(range(len(packets)))):
            result = assembler.accept_datagram(
                packets[index],
                sender,
                now=0.01 * step,
            )
            if result is not None:
                completed = result

        self.assertIsNotNone(completed)
        assert completed is not None
        self.assertEqual(completed.jpeg, jpeg)
        self.assertEqual(completed.mode, "Headset POV")
        self.assertEqual(completed.width, 640)
        self.assertEqual(completed.height, 360)

    def test_discards_partial_frame_after_timeout(self) -> None:
        jpeg = b"\xff\xd8" + b"payload-for-timeout" + b"\xff\xd9"
        packets = make_packets(jpeg, payload_size=8)
        assembler = viewer.FrameAssembler(frame_timeout=0.25)
        sender = ("10.0.0.2", 50000)

        self.assertIsNone(
            assembler.accept_datagram(packets[0], sender, now=1.0)
        )
        assembler.expire(now=1.3)
        stats = assembler.snapshot(now=1.3)

        self.assertEqual(stats["frames_timed_out"], 1)
        self.assertEqual(stats["pending_frames"], 0)

    def test_rejects_mismatched_header_metadata(self) -> None:
        jpeg = b"\xff\xd8" + b"metadata" + b"\xff\xd9"
        packets = make_packets(jpeg, payload_size=6)
        assembler = viewer.FrameAssembler()
        sender = ("10.0.0.3", 50001)

        self.assertIsNone(
            assembler.accept_datagram(packets[0], sender, now=1.0)
        )
        values = list(viewer.HEADER.unpack_from(packets[1]))
        values[7] = 800
        malformed = viewer.HEADER.pack(*values) + packets[1][viewer.HEADER_SIZE :]
        self.assertIsNone(
            assembler.accept_datagram(malformed, sender, now=1.01)
        )

        stats = assembler.snapshot(now=1.01)
        self.assertEqual(stats["invalid_datagrams"], 1)
        self.assertEqual(stats["pending_frames"], 0)

    def test_accepts_frame_id_wraparound(self) -> None:
        jpeg = b"\xff\xd8wrap\xff\xd9"
        assembler = viewer.FrameAssembler()
        sender = ("10.0.0.4", 50002)

        first = make_packets(jpeg, frame_id=0xFFFFFFFF, payload_size=100)[0]
        wrapped = make_packets(jpeg, frame_id=0, payload_size=100)[0]

        self.assertIsNotNone(
            assembler.accept_datagram(first, sender, now=1.0)
        )
        self.assertIsNotNone(
            assembler.accept_datagram(wrapped, sender, now=1.01)
        )

    def test_rejects_chunks_exceeding_declared_frame_size(self) -> None:
        jpeg = b"\xff\xd8" + b"abcdefgh" + b"\xff\xd9"
        packets = make_packets(jpeg, payload_size=6)
        malformed_packets: list[bytes] = []

        for packet in packets:
            values = list(viewer.HEADER.unpack_from(packet))
            values[9] = 8
            malformed_packets.append(
                viewer.HEADER.pack(*values) + packet[viewer.HEADER_SIZE :]
            )

        assembler = viewer.FrameAssembler()
        sender = ("10.0.0.5", 50003)
        self.assertIsNone(
            assembler.accept_datagram(malformed_packets[0], sender, now=1.0)
        )
        self.assertIsNone(
            assembler.accept_datagram(malformed_packets[1], sender, now=1.01)
        )

        stats = assembler.snapshot(now=1.01)
        self.assertEqual(stats["invalid_datagrams"], 1)
        self.assertEqual(stats["pending_frames"], 0)

    def test_rejects_chunk_count_above_transport_limit(self) -> None:
        packet = viewer.HEADER.pack(
            viewer.MAGIC,
            viewer.PROTOCOL_VERSION,
            viewer.HEADSET_POV_FLAG,
            viewer.HEADER_SIZE,
            99,
            0,
            viewer.MAX_CHUNKS + 1,
            640,
            360,
            1,
        ) + b"x"
        assembler = viewer.FrameAssembler()

        self.assertIsNone(
            assembler.accept_datagram(packet, ("10.0.0.6", 50004), now=1.0)
        )
        self.assertEqual(assembler.snapshot(now=1.0)["invalid_datagrams"], 1)


class ReceiverIntegrationTests(unittest.TestCase):
    def test_udp_reassembly_is_visible_through_http_status(self) -> None:
        assembler = viewer.FrameAssembler(frame_timeout=0.5)
        store = viewer.LatestFrameStore()
        stopping = threading.Event()
        receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        receiver.bind(("127.0.0.1", 0))
        receiver.settimeout(0.05)
        receiver_thread = threading.Thread(
            target=viewer.receive_udp,
            args=(receiver, assembler, store, stopping, False),
            daemon=True,
        )
        server = viewer.SpectatorHttpServer(
            ("127.0.0.1", 0),
            store,
            assembler,
            stopping,
        )
        server_thread = threading.Thread(
            target=server.serve_forever,
            kwargs={"poll_interval": 0.01},
            daemon=True,
        )
        receiver_thread.start()
        server_thread.start()

        try:
            jpeg = b"\xff\xd8integration-frame\xff\xd9"
            packets = make_packets(jpeg, frame_id=123, payload_size=7)
            sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

            try:
                for packet in reversed(packets):
                    sender.sendto(packet, receiver.getsockname())
            finally:
                sender.close()

            generation, frame = store.wait_for_frame(0, timeout=1.0)
            self.assertEqual(generation, 1)
            self.assertIsNotNone(frame)

            with urllib.request.urlopen(
                f"http://127.0.0.1:{server.server_port}/status.json",
                timeout=1.0,
            ) as response:
                status = json.loads(response.read())

            self.assertEqual(status["frame_id"], 123)
            self.assertEqual(status["mode"], "Headset POV")
            self.assertEqual(status["width"], 640)
            self.assertEqual(status["height"], 360)
            self.assertEqual(status["frames_completed"], 1)
            self.assertEqual(status["invalid_datagrams"], 0)
        finally:
            stopping.set()
            server.shutdown()
            server.server_close()
            receiver.close()
            server_thread.join(timeout=1.0)
            receiver_thread.join(timeout=1.0)


if __name__ == "__main__":
    unittest.main()
