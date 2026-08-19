from __future__ import annotations

import asyncio
import json
from pathlib import Path

from .models import HostState, RecordingStatus, SentenceItem, TakeItem
from .protocol import command_id, unix_ms
from .repository import RecordingRepository


def load_sentence_catalog() -> list[SentenceItem]:
    path = Path(__file__).with_name("sentence_catalog.json")
    payload = json.loads(path.read_text(encoding="utf-8"))
    sentences = [SentenceItem.model_validate(item) for item in payload]
    if len(sentences) != 300:
        raise ValueError(f"语料应为 300 句，实际为 {len(sentences)} 句")
    for index, sentence in enumerate(sentences):
        if sentence.index != index or sentence.sentence_id != f"sentence_{index + 1:03d}":
            raise ValueError(f"语料编号不连续：{sentence.sentence_id}")
    return sentences


class RecordingService:
    def __init__(self, repository: RecordingRepository) -> None:
        self._repository = repository
        self._catalog = load_sentence_catalog()
        sentences = [sentence.model_copy(deep=True) for sentence in self._catalog]
        sentences[0].status = "current"
        self._state = HostState(
            station_id=repository.station_id,
            recording_status=RecordingStatus.READY,
            current_sentence_index=0,
            sentences=sentences,
        )
        self._lock = asyncio.Lock()
        self._active_start_command_id: str | None = None
        self._retake_sentence_index: int | None = None

    async def snapshot(self) -> HostState:
        async with self._lock:
            return self._state.model_copy(deep=True)

    async def restore(self, state: HostState) -> HostState:
        async with self._lock:
            self._state = state.model_copy(deep=True)
            self._active_start_command_id = None
            self._retake_sentence_index = None
            return self._state.model_copy(deep=True)

    async def set_selected_device(self, device_id: str) -> HostState:
        async with self._lock:
            self._state.selected_device_id = device_id
            return self._state.model_copy(deep=True)

    async def set_guidance_enabled(self, enabled: bool) -> tuple[HostState, dict, str]:
        async with self._lock:
            self._state.guidance_enabled = enabled
            cmd_id = command_id()
            from .protocol import guidance_packet

            packet = guidance_packet(cmd_id=cmd_id, enabled=enabled)
            return self._state.model_copy(deep=True), packet, cmd_id

    async def set_help_requested(self, requested: bool) -> HostState:
        async with self._lock:
            self._state.help_requested = requested
            return self._state.model_copy(deep=True)

    async def create_round(self, batch_id: str, round_id: str) -> HostState:
        async with self._lock:
            self._require_ready()
            self._repository.create_round(batch_id, round_id, len(self._catalog))
            self._load_round_locked(batch_id, round_id)
            return self._state.model_copy(deep=True)

    async def select_round(self, batch_id: str, round_id: str) -> HostState:
        async with self._lock:
            self._require_ready()
            self._load_round_locked(batch_id, round_id)
            return self._state.model_copy(deep=True)

    async def select_sentence(self, sentence_index: int) -> HostState:
        async with self._lock:
            self._require_ready()
            self._require_round()
            if sentence_index >= len(self._state.sentences):
                raise ValueError("句子编号超出当前语料范围")
            self._retake_sentence_index = None
            self._select_sentence_locked(sentence_index, persist=True)
            return self._state.model_copy(deep=True)

    async def start(
        self,
        batch_id: str,
        round_id: str,
        take_id: str,
        take_index: int,
    ) -> tuple[HostState, dict, str, int]:
        async with self._lock:
            self._require_ready()
            self._require_round()
            if self._state.batch_id != batch_id or self._state.round_id != round_id:
                raise ValueError("网页选择的批次或轮次与当前录制上下文不一致")
            self._retake_sentence_index = None
            take = TakeItem(take_id=take_id, take_index=take_index, status="recording")
            self._state.current_take = take
            self._state.takes.append(take)
            sentence = self._state.sentences[self._state.current_sentence_index]
            sentence.take_count = len(self._state.takes)
            self._state.recording_status = RecordingStatus.COUNTDOWN
            start_at = unix_ms() + int(self._state.countdown_seconds * 1000)
            self._state.started_at_unix_ms = start_at
            cmd_id = command_id()
            self._active_start_command_id = cmd_id
            packet = self._command_packet("start_take", cmd_id, start_at)
            return self._state.model_copy(deep=True), packet, cmd_id, start_at

    async def mark_recording_started(self, start_command_id: str) -> HostState | None:
        async with self._lock:
            if (
                self._state.recording_status is not RecordingStatus.COUNTDOWN
                or self._active_start_command_id != start_command_id
            ):
                return None
            self._state.recording_status = RecordingStatus.RECORDING
            return self._state.model_copy(deep=True)

    async def stop(self) -> tuple[HostState, dict, str]:
        async with self._lock:
            if self._state.recording_status not in (
                RecordingStatus.COUNTDOWN,
                RecordingStatus.RECORDING,
            ):
                raise ValueError("当前没有正在进行的录制")
            self._state.recording_status = RecordingStatus.STOPPING
            if self._state.current_take:
                self._state.current_take.status = "candidate"
                self._state.takes[-1].status = "candidate"
            cmd_id = command_id()
            packet = self._command_packet("stop_take", cmd_id, None)
            self._state.recording_status = RecordingStatus.READY
            self._state.started_at_unix_ms = None
            self._active_start_command_id = None
            self._retake_sentence_index = self._state.current_sentence_index
            next_index = min(
                self._state.current_sentence_index + 1,
                len(self._state.sentences) - 1,
            )
            self._select_sentence_locked(next_index, persist=True)
            return self._state.model_copy(deep=True), packet, cmd_id

    async def reset(self) -> tuple[HostState, dict, str]:
        async with self._lock:
            self._require_round()
            if self._state.current_take:
                self._state.current_take.status = "candidate"
                self._state.takes[-1].status = "candidate"
            target_sentence_index = (
                self._retake_sentence_index
                if self._state.recording_status is RecordingStatus.READY
                and self._state.current_take is None
                and self._retake_sentence_index is not None
                else self._state.current_sentence_index
            )
            self._select_sentence_locked(target_sentence_index, persist=True)
            cmd_id = command_id()
            packet = self._command_packet("reset_take", cmd_id, None)
            self._state.recording_status = RecordingStatus.READY
            self._state.started_at_unix_ms = None
            self._active_start_command_id = None
            self._retake_sentence_index = None
            return self._state.model_copy(deep=True), packet, cmd_id

    async def refresh_after_upload(self, session_id: str, sentence_id: str) -> HostState:
        async with self._lock:
            batch_id, round_id = self._repository.resolve_session(session_id)
            if (
                round_id is None
                or self._state.batch_id != batch_id
                or self._state.round_id != round_id
            ):
                return self._state.model_copy(deep=True)
            sentence = next(
                (item for item in self._state.sentences if item.sentence_id == sentence_id),
                None,
            )
            if sentence is None:
                return self._state.model_copy(deep=True)
            completed, take_count = self._repository.sentence_progress(
                batch_id,
                round_id,
                sentence_id,
            )
            sentence.completed = completed
            sentence.take_count = take_count
            sentence.status = (
                "current"
                if sentence.index == self._state.current_sentence_index
                else "completed"
                if completed
                else "pending"
            )
            if sentence.index == self._state.current_sentence_index:
                self._state.takes = self._repository.list_takes(
                    batch_id,
                    round_id,
                    sentence_id,
                )
                if self._state.current_take is not None:
                    self._state.current_take = next(
                        (
                            take
                            for take in self._state.takes
                            if take.take_id == self._state.current_take.take_id
                        ),
                        self._state.current_take,
                    )
            return self._state.model_copy(deep=True)

    def _load_round_locked(self, batch_id: str, round_id: str) -> None:
        info = self._repository.round_info(batch_id, round_id)
        if info.total_sentences != len(self._catalog):
            raise ValueError(
                f"轮次语料数量为 {info.total_sentences}，当前语料为 {len(self._catalog)}，无法混用"
            )
        progress = self._repository.round_progress(batch_id, round_id)
        sentences: list[SentenceItem] = []
        for catalog_item in self._catalog:
            sentence = catalog_item.model_copy(deep=True)
            sentence.completed, sentence.take_count = progress.get(
                sentence.sentence_id,
                (False, 0),
            )
            sentence.status = "completed" if sentence.completed else "pending"
            sentences.append(sentence)
        self._state.batch_id = info.batch_id
        self._state.round_id = info.round_id
        self._state.session_id = info.session_id
        self._state.sentences = sentences
        self._state.current_take = None
        self._state.recording_status = RecordingStatus.READY
        self._state.started_at_unix_ms = None
        self._retake_sentence_index = None
        self._select_sentence_locked(info.current_sentence_index, persist=False)

    def _select_sentence_locked(self, sentence_index: int, *, persist: bool) -> None:
        for sentence in self._state.sentences:
            sentence.status = "completed" if sentence.completed else "pending"
        self._state.current_sentence_index = sentence_index
        current = self._state.sentences[sentence_index]
        current.status = "current"
        self._state.current_take = None
        if self._state.batch_id is not None and self._state.round_id is not None:
            self._state.takes = self._repository.list_takes(
                self._state.batch_id,
                self._state.round_id,
                current.sentence_id,
            )
            if persist:
                self._repository.update_round_current_sentence(
                    self._state.batch_id,
                    self._state.round_id,
                    sentence_index,
                )
        else:
            self._state.takes = []

    def _require_ready(self) -> None:
        if self._state.recording_status is not RecordingStatus.READY:
            raise ValueError("当前状态不能切换录制上下文")

    def _require_round(self) -> None:
        if (
            self._state.batch_id is None
            or self._state.round_id is None
            or not self._state.session_id
        ):
            raise ValueError("请先选择录制批次和轮次")

    def _command_packet(self, action: str, cmd_id: str, start_at: int | None) -> dict:
        sentence = self._state.sentences[self._state.current_sentence_index]
        take = self._state.current_take
        return {
            "type": "command",
            "version": 3,
            "command_id": cmd_id,
            "action": action,
            "session_id": self._state.session_id,
            "batch_id": self._state.batch_id,
            "round_id": self._state.round_id,
            "sentence_id": sentence.sentence_id,
            "sentence_index": sentence.index,
            "prompt": sentence.text,
            "take_id": take.take_id if take else None,
            "take_index": take.take_index if take else None,
            "start_at_unix_ms": start_at,
            "countdown_seconds": (
                self._state.countdown_seconds if action == "start_take" else 0.0
            ),
        }
