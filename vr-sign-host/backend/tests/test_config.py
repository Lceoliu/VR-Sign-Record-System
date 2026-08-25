from __future__ import annotations

import json
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
    assert settings.quest_control_port == 5012


def test_device_allowlist_is_normalized(monkeypatch):
    monkeypatch.setenv(
        "SIGNVR_ALLOWED_DEVICE_IDS",
        " Quest-A,quest-b,,QUEST-A ",
    )

    settings = Settings.from_environment()

    assert settings.allowed_device_ids == frozenset({"quest-a", "quest-b"})


def test_portable_defaults_do_not_pin_a_device_or_shared_secret():
    config_path = Path(__file__).resolve().parents[2] / "config" / "pointing-station.json"
    config = json.loads(config_path.read_text(encoding="utf-8"))

    assert config["station_id"] == ""
    assert config["allowed_device_ids"] == []
    assert "pairing_key" not in config
