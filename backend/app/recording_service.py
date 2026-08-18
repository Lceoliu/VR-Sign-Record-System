from __future__ import annotations

import asyncio
from datetime import datetime

from .models import HostState, RecordingStatus, SentenceItem, TakeItem
from .protocol import command_id, unix_ms


class RecordingService:
    def __init__(self) -> None:
        session_id = datetime.now().strftime("%Y-%m-%d-A")
        self._state = HostState(
            session_id=session_id,
            recording_status=RecordingStatus.READY,
            current_sentence_index=0,
            sentences=[
                SentenceItem(
                    sentence_id="sentence_001",
                    index=0,
                    text="请介绍一下你今天来到这里的交通方式。",
                    status="current",
                )
            ],
        )
        self._lock = asyncio.Lock()

    async def snapshot(self) -> HostState:
        async with self._lock:
            return self._state.model_copy(deep=True)

    async def restore(self, state: HostState) -> HostState:
        async with self._lock:
            self._state = state.model_copy(deep=True)
            return self._state.model_copy(deep=True)

    async def set_selected_device(self, device_id: str) -> HostState:
        async with self._lock:
            self._state.selected_device_id = device_id
            return self._state.model_copy(deep=True)

    async def import_sentences(self, values: list[str]) -> HostState:
        sentences = [value.strip() for value in values if value.strip()]
        if not sentences:
            raise ValueError("句子列表不能为空")
        async with self._lock:
            self._state.sentences = [
                SentenceItem(
                    sentence_id=f"sentence_{index + 1:03d}",
                    index=index,
                    text=text,
                    status="current" if index == 0 else "pending",
                )
                for index, text in enumerate(sentences)
            ]
            self._state.current_sentence_index = 0
            self._state.current_take = None
            self._state.takes = []
            self._state.recording_status = RecordingStatus.READY
            return self._state.model_copy(deep=True)

    async def start(self) -> tuple[HostState, dict, str, int]:
        async with self._lock:
            if self._state.recording_status is not RecordingStatus.READY:
                raise ValueError("当前状态不能开始录制")
            take_index = len(self._state.takes) + 1
            take = TakeItem(take_id=f"take_{take_index:03d}", take_index=take_index, status="recording")
            self._state.current_take = take
            self._state.takes.append(take)
            self._state.recording_status = RecordingStatus.COUNTDOWN
            start_at = unix_ms() + int(self._state.countdown_seconds * 1000)
            self._state.started_at_unix_ms = start_at
            cmd_id = command_id()
            packet = self._command_packet("start_take", cmd_id, start_at)
            return self._state.model_copy(deep=True), packet, cmd_id, start_at

    async def mark_recording_started(self) -> HostState:
        async with self._lock:
            if self._state.recording_status is RecordingStatus.COUNTDOWN:
                self._state.recording_status = RecordingStatus.RECORDING
            return self._state.model_copy(deep=True)

    async def stop(self) -> tuple[HostState, dict, str]:
        async with self._lock:
            if self._state.recording_status not in (RecordingStatus.COUNTDOWN, RecordingStatus.RECORDING):
                raise ValueError("当前没有正在进行的录制")
            self._state.recording_status = RecordingStatus.STOPPING
            if self._state.current_take:
                self._state.current_take.status = "candidate"
                self._state.takes[-1].status = "candidate"
            cmd_id = command_id()
            packet = self._command_packet("stop_take", cmd_id, None)
            self._state.recording_status = RecordingStatus.READY
            self._state.started_at_unix_ms = None
            return self._state.model_copy(deep=True), packet, cmd_id

    async def reset(self) -> tuple[HostState, dict, str]:
        async with self._lock:
            if self._state.current_take:
                self._state.current_take.status = "candidate"
                self._state.takes[-1].status = "candidate"
            cmd_id = command_id()
            packet = self._command_packet("reset_take", cmd_id, None)
            self._state.recording_status = RecordingStatus.READY
            self._state.current_take = None
            self._state.started_at_unix_ms = None
            return self._state.model_copy(deep=True), packet, cmd_id

    async def attach_files(
        self,
        take_id: str,
        *,
        pose_file: str | None = None,
        meta_file: str | None = None,
        video_file: str | None = None,
    ) -> HostState:
        async with self._lock:
            take = next((item for item in self._state.takes if item.take_id == take_id), None)
            if take is None:
                take_index = len(self._state.takes) + 1
                take = TakeItem(take_id=take_id, take_index=take_index, status="candidate")
                self._state.takes.append(take)
            if pose_file:
                take.pose_file = pose_file
            if meta_file:
                take.meta_file = meta_file
            if video_file:
                take.video_file = video_file
            return self._state.model_copy(deep=True)

    def _command_packet(self, action: str, cmd_id: str, start_at: int | None) -> dict:
        sentence = self._state.sentences[self._state.current_sentence_index]
        take = self._state.current_take
        return {
            "type": "command",
            "version": 2,
            "command_id": cmd_id,
            "action": action,
            "session_id": self._state.session_id,
            "sentence_id": sentence.sentence_id,
            "sentence_index": sentence.index,
            "prompt": sentence.text,
            "take_id": take.take_id if take else None,
            "take_index": take.take_index if take else None,
            "start_at_unix_ms": start_at,
        }
