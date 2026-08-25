from __future__ import annotations

import asyncio
import json
import math
import time
from collections.abc import AsyncIterable, Callable
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import TypeVar

from pydantic import BaseModel, ValidationError

from .interaction_models import (
    INTERACTION_SCHEMA_VERSION,
    InteractionAbortRequest,
    InteractionAck,
    InteractionArtifactType,
    InteractionCameraReadinessStatus,
    InteractionCameraReadinessUpdate,
    InteractionCompleteRequest,
    InteractionEvent,
    InteractionRunAccepted,
    InteractionRunPlan,
    InteractionRunSnapshot,
    InteractionRunState,
    InteractionQuestReadinessStatus,
    InteractionQuestReadinessUpdate,
    StoredInteractionRunStatus,
    validate_interaction_id,
)
from .interaction_repository import (
    InteractionRepository,
    StagedInteractionArtifact,
)


_ModelT = TypeVar("_ModelT", bound=BaseModel)
_JSONL_CONTENT_TYPES = frozenset(
    {
        "application/x-ndjson",
        "application/jsonl",
        "application/json",
        "application/octet-stream",
        "text/plain",
    }
)
_SUMMARY_CONTENT_TYPES = frozenset({"application/json", "application/octet-stream"})
_WEBCAM_CONTENT_TYPES = frozenset({"video/webm", "application/octet-stream"})
INTERACTION_READINESS_TTL_SECONDS = 5.0


class InteractionStateConflict(RuntimeError):
    pass


class InteractionQuestPresenceConflict(RuntimeError):
    pass


class InteractionReadinessConflict(RuntimeError):
    pass


class InteractionArtifactError(ValueError):
    pass


class DuplicateJsonKey(ValueError):
    pass


@dataclass(frozen=True, slots=True)
class InteractionReadinessFacts:
    storage_ready: bool
    storage_error: str | None
    quest_fresh: bool
    quest_ready: bool
    quest_device_id: str | None
    quest_last_seen_utc: datetime | None
    camera_fresh: bool
    camera_ready: bool
    camera_last_seen_utc: datetime | None
    participant_fresh: bool
    participant_ready: bool
    participant_id: str | None
    participant_last_seen_utc: datetime | None

    @property
    def ready(self) -> bool:
        return (
            self.storage_ready
            and self.quest_ready
            and self.camera_ready
            and self.participant_ready
        )


