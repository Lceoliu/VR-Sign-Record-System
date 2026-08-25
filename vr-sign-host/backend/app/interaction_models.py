from __future__ import annotations

import re
from datetime import datetime, timedelta
from enum import Enum
from pathlib import PurePosixPath
from typing import Annotated, Any, Literal

from pydantic import (
    BaseModel,
    ConfigDict,
    Field,
    JsonValue,
    StrictBool,
    StrictInt,
    StrictStr,
    field_validator,
    model_validator,
)


INTERACTION_SCHEMA_VERSION = 1
REQUIRED_ARTIFACTS = ("events", "poses", "objects", "summary", "webcam")
ARTIFACT_FILENAMES = {
    "events": "events.jsonl",
    "poses": "poses.jsonl",
    "objects": "objects.jsonl",
    "summary": "summary.json",
    "webcam": "webcam.webm",
}
PHASE_SENTENCE_RANGES = {
    1: (1, 3),
    2: (4, 12),
    3: (13, 15),
    4: (16, 18),
    5: (19, 25),
    6: (26, 31),
}
_STORAGE_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$", re.ASCII)
_PATH_SEGMENT = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", re.ASCII)
_SHA256 = re.compile(r"^[0-9a-fA-F]{64}$", re.ASCII)
_SENTENCE_ID = re.compile(r"^[0-9]{3}$", re.ASCII)
_EVENT_TYPE = re.compile(r"^[a-z][a-z0-9_]{0,79}$", re.ASCII)
_WINDOWS_RESERVED_NAMES = {
    "CON",
    "PRN",
    "AUX",
    "NUL",
    *(f"COM{index}" for index in range(1, 10)),
    *(f"LPT{index}" for index in range(1, 10)),
}

NonNegativeInt = Annotated[StrictInt, Field(ge=0)]
SchemaVersion = Annotated[StrictInt, Field(ge=1, le=1)]
HeartbeatGeneration = Annotated[StrictInt, Field(ge=0, le=2**63 - 1)]
HeartbeatSequence = Annotated[StrictInt, Field(ge=1, le=2**63 - 1)]


def validate_interaction_id(value: str, label: str = "identifier") -> str:
    if not _STORAGE_ID.fullmatch(value):
        raise ValueError(
            f"{label} must be 1-80 ASCII letters, digits, dot, underscore, or hyphen"
        )
    if value in {".", ".."} or value.endswith((" ", ".")):
        raise ValueError(f"{label} is not a safe storage identifier")
    if value.split(".", 1)[0].upper() in _WINDOWS_RESERVED_NAMES:
        raise ValueError(f"{label} is reserved by Windows")
    return value


def validate_relative_artifact_path(value: str) -> str:
    if not value or len(value) > 512 or "\\" in value or ":" in value:
        raise ValueError("artifact_path must be a safe relative POSIX path")
    path = PurePosixPath(value)
    if path.is_absolute() or path.as_posix() != value:
        raise ValueError("artifact_path must be a normalized relative POSIX path")
    if len(path.parts) < 2 or any(
        part in {"", ".", ".."} or not _PATH_SEGMENT.fullmatch(part)
        for part in path.parts
    ):
        raise ValueError("artifact_path contains an unsafe path segment")
    return value


def require_utc(value: datetime, label: str) -> datetime:
    if value.tzinfo is None or value.utcoffset() != timedelta(0):
        raise ValueError(f"{label} must include an explicit UTC offset")
    return value


class AssistanceCondition(str, Enum):
    TEXT_AND_POINTING = "TextAndPointing"
    TEXT_ONLY = "TextOnly"
    SIGN_ONLY = "SignOnly"


class InteractionRunState(str, Enum):
    SCHEDULED = "Scheduled"
    RUNNING = "Running"
    COMPLETED = "Completed"
    ABORTED = "Aborted"


class InteractionArtifactType(str, Enum):
    EVENTS = "events"
    POSES = "poses"
    OBJECTS = "objects"
    SUMMARY = "summary"
    WEBCAM = "webcam"


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid")


class ConditionAssignment(StrictModel):
    block_index: NonNegativeInt
    slot_index: Annotated[StrictInt, Field(ge=0, le=2)]


