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


def test_round_survives_a_legacy_directory_rename(tmp_path):
    repository = RecordingRepository(tmp_path, "station-a")
    created = repository.create_round("batch-a", "round_003", 300)
    original = tmp_path / "recordings" / "batch-a" / "round_003"
    renamed = original.with_name("CSL-Daily-15")
    original.rename(renamed)

    reloaded = RecordingRepository(tmp_path, "station-a")

    assert reloaded.resolve_session(created.session_id) == ("batch-a", "round_003")
    assert reloaded.list_rounds("batch-a")[0].round_id == "round_003"
    assert reloaded._round_directory("batch-a", "round_003") == renamed
    with pytest.raises(FileExistsError, match="round_003"):
        reloaded.create_round("batch-a", "round_003", 300)


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


def test_start_confirmation_uses_actual_quest_time_and_failure_recovers(tmp_path):
    async def scenario() -> None:
        repository = RecordingRepository(tmp_path)
        service = RecordingService(repository)
        await service.create_round("batch-a", "round_001")

        take_id, take_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_001"
        )
        _, _, command_id, _ = await service.start(
            "batch-a", "round_001", take_id, take_index
        )
        started = await service.mark_recording_started(command_id, 123456789)
        assert started is not None
        assert started.recording_status is RecordingStatus.RECORDING
        assert started.started_at_unix_ms == 123456789

        await service.abort()
        retry_id, retry_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_001"
        )
        _, _, retry_command_id, _ = await service.start(
            "batch-a", "round_001", retry_id, retry_index
        )
        failed = await service.fail_recording_start(retry_command_id)
        assert failed is not None
        assert failed.recording_status is RecordingStatus.READY
        assert failed.current_sentence_index == 0
        assert failed.current_take is not None
        assert failed.current_take.status == "candidate"

    anyio.run(scenario)


def test_countdown_without_actual_quest_confirmation_times_out(tmp_path):
    async def scenario() -> None:
        repository = RecordingRepository(tmp_path)
        service = RecordingService(repository)
        await service.create_round("batch-a", "round_001")
        take_id, take_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_001"
        )
        await service.start("batch-a", "round_001", take_id, take_index)

        service._state.started_at_unix_ms = 1
        assert await service.start_confirmation_timed_out(5.0) is True

    anyio.run(scenario)


def test_fifty_sentence_blocks_alternate_rough_and_precise(tmp_path):
    async def scenario() -> None:
        repository = RecordingRepository(tmp_path)
        service = RecordingService(repository)
        await service.create_round("batch-a", "round_001")
        await service.select_sentence(49)
        take_id, take_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_050"
        )
        await service.start("batch-a", "round_001", take_id, take_index)

        precise, _, _ = await service.stop()
        assert precise.round_id == "round_002"
        assert precise.signing_mode == "precise"
        assert precise.current_sentence_index == 0
        assert precise.mode_switch_notice == "粗打完成，请切换为精打"

        retake, packet, _ = await service.reset()
        assert retake.round_id == "round_001"
        assert retake.current_sentence_index == 49
        assert packet["sentence_id"] == "sentence_050"

    anyio.run(scenario)


def test_final_short_block_switches_at_actual_last_sentence(tmp_path):
    async def scenario() -> None:
        repository = RecordingRepository(tmp_path)
        defaults = RecordingService(repository)._catalog[:53]
        repository.ensure_batch_sentences("short-batch", defaults)
        repository.create_round("short-batch", "round_001", 53)
        repository.create_round("short-batch", "round_002", 53)
        service = RecordingService(repository)

        await service.select_round("short-batch", "round_001")
        await service.select_sentence(52)
        take_id, take_index, _ = repository.reserve_take(
            "short-batch", "round_001", "sentence_053"
        )
        await service.start("short-batch", "round_001", take_id, take_index)
        precise, _, _ = await service.stop()
        assert precise.round_id == "round_002"
        assert precise.current_sentence_index == 50

        await service.select_sentence(52)
        take_id, take_index, _ = repository.reserve_take(
            "short-batch", "round_002", "sentence_053"
        )
        await service.start("short-batch", "round_002", take_id, take_index)
        completed, _, _ = await service.stop()
        assert completed.round_id == "round_002"
        assert completed.current_sentence_index == 52
        assert completed.mode_switch_notice == "全部句子已完成"

    anyio.run(scenario)


def test_batch_sentence_edit_and_insert_are_shared_by_both_rounds(tmp_path):
    async def scenario() -> None:
        repository = RecordingRepository(tmp_path)
        service = RecordingService(repository)
        await service.create_round("batch-a", "round_001")
        edited = await service.update_sentence(0, "修改后的第一句")
        assert edited.sentences[0].text == "修改后的第一句"
        inserted = await service.add_sentence(0, "临时添加的一句")
        assert inserted.current_sentence_index == 1
        assert inserted.sentences[1].sentence_id == "custom_001"
        assert len(inserted.sentences) == 301

        precise = await service.select_round("batch-a", "round_002")
        assert precise.sentences[0].text == "修改后的第一句"
        assert precise.sentences[1].text == "临时添加的一句"
        assert precise.sentences[1].sentence_id == "custom_001"

    anyio.run(scenario)
