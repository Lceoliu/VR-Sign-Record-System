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
    last_seen_unix_ms: int
    preview_frames: int = 0
    pose_packets: int = 0


class SentenceItem(BaseModel):
    sentence_id: str
    index: int
    text: str
    status: Literal["completed", "current", "pending"] = "pending"


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
    session_id: str
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


class SentenceImportRequest(BaseModel):
    sentences: list[str] = Field(min_length=1)


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

