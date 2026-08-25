from __future__ import annotations

import asyncio
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import TypeVar

from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, ValidationError

from .device_registry import DeviceRegistry
from .interaction_models import (
    InteractionAbortRequest,
    InteractionArtifactType,
    InteractionCameraReadinessUpdate,
    InteractionCompleteRequest,
    InteractionReadiness,
    InteractionRunSnapshot,
    InteractionRunState,
)
from .interaction_service import (
    InteractionArtifactError,
    InteractionService,
    InteractionStateConflict,
    parse_json_model,
)
from .realtime import RealtimeHub


_RequestModel = TypeVar("_RequestModel", bound=BaseModel)


@dataclass(frozen=True, slots=True)
class _InteractionReadinessFacts:
    storage_ready: bool
    storage_error: str | None
    quest_ready: bool
    quest_device_id: str | None
    camera_ready: bool
    camera_last_seen_utc: datetime | None

    @property
    def ready(self) -> bool:
        return self.storage_ready and self.quest_ready and self.camera_ready


def create_interaction_router(
    interactions: InteractionService,
    registry: DeviceRegistry,
    hub: RealtimeHub,
) -> APIRouter:
    router = APIRouter(prefix="/api/interaction", tags=["interaction"])
    running_tasks: set[asyncio.Task[None]] = set()

    @router.get("/readiness", response_model=InteractionReadiness)
    async def readiness() -> InteractionReadiness:
        facts = await _readiness_facts(interactions, registry)
        return InteractionReadiness(
            backend_ready=True,
            storage_ready=facts.storage_ready,
            storage_error=facts.storage_error,
            quest_ready=facts.quest_ready,
            camera_ready=facts.camera_ready,
            ready=facts.ready,
            quest_device_id=facts.quest_device_id,
            camera_last_seen_utc=facts.camera_last_seen_utc,
            server_utc=datetime.now(timezone.utc),
            interaction_root=str(interactions.interaction_root),
            active_run=await interactions.active_snapshot(),
        )

    @router.put("/readiness/camera")
    async def update_camera_readiness(request: Request):
        body = await _validated_body(request, InteractionCameraReadinessUpdate)
        camera_ready, last_seen_utc = interactions.set_camera_readiness(body.ready)
        await hub.publish_event(
            {
                "type": "interaction_camera_readiness",
                "payload": {
                    "camera_ready": camera_ready,
                    "camera_last_seen_utc": (
                        last_seen_utc.isoformat() if last_seen_utc else None
                    ),
                },
            }
        )
        return {
            "schema_version": 1,
            "camera_ready": camera_ready,
            "camera_last_seen_utc": last_seen_utc,
        }

    @router.post("/runs")
    async def accept_run(request: Request):
        _require_media_type(request, {"application/json"})
        facts = await _readiness_facts(interactions, registry)
        if not facts.ready:
            missing = [
                label
                for label, ready in (
                    ("storage", facts.storage_ready),
                    ("selected and paired Quest", facts.quest_ready),
                    ("fresh camera heartbeat", facts.camera_ready),
                )
                if not ready
            ]
            raise HTTPException(
                status_code=409,
                detail=f"Interaction Host is not ready: {', '.join(missing)}",
            )
        active = await interactions.active_snapshot()
        if active is not None:
            raise HTTPException(
                status_code=409,
                detail=(
                    "An Interaction Run is already active: "
                    f"{active.run_id} ({active.state.value})"
                ),
            )
        manifest_bytes = await request.body()
        try:
            response = await interactions.accept_run_if_idle(manifest_bytes)
            snapshot = await interactions.snapshot(response.run_id)
        except ValidationError as exc:
            raise HTTPException(status_code=422, detail=str(exc)) from exc
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        except FileExistsError as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc

        await hub.publish_event(
            {
                "type": "interaction_run_scheduled",
                "payload": snapshot.model_dump(mode="json"),
            }
        )
        task = asyncio.create_task(
            _publish_running_at_start(interactions, hub, response.run_id, response.start_at_utc)
        )
        running_tasks.add(task)
        task.add_done_callback(running_tasks.discard)
        return response

    @router.get("/runs/{run_id}", response_model=InteractionRunSnapshot)
    async def run_snapshot(run_id: str) -> InteractionRunSnapshot:
        try:
            return await interactions.snapshot(run_id)
        except FileNotFoundError as exc:
            raise HTTPException(status_code=404, detail=str(exc)) from exc
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @router.post("/runs/{run_id}/complete")
    async def complete_run(run_id: str, request: Request):
        body = await _validated_body(request, InteractionCompleteRequest)
        try:
            snapshot = await interactions.complete_run(run_id, body)
        except FileNotFoundError as exc:
            raise HTTPException(status_code=404, detail=str(exc)) from exc
        except InteractionStateConflict as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        await hub.publish_event(
            {
                "type": "interaction_run_completed",
                "payload": snapshot.model_dump(mode="json"),
            }
        )
        return snapshot

    @router.post("/runs/{run_id}/abort")
    async def abort_run(run_id: str, request: Request):
        body = await _validated_body(request, InteractionAbortRequest)
        try:
            snapshot = await interactions.abort_run(run_id, body)
        except FileNotFoundError as exc:
            raise HTTPException(status_code=404, detail=str(exc)) from exc
        except InteractionStateConflict as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        await hub.publish_event(
            {
                "type": "interaction_run_aborted",
                "payload": snapshot.model_dump(mode="json"),
            }
        )
        return snapshot

    @router.put("/runs/{run_id}/artifacts/{artifact_type}")
    async def upload_artifact(run_id: str, artifact_type: str, request: Request):
        try:
            resolved_type = InteractionArtifactType(artifact_type)
        except ValueError as exc:
            raise HTTPException(
                status_code=400,
                detail=f"Unsupported Interaction artifact type: {artifact_type}",
            ) from exc
        try:
            snapshot = await interactions.store_artifact(
                run_id,
                resolved_type,
                request.headers.get("content-type"),
                request.stream(),
            )
        except FileNotFoundError as exc:
            raise HTTPException(status_code=404, detail=str(exc)) from exc
        except FileExistsError as exc:
            raise HTTPException(
                status_code=409,
                detail=f"{resolved_type.value} artifact already exists; Host refuses to overwrite it",
            ) from exc
        except InteractionArtifactError as exc:
            raise HTTPException(status_code=422, detail=str(exc)) from exc
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

        await hub.publish_event(
            {
                "type": "interaction_artifact_stored",
                "payload": snapshot.model_dump(mode="json"),
            }
        )
        if snapshot.acknowledged:
            await hub.publish_event(
                {
                    "type": "interaction_upload_acknowledged",
                    "payload": snapshot.model_dump(mode="json"),
                }
            )
        return {
            "stored": True,
            "artifact_type": resolved_type.value,
            "run_id": snapshot.run_id,
            "acknowledged": snapshot.acknowledged,
            "missing_artifacts": snapshot.missing_artifacts,
        }

    @router.get("/runs/{run_id}/ack")
    async def artifact_ack(run_id: str):
        try:
            return await interactions.ack(run_id)
        except FileNotFoundError as exc:
            raise HTTPException(status_code=404, detail=str(exc)) from exc
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    return router


