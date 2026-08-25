from __future__ import annotations

from datetime import datetime, timedelta, timezone

from app.interaction_repository import InteractionRepository
from app.interaction_service import InteractionService


def test_camera_readiness_requires_a_fresh_browser_heartbeat(tmp_path):
    now = datetime(2026, 8, 25, 10, 0, tzinfo=timezone.utc)

    def clock() -> datetime:
        return now

    service = InteractionService(
        InteractionRepository(tmp_path),
        camera_readiness_ttl_seconds=5,
        clock=clock,
    )
    assert service.camera_readiness() == (False, None)

    ready, last_seen = service.set_camera_readiness(True)
    assert ready is True
    assert last_seen == now

    now += timedelta(seconds=6)
    assert service.camera_readiness() == (False, last_seen)

    ready, refreshed = service.set_camera_readiness(True)
    assert ready is True
    assert refreshed == now

    ready, stopped = service.set_camera_readiness(False)
    assert ready is False
    assert stopped == now
