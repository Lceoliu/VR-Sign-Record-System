from __future__ import annotations

from enum import StrEnum
from typing import Any, Literal

from pydantic import BaseModel, Field


class RecordingStatus(StrEnum):
    READY = "ready"
    COUNTDOWN = "countdown"
    RECORDING = "recording"
    STOPPING = "stopping"


class DeviceInfo(BaseModel):
    device_id: str
    name: str
    model: str = "Quest"
    app_version: str = "unknown"
    ip: str
    control_port: int = 5006
    capabilities: list[str] = Field(default_factory=list)
    state: str = "available"
    selected: bool = False
    paired: bool = False
    paired_station_id: str | None = None
    last_seen_unix_ms: int
    preview_frames: int = 0
    pose_packets: int = 0


class SentenceItem(BaseModel):
    sentence_id: str
    index: int
    category: str
    text: str
    status: Literal["completed", "current", "pending"] = "pending"
    completed: bool = False
    take_count: int = 0


class TakeQuality(BaseModel):
    """Measured hand tracking quality for a take, read from its meta.json."""

    frames: int = 0
    clean_ratio: float = 0.0
    left_tracked_ratio: float = 0.0
    right_tracked_ratio: float = 0.0
    left_inside_ratio: float = 0.0
    right_inside_ratio: float = 0.0
    guidance_enabled: bool = True


class TakeItem(BaseModel):
    take_id: str
    take_index: int
    status: Literal["recording", "candidate", "complete"] = "candidate"
    pose_file: str | None = None
    meta_file: str | None = None
    video_file: str | None = None
    quality: TakeQuality | None = None


class HostState(BaseModel):
    service_online: bool = True
    station_id: str
    session_id: str = ""
    batch_id: str | None = None
    round_id: str | None = None
    recording_status: RecordingStatus
    selected_device_id: str | None = None
    current_sentence_index: int
    sentences: list[SentenceItem]
    current_take: TakeItem | None = None
    takes: list[TakeItem] = Field(default_factory=list)
    countdown_seconds: float = 2.0
    started_at_unix_ms: int | None = None
    # Boundary guidance is toggled per recording pass so the same teacher can be
    # recorded with and without it for the A/B comparison.
    guidance_enabled: bool = True
    help_requested: bool = False


class RoundInfo(BaseModel):
    station_id: str
    batch_id: str
    round_id: str
    session_id: str
    current_sentence_index: int
    completed_sentences: int
    total_sentences: int
    created_at_unix_ms: int
    updated_at_unix_ms: int


class RoundCreateRequest(BaseModel):
    round_id: str = Field(min_length=1, max_length=80)


class SentenceSelectRequest(BaseModel):
    sentence_index: int = Field(ge=0)


class StartRecordingRequest(BaseModel):
    batch_id: str = Field(min_length=1, max_length=80)
    round_id: str = Field(min_length=1, max_length=80)


class ReviewLabelRequest(BaseModel):
    dataset: str = Field(min_length=1, max_length=80)
    item_id: str = Field(min_length=1, max_length=320)
    video_issue: bool = False
    sentence_issue: bool = False


class DeviceSelectResponse(BaseModel):
    selected: DeviceInfo
    command_id: str


class RecordingCommandResponse(BaseModel):
    action: str
    state: HostState
    command_id: str
    start_at_unix_ms: int | None = None


class EventMessage(BaseModel):
    type: str
    payload: dict[str, Any] = Field(default_factory=dict)
