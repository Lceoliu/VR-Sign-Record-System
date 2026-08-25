from __future__ import annotations

import asyncio
import json
from datetime import datetime, timezone

from app.interaction_models import (
    InteractionAbortRequest,
    InteractionCompleteRequest,
    InteractionRunAccepted,
    InteractionRunSnapshot,
    InteractionRunState,
)
from app.interaction_repository import InteractionRepository
from app.interaction_service import InteractionService, InteractionStateConflict


def manifest_bytes(run_id: str) -> bytes:
    phase_sentences = ["001", "004", "013", "016", "019", "026"]
    payload = {
        "schema_version": 1,
        "batch_id": "pilot-concurrency",
        "participant_id": "P001",
        "run_id": run_id,
        "app_session_id": "app_concurrency",
        "created_utc": "2026-08-25T09:00:00Z",
        "app_version": "concurrency-test",
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


def test_concurrent_complete_and_abort_choose_exactly_one_terminal_state(
    tmp_path,
    monkeypatch,
):
    async def scenario() -> None:
        now = datetime(2026, 8, 25, 10, 0, tzinfo=timezone.utc)
        repository = InteractionRepository(tmp_path)
        service = InteractionService(
            repository,
            start_delay_seconds=0,
            clock=lambda: now,
        )
        run_id = "run_complete_abort_race"
        await service.accept_run(manifest_bytes(run_id))
        assert (await service.mark_running(run_id)).state is InteractionRunState.RUNNING

        original_transition_status = repository.transition_status
        both_callers_arrived = asyncio.Event()
        caller_count = 0

        async def coordinated_transition(run_id, transition):
            nonlocal caller_count
            caller_count += 1
            if caller_count == 2:
                both_callers_arrived.set()
            await asyncio.wait_for(both_callers_arrived.wait(), timeout=1)
            return await original_transition_status(run_id, transition)

        # Both service calls must be in flight before either enters the real
        # lock-scoped read/decide/replace operation.
        monkeypatch.setattr(
            repository,
            "transition_status",
            coordinated_transition,
        )

        complete_task = asyncio.create_task(
            service.complete_run(
                run_id,
                InteractionCompleteRequest(schema_version=1, run_id=run_id),
            ),
            name="complete-terminal",
        )
        abort_task = asyncio.create_task(
            service.abort_run(
                run_id,
                InteractionAbortRequest(
                    schema_version=1,
                    run_id=run_id,
                    abort_reason="concurrent safety stop",
                ),
            ),
            name="abort-terminal",
        )
        results = await asyncio.gather(
            complete_task,
            abort_task,
            return_exceptions=True,
        )

        successes = [result for result in results if isinstance(result, InteractionRunSnapshot)]
        conflicts = [result for result in results if isinstance(result, InteractionStateConflict)]
        assert len(successes) == 1
        assert len(conflicts) == 1
        assert repository.status(run_id).state is successes[0].state

    asyncio.run(scenario())


def test_delayed_mark_running_never_overwrites_terminal_state(tmp_path, monkeypatch):
    async def scenario() -> None:
        now = datetime(2026, 8, 25, 10, 0, tzinfo=timezone.utc)
        repository = InteractionRepository(tmp_path)
        service = InteractionService(
            repository,
            start_delay_seconds=0,
            clock=lambda: now,
        )
        run_id = "run_running_terminal_race"
        await service.accept_run(manifest_bytes(run_id))

        original_transition_status = repository.transition_status
        mark_transition_arrived = asyncio.Event()
        release_delayed_running = asyncio.Event()

        async def delay_named_mark_running(run_id, transition):
            current = asyncio.current_task()
            if current is not None and current.get_name() == "delayed-mark-running":
                mark_transition_arrived.set()
                await asyncio.wait_for(release_delayed_running.wait(), timeout=1)
            return await original_transition_status(run_id, transition)

        monkeypatch.setattr(
            repository,
            "transition_status",
            delay_named_mark_running,
        )

        mark_task = asyncio.create_task(
            service.mark_running(run_id),
            name="delayed-mark-running",
        )
        await asyncio.wait_for(mark_transition_arrived.wait(), timeout=1)
        abort_task = asyncio.create_task(
            service.abort_run(
                run_id,
                InteractionAbortRequest(
                    schema_version=1,
                    run_id=run_id,
                    abort_reason="terminal wins",
                ),
            ),
            name="abort-terminal",
        )
        try:
            aborted = await asyncio.wait_for(abort_task, timeout=1)
        finally:
            release_delayed_running.set()
        marked = await asyncio.wait_for(mark_task, timeout=1)

        assert aborted.state is InteractionRunState.ABORTED
        assert marked.state is InteractionRunState.ABORTED
        assert repository.status(run_id).state is InteractionRunState.ABORTED

    asyncio.run(scenario())


def test_concurrent_run_registration_allows_exactly_one_active_run(
    tmp_path,
    monkeypatch,
):
    async def scenario() -> None:
        now = datetime(2026, 8, 25, 10, 0, tzinfo=timezone.utc)
        repository = InteractionRepository(tmp_path)
        service = InteractionService(repository, clock=lambda: now)
        original_create_run = repository.create_run
        both_callers_arrived = asyncio.Event()
        caller_count = 0

        async def coordinated_create_run(*args, **kwargs):
            nonlocal caller_count
            caller_count += 1
            if caller_count == 2:
                both_callers_arrived.set()
            await asyncio.wait_for(both_callers_arrived.wait(), timeout=1)
            return await original_create_run(*args, **kwargs)

        monkeypatch.setattr(repository, "create_run", coordinated_create_run)
        tasks = [
            asyncio.create_task(
                service.accept_run_if_idle(manifest_bytes(run_id)),
                name=f"register-{run_id}",
            )
            for run_id in ("run_concurrent_first", "run_concurrent_second")
        ]
        results = await asyncio.gather(*tasks, return_exceptions=True)

        successes = [result for result in results if isinstance(result, InteractionRunAccepted)]
        conflicts = [result for result in results if isinstance(result, FileExistsError)]
        assert len(successes) == 1
        assert len(conflicts) == 1
        active = await service.active_snapshot()
        assert active is not None
        assert active.run_id == successes[0].run_id
        assert len(list(repository.interaction_root.rglob("run.manifest.json"))) == 1

    asyncio.run(scenario())
