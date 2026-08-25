from __future__ import annotations

import asyncio
import json
import os
import uuid
from collections.abc import AsyncIterable, Callable
from dataclasses import dataclass
from pathlib import Path

import aiofiles

from .interaction_models import (
    ARTIFACT_FILENAMES,
    InteractionArtifactType,
    InteractionRunPlan,
    InteractionRunState,
    StoredInteractionRunStatus,
    validate_interaction_id,
)


_MANIFEST_FILENAME = "run.manifest.json"
_STATUS_FILENAME = "run.status.json"
_MAX_ARTIFACT_BYTES = {
    InteractionArtifactType.EVENTS: 512 * 1024 * 1024,
    InteractionArtifactType.POSES: 2 * 1024 * 1024 * 1024,
    InteractionArtifactType.OBJECTS: 1024 * 1024 * 1024,
    InteractionArtifactType.SUMMARY: 16 * 1024 * 1024,
    InteractionArtifactType.WEBCAM: 8 * 1024 * 1024 * 1024,
}


@dataclass(frozen=True, slots=True)
class InteractionRunLocation:
    plan: InteractionRunPlan
    directory: Path


@dataclass(frozen=True, slots=True)
class StagedInteractionArtifact:
    run_id: str
    artifact_type: InteractionArtifactType
    temporary: Path
    destination: Path
    size: int