class InteractionPhasePlan(StrictModel):
    phase_id: Annotated[StrictInt, Field(ge=1, le=6)]
    sentence_id: StrictStr
    signer_id: StrictStr
    take_id: StrictStr
    artifact_path: StrictStr
    artifact_sha256: StrictStr
    task_variant: dict[StrictStr, JsonValue]

    @field_validator("sentence_id")
    @classmethod
    def validate_sentence_id(cls, value: str) -> str:
        if not _SENTENCE_ID.fullmatch(value):
            raise ValueError("sentence_id must contain exactly three ASCII digits")
        return value

    @field_validator("signer_id", "take_id")
    @classmethod
    def validate_storage_identifiers(cls, value: str, info: Any) -> str:
        return validate_interaction_id(value, info.field_name)

    @field_validator("artifact_path")
    @classmethod
    def validate_artifact_path(cls, value: str) -> str:
        return validate_relative_artifact_path(value)

    @field_validator("artifact_sha256")
    @classmethod
    def validate_artifact_hash(cls, value: str) -> str:
        if not _SHA256.fullmatch(value):
            raise ValueError("artifact_sha256 must contain exactly 64 hexadecimal characters")
        return value

    @model_validator(mode="after")
    def validate_task_variant(self) -> "InteractionPhasePlan":
        if not self.task_variant:
            raise ValueError("task_variant must contain the resolved phase target")
        if any(not key or len(key) > 80 for key in self.task_variant):
            raise ValueError("task_variant keys must contain 1-80 characters")
        artifact_parts = PurePosixPath(self.artifact_path).parts
        if artifact_parts[:2] != (self.signer_id, f"sentence_{self.sentence_id}"):
            raise ValueError(
                "artifact_path must start with signer_id/sentence_<sentence_id>"
            )
        return self


class InteractionRunPlan(StrictModel):
    schema_version: SchemaVersion
    batch_id: StrictStr
    participant_id: StrictStr
    run_id: StrictStr
    app_session_id: StrictStr
    created_utc: datetime
    app_version: StrictStr
    git_commit: StrictStr
    seed: StrictInt
    assistance_condition: AssistanceCondition
    condition_assignment: ConditionAssignment
    safe_password: list[Annotated[StrictInt, Field(ge=0, le=9)]]
    chest_button_order: list[StrictStr]
    phases: list[InteractionPhasePlan]

    @field_validator("batch_id", "participant_id", "run_id", "app_session_id")
    @classmethod
    def validate_ids(cls, value: str, info: Any) -> str:
        return validate_interaction_id(value, info.field_name)

    @field_validator("created_utc")
    @classmethod
    def validate_created_utc(cls, value: datetime) -> datetime:
        return require_utc(value, "created_utc")

    @field_validator("app_version", "git_commit")
    @classmethod
    def validate_build_identity(cls, value: str, info: Any) -> str:
        if not value or value != value.strip() or len(value) > 160:
            raise ValueError(f"{info.field_name} must contain 1-160 non-padding characters")
        return value

    @model_validator(mode="after")
    def validate_resolved_plan(self) -> "InteractionRunPlan":
        if len(self.safe_password) != 4 or len(set(self.safe_password)) != 4:
            raise ValueError("safe_password must contain four unique digits")
        expected_buttons = {"blue", "red", "yellow", "green"}
        if len(self.chest_button_order) != 4 or set(self.chest_button_order) != expected_buttons:
            raise ValueError("chest_button_order must be a permutation of the four button IDs")
        if [phase.phase_id for phase in self.phases] != list(range(1, 7)):
            raise ValueError("phases must contain exactly six ordered entries with IDs 1 through 6")
        for phase in self.phases:
            lower, upper = PHASE_SENTENCE_RANGES[phase.phase_id]
            sentence_number = int(phase.sentence_id)
            if not lower <= sentence_number <= upper:
                raise ValueError(
                    f"phase {phase.phase_id} sentence_id must be in {lower:03d}-{upper:03d}"
                )
            if phase.signer_id != "wang":
                raise ValueError("Contract V1 pilot phases must use signer_id 'wang'")
        return self


class InteractionEvent(StrictModel):
    schema_version: SchemaVersion
    run_id: StrictStr
    phase_id: Annotated[StrictInt, Field(ge=0, le=6)] | None
    event_seq: NonNegativeInt
    monotonic_time_s: Annotated[float, Field(ge=0, allow_inf_nan=False)]
    utc_time: datetime
    frame: NonNegativeInt
    event_type: StrictStr
    actor_id: StrictStr | None
    target_id: StrictStr | None
    payload: dict[StrictStr, JsonValue]

    @field_validator("run_id")
    @classmethod
    def validate_run_id(cls, value: str) -> str:
        return validate_interaction_id(value, "run_id")

    @field_validator("utc_time")
    @classmethod
    def validate_event_utc(cls, value: datetime) -> datetime:
        return require_utc(value, "utc_time")

    @field_validator("event_type")
    @classmethod
    def validate_event_type(cls, value: str) -> str:
        if not _EVENT_TYPE.fullmatch(value):
            raise ValueError("event_type must be a lower_snake_case identifier")
        return value


class InteractionCompleteRequest(StrictModel):
    schema_version: SchemaVersion
    run_id: StrictStr
    completed_utc: datetime | None = None

    @field_validator("run_id")
    @classmethod
    def validate_run_id(cls, value: str) -> str:
        return validate_interaction_id(value, "run_id")

    @field_validator("completed_utc")
    @classmethod
    def validate_completed_utc(cls, value: datetime | None) -> datetime | None:
        return require_utc(value, "completed_utc") if value is not None else None


