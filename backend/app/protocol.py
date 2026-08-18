from __future__ import annotations

import json
import time
import uuid
from typing import Any


PROTOCOL_VERSION = 2


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


def pair_packet(
    *,
    cmd_id: str,
    host_ip: str,
    http_port: int,
    pose_port: int,
    session_token: str,
) -> dict[str, Any]:
    return {
        "type": "pair",
        "version": PROTOCOL_VERSION,
        "command_id": cmd_id,
        "host_ip": host_ip,
        "http_port": http_port,
        "pose_port": pose_port,
        "session_token": session_token,
    }