class InteractionRepository:
    """Filesystem adapter for immutable Interaction Run plans and artifacts."""

    def __init__(self, root: Path) -> None:
        self.root = root.resolve()
        self.root.mkdir(parents=True, exist_ok=True)
        self.interaction_root = self.root / "interaction-tests"
        self.interaction_root.mkdir(parents=True, exist_ok=True)
        self.interaction_root = self.interaction_root.resolve()
        self._write_lock = asyncio.Lock()
        self._runs: dict[str, InteractionRunLocation] = {}
        self._load_errors: list[str] = []
        self._load_runs()

    def storage_ready(self) -> bool:
        if self._load_errors:
            return False
        probe = self.interaction_root / f".readiness-{uuid.uuid4().hex}.tmp"
        try:
            with probe.open("xb") as output:
                output.write(b"ready\n")
                output.flush()
                os.fsync(output.fileno())
            return True
        except OSError:
            return False
        finally:
            probe.unlink(missing_ok=True)

    async def create_run(
        self,
        plan: InteractionRunPlan,
        manifest_bytes: bytes,
        status: StoredInteractionRunStatus,
        *,
        require_no_active_run: bool = False,
    ) -> InteractionRunLocation:
        async with self._write_lock:
            if self._load_errors:
                raise ValueError(
                    "Interaction storage contains invalid existing Runs; refusing new writes"
                )
            if require_no_active_run:
                active = self._active_run_unlocked()
                if active is not None:
                    location, active_status = active
                    raise FileExistsError(
                        "An Interaction Run is already active: "
                        f"{location.plan.run_id} ({active_status.state.value})"
                    )
            if self._contains_run_id(plan.run_id):
                raise FileExistsError(f"Interaction Run already exists: {plan.run_id}")

            batch_directory = self.interaction_root / validate_interaction_id(
                plan.batch_id, "batch_id"
            )
            batch_directory.mkdir(exist_ok=True)
            self._require_contained(batch_directory)
            participant_directory = batch_directory / validate_interaction_id(
                plan.participant_id, "participant_id"
            )
            participant_directory.mkdir(exist_ok=True)
            self._require_contained(participant_directory)

            destination = participant_directory / validate_interaction_id(
                plan.run_id, "run_id"
            )
            if destination.exists():
                raise FileExistsError(f"Interaction Run already exists: {plan.run_id}")

            staging = participant_directory / f".{plan.run_id}.{uuid.uuid4().hex}.creating"
            staging.mkdir()
            try:
                self._write_new_file(staging / _MANIFEST_FILENAME, manifest_bytes)
                self._write_new_file(
                    staging / _STATUS_FILENAME,
                    self._status_bytes(status),
                )
                os.rename(staging, destination)
            except BaseException:
                for filename in (_MANIFEST_FILENAME, _STATUS_FILENAME):
                    (staging / filename).unlink(missing_ok=True)
                if staging.exists():
                    staging.rmdir()
                raise

            location = InteractionRunLocation(plan=plan, directory=destination)
            self._runs[plan.run_id] = location
            return location

    def location(self, run_id: str) -> InteractionRunLocation:
        safe_run_id = validate_interaction_id(run_id, "run_id")
        location = self._runs.get(safe_run_id)
        if location is None:
            raise FileNotFoundError(f"Unknown Interaction Run: {safe_run_id}")
        self._require_contained(location.directory)
        return location

    def status(self, run_id: str) -> StoredInteractionRunStatus:
        location = self.location(run_id)
        return self._read_status(location)

    async def transition_status(
        self,
        run_id: str,
        transition: Callable[
            [StoredInteractionRunStatus], StoredInteractionRunStatus
        ],
    ) -> StoredInteractionRunStatus:
        """Atomically read, decide, and persist one Run state transition."""

        location = self.location(run_id)
        async with self._write_lock:
            current = self._read_status(location)
            updated = transition(current)
            if not isinstance(updated, StoredInteractionRunStatus):
                raise TypeError("Interaction status transition returned an invalid value")
            if updated.run_id != current.run_id:
                raise ValueError("Interaction status transition changed run_id")
            if updated == current:
                return current
            self._replace_status(location, updated)
            return updated

    @staticmethod
    def _read_status(
        location: InteractionRunLocation,
    ) -> StoredInteractionRunStatus:
        status_path = location.directory / _STATUS_FILENAME
        try:
            status = StoredInteractionRunStatus.model_validate_json(
                status_path.read_bytes()
            )
        except (OSError, ValueError) as exc:
            raise ValueError(
                f"Invalid stored status for Interaction Run {location.plan.run_id}"
            ) from exc
        if status.run_id != location.plan.run_id:
            raise ValueError("Stored Interaction Run status has a mismatched run_id")
        return status

    def _replace_status(
        self,
        location: InteractionRunLocation,
        status: StoredInteractionRunStatus,
    ) -> None:
        destination = location.directory / _STATUS_FILENAME
        temporary = location.directory / f".{_STATUS_FILENAME}.{uuid.uuid4().hex}.tmp"
        try:
            self._write_new_file(temporary, self._status_bytes(status))
            os.replace(temporary, destination)
        finally:
            temporary.unlink(missing_ok=True)

    def missing_artifacts(self, run_id: str) -> list[str]:
        directory = self.location(run_id).directory
        return [
            artifact_type
            for artifact_type, filename in ARTIFACT_FILENAMES.items()
            if not (directory / filename).is_file()
        ]

    async def active_run(
        self,
    ) -> tuple[InteractionRunLocation, StoredInteractionRunStatus] | None:
        async with self._write_lock:
            return self._active_run_unlocked()

    @property
    def storage_error(self) -> str | None:
        if not self._load_errors:
            return None
        return f"{len(self._load_errors)} invalid existing Interaction Run(s)"

    async def stage_artifact(
        self,
        run_id: str,
        artifact_type: InteractionArtifactType,
        chunks: AsyncIterable[bytes],
    ) -> StagedInteractionArtifact:
        location = self.location(run_id)
        destination = location.directory / ARTIFACT_FILENAMES[artifact_type.value]
        if destination.exists():
            raise FileExistsError(str(destination))
        temporary = location.directory / f".{destination.name}.{uuid.uuid4().hex}.uploading"
        size = 0
        try:
            async with aiofiles.open(temporary, "xb") as output:
                async for chunk in chunks:
                    if not isinstance(chunk, bytes):
                        raise TypeError("Artifact upload yielded a non-byte chunk")
                    if not chunk:
                        continue
                    size += len(chunk)
                    if size > _MAX_ARTIFACT_BYTES[artifact_type]:
                        raise ValueError(f"{artifact_type.value} artifact is too large")
                    await output.write(chunk)
                await output.flush()
                await asyncio.to_thread(os.fsync, output.fileno())
        except BaseException:
            temporary.unlink(missing_ok=True)
            raise
        return StagedInteractionArtifact(
            run_id=run_id,
            artifact_type=artifact_type,
            temporary=temporary,
            destination=destination,
            size=size,
        )

    async def publish_artifact(self, staged: StagedInteractionArtifact) -> Path:
        self.location(staged.run_id)
        async with self._write_lock:
            if staged.destination.exists():
                raise FileExistsError(str(staged.destination))
            try:
                # A same-volume hard link publishes the fully flushed file in one
                # non-overwriting filesystem operation.
                os.link(staged.temporary, staged.destination)
            finally:
                staged.temporary.unlink(missing_ok=True)
        return staged.destination

    def discard_artifact(self, staged: StagedInteractionArtifact) -> None:
        staged.temporary.unlink(missing_ok=True)

    def _load_runs(self) -> None:
        for manifest_path in self.interaction_root.rglob(_MANIFEST_FILENAME):
            try:
                self._load_run(manifest_path)
            except (OSError, ValueError) as exc:
                self._load_errors.append(f"{manifest_path}: {exc}")

    def _load_run(self, manifest_path: Path) -> None:
        relative = manifest_path.relative_to(self.interaction_root)
        if len(relative.parts) != 4 or any(
            part.startswith(".") for part in relative.parts[:-1]
        ):
            return
        batch_id, participant_id, run_id, _ = relative.parts
        for value, label in (
            (batch_id, "batch_id"),
            (participant_id, "participant_id"),
            (run_id, "run_id"),
        ):
            validate_interaction_id(value, label)
        self._require_contained(manifest_path.parent)
        plan = InteractionRunPlan.model_validate_json(manifest_path.read_bytes())
        if (
            plan.batch_id != batch_id
            or plan.participant_id != participant_id
            or plan.run_id != run_id
        ):
            raise ValueError(
                f"Interaction Run manifest does not match its directory: {manifest_path}"
            )
        if self._contains_run_id(run_id):
            raise ValueError(f"Duplicate globally unique Interaction Run ID: {run_id}")
        status_path = manifest_path.parent / _STATUS_FILENAME
        if not status_path.is_file():
            raise ValueError(f"Interaction Run is missing {_STATUS_FILENAME}: {run_id}")
        status = StoredInteractionRunStatus.model_validate_json(status_path.read_bytes())
        if status.run_id != run_id:
            raise ValueError(f"Interaction Run status has mismatched run_id: {run_id}")
        self._runs[run_id] = InteractionRunLocation(
            plan=plan,
            directory=manifest_path.parent,
        )

    def _contains_run_id(self, run_id: str) -> bool:
        folded = run_id.casefold()
        return any(existing.casefold() == folded for existing in self._runs)

    def _active_run_unlocked(
        self,
    ) -> tuple[InteractionRunLocation, StoredInteractionRunStatus] | None:
        active = [
            (location, status)
            for location in self._runs.values()
            if (status := self._read_status(location)).state
            in {InteractionRunState.SCHEDULED, InteractionRunState.RUNNING}
        ]
        if not active:
            return None
        return max(active, key=lambda item: item[0].plan.created_utc)

    def _require_contained(self, path: Path) -> None:
        try:
            path.resolve().relative_to(self.interaction_root)
        except ValueError as exc:
            raise ValueError("Interaction storage path escapes interaction-tests") from exc

    @staticmethod
    def _write_new_file(destination: Path, data: bytes) -> None:
        with destination.open("xb") as output:
            output.write(data)
            output.flush()
            os.fsync(output.fileno())

    @staticmethod
    def _status_bytes(status: StoredInteractionRunStatus) -> bytes:
        payload = status.model_dump(mode="json")
        return (json.dumps(payload, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
