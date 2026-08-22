from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True, slots=True)
class Settings:
    data_root: Path
    review_root: Path | None = None
    station_id: str = "development"
    http_host: str = "0.0.0.0"
    http_port: int = 8000
    udp_host: str = "0.0.0.0"
    udp_port: int = 5005
    quest_control_port: int = 5006
    frontend_origin: str = "http://localhost:5174"
    discovery_broadcast: str = "255.255.255.255"
    operator_heartbeat_timeout_seconds: float = 8.0
    start_confirmation_timeout_seconds: float = 5.0

    @classmethod
    def from_environment(cls) -> "Settings":
        default_root = Path(__file__).resolve().parents[2] / "data"
        data_root = Path(os.environ.get("SIGNVR_DATA_ROOT", default_root))
        return cls(
            data_root=data_root,
            review_root=Path(os.environ.get("SIGNVR_REVIEW_ROOT", data_root)),
            station_id=os.environ.get("SIGNVR_STATION_ID", "development").strip(),
            http_host=os.environ.get("SIGNVR_HTTP_HOST", "0.0.0.0"),
            http_port=int(os.environ.get("SIGNVR_HTTP_PORT", "8000")),
            udp_host=os.environ.get("SIGNVR_UDP_HOST", "0.0.0.0"),
            udp_port=int(os.environ.get("SIGNVR_UDP_PORT", "5005")),
            quest_control_port=int(os.environ.get("SIGNVR_QUEST_CONTROL_PORT", "5006")),
            frontend_origin=os.environ.get("SIGNVR_FRONTEND_ORIGIN", "http://localhost:5174"),
            discovery_broadcast=os.environ.get("SIGNVR_DISCOVERY_BROADCAST", "255.255.255.255"),
            operator_heartbeat_timeout_seconds=float(
                os.environ.get("SIGNVR_OPERATOR_HEARTBEAT_TIMEOUT_SECONDS", "8")
            ),
            start_confirmation_timeout_seconds=float(
                os.environ.get("SIGNVR_START_CONFIRMATION_TIMEOUT_SECONDS", "5")
            ),
        )
