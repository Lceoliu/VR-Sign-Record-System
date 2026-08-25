from __future__ import annotations

import json
from collections.abc import AsyncIterable, Callable
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import TypeVar

from pydantic import BaseModel, ValidationError

from .interaction_models import (
    INTERACTION_SCHEMA_VERSION,
    InteractionAbortRequest,
    InteractionAck,
    InteractionArtifactType,
    InteractionCompleteRequest,
    InteractionEvent,
    InteractionRunAccepted,
    InteractionRunPlan,
    InteractionRunSnapshot,
    InteractionRunState,
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


class InteractionStateConflict(RuntimeError):
    pass


class InteractionArtifactError(ValueError):
    pass


class DuplicateJsonKey(ValueError):
    pass


class InteractionService:
    """The Host-side Interaction interface used by routes and tests."""

    def __init__(
        self,
        repository: InteractionRepository,
        *,
        start_delay_seconds: float = 2.0,
        camera_readiness_ttl_seconds: float = 5.0,
        clock: Callable[[], datetime] | None = None,
    ) -> None:
        self.repository = repository
        self.start_delay_seconds = start_delay_seconds
        self.camera_readiness_ttl_seconds = camera_readiness_ttl_seconds
        self._clock = clock or (lambda: datetime.now(timezone.utc))
        self._camera_reported_ready = False
        self._camera_last_seen_utc: datetime | None = None

    async def accept_run(self, manifest_bytes: bytes) -> InteractionRunAccepted:
        return await self._accept_run(manifest_bytes, require_no_active_run=False)

    async def accept_run_if_idle(
        self,
        manifest_bytes: bytes,
    ) -> InteractionRunAccepted:
        return await self._accept_run(manifest_bytes, require_no_active_run=True)

    async def _accept_run(
        self,
        manifest_bytes: bytes,
        *,
        require_no_active_run: bool,
    ) -> InteractionRunAccepted:
        if not manifest_bytes or len(manifest_bytes) > 1024 * 1024:
            raise ValueError("Run Plan must contain 1 byte to 1 MiB of JSON")
        plan = parse_json_model(manifest_bytes, InteractionRunPlan)
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

    def set_camera_readiness(self, ready: bool) -> tuple[bool, datetime]:
        self._camera_reported_ready = ready
        self._camera_last_seen_utc = self._now()
        return self.camera_readiness()

    def camera_readiness(self) -> tuple[bool, datetime | None]:
        last_seen = self._camera_last_seen_utc
        if not self._camera_reported_ready or last_seen is None:
            return False, last_seen
        fresh = (self._now() - last_seen).total_seconds() <= self.camera_readiness_ttl_seconds
        return fresh, last_seen

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
