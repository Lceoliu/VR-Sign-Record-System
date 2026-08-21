from __future__ import annotations

import json
import time
import uuid
from pathlib import Path, PurePosixPath

from .repository import safe_segment


class ReviewRepository:
    def __init__(self, root: Path) -> None:
        self.root = root.resolve()

    def list_datasets(self) -> list[str]:
        if not self.root.is_dir():
            return []
        return sorted(
            path.name
            for path in self.root.iterdir()
            if path.is_dir() and next(path.glob("*/round_*/sentence_*/take_*/*.meta.json"), None)
        )

    def list_items(self, dataset: str) -> list[dict]:
        dataset_directory = self._dataset_directory(dataset)
        labels = self._read_labels(dataset_directory)
        items: list[dict] = []

        for meta_path in sorted(dataset_directory.glob("*/round_*/sentence_*/take_*/*.meta.json")):
            relative = meta_path.relative_to(dataset_directory)
            teacher, round_id, sentence_id, take_id, _ = relative.parts
            meta = json.loads(meta_path.read_text(encoding="utf-8-sig"))
            if meta.get("capture_status") != "completed":
                continue

            item_id = PurePosixPath(teacher, round_id, sentence_id, take_id).as_posix()
            take_directory = meta_path.parent
            pose_path = take_directory / f"{take_id}.pose.jsonl"
            video_path = take_directory / f"{take_id}.camera.webm"
            label = labels.get(item_id, {})

            items.append(
                {
                    "id": item_id,
                    "dataset": dataset_directory.name,
                    "teacher": teacher,
                    "round_id": round_id,
                    "sentence_id": sentence_id,
                    "take_id": take_id,
                    "sentence_text": str(meta.get("sentence_text", "")),
                    "meta": meta,
                    "pose_file": self._relative_file(pose_path),
                    "video_file": self._relative_file(video_path),
                    "video_issue": bool(label.get("video_issue", False)),
                    "sentence_issue": bool(label.get("sentence_issue", False)),
                }
            )
        return items

    def media_file(self, relative_path: str) -> Path:
        parts = PurePosixPath(relative_path).parts
        if not parts or any(part in {"", ".", ".."} for part in parts):
            raise ValueError("无效的审核文件路径")
        path = (self.root / Path(*parts)).resolve()
        try:
            path.relative_to(self.root)
        except ValueError as exc:
            raise ValueError("审核文件路径超出数据目录") from exc
        if path.suffix.lower() not in {".webm", ".jsonl"}:
            raise ValueError("该文件类型不能通过审核页面读取")
        if not path.is_file():
            raise FileNotFoundError(path)
        return path

    def update_label(
        self,
        dataset: str,
        item_id: str,
        *,
        video_issue: bool,
        sentence_issue: bool,
    ) -> dict:
        dataset_directory = self._dataset_directory(dataset)
        parts = PurePosixPath(item_id).parts
        if len(parts) != 4 or any(safe_segment(part) != part for part in parts):
            raise ValueError("无效的审核项目")
        take_directory = dataset_directory.joinpath(*parts)
        if not take_directory.is_dir():
            raise FileNotFoundError(take_directory)

        labels = self._read_labels(dataset_directory)
        if video_issue or sentence_issue:
            labels[item_id] = {
                "video_issue": video_issue,
                "sentence_issue": sentence_issue,
            }
        else:
            labels.pop(item_id, None)

        payload = {
            "version": 1,
            "updated_at_unix_ms": int(time.time() * 1000),
            "items": dict(sorted(labels.items())),
        }
        destination = dataset_directory / "review_labels.json"
        temporary = dataset_directory / f".review_labels.{uuid.uuid4().hex}.tmp"
        temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        temporary.replace(destination)
        return {
            "item_id": item_id,
            "video_issue": video_issue,
            "sentence_issue": sentence_issue,
        }

    def _dataset_directory(self, dataset: str) -> Path:
        directory = self.root / safe_segment(dataset)
        if not directory.is_dir():
            raise FileNotFoundError(directory)
        return directory

    def _relative_file(self, path: Path) -> str | None:
        if not path.is_file():
            return None
        return path.relative_to(self.root).as_posix()

    @staticmethod
    def _read_labels(dataset_directory: Path) -> dict[str, dict]:
        path = dataset_directory / "review_labels.json"
        if not path.is_file():
            return {}
        payload = json.loads(path.read_text(encoding="utf-8-sig"))
        return dict(payload.get("items", {}))
