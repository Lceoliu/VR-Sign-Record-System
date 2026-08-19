"""Receive SignVR spectator JPEG frames over UDP and serve them to a browser."""

from __future__ import annotations

import argparse
import json
import socket
import struct
import threading
import time
import webbrowser
from collections import deque
from dataclasses import dataclass, field
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any


MAGIC = b"SVR1"
PROTOCOL_VERSION = 1
HEADER = struct.Struct("<4sBBHIHHHHI")
HEADER_SIZE = HEADER.size
HEADSET_POV_FLAG = 1 << 0
FLIP_VERTICAL_FLAG = 1 << 1
MAX_FRAME_BYTES = 8 * 1024 * 1024
MAX_CHUNKS = 8192
MAX_PENDING_FRAMES = 16
MJPEG_BOUNDARY = b"signvr-frame"


def local_ipv4_addresses() -> list[str]:
    """Return likely LAN addresses to enter in the Unity component."""
    addresses: set[str] = set()

    try:
        for result in socket.getaddrinfo(
            socket.gethostname(),
            None,
            socket.AF_INET,
            socket.SOCK_DGRAM,
        ):
            address = result[4][0]
            if not address.startswith("127."):
                addresses.add(address)
    except socket.gaierror:
        pass

    try:
        probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        probe.connect(("192.0.2.1", 9))
        address = probe.getsockname()[0]
        if not address.startswith("127."):
            addresses.add(address)
        probe.close()
    except OSError:
        pass

    return sorted(addresses)


@dataclass(frozen=True)
class CompletedFrame:
    frame_id: int
    flags: int
    width: int
    height: int
    jpeg: bytes
    sender: tuple[str, int]
    received_at: float

    @property
    def mode(self) -> str:
        return "Headset POV" if self.flags & HEADSET_POV_FLAG else "Fixed"

    @property
    def flip_vertical(self) -> bool:
        return bool(self.flags & FLIP_VERTICAL_FLAG)


@dataclass
class PendingFrame:
    frame_id: int
    flags: int
    width: int
    height: int
    frame_size: int
    chunk_count: int
    first_seen: float
    last_seen: float
    chunks: dict[int, bytes] = field(default_factory=dict)
    payload_bytes: int = 0


