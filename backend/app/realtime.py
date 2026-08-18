from __future__ import annotations

import asyncio
from collections import defaultdict

from fastapi import WebSocket


class RealtimeHub:
    def __init__(self) -> None:
        self._event_clients: set[WebSocket] = set()
        self._preview_clients: dict[str, set[WebSocket]] = defaultdict(set)
        self._latest_frames: dict[str, bytes] = {}
        self._pose_clients: dict[str, set[WebSocket]] = defaultdict(set)
        self._latest_skeletons: dict[str, dict] = {}
        self._lock = asyncio.Lock()

    async def add_events(self, websocket: WebSocket) -> None:
        await websocket.accept()
        async with self._lock:
            self._event_clients.add(websocket)

    async def remove_events(self, websocket: WebSocket) -> None:
        async with self._lock:
            self._event_clients.discard(websocket)

    async def add_preview(self, device_id: str, websocket: WebSocket) -> None:
        await websocket.accept()
        async with self._lock:
            self._preview_clients[device_id].add(websocket)
            frame = self._latest_frames.get(device_id)
        if frame:
            await websocket.send_bytes(frame)

    async def remove_preview(self, device_id: str, websocket: WebSocket) -> None:
        async with self._lock:
            clients = self._preview_clients.get(device_id)
            if clients:
                clients.discard(websocket)

    async def publish_event(self, message: dict) -> None:
        async with self._lock:
            clients = tuple(self._event_clients)
        disconnected: list[WebSocket] = []
        for websocket in clients:
            try:
                await websocket.send_json(message)
            except RuntimeError:
                disconnected.append(websocket)
        if disconnected:
            async with self._lock:
                self._event_clients.difference_update(disconnected)

    async def publish_preview(self, device_id: str, jpeg: bytes) -> None:
        async with self._lock:
            self._latest_frames[device_id] = jpeg
            clients = tuple(self._preview_clients.get(device_id, ()))
        disconnected: list[WebSocket] = []
        for websocket in clients:
            try:
                await websocket.send_bytes(jpeg)
            except RuntimeError:
                disconnected.append(websocket)
        if disconnected:
            async with self._lock:
                self._preview_clients[device_id].difference_update(disconnected)

    async def latest_preview(self, device_id: str) -> bytes | None:
        async with self._lock:
            return self._latest_frames.get(device_id)

    async def add_pose(self, device_id: str, websocket: WebSocket) -> None:
        await websocket.accept()
        async with self._lock:
            self._pose_clients[device_id].add(websocket)
            skeleton = self._latest_skeletons.get(device_id)
        if skeleton:
            await websocket.send_json(skeleton)

    async def remove_pose(self, device_id: str, websocket: WebSocket) -> None:
        async with self._lock:
            clients = self._pose_clients.get(device_id)
            if clients:
                clients.discard(websocket)

    async def publish_pose(self, device_id: str, packet: dict) -> None:
        """Forward a Quest skeleton/frame packet to the operator console.

        Skeleton topology arrives only every few seconds, so the newest one is
        cached and replayed to clients that connect between two of them.
        """
        async with self._lock:
            if packet.get("type") == "skeleton":
                self._latest_skeletons[device_id] = packet
            clients = tuple(self._pose_clients.get(device_id, ()))
        disconnected: list[WebSocket] = []
        for websocket in clients:
            try:
                await websocket.send_json(packet)
            except RuntimeError:
                disconnected.append(websocket)
        if disconnected:
            async with self._lock:
                self._pose_clients[device_id].difference_update(disconnected)