class InteractionAbortRequest(StrictModel):
    schema_version: SchemaVersion
    run_id: StrictStr
    abort_reason: StrictStr = Field(min_length=1, max_length=500)
    aborted_utc: datetime | None = None

    @field_validator("run_id")
    @classmethod
    def validate_run_id(cls, value: str) -> str:
        return validate_interaction_id(value, "run_id")

    @field_validator("abort_reason")
    @classmethod
    def validate_abort_reason(cls, value: str) -> str:
        if value != value.strip():
            raise ValueError("abort_reason cannot start or end with whitespace")
        return value

    @field_validator("aborted_utc")
    @classmethod
    def validate_aborted_utc(cls, value: datetime | None) -> datetime | None:
        return require_utc(value, "aborted_utc") if value is not None else None


class InteractionRunSnapshot(StrictModel):
    schema_version: SchemaVersion = INTERACTION_SCHEMA_VERSION
    batch_id: str
    participant_id: str
    run_id: str
    state: InteractionRunState
    start_at_utc: datetime
    missing_artifacts: list[str]
    acknowledged: bool
    terminal_utc: datetime | None = None
    abort_reason: str | None = None


class StoredInteractionRunStatus(StrictModel):
    schema_version: SchemaVersion = INTERACTION_SCHEMA_VERSION
    run_id: str
    state: InteractionRunState
    start_at_utc: datetime
    terminal_utc: datetime | None = None
    abort_reason: str | None = None

    @field_validator("run_id")
    @classmethod
    def validate_run_id(cls, value: str) -> str:
        return validate_interaction_id(value, "run_id")

    @field_validator("start_at_utc", "terminal_utc")
    @classmethod
    def validate_status_utc(cls, value: datetime | None, info: Any) -> datetime | None:
        return require_utc(value, info.field_name) if value is not None else None


class InteractionRunAccepted(StrictModel):
    accepted: Literal[True] = True
    run_id: str
    start_at_utc: datetime
    missing_artifacts: list[str]
    state: InteractionRunState


class InteractionAck(StrictModel):
    run_id: str
    state: InteractionRunState
    acknowledged: bool
    missing_artifacts: list[str]


class InteractionCameraReadinessUpdate(StrictModel):
    schema_version: SchemaVersion
    participant_id: StrictStr | None
    ready: StrictBool
    heartbeat_generation: HeartbeatGeneration
    heartbeat_sequence: HeartbeatSequence

    @field_validator("participant_id")
    @classmethod
    def validate_participant_id(cls, value: str | None) -> str | None:
        return (
            validate_interaction_id(value, "participant_id")
            if value is not None
            else None
        )

    @model_validator(mode="after")
    def require_participant_when_ready(self) -> "InteractionCameraReadinessUpdate":
        if self.ready and self.participant_id is None:
            raise ValueError("participant_id is required when camera ready is true")
        return self


class InteractionCameraReadinessStatus(StrictModel):
    schema_version: SchemaVersion = INTERACTION_SCHEMA_VERSION
    accepted: bool
    camera_fresh: bool
    camera_ready: bool
    camera_last_seen_utc: datetime | None
    participant_ready: bool
    participant_fresh: bool
    participant_id: str | None
    participant_last_seen_utc: datetime | None
    heartbeat_generation: int | None
    heartbeat_sequence: int | None


class InteractionQuestReadinessUpdate(StrictModel):
    schema_version: SchemaVersion
    quest_device_id: StrictStr
    ready: StrictBool
    heartbeat_generation: HeartbeatGeneration
    heartbeat_sequence: HeartbeatSequence

    @field_validator("quest_device_id")
    @classmethod
    def validate_quest_device_id(cls, value: str) -> str:
        return validate_interaction_id(value, "quest_device_id")


class InteractionQuestReadinessStatus(StrictModel):
    schema_version: SchemaVersion = INTERACTION_SCHEMA_VERSION
    accepted: bool
    quest_fresh: bool
    quest_ready: bool
    quest_device_id: str | None
    quest_last_seen_utc: datetime | None
    heartbeat_generation: int | None
    heartbeat_sequence: int | None


class InteractionReadiness(StrictModel):
    schema_version: SchemaVersion = INTERACTION_SCHEMA_VERSION
    backend_ready: bool
    storage_ready: bool
    storage_error: str | None
    quest_fresh: bool
    quest_ready: bool
    camera_fresh: bool
    camera_ready: bool
    participant_fresh: bool
    participant_ready: bool
    ready: bool
    quest_device_id: str | None
    quest_last_seen_utc: datetime | None
    camera_last_seen_utc: datetime | None
    participant_id: str | None
    participant_last_seen_utc: datetime | None
    server_utc: datetime
    interaction_root: str
    active_run: InteractionRunSnapshot | None