class FrameAssembler:
    """Reassemble out-of-order UDP chunks and discard incomplete frames."""

    def __init__(self, frame_timeout: float = 0.75) -> None:
        if frame_timeout <= 0:
            raise ValueError("frame_timeout must be positive")

        self.frame_timeout = frame_timeout
        self.pending: dict[tuple[str, int, int], PendingFrame] = {}
        self.last_completed: dict[tuple[str, int], tuple[int, float]] = {}
        self.completed_times: deque[float] = deque(maxlen=240)
        self.lock = threading.Lock()
        self.datagrams_received = 0
        self.bytes_received = 0
        self.invalid_datagrams = 0
        self.invalid_frames = 0
        self.frames_completed = 0
        self.frames_timed_out = 0
        self.frames_superseded = 0
        self.duplicate_chunks = 0
        self.last_sender: tuple[str, int] | None = None
        self.last_datagram_time: float | None = None

    def accept_datagram(
        self,
        datagram: bytes,
        sender: tuple[str, int],
        now: float | None = None,
    ) -> CompletedFrame | None:
        now = time.monotonic() if now is None else now

        with self.lock:
            self.datagrams_received += 1
            self.bytes_received += len(datagram)
            self.last_sender = sender
            self.last_datagram_time = now
            self._expire_locked(now)

            parsed = self._parse_header_locked(datagram)
            if parsed is None:
                return None

            (
                flags,
                frame_id,
                chunk_index,
                chunk_count,
                width,
                height,
                frame_size,
            ) = parsed
            payload = datagram[HEADER_SIZE:]
            key = (sender[0], sender[1], frame_id)
            pending = self.pending.get(key)

            if pending is None:
                self._make_room_locked()
                pending = PendingFrame(
                    frame_id=frame_id,
                    flags=flags,
                    width=width,
                    height=height,
                    frame_size=frame_size,
                    chunk_count=chunk_count,
                    first_seen=now,
                    last_seen=now,
                )
                self.pending[key] = pending
            elif (
                pending.flags != flags
                or pending.width != width
                or pending.height != height
                or pending.frame_size != frame_size
                or pending.chunk_count != chunk_count
            ):
                self.invalid_datagrams += 1
                del self.pending[key]
                return None

            existing = pending.chunks.get(chunk_index)
            if existing is not None:
                if existing == payload:
                    self.duplicate_chunks += 1
                else:
                    self.invalid_datagrams += 1
                    del self.pending[key]
                return None

            payload_bytes = pending.payload_bytes + len(payload)
            if payload_bytes > pending.frame_size:
                self.invalid_datagrams += 1
                del self.pending[key]
                return None

            pending.chunks[chunk_index] = payload
            pending.payload_bytes = payload_bytes
            pending.last_seen = now

            if len(pending.chunks) != pending.chunk_count:
                return None

            jpeg = b"".join(
                pending.chunks[index]
                for index in range(pending.chunk_count)
            )
            del self.pending[key]

            if (
                len(jpeg) != pending.frame_size
                or not jpeg.startswith(b"\xff\xd8")
                or not jpeg.endswith(b"\xff\xd9")
            ):
                self.invalid_frames += 1
                return None

            sender_key = (sender[0], sender[1])
            previous = self.last_completed.get(sender_key)
            if previous is not None:
                previous_id, previous_time = previous
                recently_active = now - previous_time <= self.frame_timeout * 2
                if recently_active and not _is_newer_frame(frame_id, previous_id):
                    self.frames_superseded += 1
                    return None

            self.last_completed[sender_key] = (frame_id, now)
            self._discard_older_pending_locked(sender, frame_id)
            self.frames_completed += 1
            self.completed_times.append(now)
            return CompletedFrame(
                frame_id=frame_id,
                flags=flags,
                width=width,
                height=height,
                jpeg=jpeg,
                sender=sender,
                received_at=now,
            )

    def expire(self, now: float | None = None) -> None:
        now = time.monotonic() if now is None else now
        with self.lock:
            self._expire_locked(now)

    def snapshot(self, now: float | None = None) -> dict[str, Any]:
        now = time.monotonic() if now is None else now
        with self.lock:
            self._expire_locked(now)
            while self.completed_times and now - self.completed_times[0] > 2.0:
                self.completed_times.popleft()

            fps = len(self.completed_times) / 2.0
            sender = (
                f"{self.last_sender[0]}:{self.last_sender[1]}"
                if self.last_sender is not None
                else None
            )
            age = (
                now - self.last_datagram_time
                if self.last_datagram_time is not None
                else None
            )
            return {
                "datagrams_received": self.datagrams_received,
                "bytes_received": self.bytes_received,
                "invalid_datagrams": self.invalid_datagrams,
                "invalid_frames": self.invalid_frames,
                "frames_completed": self.frames_completed,
                "frames_timed_out": self.frames_timed_out,
                "frames_superseded": self.frames_superseded,
                "duplicate_chunks": self.duplicate_chunks,
                "pending_frames": len(self.pending),
                "receive_fps": fps,
                "last_sender": sender,
                "last_datagram_age": age,
            }

    def _parse_header_locked(
        self,
        datagram: bytes,
    ) -> tuple[int, int, int, int, int, int, int] | None:
        if len(datagram) <= HEADER_SIZE:
            self.invalid_datagrams += 1
            return None

        try:
            (
                magic,
                version,
                flags,
                header_size,
                frame_id,
                chunk_index,
                chunk_count,
                width,
                height,
                frame_size,
            ) = HEADER.unpack_from(datagram)
        except struct.error:
            self.invalid_datagrams += 1
            return None

        if (
            magic != MAGIC
            or version != PROTOCOL_VERSION
            or header_size != HEADER_SIZE
            or chunk_count == 0
            or chunk_count > MAX_CHUNKS
            or chunk_index >= chunk_count
            or width == 0
            or height == 0
            or frame_size == 0
            or frame_size > MAX_FRAME_BYTES
            or len(datagram) - HEADER_SIZE > frame_size
        ):
            self.invalid_datagrams += 1
            return None

        return (
            flags,
            frame_id,
            chunk_index,
            chunk_count,
            width,
            height,
            frame_size,
        )

    def _expire_locked(self, now: float) -> None:
        expired = [
            key
            for key, pending in self.pending.items()
            if now - pending.last_seen > self.frame_timeout
        ]
        for key in expired:
            del self.pending[key]
            self.frames_timed_out += 1

    def _make_room_locked(self) -> None:
        while len(self.pending) >= MAX_PENDING_FRAMES:
            oldest_key = min(
                self.pending,
                key=lambda key: self.pending[key].first_seen,
            )
            del self.pending[oldest_key]
            self.frames_superseded += 1

    def _discard_older_pending_locked(
        self,
        sender: tuple[str, int],
        completed_id: int,
    ) -> None:
        obsolete = [
            key
            for key in self.pending
            if key[:2] == sender
            and not _is_newer_frame(key[2], completed_id)
        ]
        for key in obsolete:
            del self.pending[key]
            self.frames_superseded += 1


