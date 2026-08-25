from __future__ import annotations

import asyncio
from datetime import datetime, timezone
from typing import TypeVar

from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, ValidationError

from .interaction_models import (
    InteractionAbortRequest,
    InteractionArtifactType,
    InteractionCameraReadinessStatus,
    InteractionCameraReadinessUpdate,
    InteractionCompleteRequest,
    InteractionReadiness,
    InteractionQuestReadinessStatus,
    InteractionQuestReadinessUpdate,
    InteractionRunSnapshot,
    InteractionRunState,
)
from .interaction_service import (
    InteractionArtifactError,
    InteractionQuestPresenceConflict,
    InteractionReadinessConflict,
    InteractionService,
    InteractionStateConflict,
    parse_json_model,
)
from .realtime import RealtimeHub


_RequestModel = TypeVar("_RequestModel", bound=BaseModel)


def create_interaction_router(
    interactions: InteractionService,
    hub: RealtimeHub,
) -> APIRouter:
    router = APIRouter(prefix="/api/interaction", tags=["interaction"])
    running_tasks: set[asyncio.Task[None]] = set()

    @router.get("/readiness", response_model=InteractionReadiness)
    async def readiness() -> InteractionReadiness:
        facts = await interactions.readiness()
        return InteractionReadiness(
            backend_ready=True,
            storage_ready=facts.storage_ready,
            storage_error=facts.storage_error,
            quest_fresh=facts.quest_fresh,
            quest_ready=facts.quest_ready,
            camera_fresh=facts.camera_fresh,
            camera_ready=facts.camera_ready,
            participant_fresh=facts.participant_fresh,
            participant_ready=facts.participant_ready,
            ready=facts.ready,
            quest_device_id=facts.quest_device_id,
            quest_last_seen_utc=facts.quest_last_seen_utc,
            camera_last_seen_utc=facts.camera_last_seen_utc,
            participant_id=facts.participant_id,
            participant_last_seen_utc=facts.participant_last_seen_utc,
            server_utc=datetime.now(timezone.utc),
            interaction_root=str(interactions.interaction_root),
            active_run=await interactions.active_snapshot(),
        )

    @router.put(
        "/readiness/quest",
        response_model=InteractionQuestReadinessStatus,
    )
    async def update_quest_readiness(request: Request) -> InteractionQuestReadinessStatus:
        body = await _validated_body(request, InteractionQuestReadinessUpdate)
        try:
            status = await interactions.update_quest_readiness(body)
        except InteractionQuestPresenceConflict as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
        await hub.publish_event(
            {
                "type": "interaction_quest_readiness",
                "payload": status.model_dump(mode="json"),
            }
        )
        return status

    @router.put(
        "/readiness/camera",
        response_model=InteractionCameraReadinessStatus,
    )
    async def update_camera_readiness(request: Request) -> InteractionCameraReadinessStatus:
        body = await _validated_body(request, InteractionCameraReadinessUpdate)
        status = await interactions.update_camera_readiness(body)
        await hub.publish_event(
            {
                "type": "interaction_camera_readiness",
                "payload": status.model_dump(mode="json"),
            }
        )
        return status

    @router.post("/runs")
    async def accept_run(request: Request):
        _require_media_type(request, {"application/json"})
        manifest_bytes = await request.body()
        try:
            response = await interactions.accept_run_if_ready(
                manifest_bytes,
                request.headers.get("X-SignVR-Quest-Id"),
            )
            snapshot = await interactions.snapshot(response.run_id)
        except InteractionReadinessConflict as exc:
            raise HTTPException(status_code=409, detail=str(exc)) from exc
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
