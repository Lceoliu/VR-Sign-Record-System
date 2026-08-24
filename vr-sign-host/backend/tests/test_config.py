from __future__ import annotations

from pathlib import Path

import app.config as config_module
from app.config import Settings


def test_relative_data_root_is_resolved_from_host_root(monkeypatch):
    monkeypatch.setenv("SIGNVR_DATA_ROOT", "portable-data")

    settings = Settings.from_environment()

    host_root = Path(config_module.__file__).resolve().parents[2]
    assert settings.data_root == (host_root / "portable-data").resolve()


def test_default_ports_allow_the_legacy_host_to_keep_running(monkeypatch):
    monkeypatch.delenv("SIGNVR_HTTP_PORT", raising=False)
    monkeypatch.delenv("SIGNVR_UDP_PORT", raising=False)

    settings = Settings.from_environment()

    assert settings.http_port == 8011
    assert settings.udp_port == 5011