class LatestFrameStore:
    def __init__(self) -> None:
        self.condition = threading.Condition()
        self.generation = 0
        self.latest: CompletedFrame | None = None

    def update(self, frame: CompletedFrame) -> None:
        with self.condition:
            self.generation += 1
            self.latest = frame
            self.condition.notify_all()

    def wait_for_frame(
        self,
        after_generation: int,
        timeout: float,
    ) -> tuple[int, CompletedFrame | None]:
        with self.condition:
            if self.generation == after_generation:
                self.condition.wait(timeout)
            return self.generation, self.latest

    def snapshot(self) -> tuple[int, CompletedFrame | None]:
        with self.condition:
            return self.generation, self.latest


def _is_newer_frame(candidate: int, previous: int) -> bool:
    difference = (candidate - previous) & 0xFFFFFFFF
    return 0 < difference < 0x80000000


def receive_udp(
    receiver: socket.socket,
    assembler: FrameAssembler,
    store: LatestFrameStore,
    stopping: threading.Event,
    verbose: bool,
) -> None:
    next_log = time.monotonic() + 5.0

    while not stopping.is_set():
        try:
            datagram, sender = receiver.recvfrom(65535)
        except socket.timeout:
            assembler.expire()
            continue
        except OSError:
            break

        frame = assembler.accept_datagram(datagram, sender)
        if frame is not None:
            store.update(frame)
            if verbose:
                print(
                    "[FRAME] "
                    f"id={frame.frame_id}, mode={frame.mode}, "
                    f"size={frame.width}x{frame.height}, "
                    f"jpeg={len(frame.jpeg)} bytes"
                )

        now = time.monotonic()
        if verbose and now >= next_log:
            stats = assembler.snapshot(now)
            print(
                "[RECEIVER] "
                f"fps={stats['receive_fps']:.1f}, "
                f"frames={stats['frames_completed']}, "
                f"timeouts={stats['frames_timed_out']}, "
                f"invalid={stats['invalid_datagrams']}"
            )
            next_log = now + 5.0


class SpectatorHttpServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(
        self,
        address: tuple[str, int],
        store: LatestFrameStore,
        assembler: FrameAssembler,
        stopping: threading.Event,
    ) -> None:
        self.store = store
        self.assembler = assembler
        self.stopping = stopping
        super().__init__(address, SpectatorRequestHandler)


