from __future__ import annotations

import json
import time
import uuid
from typing import Any


PROTOCOL_VERSION = 3
LEGACY_COMPATIBILITY_TOKEN = "trusted-lan"


def unix_ms() -> int:
    return int(time.time() * 1000)


def command_id() -> str:
    return uuid.uuid4().hex


def encode_packet(packet: dict[str, Any]) -> bytes:
    return json.dumps(packet, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def decode_packet(data: bytes) -> dict[str, Any]:
    value = json.loads(data.decode("utf-8"))
    if not isinstance(value, dict):
        raise ValueError("UDP packet must be a JSON object")
    return value


def discovery_packet(reply_port: int) -> dict[str, Any]:
    return {
        "type": "discover",
        "version": PROTOCOL_VERSION,
        "nonce": uuid.uuid4().hex,
        "reply_port": reply_port,
        "sent_at_unix_ms": unix_ms(),
    }


def pedal_packet(*, cmd_id: str, phase: str, progress: float = 0.0) -> dict[str, Any]:
    """Mirror the pedal into the headset.

    The teacher cannot hear the pedal click, so the press has to produce an
    immediate visual receipt in the HMD, and the long-press ring has to fill
    inside the headset rather than only in the operator's browser.
    """
    return {
        "type": "command",
        "version": PROTOCOL_VERSION,
        "command_id": cmd_id,
        "action": "pedal",
        "phase": phase,
        "progress": progress,
    }


def guidance_packet(*, cmd_id: str, enabled: bool) -> dict[str, Any]:
    return {
        "type": "command",
        "version": PROTOCOL_VERSION,
        "command_id": cmd_id,
        "action": "set_guidance",
        "enabled": enabled,
    }


def pair_packet(
    *,
    cmd_id: str,
    device_id: str,
    host_ip: str,
    http_port: int,
    pose_port: int,
    station_id: str,
    session_token: str,
    pairing_key: str,
) -> dict[str, Any]:
    return {
        "type": "pair",
        "version": PROTOCOL_VERSION,
        "command_id": cmd_id,
        "device_id": device_id,
        "host_ip": host_ip,
        "http_port": http_port,
        "pose_port": pose_port,
        "station_id": station_id,
        "session_token": session_token,
        "pairing_key": pairing_key,
    }
