"""Receive Meta Quest body tracking over UDP and show it in a web browser."""

from __future__ import annotations

import argparse
import json
import socket
import time
from collections import Counter
from typing import Any

import numpy as np
import viser


PROTOCOL_VERSION = 1
JOINT_COLOR = (255, 166, 54)
BONE_COLOR = (65, 184, 255)


def local_ipv4_addresses() -> list[str]:
    """Return likely LAN addresses to enter in Unity for unicast testing."""
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

    # Connecting a UDP socket does not send a packet. It asks the OS which
    # interface it would use, which often finds Wi-Fi addresses omitted above.
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


class SkeletonViewer:
    def __init__(self, server: viser.ViserServer) -> None:
        self.server = server
        self.parent_indices: np.ndarray | None = None
        self.joint_names: list[str] = []
        self.point_handle: Any | None = None
        self.line_handle: Any | None = None
        self.skeleton_signature: tuple[Any, ...] | None = None

    def set_skeleton(self, packet: dict[str, Any]) -> bool:
        parents = np.asarray(packet.get("parent_indices", []), dtype=np.int32)
        names = packet.get("joint_names", [])

        if parents.ndim != 1 or not isinstance(names, list):
            raise ValueError("invalid skeleton metadata")

        signature = (
            packet.get("skeleton_type", "unknown"),
            tuple(parents.tolist()),
            tuple(str(name) for name in names),
        )
        changed = signature != self.skeleton_signature

        self.parent_indices = parents
        self.joint_names = [str(name) for name in names]
        self.skeleton_signature = signature

        if changed:
            print(
                "[SKELETON RECEIVED] "
                f"type={packet.get('skeleton_type', 'unknown')}, "
                f"joints={parents.size}"
            )

        return changed

    def update(self, packet: dict[str, Any]) -> tuple[int, int] | None:
        if self.parent_indices is None:
            return None

        joint_count = int(packet.get("joint_count", 0))
        positions = np.asarray(packet.get("positions", []), dtype=np.float32)
        valid = np.asarray(packet.get("valid", []), dtype=np.bool_)

        if (
            joint_count <= 0
            or positions.size != joint_count * 3
            or valid.size != joint_count
            or self.parent_indices.size != joint_count
        ):
            raise ValueError(
                "frame does not match skeleton metadata "
                f"(frame={joint_count}, skeleton={self.parent_indices.size})"
            )

        positions = positions.reshape(joint_count, 3)
        visible_points = positions[valid]

        bone_pairs: list[np.ndarray] = []
        for child_index, parent_index in enumerate(self.parent_indices):
            if (
                0 <= parent_index < joint_count
                and valid[parent_index]
                and valid[child_index]
            ):
                bone_pairs.append(
                    np.stack(
                        (positions[parent_index], positions[child_index]),
                        axis=0,
                    )
                )

        if bone_pairs:
            segments = np.stack(bone_pairs, axis=0).astype(np.float32)
        else:
            segments = np.empty((0, 2, 3), dtype=np.float32)

        if self.point_handle is None:
            self.point_handle = self.server.scene.add_point_cloud(
                "/player/joints",
                points=visible_points,
                colors=JOINT_COLOR,
                point_size=0.025,
                point_shape="circle",
                precision="float32",
            )
        else:
            self.point_handle.points = visible_points

        if self.line_handle is None:
            self.line_handle = self.server.scene.add_line_segments(
                "/player/bones",
                points=segments,
                colors=BONE_COLOR,
                # line_width is supported by the Viser 1.0.x release in this
                # project. Newer development docs call this "thickness".
                line_width=4.0,
            )
        else:
            self.line_handle.points = segments

        return int(valid.sum()), int(segments.shape[0])


