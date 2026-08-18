from __future__ import annotations

import asyncio
import json
import os
import re
import time
import uuid
from pathlib import Path

import aiofiles
from fastapi import UploadFile

from .models import RoundInfo, TakeItem, TakeQuality


_TAKE_DIRECTORY = re.compile(r"^take_(\d+)$")
_ROUND_DIRECTORY = re.compile(r"^round_(\d+)$")
_ROUND_MANIFEST = "round.json"
_WINDOWS_RESERVED_NAMES = {
    "CON",
    "PRN",
    "AUX",
    "NUL",
    *(f"COM{index}" for index in range(1, 10)),
    *(f"LPT{index}" for index in range(1, 10)),
}
_WINDOWS_INVALID_CHARACTERS = frozenset('<>:"/\\|?*')


def safe_segment(value: str) -> str:
    cleaned = value.strip()
    if not cleaned or cleaned in {".", ".."}:
        raise ValueError("Storage identifier is empty")
    if len(cleaned) > 80:
        raise ValueError("Storage identifier is longer than 80 characters")
    if cleaned.endswith((" ", ".")):
        raise ValueError("Storage identifier cannot end with a space or dot")
    if any(character in _WINDOWS_INVALID_CHARACTERS or ord(character) < 32 for character in cleaned):
        raise ValueError("Storage identifier contains an invalid path character")
    if cleaned.split(".", 1)[0].upper() in _WINDOWS_RESERVED_NAMES:
        raise ValueError("Storage identifier is reserved by Windows")
    return cleaned