class InteractionService:
    """The Host-side Interaction interface used by routes and tests."""

    def __init__(
        self,
        repository: InteractionRepository,
        *,
        start_delay_seconds: float = 2.0,
        readiness_ttl_seconds: float = INTERACTION_READINESS_TTL_SECONDS,
        clock: Callable[[], datetime] | None = None,
        monotonic_clock: Callable[[], float] | None = None,
    ) -> None:
        self.repository = repository
        self.start_delay_seconds = start_delay_seconds
        self.readiness_ttl_seconds = readiness_ttl_seconds
        self._clock = clock or (lambda: datetime.now(timezone.utc))
        self._monotonic_clock = monotonic_clock or time.monotonic
        self._readiness_lock = asyncio.Lock()
        self._quest_reported_ready = False
        self._quest_device_id: str | None = None
        self._quest_last_seen_utc: datetime | None = None
        self._quest_last_seen_monotonic: float | None = None
        self._quest_heartbeat_generation: int | None = None
        self._quest_heartbeat_sequence: int | None = None
        self._camera_reported_ready = False
        self._camera_last_seen_utc: datetime | None = None
        self._camera_last_seen_monotonic: float | None = None
        self._participant_id: str | None = None
        self._participant_last_seen_utc: datetime | None = None
        self._participant_last_seen_monotonic: float | None = None
        self._camera_heartbeat_generation: int | None = None
        self._camera_heartbeat_sequence: int | None = None

    async def accept_run(self, manifest_bytes: bytes) -> InteractionRunAccepted:
        return await self._accept_run(manifest_bytes, require_no_active_run=False)

    async def accept_run_if_idle(
        self,
        manifest_bytes: bytes,
    ) -> InteractionRunAccepted:
        return await self._accept_run(manifest_bytes, require_no_active_run=True)

    async def accept_run_if_ready(
        self,
        manifest_bytes: bytes,
        quest_device_id: str | None,
    ) -> InteractionRunAccepted:
        plan = self._parse_run_plan(manifest_bytes)
        async with self._readiness_lock:
            facts = self._readiness_facts_locked(self._monotonic_now())
            missing = [
                label
                for label, ready in (
                    ("storage", facts.storage_ready),
                    ("fresh Quest heartbeat", facts.quest_ready),
                    ("fresh camera heartbeat", facts.camera_ready),
                    ("fresh Host participant heartbeat", facts.participant_ready),
                )
                if not ready
            ]
            if missing:
                raise InteractionReadinessConflict(
                    f"Interaction Host is not ready: {', '.join(missing)}"
                )
            if quest_device_id is None:
                raise InteractionReadinessConflict(
                    "X-SignVR-Quest-Id is required for Interaction Run registration"
                )
            try:
                safe_quest_device_id = validate_interaction_id(
                    quest_device_id,
                    "X-SignVR-Quest-Id",
                )
            except ValueError as exc:
                raise InteractionReadinessConflict(str(exc)) from exc
            if safe_quest_device_id != facts.quest_device_id:
                raise InteractionReadinessConflict(
                    "X-SignVR-Quest-Id does not match the fresh Quest heartbeat"
                )
            if plan.participant_id != facts.participant_id:
                raise InteractionReadinessConflict(
                    "Run Plan participant_id does not match the fresh Host participant heartbeat"
                )
            return await self._accept_plan(
                plan,
                manifest_bytes,
                require_no_active_run=True,
            )

    async def _accept_run(
        self,
        manifest_bytes: bytes,
        *,
        require_no_active_run: bool,
    ) -> InteractionRunAccepted:
        plan = self._parse_run_plan(manifest_bytes)
        return await self._accept_plan(
            plan,
            manifest_bytes,
            require_no_active_run=require_no_active_run,
        )

    def _parse_run_plan(self, manifest_bytes: bytes) -> InteractionRunPlan:
        if not manifest_bytes or len(manifest_bytes) > 1024 * 1024:
            raise ValueError("Run Plan must contain 1 byte to 1 MiB of JSON")
        return parse_json_model(manifest_bytes, InteractionRunPlan)

    async def _accept_plan(
        self,
        plan: InteractionRunPlan,
        manifest_bytes: bytes,
        *,
        require_no_active_run: bool,
    ) -> InteractionRunAccepted:
        now = self._now()
        start_at = now + timedelta(seconds=self.start_delay_seconds)
        status = StoredInteractionRunStatus(
            run_id=plan.run_id,
            state=InteractionRunState.SCHEDULED,
            start_at_utc=start_at,
        )
        await self.repository.create_run(
            plan,
            manifest_bytes,
            status,
            require_no_active_run=require_no_active_run,
        )
        return InteractionRunAccepted(
            run_id=plan.run_id,
            start_at_utc=start_at,
            missing_artifacts=self.repository.missing_artifacts(plan.run_id),
            state=status.state,
        )

    async def complete_run(
        self,
        path_run_id: str,
        request: InteractionCompleteRequest,
    ) -> InteractionRunSnapshot:
        run_id = self._consistent_run_id(path_run_id, request.run_id)
        terminal_utc = request.completed_utc or self._now()
        self._validate_terminal_time(run_id, terminal_utc)

        def complete(
            current: StoredInteractionRunStatus,
        ) -> StoredInteractionRunStatus:
            if current.state is InteractionRunState.ABORTED:
                raise InteractionStateConflict(
                    "An aborted Interaction Run cannot be completed"
                )
            if current.state is InteractionRunState.COMPLETED:
                return current
            return current.model_copy(
                update={
                    "state": InteractionRunState.COMPLETED,
                    "terminal_utc": terminal_utc,
                    "abort_reason": None,
                }
            )

        status = await self.repository.transition_status(run_id, complete)
        return self._snapshot(run_id, status)

    async def abort_run(
        self,
        path_run_id: str,
        request: InteractionAbortRequest,
    ) -> InteractionRunSnapshot:
        run_id = self._consistent_run_id(path_run_id, request.run_id)
        terminal_utc = request.aborted_utc or self._now()
        self._validate_terminal_time(run_id, terminal_utc)

        def abort(
            current: StoredInteractionRunStatus,
        ) -> StoredInteractionRunStatus:
            if current.state is InteractionRunState.COMPLETED:
                raise InteractionStateConflict(
                    "A completed Interaction Run cannot be aborted"
                )
            if current.state is InteractionRunState.ABORTED:
                if current.abort_reason != request.abort_reason:
                    raise InteractionStateConflict(
                        "The Interaction Run is already aborted with a different reason"
                    )
                return current
            return current.model_copy(
                update={
                    "state": InteractionRunState.ABORTED,
                    "terminal_utc": terminal_utc,
                    "abort_reason": request.abort_reason,
                }
            )

        status = await self.repository.transition_status(run_id, abort)
        return self._snapshot(run_id, status)

    async def mark_running(self, run_id: str) -> InteractionRunSnapshot:
        safe_run_id = validate_interaction_id(run_id, "run_id")
        status = await self._effective_status(safe_run_id)
        return self._snapshot(safe_run_id, status)

    async def store_artifact(
        self,
        path_run_id: str,
        artifact_type: InteractionArtifactType,
        content_type: str | None,
        chunks: AsyncIterable[bytes],
    ) -> InteractionRunSnapshot:
        run_id = validate_interaction_id(path_run_id, "run_id")
        self.repository.location(run_id)
        self._validate_content_type(artifact_type, content_type)
        staged = await self.repository.stage_artifact(run_id, artifact_type, chunks)
        try:
            self._validate_staged_artifact(staged)
            await self.repository.publish_artifact(staged)
        finally:
            self.repository.discard_artifact(staged)
        return await self.snapshot(run_id)

    async def ack(self, run_id: str) -> InteractionAck:
        snapshot = await self.snapshot(run_id)
        return InteractionAck(
            run_id=snapshot.run_id,
            state=snapshot.state,
            acknowledged=snapshot.acknowledged,
            missing_artifacts=snapshot.missing_artifacts,
        )

    async def snapshot(self, run_id: str) -> InteractionRunSnapshot:
        safe_run_id = validate_interaction_id(run_id, "run_id")
        status = await self._effective_status(safe_run_id)
        return self._snapshot(safe_run_id, status)

    async def active_snapshot(self) -> InteractionRunSnapshot | None:
        active = await self.repository.active_run()
        if active is None:
            return None
        location, _stored_status = active
        status = await self._effective_status(location.plan.run_id)
        if status.state not in {
            InteractionRunState.SCHEDULED,
            InteractionRunState.RUNNING,
        }:
            return None
        return self._snapshot(location.plan.run_id, status)

    def storage_ready(self) -> bool:
        return self.repository.storage_ready()

    @property
    def storage_error(self) -> str | None:
        return self.repository.storage_error

    async def update_quest_readiness(
        self,
        update: InteractionQuestReadinessUpdate,
    ) -> InteractionQuestReadinessStatus:
        async with self._readiness_lock:
            monotonic_now = self._monotonic_now()
            owner_changed = (
                self._quest_device_id is not None
                and update.quest_device_id != self._quest_device_id
            )
            if (
                owner_changed
                and self._is_fresh(self._quest_last_seen_monotonic, monotonic_now)
            ):
                raise InteractionQuestPresenceConflict(
                    "Fresh Interaction Quest "
                    f"{self._quest_device_id} already owns readiness"
                )
            incoming_watermark = (
                update.heartbeat_generation,
                update.heartbeat_sequence,
            )
            current_watermark = (
                self._quest_heartbeat_generation,
                self._quest_heartbeat_sequence,
            )
            accepted = (
                current_watermark[0] is None
                or owner_changed
                or incoming_watermark > current_watermark
            )
            if accepted:
                self._quest_reported_ready = update.ready
                self._quest_device_id = update.quest_device_id
                self._quest_last_seen_utc = self._now()
                self._quest_last_seen_monotonic = monotonic_now
                self._quest_heartbeat_generation = update.heartbeat_generation
                self._quest_heartbeat_sequence = update.heartbeat_sequence
            return self._quest_readiness_status(
                accepted=accepted,
                monotonic_now=monotonic_now,
            )

    async def quest_readiness(self) -> InteractionQuestReadinessStatus:
        async with self._readiness_lock:
            return self._quest_readiness_status(
                accepted=True,
                monotonic_now=self._monotonic_now(),
            )

    async def readiness(self) -> InteractionReadinessFacts:
        async with self._readiness_lock:
            return self._readiness_facts_locked(self._monotonic_now())

    async def update_camera_readiness(
        self,
        update: InteractionCameraReadinessUpdate,
    ) -> InteractionCameraReadinessStatus:
        async with self._readiness_lock:
            monotonic_now = self._monotonic_now()
            incoming_watermark = (
                update.heartbeat_generation,
                update.heartbeat_sequence,
            )
            current_watermark = (
                self._camera_heartbeat_generation,
                self._camera_heartbeat_sequence,
            )
            accepted = (
                current_watermark[0] is None
                or incoming_watermark > current_watermark
            )
            if accepted:
                received_at = self._now()
                self._camera_reported_ready = update.ready
                self._camera_last_seen_utc = received_at
                self._camera_last_seen_monotonic = monotonic_now
                self._participant_id = update.participant_id
                self._participant_last_seen_utc = received_at
                self._participant_last_seen_monotonic = monotonic_now
                self._camera_heartbeat_generation = update.heartbeat_generation
                self._camera_heartbeat_sequence = update.heartbeat_sequence
            return self._camera_readiness_status(
                accepted=accepted,
                monotonic_now=monotonic_now,
            )

    async def camera_readiness(self) -> InteractionCameraReadinessStatus:
        async with self._readiness_lock:
            return self._camera_readiness_status(
                accepted=True,
                monotonic_now=self._monotonic_now(),
            )

    def _quest_readiness_status(
        self,
        *,
        accepted: bool,
        monotonic_now: float,
    ) -> InteractionQuestReadinessStatus:
        last_seen = self._quest_last_seen_utc
        fresh = self._is_fresh(self._quest_last_seen_monotonic, monotonic_now)
        return InteractionQuestReadinessStatus(
            accepted=accepted,
            quest_fresh=fresh,
            quest_ready=self._quest_reported_ready and fresh,
            quest_device_id=self._quest_device_id,
            quest_last_seen_utc=last_seen,
            heartbeat_generation=self._quest_heartbeat_generation,
            heartbeat_sequence=self._quest_heartbeat_sequence,
        )

    def _camera_readiness_status(
        self,
        *,
        accepted: bool,
        monotonic_now: float,
    ) -> InteractionCameraReadinessStatus:
        last_seen = self._camera_last_seen_utc
        camera_fresh = self._is_fresh(
            self._camera_last_seen_monotonic,
            monotonic_now,
        )
        participant_fresh = self._is_fresh(
            self._participant_last_seen_monotonic,
            monotonic_now,
        )
        return InteractionCameraReadinessStatus(
            accepted=accepted,
            camera_fresh=camera_fresh,
            camera_ready=self._camera_reported_ready and camera_fresh,
            camera_last_seen_utc=last_seen,
            participant_ready=self._participant_id is not None and participant_fresh,
            participant_fresh=self._participant_id is not None and participant_fresh,
            participant_id=self._participant_id,
            participant_last_seen_utc=self._participant_last_seen_utc,
            heartbeat_generation=self._camera_heartbeat_generation,
            heartbeat_sequence=self._camera_heartbeat_sequence,
        )

    def _readiness_facts_locked(
        self,
        monotonic_now: float,
    ) -> InteractionReadinessFacts:
        quest = self._quest_readiness_status(
            accepted=True,
            monotonic_now=monotonic_now,
        )
        camera = self._camera_readiness_status(
            accepted=True,
            monotonic_now=monotonic_now,
        )
        return InteractionReadinessFacts(
            storage_ready=self.storage_ready(),
            storage_error=self.storage_error,
            quest_fresh=quest.quest_fresh,
            quest_ready=quest.quest_ready,
            quest_device_id=quest.quest_device_id,
            quest_last_seen_utc=quest.quest_last_seen_utc,
            camera_fresh=camera.camera_fresh,
            camera_ready=camera.camera_ready,
            camera_last_seen_utc=camera.camera_last_seen_utc,
            participant_fresh=camera.participant_fresh,
            participant_ready=camera.participant_ready,
            participant_id=camera.participant_id,
            participant_last_seen_utc=camera.participant_last_seen_utc,
        )

    def _is_fresh(
        self,
        last_seen_monotonic: float | None,
        monotonic_now: float,
    ) -> bool:
        if last_seen_monotonic is None:
            return False
        age_seconds = monotonic_now - last_seen_monotonic
        return 0 <= age_seconds <= self.readiness_ttl_seconds

    @property
    def interaction_root(self) -> Path:
        return self.repository.interaction_root

    async def _effective_status(self, run_id: str) -> StoredInteractionRunStatus:
        now = self._now()

        def mark_running(
            current: StoredInteractionRunStatus,
        ) -> StoredInteractionRunStatus:
            if (
                current.state is InteractionRunState.SCHEDULED
                and now >= current.start_at_utc
            ):
                return current.model_copy(update={"state": InteractionRunState.RUNNING})
            return current

        return await self.repository.transition_status(run_id, mark_running)

    def _snapshot(
        self,
        run_id: str,
        status: StoredInteractionRunStatus,
    ) -> InteractionRunSnapshot:
        plan = self.repository.location(run_id).plan
        missing = self.repository.missing_artifacts(run_id)
        acknowledged = (
            status.state in {InteractionRunState.COMPLETED, InteractionRunState.ABORTED}
            and not missing
        )
        return InteractionRunSnapshot(
            batch_id=plan.batch_id,
            participant_id=plan.participant_id,
            run_id=plan.run_id,
            state=status.state,
            start_at_utc=status.start_at_utc,
            missing_artifacts=missing,
            acknowledged=acknowledged,
            terminal_utc=status.terminal_utc,
            abort_reason=status.abort_reason,
        )

    def _consistent_run_id(self, path_run_id: str, body_run_id: str) -> str:
        safe_path_id = validate_interaction_id(path_run_id, "run_id")
        if safe_path_id != body_run_id:
            raise InteractionStateConflict("Path run_id does not match request run_id")
        self.repository.location(safe_path_id)
        return safe_path_id

    def _validate_terminal_time(self, run_id: str, terminal_utc: datetime) -> None:
        plan = self.repository.location(run_id).plan
        if terminal_utc < plan.created_utc:
            raise ValueError("Terminal timestamp cannot precede Run Plan creation")

    def _validate_content_type(
        self,
        artifact_type: InteractionArtifactType,
        content_type: str | None,
    ) -> None:
        media_type = (content_type or "").split(";", 1)[0].strip().lower()
        allowed = (
            _WEBCAM_CONTENT_TYPES
            if artifact_type is InteractionArtifactType.WEBCAM
            else _SUMMARY_CONTENT_TYPES
            if artifact_type is InteractionArtifactType.SUMMARY
            else _JSONL_CONTENT_TYPES
        )
        if media_type not in allowed:
            raise InteractionArtifactError(
                f"Content-Type {media_type or '<missing>'} is invalid for {artifact_type.value}"
            )

    def _validate_staged_artifact(self, staged: StagedInteractionArtifact) -> None:
        if staged.artifact_type is InteractionArtifactType.WEBCAM:
            with staged.temporary.open("rb") as source:
                webm_header = source.read(4)
            if staged.size < 4 or webm_header != b"\x1aE\xdf\xa3":
                raise InteractionArtifactError("webcam artifact is not a WebM container")
            return
        if staged.artifact_type is InteractionArtifactType.SUMMARY:
            payload = parse_json_object(staged.temporary.read_bytes())
            self._validate_capture_identity(payload, staged.run_id, "summary")
            return

        record_count = 0
        previous_event_seq = -1
        try:
            with staged.temporary.open("r", encoding="utf-8", newline="") as source:
                for line_number, line in enumerate(source, start=1):
                    if not line.strip():
                        continue
                    record_count += 1
                    payload = parse_json_object(line.encode("utf-8"))
                    if staged.artifact_type is InteractionArtifactType.EVENTS:
                        try:
                            event = InteractionEvent.model_validate(payload)
                        except ValidationError as exc:
                            raise InteractionArtifactError(
                                f"events line {line_number} does not match Contract V1: {exc}"
                            ) from exc
                        if event.run_id != staged.run_id:
                            raise InteractionArtifactError(
                                f"events line {line_number} has a mismatched run_id"
                            )
                        if event.event_seq <= previous_event_seq:
                            raise InteractionArtifactError(
                                "events event_seq values must be strictly increasing"
                            )
                        previous_event_seq = event.event_seq
                    else:
                        self._validate_capture_identity(
                            payload,
                            staged.run_id,
                            f"{staged.artifact_type.value} line {line_number}",
                        )
        except UnicodeDecodeError as exc:
            raise InteractionArtifactError(
                f"{staged.artifact_type.value} artifact is not UTF-8 JSONL"
            ) from exc
        if record_count == 0 and staged.artifact_type is InteractionArtifactType.EVENTS:
            raise InteractionArtifactError(
                "events artifact must contain at least one Contract V1 event"
            )

    @staticmethod
    def _validate_capture_identity(payload: dict, run_id: str, label: str) -> None:
        schema_version = payload.get("schema_version")
        if type(schema_version) is not int or schema_version != INTERACTION_SCHEMA_VERSION:
            raise InteractionArtifactError(f"{label} must declare schema_version 1")
        if payload.get("run_id") != run_id:
            raise InteractionArtifactError(f"{label} has a mismatched run_id")

    def _now(self) -> datetime:
        now = self._clock()
        if now.tzinfo is None:
            raise RuntimeError("InteractionService clock must return a timezone-aware datetime")
        return now.astimezone(timezone.utc)

    def _monotonic_now(self) -> float:
        now = self._monotonic_clock()
        if not math.isfinite(now):
            raise RuntimeError("InteractionService monotonic clock must return a finite value")
        return now


def parse_json_model(data: bytes, model_type: type[_ModelT]) -> _ModelT:
    return model_type.model_validate(parse_json_object(data))


def parse_json_object(data: bytes) -> dict:
    try:
        text = data.decode("utf-8")
        payload = json.loads(
            text,
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=lambda value: (_raise_invalid_constant(value)),
        )
    except (UnicodeDecodeError, json.JSONDecodeError, DuplicateJsonKey) as exc:
        raise ValueError(f"Invalid strict JSON: {exc}") from exc
    if not isinstance(payload, dict):
        raise ValueError("JSON payload must be an object")
    return payload


def _reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateJsonKey(f"duplicate key: {key}")
        result[key] = value
    return result


def _raise_invalid_constant(value: str) -> None:
    raise ValueError(f"invalid JSON constant: {value}")
