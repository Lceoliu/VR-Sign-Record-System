from __future__ import annotations

import asyncio
import time
from contextlib import suppress
from dataclasses import dataclass
from typing import Any, Literal

from fastapi import WebSocket
from starlette.websockets import WebSocketDisconnect


OutboundMessage = tuple[Literal["json", "bytes"], dict[str, Any] | bytes]


@dataclass(slots=True)
class _Client:
    websocket: WebSocket
    queue: asyncio.Queue[OutboundMessage]
    sender: asyncio.Task[None] | None = None


class RealtimeHub:
    def __init__(self) -> None:
        self._event_clients: dict[WebSocket, _Client] = {}
        self._preview_clients: dict[str, dict[WebSocket, _Client]] = {}
        self._latest_frames: dict[str, tuple[float, bytes]] = {}
        self._pose_clients: dict[str, dict[WebSocket, _Client]] = {}
        self._latest_skeletons: dict[str, dict] = {}
        self._lock = asyncio.Lock()

    async def close(self) -> None:
        async with self._lock:
            clients = list(self._event_clients.values())
            clients.extend(client for group in self._preview_clients.values() for client in group.values())
            clients.extend(client for group in self._pose_clients.values() for client in group.values())
            self._event_clients.clear()
            self._preview_clients.clear()
            self._pose_clients.clear()
        tasks = [client.sender for client in clients if client.sender is not None]
        for task in tasks:
            task.cancel()
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)

    async def add_events(self, websocket: WebSocket, initial_message: dict | None = None) -> None:
        client = await self._create_client(websocket, capacity=64)
        async with self._lock:
            self._event_clients[websocket] = client
        if initial_message is not None:
            self._enqueue_latest(client, ("json", initial_message))

    async def remove_events(self, websocket: WebSocket) -> None:
        async with self._lock:
            client = self._event_clients.pop(websocket, None)
        await self._stop_client(client)

    async def add_preview(self, device_id: str, websocket: WebSocket) -> None:
        client = await self._create_client(websocket, capacity=1)
        async with self._lock:
            self._preview_clients.setdefault(device_id, {})[websocket] = client
            cached = self._latest_frames.get(device_id)
        if cached and time.monotonic() - cached[0] <= 5.0:
            self._enqueue_latest(client, ("bytes", cached[1]))

    async def remove_preview(self, device_id: str, websocket: WebSocket) -> None:
        async with self._lock:
            group = self._preview_clients.get(device_id)
            client = group.pop(websocket, None) if group else None
            if group is not None and not group:
                self._preview_clients.pop(device_id, None)
        await self._stop_client(client)

    async def publish_event(self, message: dict) -> None:
        async with self._lock:
            clients = tuple(self._event_clients.values())
        for client in clients:
            self._enqueue_latest(client, ("json", message))

    async def publish_preview(self, device_id: str, jpeg: bytes) -> None:
        async with self._lock:
            self._latest_frames[device_id] = (time.monotonic(), jpeg)
            clients = tuple(self._preview_clients.get(device_id, {}).values())
        for client in clients:
            self._enqueue_latest(client, ("bytes", jpeg))

    async def latest_preview(self, device_id: str) -> bytes | None:
        async with self._lock:
            cached = self._latest_frames.get(device_id)
        if cached is None or time.monotonic() - cached[0] > 5.0:
            return None
        return cached[1]

    async def add_pose(self, device_id: str, websocket: WebSocket) -> None:
        client = await self._create_client(websocket, capacity=2)
        async with self._lock:
            self._pose_clients.setdefault(device_id, {})[websocket] = client
            skeleton = self._latest_skeletons.get(device_id)
        if skeleton:
            self._enqueue_latest(client, ("json", skeleton))

    async def remove_pose(self, device_id: str, websocket: WebSocket) -> None:
        async with self._lock:
            group = self._pose_clients.get(device_id)
            client = group.pop(websocket, None) if group else None
            if group is not None and not group:
                self._pose_clients.pop(device_id, None)
        await self._stop_client(client)

    async def publish_pose(self, device_id: str, packet: dict) -> None:
        async with self._lock:
            if packet.get("type") == "skeleton":
                self._latest_skeletons[device_id] = packet
            clients = tuple(self._pose_clients.get(device_id, {}).values())
        for client in clients:
            self._enqueue_latest(client, ("json", packet))

    async def _create_client(self, websocket: WebSocket, *, capacity: int) -> _Client:
        await websocket.accept()
        client = _Client(websocket=websocket, queue=asyncio.Queue(maxsize=capacity))
        client.sender = asyncio.create_task(self._send_loop(client))
        return client

    async def _send_loop(self, client: _Client) -> None:
        try:
            while True:
                message_type, payload = await client.queue.get()
                if message_type == "bytes":
                    await client.websocket.send_bytes(payload)  # type: ignore[arg-type]
                else:
                    await client.websocket.send_json(payload)
        except (RuntimeError, WebSocketDisconnect, OSError):
            pass
        finally:
            await self._discard_client(client)

    async def _discard_client(self, client: _Client) -> None:
        async with self._lock:
            if self._event_clients.get(client.websocket) is client:
                self._event_clients.pop(client.websocket, None)
            for collection in (self._preview_clients, self._pose_clients):
                empty_groups: list[str] = []
                for device_id, group in collection.items():
                    if group.get(client.websocket) is client:
                        group.pop(client.websocket, None)
                    if not group:
                        empty_groups.append(device_id)
                for device_id in empty_groups:
                    collection.pop(device_id, None)

    async def _stop_client(self, client: _Client | None) -> None:
        if client is None or client.sender is None or client.sender is asyncio.current_task():
            return
        client.sender.cancel()
        with suppress(asyncio.CancelledError):
            await client.sender

    @staticmethod
    def _enqueue_latest(client: _Client, message: OutboundMessage) -> None:
        if client.queue.full():
            client.queue.get_nowait()
        client.queue.put_nowait(message)