class RecordingRepository:
    def __init__(self, root: Path) -> None:
        self.root = root.resolve()
        self.root.mkdir(parents=True, exist_ok=True)
        self.recordings_root = self.root / "recordings"
        self.recordings_root.mkdir(parents=True, exist_ok=True)
        self._write_lock = asyncio.Lock()
        self._sessions: dict[str, tuple[str, str]] = {}
        self._load_round_sessions()

    def list_batches(self) -> list[str]:
        return sorted(
            (path.name for path in self.recordings_root.iterdir() if path.is_dir()),
            key=str.casefold,
        )

    def list_rounds(self, batch_id: str) -> list[RoundInfo]:
        batch_directory = self.recordings_root / safe_segment(batch_id)
        if not batch_directory.exists():
            return []
        rounds = [
            self.round_info(batch_id, path.name)
            for path in batch_directory.iterdir()
            if path.is_dir() and (path / _ROUND_MANIFEST).is_file()
        ]
        return sorted(rounds, key=lambda item: self._round_sort_key(item.round_id))

    def suggested_round_id(self, batch_id: str) -> str:
        existing = {round_info.round_id for round_info in self.list_rounds(batch_id)}
        index = 1
        while f"round_{index:03d}" in existing:
            index += 1
        return f"round_{index:03d}"

    def create_round(self, batch_id: str, round_id: str, total_sentences: int) -> RoundInfo:
        safe_batch = safe_segment(batch_id)
        safe_round = safe_segment(round_id)
        batch_directory = self.recordings_root / safe_batch
        batch_directory.mkdir(parents=True, exist_ok=True)
        round_directory = batch_directory / safe_round
        round_directory.mkdir()
        now = int(time.time() * 1000)
        manifest = {
            "version": 1,
            "batch_id": safe_batch,
            "round_id": safe_round,
            "session_id": f"session_{uuid.uuid4().hex}",
            "current_sentence_index": 0,
            "total_sentences": total_sentences,
            "created_at_unix_ms": now,
            "updated_at_unix_ms": now,
        }
        self._write_round_manifest(round_directory, manifest)
        self._sessions[manifest["session_id"]] = (safe_batch, safe_round)
        return self.round_info(safe_batch, safe_round)

    def round_info(self, batch_id: str, round_id: str) -> RoundInfo:
        manifest = self._read_round_manifest(self._round_directory(batch_id, round_id))
        progress = self.round_progress(batch_id, round_id)
        return RoundInfo(
            batch_id=manifest["batch_id"],
            round_id=manifest["round_id"],
            session_id=manifest["session_id"],
            current_sentence_index=manifest["current_sentence_index"],
            completed_sentences=sum(1 for completed, _ in progress.values() if completed),
            total_sentences=manifest["total_sentences"],
            created_at_unix_ms=manifest["created_at_unix_ms"],
            updated_at_unix_ms=manifest["updated_at_unix_ms"],
        )

    def update_round_current_sentence(self, batch_id: str, round_id: str, sentence_index: int) -> None:
        round_directory = self._round_directory(batch_id, round_id)
        manifest = self._read_round_manifest(round_directory)
        if sentence_index >= manifest["total_sentences"]:
            raise ValueError("句子编号超出当前语料范围")
        manifest["current_sentence_index"] = sentence_index
        manifest["updated_at_unix_ms"] = int(time.time() * 1000)
        self._write_round_manifest(round_directory, manifest)

    def round_progress(self, batch_id: str, round_id: str) -> dict[str, tuple[bool, int]]:
        round_directory = self._round_directory(batch_id, round_id)
        progress: dict[str, tuple[bool, int]] = {}
        for sentence_directory in round_directory.iterdir():
            if not sentence_directory.is_dir() or not sentence_directory.name.startswith("sentence_"):
                continue
            takes = [
                path
                for path in sentence_directory.iterdir()
                if path.is_dir() and _TAKE_DIRECTORY.fullmatch(path.name)
            ]
            progress[sentence_directory.name] = (
                any(self._take_is_complete(path) for path in takes),
                len(takes),
            )
        return progress

    def sentence_progress(self, batch_id: str, round_id: str, sentence_id: str) -> tuple[bool, int]:
        sentence_directory = self._round_directory(batch_id, round_id) / safe_segment(sentence_id)
        if not sentence_directory.exists():
            return False, 0
        takes = [
            path
            for path in sentence_directory.iterdir()
            if path.is_dir() and _TAKE_DIRECTORY.fullmatch(path.name)
        ]
        return any(self._take_is_complete(path) for path in takes), len(takes)

    def list_takes(self, batch_id: str, round_id: str, sentence_id: str) -> list[TakeItem]:
        sentence_directory = self._round_directory(batch_id, round_id) / safe_segment(sentence_id)
        if not sentence_directory.exists():
            return []
        take_directories = sorted(
            (
                (int(match.group(1)), path)
                for path in sentence_directory.iterdir()
                if path.is_dir() and (match := _TAKE_DIRECTORY.fullmatch(path.name))
            ),
            key=lambda item: item[0],
        )
        return [self._take_item(index, path) for index, path in take_directories]

    def reserve_take(
        self,
        batch_id: str,
        round_id: str,
        sentence_id: str,
    ) -> tuple[str, int, Path]:
        sentence_directory = self._round_directory(batch_id, round_id) / safe_segment(sentence_id)
        sentence_directory.mkdir(parents=True, exist_ok=True)
        existing_indices = [
            int(match.group(1))
            for path in sentence_directory.iterdir()
            if path.is_dir() and (match := _TAKE_DIRECTORY.fullmatch(path.name))
        ]
        take_index = max(existing_indices, default=0) + 1
        while True:
            take_id = f"take_{take_index:03d}"
            destination = sentence_directory / take_id
            try:
                destination.mkdir()
            except FileExistsError:
                take_index += 1
                continue
            return take_id, take_index, destination

    def take_directory(self, session_id: str, sentence_id: str, take_id: str) -> Path:
        batch_id, round_id = self.resolve_session(session_id)
        if round_id is None:
            base = self.recordings_root / batch_id
        else:
            base = self._round_directory(batch_id, round_id)
        path = base / safe_segment(sentence_id) / safe_segment(take_id)
        path.mkdir(parents=True, exist_ok=True)
        return path

    def resolve_session(self, session_id: str) -> tuple[str, str | None]:
        safe_session = safe_segment(session_id)
        location = self._sessions.get(safe_session)
        if location is not None:
            return location
        legacy_directory = self.recordings_root / safe_session
        if legacy_directory.is_dir():
            return safe_session, None
        raise ValueError("未知录制会话，请在网页端重新选择轮次")

    def _load_round_sessions(self) -> None:
        for batch_directory in self.recordings_root.iterdir():
            if not batch_directory.is_dir():
                continue
            for round_directory in batch_directory.iterdir():
                if not round_directory.is_dir() or not (round_directory / _ROUND_MANIFEST).is_file():
                    continue
                manifest = self._read_round_manifest(round_directory)
                self._sessions[manifest["session_id"]] = (
                    manifest["batch_id"],
                    manifest["round_id"],
                )

    def _round_directory(self, batch_id: str, round_id: str) -> Path:
        directory = self.recordings_root / safe_segment(batch_id) / safe_segment(round_id)
        if not (directory / _ROUND_MANIFEST).is_file():
            raise FileNotFoundError(f"未找到轮次：{round_id}")
        return directory

    def _read_round_manifest(self, round_directory: Path) -> dict:
        payload = json.loads((round_directory / _ROUND_MANIFEST).read_text(encoding="utf-8"))
        required = {
            "batch_id",
            "round_id",
            "session_id",
            "current_sentence_index",
            "total_sentences",
            "created_at_unix_ms",
            "updated_at_unix_ms",
        }
        missing = required.difference(payload)
        if missing:
            raise ValueError(f"轮次清单缺少字段：{', '.join(sorted(missing))}")
        if payload["batch_id"] != round_directory.parent.name or payload["round_id"] != round_directory.name:
            raise ValueError(f"轮次清单与目录不匹配：{round_directory}")
        safe_segment(str(payload["session_id"]))
        return payload

    def _write_round_manifest(self, round_directory: Path, manifest: dict) -> None:
        destination = round_directory / _ROUND_MANIFEST
        temporary = round_directory / f".{_ROUND_MANIFEST}.{uuid.uuid4().hex}.tmp"
        temporary.write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        os.replace(temporary, destination)

    def _take_is_complete(self, take_directory: Path) -> bool:
        take_id = take_directory.name
        return all(
            (take_directory / f"{take_id}.{suffix}").is_file()
            for suffix in ("pose.jsonl", "meta.json", "camera.webm")
        )

    def _take_item(self, take_index: int, take_directory: Path) -> TakeItem:
        take_id = take_directory.name
        pose_path = take_directory / f"{take_id}.pose.jsonl"
        meta_path = take_directory / f"{take_id}.meta.json"
        video_path = take_directory / f"{take_id}.camera.webm"
        return TakeItem(
            take_id=take_id,
            take_index=take_index,
            status="complete" if self._take_is_complete(take_directory) else "candidate",
            pose_file=self._relative_file(pose_path),
            meta_file=self._relative_file(meta_path),
            video_file=self._relative_file(video_path),
            quality=self._read_quality(meta_path),
        )

    def _relative_file(self, path: Path) -> str | None:
        return str(path.relative_to(self.root)) if path.is_file() else None

    def _read_quality(self, meta_path: Path) -> TakeQuality | None:
        if not meta_path.is_file():
            return None
        payload = json.loads(meta_path.read_text(encoding="utf-8"))
        summary = payload.get("hand_capture_quality")
        if not isinstance(summary, dict) or not summary.get("frames"):
            return None
        return TakeQuality(
            frames=int(summary.get("frames", 0)),
            clean_ratio=float(summary.get("clean_ratio", 0.0)),
            left_tracked_ratio=float(summary.get("left_tracked_ratio", 0.0)),
            right_tracked_ratio=float(summary.get("right_tracked_ratio", 0.0)),
            left_inside_ratio=float(summary.get("left_inside_ratio", 0.0)),
            right_inside_ratio=float(summary.get("right_inside_ratio", 0.0)),
            guidance_enabled=bool(summary.get("guidance_enabled", True)),
        )

    @staticmethod
    def _round_sort_key(round_id: str) -> tuple[int, int | str]:
        match = _ROUND_DIRECTORY.fullmatch(round_id)
        return (0, int(match.group(1))) if match else (1, round_id.casefold())

    async def save_upload(self, upload: UploadFile, destination: Path) -> Path:
        return (await self.save_uploads([(upload, destination)]))[0]

    async def save_uploads(self, uploads: list[tuple[UploadFile, Path]]) -> list[Path]:
        staged: list[tuple[Path, Path]] = []
        try:
            for upload, destination in uploads:
                temporary = destination.parent / f".{destination.name}.{uuid.uuid4().hex}.uploading"
                staged.append((temporary, destination))
                try:
                    async with aiofiles.open(temporary, "xb") as output:
                        while chunk := await upload.read(1024 * 1024):
                            await output.write(chunk)
                finally:
                    await upload.close()

            async with self._write_lock:
                conflicts = [destination for _, destination in staged if destination.exists()]
                if conflicts:
                    raise FileExistsError(str(conflicts[0]))

                published: list[Path] = []
                try:
                    for temporary, destination in staged:
                        os.link(temporary, destination)
                        published.append(destination)
                except BaseException:
                    for destination in published:
                        destination.unlink(missing_ok=True)
                    raise
            return [destination for _, destination in staged]
        finally:
            for temporary, _ in staged:
                temporary.unlink(missing_ok=True)

    async def save_bytes(self, data: bytes, destination: Path) -> Path:
        temporary = destination.parent / f".{destination.name}.{uuid.uuid4().hex}.uploading"
        try:
            async with aiofiles.open(temporary, "xb") as output:
                await output.write(data)
            async with self._write_lock:
                if destination.exists():
                    raise FileExistsError(str(destination))
                os.link(temporary, destination)
            return destination
        finally:
            temporary.unlink(missing_ok=True)
