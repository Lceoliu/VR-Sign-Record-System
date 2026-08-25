from __future__ import annotations

from pathlib import Path

import anyio
import pytest

from app.models import RecordingStatus
from app.recording_service import RecordingService, load_sentence_catalog
from app.repository import RecordingRepository


def test_pointing_catalog_has_correct_three_button_and_breaker_combinations():
    catalog = load_sentence_catalog(
        Path(__file__).parents[1] / "app" / "pointing_sentence_catalog.json"
    )

    assert len(catalog) == 31
    assert [
        sum(item.viewpoint_id == f"state_{state:02d}" for item in catalog)
        for state in range(1, 7)
    ] == [3, 9, 3, 3, 7, 6]

    button_targets = [
        tuple(item.highlight_target_ids) for item in catalog[18:25]
    ]
    assert button_targets == [
        ("industrial_button",),
        ("red_button",),
        ("alarm_button",),
        ("industrial_button", "red_button"),
        ("industrial_button", "alarm_button"),
        ("red_button", "alarm_button"),
        ("industrial_button", "red_button", "alarm_button"),
    ]

    breaker_ids = (
        "free_switch_handler_ue5",
        "free_switch_handler_ue5 (1)",
        "free_switch_handler_ue5 (2)",
    )
    assert [tuple(item.highlight_target_ids) for item in catalog[25:31]] == [
        breaker_ids
    ] * 6
    assert [item.sequence_numbers for item in catalog[25:31]] == [
        [1, 2, 3],
        [1, 3, 2],
        [2, 1, 3],
        [3, 1, 2],
        [2, 3, 1],
        [3, 2, 1],
    ]


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
        assert first_packet["host_unix_ms"] > 0
        assert first_packet["start_at_unix_ms"] >= first_packet["host_unix_ms"]
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
    first.create_round("batch-a", "round_001", 31)

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

        sync_packet, sync_command_id = await service.current_sentence_command()
        assert sync_packet["command_id"] == sync_command_id
        assert sync_packet["action"] == "select_sentence"
        assert sync_packet["sentence_id"] == "sentence_002"
        assert sync_packet["sentence_index"] == 1

        reset, packet, _ = await service.reset()
        assert reset.current_sentence_index == 0
        assert packet["sentence_id"] == "sentence_001"
        assert packet["sentence_index"] == 0
        assert packet["viewpoint_id"] == reset.sentences[0].viewpoint_id
        assert packet["prompt"] == reset.sentences[0].text

        second_take_id, second_take_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_001"
        )
        assert second_take_id == "take_002"
        assert second_take_index == 2

    anyio.run(scenario)


def test_stop_during_countdown_keeps_sentence_and_releases_take(tmp_path):
    async def scenario() -> None:
        repository = RecordingRepository(tmp_path)
        service = RecordingService(repository)
        await service.create_round("batch-a", "round_001")
        take_id, take_index, _ = repository.reserve_take(
            "batch-a", "round_001", "sentence_001"
        )
        started, _, _, _ = await service.start(
            "batch-a", "round_001", take_id, take_index
        )
        assert started.current_sentence_index == 0
        assert started.current_take is not None

        stopped, packet, _ = await service.stop()

        assert packet["action"] == "stop_take"
        assert packet["host_unix_ms"] > 0
        assert stopped.recording_status is RecordingStatus.READY
        assert stopped.current_sentence_index == 0
        assert stopped.current_take is None
        assert repository.list_takes(
            "batch-a", "round_001", "sentence_001"
        ) == []

    anyio.run(scenario)


def test_sentence_selection_builds_quest_sync_command(tmp_path):
    async def scenario() -> None:
        repository = RecordingRepository(tmp_path)
        service = RecordingService(repository)
        await service.create_round("batch-a", "round_001")

        state, packet, command_id = await service.select_sentence_command(1)

        assert state.current_sentence_index == 1
        assert packet["type"] == "command"
        assert packet["action"] == "select_sentence"
        assert packet["command_id"] == command_id
        assert packet["sentence_id"] == "sentence_002"
        assert packet["sentence_index"] == 1
        assert packet["viewpoint_id"] == state.sentences[1].viewpoint_id
        assert packet["prompt"] == state.sentences[1].text

    anyio.run(scenario)
