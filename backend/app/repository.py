from __future__ import annotations

import re
from pathlib import Path

import aiofiles
from fastapi import UploadFile


_SAFE_SEGMENT = re.compile(r"[^0-9A-Za-z._-]+")


def safe_segment(value: str) -> str:
    cleaned = _SAFE_SEGMENT.sub("_", value.strip()).strip("._")
    if not cleaned:
        raise ValueError("Storage identifier is empty")
    return cleaned


class RecordingRepository:
    def __init__(self, root: Path) -> None:
        self.root = root.resolve()
        self.root.mkdir(parents=True, exist_ok=True)

    def take_directory(self, session_id: str, sentence_id: str, take_id: str) -> Path:
        path = self.root / "recordings" / safe_segment(session_id) / safe_segment(sentence_id) / safe_segment(take_id)
        path.mkdir(parents=True, exist_ok=True)
        return path

    async def save_upload(self, upload: UploadFile, destination: Path) -> Path:
        async with aiofiles.open(destination, "wb") as output:
            while chunk := await upload.read(1024 * 1024):
                await output.write(chunk)
        await upload.close()
        return destination

    async def save_bytes(self, data: bytes, destination: Path) -> Path:
        async with aiofiles.open(destination, "wb") as output:
            await output.write(data)
        return destination

