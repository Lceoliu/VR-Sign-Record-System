from __future__ import annotations

import asyncio
import json
from datetime import datetime, timedelta, timezone

import pytest

from app.interaction_models import (
    InteractionCameraReadinessUpdate,
    InteractionQuestReadinessUpdate,
)
from app.interaction_repository import InteractionRepository
from app.interaction_service import InteractionReadinessConflict, InteractionService


def manifest_bytes(run_id: str) -> bytes:
    phase_sentences = ["001", "004", "013", "016", "019", "026"]
    payload = {
        "schema_version": 1,
        "batch_id": "pilot-readiness",
        "participant_id": "P001",
        "run_id": run_id,
        "app_session_id": "app_readiness",
        "created_utc": "2026-08-25T09:00:00Z",
        "app_version": "readiness-test",
        "git_commit": "35fcc93",
        "seed": 42,
        "assistance_condition": "TextOnly",
        "condition_assignment": {"block_index": 0, "slot_index": 1},
        "safe_password": [1, 3, 5, 7],
        "chest_button_order": ["green", "yellow", "red", "blue"],
        "phases": [
            {
                "phase_id": phase_id,
                "sentence_id": sentence_id,
                "signer_id": "wang",
                "take_id": f"take_{phase_id:03d}",
                "artifact_path": (
                    f"wang/sentence_{sentence_id}/take_{phase_id:03d}.pose.jsonl"
                ),
                "artifact_sha256": f"{phase_id:x}" * 64,
                "task_variant": {"target_id": f"target_{phase_id}"},
            }
            for phase_id, sentence_id in enumerate(phase_sentences, start=1)
        ],
    }
    return json.dumps(payload, separators=(",", ":")).encode("utf-8")


def test_presence_freshness_uses_monotonic_time_and_includes_the_five_second_boundary(
    tmp_path,
):
    async def scenario() -> None:
        utc_now = datetime(2026, 8, 25, 10, 0, tzinfo=timezone.utc)
        monotonic_now = 100.0

        service = InteractionService(
            InteractionRepository(tmp_path),
            readiness_ttl_seconds=5,
            clock=lambda: utc_now,
            monotonic_clock=lambda: monotonic_now,
        )
        quest = await service.update_quest_readiness(
            InteractionQuestReadinessUpdate(
                schema_version=1,
                quest_device_id="quest-alpha",
                ready=True,
                heartbeat_generation=1,
                heartbeat_sequence=1,
            )
        )
        camera = await service.update_camera_readiness(
            InteractionCameraReadinessUpdate(
                schema_version=1,
                participant_id="P001",
                ready=True,
                heartbeat_generation=1,
                heartbeat_sequence=1,
            )
        )
        assert quest.quest_fresh is True
        assert quest.quest_ready is True
        assert camera.camera_fresh is True
        assert camera.camera_ready is True
        assert camera.participant_fresh is True
        assert camera.participant_ready is True
        quest_seen_utc = quest.quest_last_seen_utc
        camera_seen_utc = camera.camera_last_seen_utc

        monotonic_now = 105.0
        utc_now -= timedelta(days=1)
        boundary_quest = await service.quest_readiness()
        assert boundary_quest.quest_fresh is True
        assert boundary_quest.quest_ready is True
        at_boundary = await service.camera_readiness()
        assert at_boundary.camera_fresh is True
        assert at_boundary.camera_ready is True
        assert at_boundary.participant_ready is True

        monotonic_now = 105.000001
        expired_quest = await service.quest_readiness()
        assert expired_quest.quest_fresh is False
        assert expired_quest.quest_ready is False
        expired = await service.camera_readiness()
        assert expired.camera_fresh is False
        assert expired.camera_ready is False
        assert expired.participant_ready is False
        assert expired.participant_fresh is False
        assert expired.camera_last_seen_utc == camera_seen_utc
        assert (await service.quest_readiness()).quest_last_seen_utc == quest_seen_utc

    asyncio.run(scenario())


def test_expired_quest_presence_allows_a_different_device_to_take_over(tmp_path):
    async def scenario() -> None:
        monotonic_now = 200.0
        service = InteractionService(
            InteractionRepository(tmp_path),
            monotonic_clock=lambda: monotonic_now,
        )
        first = await service.update_quest_readiness(
            InteractionQuestReadinessUpdate(
                schema_version=1,
                quest_device_id="quest-alpha",
                ready=True,
                heartbeat_generation=99,
                heartbeat_sequence=500,
            )
        )
        assert first.quest_ready is True

        monotonic_now += 5.000001
        takeover = await service.update_quest_readiness(
            InteractionQuestReadinessUpdate(
                schema_version=1,
                quest_device_id="quest-beta",
                ready=True,
                heartbeat_generation=1,
                heartbeat_sequence=1,
            )
        )

        assert takeover.accepted is True
        assert takeover.quest_ready is True
        assert takeover.quest_device_id == "quest-beta"
        assert takeover.heartbeat_generation == 1
        assert takeover.heartbeat_sequence == 1

    asyncio.run(scenario())


def test_run_admission_is_allowed_at_five_seconds_and_rejected_after_it(tmp_path):
    async def scenario() -> None:
        for label, age_seconds, should_accept in (
            ("boundary", 5.0, True),
            ("expired", 5.000001, False),
        ):
            monotonic_now = 300.0
            repository = InteractionRepository(tmp_path / label)
            service = InteractionService(
                repository,
                monotonic_clock=lambda: monotonic_now,
            )
            await service.update_quest_readiness(
                InteractionQuestReadinessUpdate(
                    schema_version=1,
                    quest_device_id="quest-alpha",
                    ready=True,
                    heartbeat_generation=1,
                    heartbeat_sequence=1,
                )
            )
            await service.update_camera_readiness(
                InteractionCameraReadinessUpdate(
                    schema_version=1,
                    participant_id="P001",
                    ready=True,
                    heartbeat_generation=1,
                    heartbeat_sequence=1,
                )
            )
            monotonic_now += age_seconds
            raw = manifest_bytes(f"run_{label}")

            if should_accept:
                accepted = await service.accept_run_if_ready(raw, "quest-alpha")
                assert accepted.run_id == "run_boundary"
            else:
                with pytest.raises(InteractionReadinessConflict, match="not ready"):
                    await service.accept_run_if_ready(raw, "quest-alpha")
                assert list(repository.interaction_root.rglob("run.manifest.json")) == []

    asyncio.run(scenario())