class Diagnostics:
    def __init__(
        self,
        server: viser.ViserServer,
        udp_binding: str,
        local_addresses: list[str],
    ) -> None:
        self.udp_binding = udp_binding
        self.local_addresses = local_addresses
        self.packet_counts: Counter[str] = Counter()
        self.bytes_received = 0
        self.invalid_packets = 0
        self.frames_received = 0
        self.frames_in_window = 0
        self.current_fps = 0.0
        self.sequence_gaps = 0
        self.out_of_order_frames = 0
        self.last_sequence: int | None = None
        self.last_packet_time: float | None = None
        self.last_frame_time: float | None = None
        self.last_sender: tuple[str, int] | None = None
        self.last_status: dict[str, Any] = {}
        self.last_confidence = 0.0
        self.valid_joints = 0
        self.joint_count = 0
        self.visible_bones = 0
        self.skeleton_received = False
        self.frames_before_skeleton = 0
        self.window_start = time.monotonic()
        self.next_gui_update = 0.0
        self.last_wait_log = 0.0
        self.last_stale_log = 0.0
        self.last_status_signature: tuple[Any, ...] | None = None
        self.first_packet_types: set[str] = set()

        server.gui.set_panel_label("Motion Stream Diagnostics")
        self.status_handle = server.gui.add_markdown(
            "### Motion Stream Diagnostics\nWaiting for UDP packets..."
        )

    def note_packet(
        self,
        packet_type: str,
        payload_size: int,
        sender: tuple[str, int],
        now: float,
        verbose: bool,
    ) -> None:
        self.packet_counts[packet_type] += 1
        self.bytes_received += payload_size
        self.last_packet_time = now
        self.last_sender = sender

        if packet_type not in self.first_packet_types:
            self.first_packet_types.add(packet_type)
            print(
                f"[UDP RECEIVED] first '{packet_type}' packet from "
                f"{sender[0]}:{sender[1]} ({payload_size} bytes)"
            )
        elif verbose and packet_type != "frame":
            print(
                f"[UDP RECEIVED] type={packet_type}, "
                f"from={sender[0]}:{sender[1]}, bytes={payload_size}"
            )

    def note_status(self, packet: dict[str, Any]) -> None:
        self.last_status = packet
        signature = (
            packet.get("socket_open"),
            packet.get("broadcast"),
            packet.get("body_state_available"),
            packet.get("pose_valid"),
            packet.get("skeleton_metadata_available"),
            packet.get("last_error"),
        )

        if signature != self.last_status_signature:
            self.last_status_signature = signature
            print(
                "[UNITY STATUS] "
                f"socket={packet.get('socket_open')}, "
                f"broadcast={packet.get('broadcast')}, "
                f"destination={packet.get('destination')}, "
                f"bodyState={packet.get('body_state_available')}, "
                f"poseValid={packet.get('pose_valid')}, "
                f"validJoints={packet.get('valid_joint_count', 0)}/"
                f"{packet.get('joint_count', 0)}, "
                f"error={packet.get('last_error') or 'none'}"
            )

    def note_frame(
        self,
        packet: dict[str, Any],
        rendered: tuple[int, int] | None,
        now: float,
        verbose: bool,
    ) -> None:
        self.frames_received += 1
        self.frames_in_window += 1
        self.last_frame_time = now
        self.last_confidence = float(packet.get("confidence", 0.0))
        self.joint_count = int(packet.get("joint_count", 0))

        sequence = int(packet.get("sequence", -1))
        if self.last_sequence is not None:
            if sequence > self.last_sequence + 1:
                self.sequence_gaps += sequence - self.last_sequence - 1
            elif sequence <= self.last_sequence:
                # A large backwards jump normally means the Unity app restarted.
                if self.last_sequence - sequence > 100:
                    self.last_sequence = None
                else:
                    self.out_of_order_frames += 1
        self.last_sequence = sequence

        if rendered is None:
            self.frames_before_skeleton += 1
        else:
            self.valid_joints, self.visible_bones = rendered

        if verbose and self.frames_received % 30 == 0:
            print(
                f"[FRAME] sequence={sequence}, "
                f"confidence={self.last_confidence:.2f}, "
                f"validJoints={self.valid_joints}/{self.joint_count}"
            )

    def note_invalid_packet(self) -> None:
        self.invalid_packets += 1

    def refresh(self, now: float) -> None:
        elapsed = now - self.window_start
        if elapsed >= 2.0:
            self.current_fps = self.frames_in_window / elapsed
            self.frames_in_window = 0
            self.window_start = now

            if self.last_packet_time is not None:
                print(
                    "[CLIENT DIAGNOSTICS] "
                    f"receiveRate={self.current_fps:.1f} fps, "
                    f"frames={self.frames_received}, "
                    f"sequenceGaps={self.sequence_gaps}, "
                    f"invalidPackets={self.invalid_packets}, "
                    f"lastPacketAge={now - self.last_packet_time:.1f}s"
                )

        if now >= self.next_gui_update:
            self.status_handle.content = self._markdown(now)
            self.next_gui_update = now + 0.25

        if self.last_packet_time is None:
            if now - self.last_wait_log >= 5.0:
                self.last_wait_log = now
                addresses = ", ".join(self.local_addresses) or "not detected"
                print(
                    "[NO UDP PACKETS] Unity now sends a status heartbeat even "
                    "without body tracking. Check the same Wi-Fi, Windows "
                    f"firewall, UDP binding {self.udp_binding}, or set Unity "
                    "Remote Host to this "
                    f"PC address: {addresses}"
                )
        elif now - self.last_packet_time >= 3.0:
            if now - self.last_stale_log >= 5.0:
                self.last_stale_log = now
                print(
                    "[UDP STALE] Packets were received previously but have "
                    f"stopped for {now - self.last_packet_time:.1f}s."
                )

    def _markdown(self, now: float) -> str:
        if self.last_packet_time is None:
            state = "🔴 **No UDP packet received**"
            packet_age = "never"
        else:
            age = now - self.last_packet_time
            packet_age = f"{age:.1f} s"
            if age < 2.0:
                state = "🟢 **UDP receiving**"
            elif age < 5.0:
                state = "🟠 **UDP delayed**"
            else:
                state = "🔴 **UDP stopped**"

        sender = (
            f"{self.last_sender[0]}:{self.last_sender[1]}"
            if self.last_sender is not None
            else "unknown"
        )
        addresses = ", ".join(self.local_addresses) or "not detected"
        status = self.last_status
        body_state = status.get("body_state_available", "unknown")
        pose_valid = status.get("pose_valid", "unknown")
        destination = status.get("destination", "unknown")
        mode = "broadcast" if status.get("broadcast") else "unicast/unknown"
        unity_error = status.get("last_error") or "none"

        return f"""### Motion Stream Diagnostics
{state}

| Client | Value |
|---|---|
| UDP bind | `{self.udp_binding}` |
| PC IPv4 candidates | `{addresses}` |
| Quest sender | `{sender}` |
| Last packet age | {packet_age} |
| Receive rate | {self.current_fps:.1f} frame/s |
| Packets | {sum(self.packet_counts.values())} ({dict(self.packet_counts)}) |
| Bytes | {self.bytes_received} |
| Sequence gaps | {self.sequence_gaps} |
| Invalid packets | {self.invalid_packets} |

#### Unity sender report

| Unity | Value |
|---|---|
| Device | `{status.get('device_model', 'unknown')}` |
| Destination | `{destination}` ({mode}) |
| Socket open | {status.get('socket_open', 'unknown')} |
| UDP packets accepted locally | {status.get('packets_sent', 'unknown')} |
| Motion frames accepted locally | {status.get('frames_sent', 'unknown')} |
| Send failures | {status.get('send_failures', 'unknown')} |
| BodyState available | {body_state} |
| Pose valid | {pose_valid} |
| Valid joints | {status.get('valid_joint_count', 0)}/{status.get('joint_count', 0)} |
| Confidence | {float(status.get('confidence', 0.0)):.2f} |
| Skeleton received by client | {self.skeleton_received} |
| Client-rendered joints/bones | {self.valid_joints}/{self.visible_bones} |
| Last Unity error | `{unity_error}` |
"""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Live 3D viewer for the Quest body-motion UDP stream."
    )
    parser.add_argument(
        "--udp-host",
        default="0.0.0.0",
        help="Network interface on which to receive motion data.",
    )
    parser.add_argument(
        "--udp-port",
        type=int,
        default=5005,
        help="UDP port used by the Unity streamer.",
    )
    parser.add_argument(
        "--web-host",
        default="0.0.0.0",
        help="Network interface for the browser viewer.",
    )
    parser.add_argument(
        "--web-port",
        type=int,
        default=8080,
        help="Browser viewer port.",
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Print additional packet and frame diagnostics.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    pc_addresses = local_ipv4_addresses()

    server = viser.ViserServer(host=args.web_host, port=args.web_port)
    server.scene.set_up_direction("+y")
    viewer = SkeletonViewer(server)

    receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    receiver.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    receiver.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 4 * 1024 * 1024)

    try:
        receiver.bind((args.udp_host, args.udp_port))
    except OSError as error:
        receiver.close()
        raise SystemExit(
            f"[CLIENT START FAILED] Cannot bind UDP "
            f"{args.udp_host}:{args.udp_port}: {error}"
        ) from error

    receiver.settimeout(0.2)
    binding = f"{args.udp_host}:{args.udp_port}"
    diagnostics = Diagnostics(server, binding, pc_addresses)
    receive_buffer = receiver.getsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF)

    print(f"[CLIENT READY] Listening on UDP {binding}")
    print(f"[CLIENT READY] UDP receive buffer: {receive_buffer} bytes")
    print(
        "[NETWORK] PC IPv4 candidates for Unity Remote Host: "
        + (", ".join(pc_addresses) or "not detected")
    )
    print(f"[WEB VIEWER] Open http://127.0.0.1:{args.web_port}")
    print(
        "[CHECK] A 'status' packet should arrive once per second even if "
        "Meta body tracking is unavailable."
    )

    try:
        while True:
            now = time.monotonic()

            try:
                payload, sender = receiver.recvfrom(65535)
            except socket.timeout:
                diagnostics.refresh(time.monotonic())
                continue
            except OSError as error:
                print(f"[UDP RECEIVE ERROR] {error}")
                diagnostics.refresh(time.monotonic())
                continue

            try:
                packet = json.loads(payload.decode("utf-8"))

                if packet.get("version") != PROTOCOL_VERSION:
                    raise ValueError(
                        "unsupported protocol version "
                        f"{packet.get('version')}"
                    )

                packet_type = str(packet.get("type", "unknown"))
                diagnostics.note_packet(
                    packet_type,
                    len(payload),
                    sender,
                    now,
                    args.verbose,
                )

                if packet_type == "status":
                    diagnostics.note_status(packet)
                elif packet_type == "skeleton":
                    viewer.set_skeleton(packet)
                    diagnostics.skeleton_received = True
                elif packet_type == "frame":
                    rendered = viewer.update(packet)
                    diagnostics.note_frame(
                        packet,
                        rendered,
                        now,
                        args.verbose,
                    )
                else:
                    print(f"[UNKNOWN PACKET] type={packet_type}")
            except (UnicodeDecodeError, json.JSONDecodeError, ValueError) as error:
                diagnostics.note_invalid_packet()
                print(f"[INVALID PACKET] from {sender}: {error}")

            diagnostics.refresh(time.monotonic())
    except KeyboardInterrupt:
        print("[CLIENT STOPPED] Viewer stopped by user.")
    finally:
        receiver.close()


if __name__ == "__main__":
    main()