async def _readiness_facts(
    interactions: InteractionService,
    registry: DeviceRegistry,
) -> _InteractionReadinessFacts:
    selected = await registry.selected()
    camera_ready, camera_last_seen_utc = interactions.camera_readiness()
    return _InteractionReadinessFacts(
        storage_ready=interactions.storage_ready(),
        storage_error=interactions.storage_error,
        quest_ready=bool(selected and selected.selected and selected.paired),
        quest_device_id=selected.device_id if selected else None,
        camera_ready=camera_ready,
        camera_last_seen_utc=camera_last_seen_utc,
    )


async def _validated_body(
    request: Request,
    model_type: type[_RequestModel],
) -> _RequestModel:
    _require_media_type(request, {"application/json"})
    try:
        return parse_json_model(await request.body(), model_type)
    except ValidationError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


def _require_media_type(request: Request, allowed: set[str]) -> None:
    media_type = request.headers.get("content-type", "").split(";", 1)[0].strip().lower()
    if media_type not in allowed:
        raise HTTPException(
            status_code=415,
            detail=f"Expected one of {', '.join(sorted(allowed))}",
        )


async def _publish_running_at_start(
    interactions: InteractionService,
    hub: RealtimeHub,
    run_id: str,
    start_at_utc: datetime,
) -> None:
    delay = max(
        0.0,
        (start_at_utc - datetime.now(timezone.utc)).total_seconds(),
    )
    await asyncio.sleep(delay)
    snapshot = await interactions.mark_running(run_id)
    if snapshot.state is not InteractionRunState.RUNNING:
        return
    await hub.publish_event(
        {
            "type": "interaction_run_running",
            "payload": snapshot.model_dump(mode="json"),
        }
    )
