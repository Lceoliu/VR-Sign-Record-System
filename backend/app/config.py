from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True, slots=True)
class Settings:
    data_root: Path
    http_host: str = "0.0.0.0"
    http_port: int = 8000
    udp_host: str = "0.0.0.0"
    udp_port: int = 5005
    quest_control_port: int = 5006
    frontend_origin: str = "http://localhost:5174"
    discovery_broadcast: str = "255.255.255.255"

    @classmethod
    def from_environment(cls) -> "Settings":
        default_root = Path(__file__).resolve().parents[2] / "data"
        return cls(
            data_root=Path(os.environ.get("SIGNVR_DATA_ROOT", default_root)),
            http_host=os.environ.get("SIGNVR_HTTP_HOST", "0.0.0.0"),
            http_port=int(os.environ.get("SIGNVR_HTTP_PORT", "8000")),
            udp_host=os.environ.get("SIGNVR_UDP_HOST", "0.0.0.0"),
            udp_port=int(os.environ.get("SIGNVR_UDP_PORT", "5005")),
            quest_control_port=int(os.environ.get("SIGNVR_QUEST_CONTROL_PORT", "5006")),
            frontend_origin=os.environ.get("SIGNVR_FRONTEND_ORIGIN", "http://localhost:5174"),
            discovery_broadcast=os.environ.get("SIGNVR_DISCOVERY_BROADCAST", "255.255.255.255"),
        )

