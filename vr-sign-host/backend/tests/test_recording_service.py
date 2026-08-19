from __future__ import annotations

import anyio
import pytest

from app.models import RecordingStatus
from app.recording_service import RecordingService
from app.repository import RecordingRepository


def test_stale_countdown_cannot_start_a_new_take_early(tmp_path):
    async def scenario() -> None:
        repository = RecordingRepository(tmp_path)
        service = RecordingService(repository)
        await service.create_round("batch-a", "round_001")
        first_take_id, first_take_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_001"
        )
        _, first_packet, first_command_id, _ = await service.start(
            "batch-a", "round_001", first_take_id, first_take_index
        )
        assert first_packet["countdown_seconds"] == 2.0
        await service.reset()
        second_take_id, second_take_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_001"
        )
        _, _, second_command_id, _ = await service.start(
            "batch-a", "round_001", second_take_id, second_take_index
        )

        assert await service.mark_recording_started(first_command_id) is None
        assert (await service.snapshot()).recording_status is RecordingStatus.COUNTDOWN

        current = await service.mark_recording_started(second_command_id)
        assert current is not None
        assert current.recording_status is RecordingStatus.RECORDING

    anyio.run(scenario)


def test_round_cannot_be_opened_by_another_station(tmp_path):
    first = RecordingRepository(tmp_path, "station-a")
    first.create_round("batch-a", "round_001", 300)

    with pytest.raises(ValueError, match="station-a"):
        RecordingRepository(tmp_path, "station-b")


def test_long_press_after_stop_returns_to_just_recorded_sentence(tmp_path):
    async def scenario() -> None:
        repository = RecordingRepository(tmp_path)
        service = RecordingService(repository)
        await service.create_round("batch-a", "round_001")
        first_take_id, first_take_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_001"
        )
        _, _, start_command_id, _ = await service.start(
            "batch-a", "round_001", first_take_id, first_take_index
        )
        await service.mark_recording_started(start_command_id)

        stopped, _, _ = await service.stop()
        assert stopped.current_sentence_index == 1

        reset, packet, _ = await service.reset()
        assert reset.current_sentence_index == 0
        assert packet["sentence_id"] == "sentence_001"

        second_take_id, second_take_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_001"
        )
        assert second_take_id == "take_002"
        assert second_take_index == 2

    anyio.run(scenario)
