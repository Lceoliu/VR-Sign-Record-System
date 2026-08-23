from __future__ import annotations

import asyncio
import json
import time
from pathlib import Path

from .models import HostState, RecordingStatus, SentenceItem, TakeItem
from .protocol import command_id, prompt_context_packet, unix_ms
from .repository import RecordingRepository


def load_sentence_catalog() -> list[SentenceItem]:
    path = Path(__file__).with_name("sentence_catalog.json")
    payload = json.loads(path.read_text(encoding="utf-8"))
    sentences = [SentenceItem.model_validate(item) for item in payload]
    if not sentences:
        raise ValueError("默认语料不能为空")
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
        self._retake_context: tuple[str, str, int] | None = None
        self._operator_last_seen = time.monotonic()

    async def snapshot(self) -> HostState:
        async with self._lock:
            return self._state.model_copy(deep=True)

    async def restore(self, state: HostState) -> HostState:
        async with self._lock:
            self._state = state.model_copy(deep=True)
            self._active_start_command_id = None
            self._retake_context = None
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
            catalog = self._repository.ensure_batch_sentences(batch_id, self._catalog)
            existing = {item.round_id for item in self._repository.list_rounds(batch_id)}
            self._repository.create_round(batch_id, round_id, len(catalog))
            if round_id in {"round_001", "round_002"}:
                partner = "round_002" if round_id == "round_001" else "round_001"
                if partner not in existing:
                    self._repository.create_round(batch_id, partner, len(catalog))
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
            self._retake_context = None
            self._select_sentence_locked(sentence_index, persist=True)
            self._state.mode_switch_notice = None
            return self._state.model_copy(deep=True)

    async def update_sentence(self, sentence_index: int, text: str) -> HostState:
        async with self._lock:
            self._require_ready()
            self._require_round()
            cleaned = text.strip()
            if not cleaned:
                raise ValueError("句子内容不能为空")
            if sentence_index >= len(self._state.sentences):
                raise ValueError("句子编号超出当前语料范围")
            self._state.sentences[sentence_index].text = cleaned
            self._repository.save_batch_sentences(self._state.batch_id, self._state.sentences)
            self._state.mode_switch_notice = None
            return self._state.model_copy(deep=True)

    async def add_sentence(self, after_index: int, text: str) -> HostState:
        async with self._lock:
            self._require_ready()
            self._require_round()
            cleaned = text.strip()
            if not cleaned:
                raise ValueError("句子内容不能为空")
            if after_index >= len(self._state.sentences):
                raise ValueError("插入位置超出当前语料范围")
            custom_indices = [
                int(item.sentence_id.removeprefix("custom_"))
                for item in self._state.sentences
                if item.sentence_id.startswith("custom_")
                and item.sentence_id.removeprefix("custom_").isdigit()
            ]
            sentence = SentenceItem(
                sentence_id=f"custom_{max(custom_indices, default=0) + 1:03d}",
                index=after_index + 1,
                category="temporary",
                text=cleaned,
            )
            self._state.sentences.insert(after_index + 1, sentence)
            for index, item in enumerate(self._state.sentences):
                item.index = index
            self._repository.save_batch_sentences(
                self._state.batch_id,
                self._state.sentences,
            )
            self._select_sentence_locked(after_index + 1, persist=True)
            self._retake_context = None
            self._state.mode_switch_notice = None
            return self._state.model_copy(deep=True)

    async def delete_sentence(self, sentence_id: str) -> HostState:
        async with self._lock:
            self._require_ready()
            self._require_round()
            if len(self._state.sentences) == 1:
                raise ValueError("批次至少需要保留一句")
            sentence = next(
                (item for item in self._state.sentences if item.sentence_id == sentence_id),
                None,
            )
            if sentence is None:
                raise ValueError("未找到要删除的句子")
            if self._repository.sentence_has_takes(self._state.batch_id, sentence_id):
                raise ValueError("该句已有 Take，不能删除")
            remaining = [
                item for item in self._state.sentences if item.sentence_id != sentence_id
            ]
            batch_id = self._state.batch_id
            round_id = self._state.round_id
            self._repository.save_batch_sentences(batch_id, remaining)
            self._load_round_locked(batch_id, round_id)
            return self._state.model_copy(deep=True)

    async def reorder_sentences(self, sentence_ids: list[str]) -> HostState:
        async with self._lock:
            self._require_ready()
            self._require_round()
            current_by_id = {item.sentence_id: item for item in self._state.sentences}
            if len(sentence_ids) != len(current_by_id) or set(sentence_ids) != set(current_by_id):
                raise ValueError("排序必须包含当前批次的全部句子，且不能重复")
            reordered = [current_by_id[sentence_id] for sentence_id in sentence_ids]
            batch_id = self._state.batch_id
            round_id = self._state.round_id
            self._repository.save_batch_sentences(batch_id, reordered)
            self._load_round_locked(batch_id, round_id)
            return self._state.model_copy(deep=True)

    async def navigate_sentence(self, delta: int) -> HostState:
        async with self._lock:
            self._require_ready()
            self._require_round()
            if delta not in {-1, 1}:
                raise ValueError("导航方向无效")
            target = max(0, min(self._state.current_sentence_index + delta, len(self._state.sentences) - 1))
            self._select_sentence_locked(target, persist=True)
            self._retake_context = None
            self._state.mode_switch_notice = None
            return self._state.model_copy(deep=True)

    async def prompt_context(self) -> tuple[dict, str]:
        async with self._lock:
            self._require_round()
            cmd_id = command_id()
            return self._prompt_context_packet_locked(cmd_id), cmd_id

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
            self._retake_context = None
            self._state.mode_switch_notice = None
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

    async def mark_recording_started(
        self,
        start_command_id: str,
        actual_start_unix_ms: int | None = None,
    ) -> HostState | None:
        async with self._lock:
            if (
                self._state.recording_status is not RecordingStatus.COUNTDOWN
                or self._active_start_command_id != start_command_id
            ):
                return None
            self._state.recording_status = RecordingStatus.RECORDING
            if actual_start_unix_ms is not None and actual_start_unix_ms > 0:
                self._state.started_at_unix_ms = actual_start_unix_ms
            return self._state.model_copy(deep=True)

    async def fail_recording_start(self, start_command_id: str) -> HostState | None:
        async with self._lock:
            if (
                self._state.recording_status is not RecordingStatus.COUNTDOWN
                or self._active_start_command_id != start_command_id
            ):
                return None
            self._mark_current_take_candidate_locked()
            self._state.recording_status = RecordingStatus.READY
            self._state.started_at_unix_ms = None
            self._active_start_command_id = None
            self._retake_context = (
                self._state.batch_id,
                self._state.round_id,
                self._state.current_sentence_index,
            )
            return self._state.model_copy(deep=True)

    async def touch_operator(self) -> None:
        async with self._lock:
            self._operator_last_seen = time.monotonic()

    async def operator_timed_out(self, timeout_seconds: float) -> bool:
        async with self._lock:
            active = self._state.recording_status in (
                RecordingStatus.COUNTDOWN,
                RecordingStatus.RECORDING,
            )
            return active and time.monotonic() - self._operator_last_seen >= timeout_seconds

    async def start_confirmation_timed_out(self, timeout_seconds: float) -> bool:
        async with self._lock:
            return (
                self._state.recording_status is RecordingStatus.COUNTDOWN
                and self._state.started_at_unix_ms is not None
                and unix_ms() - self._state.started_at_unix_ms >= timeout_seconds * 1000
            )

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
            previous_context = (
                self._state.batch_id,
                self._state.round_id,
                self._state.current_sentence_index,
            )
            self._advance_after_stop_locked()
            self._retake_context = previous_context
            return self._state.model_copy(deep=True), packet, cmd_id

    async def abort(self) -> tuple[HostState, dict, str]:
        async with self._lock:
            if self._state.recording_status not in (
                RecordingStatus.COUNTDOWN,
                RecordingStatus.RECORDING,
                RecordingStatus.STOPPING,
            ):
                raise ValueError("当前没有需要中止的录制")
            self._mark_current_take_candidate_locked()
            cmd_id = command_id()
            packet = self._command_packet("stop_take", cmd_id, None)
            self._state.recording_status = RecordingStatus.READY
            self._state.started_at_unix_ms = None
            self._active_start_command_id = None
            self._retake_context = (
                self._state.batch_id,
                self._state.round_id,
                self._state.current_sentence_index,
            )
            return self._state.model_copy(deep=True), packet, cmd_id

    async def reset(self) -> tuple[HostState, dict, str]:
        async with self._lock:
            self._require_round()
            if self._state.current_take:
                self._state.current_take.status = "candidate"
                self._state.takes[-1].status = "candidate"
            target_context = (
                self._retake_context
                if self._state.recording_status is RecordingStatus.READY
                and self._state.current_take is None
                and self._retake_context is not None
                else (self._state.batch_id, self._state.round_id, self._state.current_sentence_index)
            )
            target_batch, target_round, target_sentence_index = target_context
            if self._state.batch_id != target_batch or self._state.round_id != target_round:
                self._load_round_locked(target_batch, target_round)
            self._select_sentence_locked(target_sentence_index, persist=True)
            cmd_id = command_id()
            packet = self._command_packet("reset_take", cmd_id, None)
            self._state.recording_status = RecordingStatus.READY
            self._state.started_at_unix_ms = None
            self._active_start_command_id = None
            self._retake_context = None
            self._state.mode_switch_notice = None
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
        catalog = self._repository.ensure_batch_sentences(batch_id, self._catalog)
        if info.total_sentences != len(catalog):
            raise ValueError(
                f"轮次语料数量为 {info.total_sentences}，当前语料为 {len(catalog)}，无法混用"
            )
        progress = self._repository.round_progress(batch_id, round_id)
        sentences: list[SentenceItem] = []
        for catalog_item in catalog:
            sentence = catalog_item.model_copy(deep=True)
            sentence.completed, sentence.take_count = progress.get(
                sentence.sentence_id,
                (False, 0),
            )
            sentence.status = "completed" if sentence.completed else "pending"
            sentences.append(sentence)
        self._state.batch_id = info.batch_id
        self._state.round_id = info.round_id
        self._state.signing_mode = info.signing_mode
        self._state.mode_switch_notice = None
        self._state.session_id = info.session_id
        self._state.sentences = sentences
        self._state.current_take = None
        self._state.recording_status = RecordingStatus.READY
        self._state.started_at_unix_ms = None
        self._retake_context = None
        self._select_sentence_locked(info.current_sentence_index, persist=False)

    def _advance_after_stop_locked(self) -> None:
        current_index = self._state.current_sentence_index
        total = len(self._state.sentences)
        block_start = current_index // 50 * 50
        block_end = min(block_start + 49, total - 1)
        if current_index < block_end:
            self._select_sentence_locked(current_index + 1, persist=True)
            self._state.mode_switch_notice = None
            return
        if self._state.round_id == "round_001":
            self._load_round_locked(self._state.batch_id, "round_002")
            self._select_sentence_locked(block_start, persist=True)
            self._state.mode_switch_notice = "粗打完成，请切换为精打"
            return
        if self._state.round_id == "round_002" and block_end < total - 1:
            self._load_round_locked(self._state.batch_id, "round_001")
            self._select_sentence_locked(block_end + 1, persist=True)
            self._state.mode_switch_notice = "精打完成，请切换为粗打"
            return
        if self._state.round_id == "round_002":
            self._state.mode_switch_notice = "全部句子已完成"
            self._select_sentence_locked(total - 1, persist=True)
            return
        self._select_sentence_locked(min(current_index + 1, total - 1), persist=True)

    def _prompt_context_packet_locked(self, cmd_id: str) -> dict:
        current_index = self._state.current_sentence_index
        sentences = self._state.sentences
        return prompt_context_packet(
            cmd_id=cmd_id,
            previous_prompt=sentences[current_index - 1].text if current_index > 0 else "",
            prompt=sentences[current_index].text,
            next_prompt=sentences[current_index + 1].text if current_index + 1 < len(sentences) else "",
            sentence_index=current_index,
            total_sentences=len(sentences),
            signing_mode=self._state.signing_mode,
            mode_switch_notice=self._state.mode_switch_notice,
        )

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

    def _mark_current_take_candidate_locked(self) -> None:
        if self._state.current_take is None:
            return
        self._state.current_take.status = "candidate"
        for take in reversed(self._state.takes):
            if take.take_id == self._state.current_take.take_id:
                take.status = "candidate"
                break

    def _command_packet(self, action: str, cmd_id: str, start_at: int | None) -> dict:
        sentence = self._state.sentences[self._state.current_sentence_index]
        take = self._state.current_take
        packet = {
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
        context = self._prompt_context_packet_locked(cmd_id)
        packet.update({
            "previous_prompt": context["previous_prompt"],
            "next_prompt": context["next_prompt"],
            "total_sentences": context["total_sentences"],
            "signing_mode": context["signing_mode"],
            "mode_switch_notice": context["mode_switch_notice"],
        })
        return packet