class SpectatorRequestHandler(BaseHTTPRequestHandler):
    server: SpectatorHttpServer

    def do_GET(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        path = self.path.split("?", 1)[0]
        if path == "/":
            self._send_bytes("text/html; charset=utf-8", VIEWER_HTML)
        elif path == "/stream.mjpg":
            self._stream_mjpeg()
        elif path == "/snapshot.jpg":
            self._send_snapshot()
        elif path == "/status.json":
            self._send_status()
        elif path == "/favicon.ico":
            self.send_response(HTTPStatus.NO_CONTENT)
            self.end_headers()
        else:
            self.send_error(HTTPStatus.NOT_FOUND)

    def log_message(self, format: str, *args: Any) -> None:
        return

    def _send_bytes(self, content_type: str, payload: bytes) -> None:
        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(payload)

    def _send_snapshot(self) -> None:
        _, frame = self.server.store.snapshot()
        if frame is None:
            self.send_error(HTTPStatus.SERVICE_UNAVAILABLE, "No frame received")
            return
        self._send_bytes("image/jpeg", frame.jpeg)

    def _send_status(self) -> None:
        now = time.monotonic()
        stats = self.server.assembler.snapshot(now)
        generation, frame = self.server.store.snapshot()
        stats.update(
            {
                "generation": generation,
                "streaming": frame is not None
                and now - frame.received_at < 2.0,
                "frame_id": frame.frame_id if frame else None,
                "mode": frame.mode if frame else None,
                "width": frame.width if frame else None,
                "height": frame.height if frame else None,
                "jpeg_bytes": len(frame.jpeg) if frame else None,
                "frame_age": now - frame.received_at if frame else None,
                "flip_vertical": frame.flip_vertical if frame else False,
            }
        )
        payload = json.dumps(stats, separators=(",", ":")).encode("utf-8")
        self._send_bytes("application/json; charset=utf-8", payload)

    def _stream_mjpeg(self) -> None:
        self.send_response(HTTPStatus.OK)
        self.send_header(
            "Content-Type",
            "multipart/x-mixed-replace; boundary="
            + MJPEG_BOUNDARY.decode("ascii"),
        )
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        self.send_header("Pragma", "no-cache")
        self.end_headers()

        generation = 0
        try:
            while not self.server.stopping.is_set():
                current, frame = self.server.store.wait_for_frame(
                    generation,
                    timeout=1.0,
                )
                if frame is None or current == generation:
                    continue
                generation = current
                self.wfile.write(b"--" + MJPEG_BOUNDARY + b"\r\n")
                self.wfile.write(b"Content-Type: image/jpeg\r\n")
                self.wfile.write(
                    f"Content-Length: {len(frame.jpeg)}\r\n\r\n".encode(
                        "ascii"
                    )
                )
                self.wfile.write(frame.jpeg)
                self.wfile.write(b"\r\n")
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            return


VIEWER_HTML = b"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>SignVR Spectator</title>
  <style>
    * { box-sizing: border-box; }
    html, body { width: 100%; height: 100%; margin: 0; }
    body {
      display: grid;
      grid-template-rows: 48px minmax(0, 1fr) 34px;
      background: #111313;
      color: #f1f3f2;
      font: 14px system-ui, sans-serif;
    }
    header, footer {
      display: flex;
      align-items: center;
      gap: 18px;
      padding: 0 16px;
      background: #1c1f1e;
      border-color: #353a38;
    }
    header { border-bottom: 1px solid #353a38; }
    footer { border-top: 1px solid #353a38; color: #aeb6b2; }
    h1 { margin: 0 auto 0 0; font-size: 16px; font-weight: 650; }
    main { min-height: 0; display: grid; place-items: center; overflow: hidden; }
    img { width: 100%; height: 100%; object-fit: contain; }
    img.flip { transform: scaleY(-1); }
    .state { color: #ffb454; }
    .state.live { color: #55d68b; }
    @media (max-width: 620px) {
      header { gap: 10px; padding: 0 10px; }
      header span:nth-of-type(2) { display: none; }
      footer { padding: 0 10px; font-size: 12px; }
    }
  </style>
</head>
<body>
  <header>
    <h1>SignVR Spectator</h1>
    <span id="state" class="state">Waiting</span>
    <span id="mode">No source</span>
    <span id="rate">0.0 fps</span>
  </header>
  <main><img id="video" src="/stream.mjpg" alt="Live spectator view"></main>
  <footer><span id="detail">UDP receiver ready</span></footer>
  <script>
    const state = document.querySelector('#state');
    const mode = document.querySelector('#mode');
    const rate = document.querySelector('#rate');
    const detail = document.querySelector('#detail');
    const video = document.querySelector('#video');
    async function refresh() {
      try {
        const response = await fetch('/status.json', { cache: 'no-store' });
        const data = await response.json();
        state.textContent = data.streaming ? 'Live' : 'Waiting';
        state.classList.toggle('live', data.streaming);
        mode.textContent = data.mode || 'No source';
        rate.textContent = `${data.receive_fps.toFixed(1)} fps`;
        video.classList.toggle('flip', data.flip_vertical);
        const size = data.width ? `${data.width} x ${data.height}` : 'no frame';
        const loss = `${data.frames_timed_out} timed out`;
        detail.textContent = `${size} | ${data.last_sender || 'no sender'} | ${loss}`;
      } catch (_) {
        state.textContent = 'Receiver unavailable';
        state.classList.remove('live');
      }
    }
    refresh();
    setInterval(refresh, 500);
  </script>
</body>
</html>
"""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Receive the SignVR spectator UDP stream in a browser."
    )
    parser.add_argument("--udp-host", default="0.0.0.0")
    parser.add_argument("--udp-port", type=int, default=5006)
    parser.add_argument("--web-host", default="127.0.0.1")
    parser.add_argument("--web-port", type=int, default=8090)
    parser.add_argument("--frame-timeout", type=float, default=0.75)
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument(
        "--open-browser",
        action="store_true",
        help="Open the local viewer page after the receiver is ready.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    assembler = FrameAssembler(frame_timeout=args.frame_timeout)
    store = LatestFrameStore()
    stopping = threading.Event()
    receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    receiver.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    receiver.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 4 * 1024 * 1024)

    try:
        receiver.bind((args.udp_host, args.udp_port))
    except OSError as error:
        receiver.close()
        raise SystemExit(
            f"Cannot bind UDP {args.udp_host}:{args.udp_port}: {error}"
        ) from error

    receiver.settimeout(0.1)
    receiver_thread = threading.Thread(
        target=receive_udp,
        args=(receiver, assembler, store, stopping, args.verbose),
        daemon=True,
        name="SignVR spectator UDP receiver",
    )
    receiver_thread.start()

    try:
        server = SpectatorHttpServer(
            (args.web_host, args.web_port),
            store,
            assembler,
            stopping,
        )
    except OSError as error:
        stopping.set()
        receiver.close()
        receiver_thread.join(timeout=1.0)
        raise SystemExit(
            f"Cannot bind HTTP {args.web_host}:{args.web_port}: {error}"
        ) from error

    print(
        f"[SPECTATOR READY] UDP {args.udp_host}:{args.udp_port}; "
        f"open http://127.0.0.1:{args.web_port}"
    )
    if args.open_browser:
        webbrowser.open(f"http://127.0.0.1:{args.web_port}")
    addresses = local_ipv4_addresses()
    print(
        "[NETWORK] Set Unity Remote Host to: "
        + (", ".join(addresses) or "no LAN IPv4 detected")
    )

    try:
        server.serve_forever(poll_interval=0.2)
    except KeyboardInterrupt:
        print("[SPECTATOR STOPPED]")
    finally:
        stopping.set()
        server.server_close()
        receiver.close()
        receiver_thread.join(timeout=1.0)


if __name__ == "__main__":
    main()
