#!/usr/bin/env python3
"""Validate and stage SignVR instruction Pose content.

The source format is the Host recording layout::

    <source-root>/<round>/sentence_NNN/take_NNN/
        take_NNN.meta.json
        take_NNN.pose.jsonl

Only completed Pose/metadata pairs are candidates.  The selected candidate for
each sentence is the greatest ``(utc_stopped, take_index)`` pair.  Camera and
visualization media are deliberately never copied.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import sys
import tempfile
import uuid
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path, PurePosixPath
from typing import Any, Iterable, Sequence


SCHEMA_VERSION = 1
MANIFEST_NAME = "instruction-content-manifest.json"
EXPECTED_SENTENCE_IDS = tuple(f"{index:03d}" for index in range(1, 32))
MEDIA_SUFFIXES = (".camera.webm", ".mp4")

EXIT_OK = 0
EXIT_USAGE = 2
EXIT_VALIDATION = 3
EXIT_STAGING = 4

_SENTENCE_DIRECTORY_RE = re.compile(r"^sentence_(\d{3})$")
_TAKE_DIRECTORY_RE = re.compile(r"^take_(\d+)$")
_SAFE_SEGMENT_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")
_WINDOWS_RESERVED_NAMES = {
    "CON",
    "PRN",
    "AUX",
    "NUL",
    *(f"COM{index}" for index in range(1, 10)),
    *(f"LPT{index}" for index in range(1, 10)),
}
_RFC3339_RE = re.compile(
    r"^(?P<year>\d{4})-(?P<month>\d{2})-(?P<day>\d{2})"
    r"T(?P<hour>\d{2}):(?P<minute>\d{2}):(?P<second>\d{2})"
    r"(?:\.(?P<fraction>\d{1,9}))?"
    r"(?P<zone>Z|[+-]\d{2}:\d{2})$"
)
_EPOCH = datetime(1970, 1, 1, tzinfo=timezone.utc)


@dataclass(frozen=True)
class Diagnostic:
    code: str
    path: str
    message: str


@dataclass(frozen=True)
class TakeCandidate:
    sentence_id: str
    take_id: str
    take_index: int
    completed_utc: str
    completed_ns: int
    source_relative_path: str
    pose_source: Path
    metadata_source: Path
    pose_bytes: int
    pose_sha256: str
    metadata_bytes: int
    metadata_sha256: str

    @property
    def selection_key(self) -> tuple[int, int]:
        return self.completed_ns, self.take_index


@dataclass(frozen=True)
class SelectionPlan:
    source_root: Path
    signer_id: str
    selected: tuple[TakeCandidate, ...]
    manifest: dict[str, Any]
    manifest_bytes: bytes
    warnings: tuple[Diagnostic, ...]
    completed_candidate_count: int
    incomplete_take_count: int
    ignored_media_count: int
    skipped_test_game_count: int


@dataclass(frozen=True)
class StageResult:
    output_root: Path
    changed: bool
    pose_bytes: int


class ContentValidationError(Exception):
    """One or more source content invariants were violated."""

    def __init__(
        self,
        issues: Iterable[Diagnostic],
        warnings: Iterable[Diagnostic] = (),
    ) -> None:
        self.issues = tuple(issues)
        self.warnings = tuple(warnings)
        super().__init__(f"instruction content validation failed ({len(self.issues)} issue(s))")


class StagingError(Exception):
    """A validated plan could not be published safely."""


class _StrictJsonError(ValueError):
    pass


def phase_for_sentence(sentence_id: str | int) -> int:
    """Return the frozen Interaction Contract V1 phase for one sentence."""

    try:
        number = int(sentence_id)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"invalid sentence ID: {sentence_id!r}") from exc
    if number < 1 or number > 31:
        raise ValueError(f"sentence ID is outside 001-031: {sentence_id!r}")
    if number <= 3:
        return 1
    if number <= 12:
        return 2
    if number <= 15:
        return 3
    if number <= 18:
        return 4
    if number <= 25:
        return 5
    return 6


def build_plan(source_root: Path | str, signer_id: str = "wang") -> SelectionPlan:
    """Scan, validate, select, and describe all 31 instruction Takes."""

    source = Path(source_root).expanduser().resolve()
    signer = _validate_segment(signer_id, "signer ID")
    if any("test_game" in part.casefold() for part in source.parts):
        raise ContentValidationError(
            [
                Diagnostic(
                    "TEST_GAME_SOURCE_FORBIDDEN",
                    str(source),
                    "source root itself is test_game content and cannot be staged",
                )
            ]
        )
    if not source.exists():
        raise ContentValidationError(
            [Diagnostic("SOURCE_NOT_FOUND", str(source), "source root does not exist")]
        )
    if not source.is_dir():
        raise ContentValidationError(
            [Diagnostic("SOURCE_NOT_DIRECTORY", str(source), "source root is not a directory")]
        )

    issues: list[Diagnostic] = []
    warnings: list[Diagnostic] = []
    candidates: list[TakeCandidate] = []
    sentence_texts: dict[str, tuple[str, str]] = {}
    incomplete_take_count = 0
    ignored_media_count = 0
    skipped_test_game_count = 0

    walk_errors: list[OSError] = []

    def record_walk_error(error: OSError) -> None:
        walk_errors.append(error)

    for current_text, directory_names, _ in os.walk(
        source, topdown=True, onerror=record_walk_error
    ):
        current = Path(current_text)
        directory_names.sort(key=str.casefold)

        allowed_directories: list[str] = []
        for name in directory_names:
            if "test_game" in name.casefold():
                skipped_test_game_count += 1
                warnings.append(
                    Diagnostic(
                        "TEST_GAME_SKIPPED",
                        _relative_display(current / name, source),
                        "test_game subtree was excluded",
                    )
                )
            else:
                allowed_directories.append(name)
        directory_names[:] = allowed_directories

        sentence_match = _SENTENCE_DIRECTORY_RE.fullmatch(current.name)
        if sentence_match is None:
            continue

        # Take directories are processed explicitly here; do not recurse into
        # their large artifact trees.
        directory_names[:] = []
        sentence_id = sentence_match.group(1)
        if sentence_id not in EXPECTED_SENTENCE_IDS:
            warnings.append(
                Diagnostic(
                    "OUT_OF_RANGE_SENTENCE_SKIPPED",
                    _relative_display(current, source),
                    "sentence is outside the required 001-031 range",
                )
            )
            continue

        try:
            child_directories = sorted(
                (child for child in current.iterdir() if child.is_dir()),
                key=lambda path: path.name.casefold(),
            )
        except OSError as exc:
            issues.append(
                Diagnostic(
                    "SENTENCE_DIRECTORY_UNREADABLE",
                    _relative_display(current, source),
                    str(exc),
                )
            )
            continue

        for take_directory in child_directories:
            take_match = _TAKE_DIRECTORY_RE.fullmatch(take_directory.name)
            if take_match is None:
                if _directory_has_recording_artifacts(take_directory):
                    issues.append(
                        Diagnostic(
                            "INVALID_TAKE_DIRECTORY",
                            _relative_display(take_directory, source),
                            "recording artifacts are not inside a take_<index> directory",
                        )
                    )
                continue

            take_id = take_directory.name
            take_index_from_directory = int(take_match.group(1))
            metadata_path = take_directory / f"{take_id}.meta.json"
            pose_path = take_directory / f"{take_id}.pose.jsonl"

            try:
                direct_files = [child for child in take_directory.iterdir() if child.is_file()]
            except OSError as exc:
                issues.append(
                    Diagnostic(
                        "TAKE_DIRECTORY_UNREADABLE",
                        _relative_display(take_directory, source),
                        str(exc),
                    )
                )
                continue

            ignored_media_count += sum(
                1
                for child in direct_files
                if any(child.name.casefold().endswith(suffix) for suffix in MEDIA_SUFFIXES)
            )
            alternate_metadata = [
                child
                for child in direct_files
                if child.name.endswith(".meta.json") and child != metadata_path
            ]

            if not metadata_path.is_file():
                if pose_path.is_file() or alternate_metadata:
                    details = "completed-Take metadata is missing"
                    if alternate_metadata:
                        details += "; unexpected metadata file(s): " + ", ".join(
                            sorted(path.name for path in alternate_metadata)
                        )
                    issues.append(
                        Diagnostic(
                            "MISSING_METADATA",
                            _relative_display(take_directory, source),
                            details,
                        )
                    )
                else:
                    incomplete_take_count += 1
                    warnings.append(
                        Diagnostic(
                            "INCOMPLETE_TAKE_SKIPPED",
                            _relative_display(take_directory, source),
                            "no metadata/Pose pair; treated as an abandoned reservation or partial camera Take",
                        )
                    )
                continue

            try:
                metadata, metadata_raw = _load_json_object(metadata_path)
            except (OSError, UnicodeDecodeError, json.JSONDecodeError, _StrictJsonError) as exc:
                issues.append(
                    Diagnostic(
                        "INVALID_METADATA_JSON",
                        _relative_display(metadata_path, source),
                        str(exc),
                    )
                )
                continue

            metadata_issues = _validate_metadata(
                metadata,
                sentence_id=sentence_id,
                take_id=take_id,
                take_index_from_directory=take_index_from_directory,
                metadata_path=metadata_path,
                source_root=source,
            )
            issues.extend(metadata_issues)
            if metadata_issues:
                continue

            sentence_text = metadata.get("sentence_text")
            if sentence_text is not None:
                if not isinstance(sentence_text, str) or not sentence_text.strip():
                    issues.append(
                        Diagnostic(
                            "INVALID_SENTENCE_TEXT",
                            _relative_display(metadata_path, source),
                            "sentence_text must be a non-empty string when present",
                        )
                    )
                    continue
                prior = sentence_texts.get(sentence_id)
                if prior is not None and prior[0] != sentence_text:
                    issues.append(
                        Diagnostic(
                            "SENTENCE_TEXT_MISMATCH",
                            _relative_display(metadata_path, source),
                            f"sentence_text differs from {prior[1]}",
                        )
                    )
                    continue
                sentence_texts[sentence_id] = (
                    sentence_text,
                    _relative_display(metadata_path, source),
                )

            if not pose_path.is_file():
                issues.append(
                    Diagnostic(
                        "MISSING_POSE",
                        _relative_display(take_directory, source),
                        f"completed metadata has no Host-normalized {pose_path.name}",
                    )
                )
                continue

            try:
                pose_bytes, pose_sha256 = _validate_pose_jsonl(pose_path, metadata)
            except (OSError, UnicodeDecodeError, json.JSONDecodeError, _StrictJsonError, ValueError) as exc:
                issues.append(
                    Diagnostic(
                        "INVALID_POSE_JSONL",
                        _relative_display(pose_path, source),
                        str(exc),
                    )
                )
                continue
            # _validate_metadata already parsed this successfully.  Parse it
            # again here to retain the exact integer-nanosecond selection key.
            completed_ns, completed_utc = _parse_rfc3339_utc(metadata["utc_stopped"])

            metadata_sha256 = hashlib.sha256(metadata_raw).hexdigest()
            candidates.append(
                TakeCandidate(
                    sentence_id=sentence_id,
                    take_id=take_id,
                    take_index=metadata["take_index"],
                    completed_utc=completed_utc,
                    completed_ns=completed_ns,
                    source_relative_path=_relative_display(take_directory, source),
                    pose_source=pose_path,
                    metadata_source=metadata_path,
                    pose_bytes=pose_bytes,
                    pose_sha256=pose_sha256,
                    metadata_bytes=len(metadata_raw),
                    metadata_sha256=metadata_sha256,
                )
            )

    for error in walk_errors:
        issues.append(Diagnostic("SOURCE_UNREADABLE", str(error.filename or source), str(error)))

    candidates_by_sentence: dict[str, list[TakeCandidate]] = {
        sentence_id: [] for sentence_id in EXPECTED_SENTENCE_IDS
    }
    for candidate in candidates:
        candidates_by_sentence[candidate.sentence_id].append(candidate)

    selected: list[TakeCandidate] = []
    for sentence_id in EXPECTED_SENTENCE_IDS:
        sentence_candidates = candidates_by_sentence[sentence_id]
        if not sentence_candidates:
            issues.append(
                Diagnostic(
                    "MISSING_SENTENCE",
                    f"sentence_{sentence_id}",
                    "no valid completed Take is available",
                )
            )
            continue
        best_key = max(candidate.selection_key for candidate in sentence_candidates)
        winners = [
            candidate for candidate in sentence_candidates if candidate.selection_key == best_key
        ]
        if len(winners) != 1:
            issues.append(
                Diagnostic(
                    "AMBIGUOUS_LATEST_TAKE",
                    f"sentence_{sentence_id}",
                    "multiple completed Takes share the winning completion time and take_index: "
                    + ", ".join(sorted(candidate.source_relative_path for candidate in winners)),
                )
            )
            continue
        selected.append(winners[0])

    if issues:
        raise ContentValidationError(issues, warnings)

    generated_ns = max(candidate.completed_ns for candidate in selected)
    generated_utc = _format_utc_ns(generated_ns)
    manifest = {
        "schema_version": SCHEMA_VERSION,
        # Deriving this value from the selected source snapshot keeps repeated
        # staging byte-for-byte deterministic.
        "generated_utc": generated_utc,
        "signer_id": signer,
        "entries": [
            {
                "phase_id": phase_for_sentence(candidate.sentence_id),
                "sentence_id": candidate.sentence_id,
                "signer_id": signer,
                "take_id": candidate.take_id,
                "completed_utc": candidate.completed_utc,
                "take_index": candidate.take_index,
                "pose_path": _output_relative_path(
                    signer, candidate.sentence_id, f"{candidate.take_id}.pose.jsonl"
                ),
                "metadata_path": _output_relative_path(
                    signer, candidate.sentence_id, f"{candidate.take_id}.meta.json"
                ),
                "pose_bytes": candidate.pose_bytes,
                "pose_sha256": candidate.pose_sha256,
            }
            for candidate in selected
        ],
    }
    manifest_bytes = (
        json.dumps(manifest, ensure_ascii=False, indent=2, separators=(",", ": ")) + "\n"
    ).encode("utf-8")
    return SelectionPlan(
        source_root=source,
        signer_id=signer,
        selected=tuple(selected),
        manifest=manifest,
        manifest_bytes=manifest_bytes,
        warnings=tuple(warnings),
        completed_candidate_count=len(candidates),
        incomplete_take_count=incomplete_take_count,
        ignored_media_count=ignored_media_count,
        skipped_test_game_count=skipped_test_game_count,
    )


def stage_plan(plan: SelectionPlan, output_root: Path | str) -> StageResult:
    """Copy one validated snapshot and transactionally publish it."""

    output = Path(output_root).expanduser().resolve()
    _validate_output_separation(plan.source_root, output)
    if output.is_symlink():
        raise StagingError(f"output root must not be a symlink: {output}")
    if output.exists() and not output.is_dir():
        raise StagingError(f"output root exists but is not a directory: {output}")
    _validate_existing_output_ownership(plan, output)

    parent = output.parent
    parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(
        tempfile.mkdtemp(prefix=f".{output.name}.staging-", dir=str(parent))
    ).resolve()
    backup: Path | None = None
    expected_fingerprints: dict[str, tuple[int, str]] = {}

    try:
        for candidate, entry in zip(plan.selected, plan.manifest["entries"]):
            pose_destination = temporary / Path(entry["pose_path"])
            metadata_destination = temporary / Path(entry["metadata_path"])
            pose_destination.parent.mkdir(parents=True, exist_ok=True)

            copied_pose = _copy_and_hash(candidate.pose_source, pose_destination)
            expected_pose = (candidate.pose_bytes, candidate.pose_sha256)
            if copied_pose != expected_pose:
                raise StagingError(
                    f"source Pose changed after validation: {candidate.pose_source}"
                )
            copied_metadata = _copy_and_hash(candidate.metadata_source, metadata_destination)
            expected_metadata = (candidate.metadata_bytes, candidate.metadata_sha256)
            if copied_metadata != expected_metadata:
                raise StagingError(
                    f"source metadata changed after validation: {candidate.metadata_source}"
                )

            expected_fingerprints[entry["pose_path"]] = expected_pose
            expected_fingerprints[entry["metadata_path"]] = expected_metadata

        manifest_path = temporary / MANIFEST_NAME
        manifest_path.write_bytes(plan.manifest_bytes)
        expected_fingerprints[MANIFEST_NAME] = (
            len(plan.manifest_bytes),
            hashlib.sha256(plan.manifest_bytes).hexdigest(),
        )

        staged_fingerprints = _tree_fingerprints(temporary)
        if staged_fingerprints != expected_fingerprints:
            raise StagingError("staged tree verification failed before publication")

        if output.exists() and _tree_fingerprints(output) == expected_fingerprints:
            _remove_owned_tree(temporary, parent, f".{output.name}.staging-")
            return StageResult(
                output_root=output,
                changed=False,
                pose_bytes=sum(candidate.pose_bytes for candidate in plan.selected),
            )

        if output.exists():
            backup = (parent / f".{output.name}.backup-{uuid.uuid4().hex}").resolve()
            os.replace(output, backup)
        failed_publish: Path | None = None
        try:
            os.replace(temporary, output)
            published_fingerprints = _tree_fingerprints(output)
            if published_fingerprints != expected_fingerprints:
                raise StagingError("published tree verification failed")
        except BaseException:
            # If an existing generation was displaced, restore it before
            # reporting failure.  A failed new generation is first moved to
            # an invocation-owned name so no partially verified tree remains
            # at the requested output path.
            if output.exists():
                failed_publish = (
                    parent / f".{output.name}.failed-{uuid.uuid4().hex}"
                ).resolve()
                os.replace(output, failed_publish)
            if backup is not None and backup.exists() and not output.exists():
                os.replace(backup, output)
                backup = None
            if failed_publish is not None and failed_publish.exists():
                _remove_owned_tree(failed_publish, parent, f".{output.name}.failed-")
            raise

        if backup is not None and backup.exists():
            _remove_owned_tree(backup, parent, f".{output.name}.backup-")
            backup = None

        return StageResult(
            output_root=output,
            changed=True,
            pose_bytes=sum(candidate.pose_bytes for candidate in plan.selected),
        )
    except StagingError:
        raise
    except OSError as exc:
        raise StagingError(str(exc)) from exc
    finally:
        if temporary.exists():
            _remove_owned_tree(temporary, parent, f".{output.name}.staging-")


def _validate_metadata(
    metadata: dict[str, Any],
    *,
    sentence_id: str,
    take_id: str,
    take_index_from_directory: int,
    metadata_path: Path,
    source_root: Path,
) -> list[Diagnostic]:
    path = _relative_display(metadata_path, source_root)
    issues: list[Diagnostic] = []
    required_fields = ("sentence_id", "take_id", "take_index", "capture_status", "utc_stopped", "pose_file")
    missing_fields = [field for field in required_fields if field not in metadata]
    if missing_fields:
        issues.append(
            Diagnostic(
                "MISSING_METADATA_FIELDS",
                path,
                "missing field(s): " + ", ".join(missing_fields),
            )
        )
        return issues

    expected_sentence = f"sentence_{sentence_id}"
    if metadata["sentence_id"] != expected_sentence:
        issues.append(
            Diagnostic(
                "SENTENCE_METADATA_MISMATCH",
                path,
                f"metadata sentence_id={metadata['sentence_id']!r}, directory={expected_sentence!r}",
            )
        )
    if metadata["take_id"] != take_id:
        issues.append(
            Diagnostic(
                "TAKE_METADATA_MISMATCH",
                path,
                f"metadata take_id={metadata['take_id']!r}, directory={take_id!r}",
            )
        )

    take_index = metadata["take_index"]
    if isinstance(take_index, bool) or not isinstance(take_index, int) or take_index < 1:
        issues.append(
            Diagnostic("INVALID_TAKE_INDEX", path, "take_index must be a positive integer")
        )
    elif take_index != take_index_from_directory:
        issues.append(
            Diagnostic(
                "TAKE_INDEX_MISMATCH",
                path,
                f"metadata take_index={take_index}, directory index={take_index_from_directory}",
            )
        )

    if metadata["capture_status"] != "completed":
        issues.append(
            Diagnostic(
                "TAKE_NOT_COMPLETED",
                path,
                f"capture_status must be 'completed', got {metadata['capture_status']!r}",
            )
        )
    if metadata.get("editor_simulation") is True or metadata.get("pose_source_simulated") is True:
        issues.append(
            Diagnostic(
                "SIMULATED_TAKE_FORBIDDEN",
                path,
                "simulated/editor Take cannot be instruction content",
            )
        )

    pose_file = metadata["pose_file"]
    if (
        not isinstance(pose_file, str)
        or not pose_file
        or Path(pose_file).name != pose_file
        or not pose_file.endswith(".pose.jsonl")
    ):
        issues.append(
            Diagnostic(
                "INVALID_METADATA_POSE_FILE",
                path,
                "pose_file must be a safe .pose.jsonl basename",
            )
        )

    try:
        _parse_rfc3339_utc(metadata["utc_stopped"])
    except (TypeError, ValueError) as exc:
        issues.append(Diagnostic("INVALID_COMPLETION_TIME", path, str(exc)))
    return issues


def _validate_pose_jsonl(path: Path, metadata: dict[str, Any]) -> tuple[int, str]:
    digest = hashlib.sha256()
    byte_count = 0
    frame_count = 0
    valid_frame_count = 0

    with path.open("rb") as stream:
        for line_number, raw_line in enumerate(stream, start=1):
            digest.update(raw_line)
            byte_count += len(raw_line)
            if not raw_line.strip():
                raise ValueError(f"blank JSONL record at line {line_number}")
            text = raw_line.decode("utf-8")
            try:
                record = _strict_json_loads(text)
            except (json.JSONDecodeError, _StrictJsonError) as exc:
                raise ValueError(f"invalid JSONL at line {line_number}: {exc}") from exc
            if not isinstance(record, dict):
                raise ValueError(f"JSONL record at line {line_number} is not an object")
            sample_index = record.get("sample_index")
            if sample_index is not None and (
                isinstance(sample_index, bool)
                or not isinstance(sample_index, int)
                or sample_index != frame_count
            ):
                raise ValueError(
                    f"sample_index at line {line_number} must equal {frame_count}"
                )
            pose_valid = record.get("pose_valid")
            if pose_valid is not None and not isinstance(pose_valid, bool):
                raise ValueError(f"pose_valid at line {line_number} is not boolean")
            if pose_valid is True:
                valid_frame_count += 1
            frame_count += 1

    if frame_count == 0:
        raise ValueError("Pose JSONL is empty")

    expected_frames = _optional_int(metadata, "pose_frame_count", minimum=1)
    if expected_frames is not None and expected_frames != frame_count:
        raise ValueError(
            f"pose_frame_count={expected_frames}, but JSONL contains {frame_count} records"
        )
    expected_valid = _optional_int(metadata, "valid_pose_frame_count", minimum=0)
    if expected_valid is not None and expected_valid != valid_frame_count:
        raise ValueError(
            f"valid_pose_frame_count={expected_valid}, but JSONL contains "
            f"{valid_frame_count} valid records"
        )
    if "valid_pose_ratio" in metadata:
        ratio = metadata["valid_pose_ratio"]
        if isinstance(ratio, bool) or not isinstance(ratio, (int, float)):
            raise ValueError("valid_pose_ratio must be numeric")
        actual_ratio = valid_frame_count / frame_count
        if abs(float(ratio) - actual_ratio) > 1e-6:
            raise ValueError(
                f"valid_pose_ratio={ratio}, but JSONL ratio is {actual_ratio}"
            )
    return byte_count, digest.hexdigest()


def _optional_int(metadata: dict[str, Any], field: str, *, minimum: int) -> int | None:
    if field not in metadata:
        return None
    value = metadata[field]
    if isinstance(value, bool) or not isinstance(value, int) or value < minimum:
        raise ValueError(f"{field} must be an integer >= {minimum}")
    return value


def _load_json_object(path: Path) -> tuple[dict[str, Any], bytes]:
    raw = path.read_bytes()
    text = raw.decode("utf-8")
    payload = _strict_json_loads(text)
    if not isinstance(payload, dict):
        raise _StrictJsonError("metadata root must be a JSON object")
    return payload, raw


def _strict_json_loads(text: str) -> Any:
    def reject_constant(value: str) -> None:
        raise _StrictJsonError(f"non-standard JSON constant: {value}")

    def unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise _StrictJsonError(f"duplicate JSON key: {key}")
            result[key] = value
        return result

    return json.loads(
        text,
        parse_constant=reject_constant,
        object_pairs_hook=unique_object,
    )


def _parse_rfc3339_utc(value: Any) -> tuple[int, str]:
    if not isinstance(value, str):
        raise TypeError("utc_stopped must be an RFC 3339 string")
    match = _RFC3339_RE.fullmatch(value)
    if match is None:
        raise ValueError(f"utc_stopped is not RFC 3339 with an explicit zone: {value!r}")

    zone_text = match.group("zone")
    if zone_text == "Z":
        zone = timezone.utc
    else:
        sign = 1 if zone_text[0] == "+" else -1
        offset_hours = int(zone_text[1:3])
        offset_minutes = int(zone_text[4:6])
        if offset_hours > 23 or offset_minutes > 59:
            raise ValueError(f"invalid RFC 3339 offset: {zone_text}")
        zone = timezone(sign * timedelta(hours=offset_hours, minutes=offset_minutes))

    second = int(match.group("second"))
    if second > 59:
        raise ValueError("leap-second timestamps are not supported")
    try:
        local_second = datetime(
            int(match.group("year")),
            int(match.group("month")),
            int(match.group("day")),
            int(match.group("hour")),
            int(match.group("minute")),
            second,
            tzinfo=zone,
        )
    except ValueError as exc:
        raise ValueError(f"invalid utc_stopped: {exc}") from exc

    utc_second = local_second.astimezone(timezone.utc)
    whole_seconds = (utc_second - _EPOCH).days * 86_400 + (utc_second - _EPOCH).seconds
    fraction = match.group("fraction") or ""
    nanoseconds = int(fraction.ljust(9, "0")) if fraction else 0
    timestamp_ns = whole_seconds * 1_000_000_000 + nanoseconds
    return timestamp_ns, _format_utc_ns(timestamp_ns)


def _format_utc_ns(timestamp_ns: int) -> str:
    whole_seconds, nanoseconds = divmod(timestamp_ns, 1_000_000_000)
    instant = _EPOCH + timedelta(seconds=whole_seconds)
    base = instant.strftime("%Y-%m-%dT%H:%M:%S")
    if nanoseconds:
        return f"{base}.{nanoseconds:09d}".rstrip("0") + "Z"
    return base + "Z"


def _validate_segment(value: str, label: str) -> str:
    invalid_windows_name = (
        isinstance(value, str)
        and value.split(".", 1)[0].upper() in _WINDOWS_RESERVED_NAMES
    )
    if (
        not isinstance(value, str)
        or not _SAFE_SEGMENT_RE.fullmatch(value)
        or value.endswith(".")
        or invalid_windows_name
    ):
        raise ContentValidationError(
            [
                Diagnostic(
                    "INVALID_IDENTIFIER",
                    label,
                    "must be a portable non-reserved segment matching "
                    "[A-Za-z0-9][A-Za-z0-9._-]*",
                )
            ]
        )
    if "test_game" in value.casefold():
        raise ContentValidationError(
            [Diagnostic("TEST_GAME_IDENTIFIER_FORBIDDEN", label, "test_game is forbidden")]
        )
    return value


def _output_relative_path(signer: str, sentence_id: str, filename: str) -> str:
    return str(PurePosixPath(signer, f"sentence_{sentence_id}", filename))


def _relative_display(path: Path, root: Path) -> str:
    try:
        return str(PurePosixPath(path.relative_to(root)))
    except ValueError:
        return str(path)


def _directory_has_recording_artifacts(path: Path) -> bool:
    try:
        return any(
            child.is_file()
            and (child.name.endswith(".meta.json") or child.name.endswith(".pose.jsonl"))
            for child in path.iterdir()
        )
    except OSError:
        return True


def _copy_and_hash(source: Path, destination: Path) -> tuple[int, str]:
    digest = hashlib.sha256()
    byte_count = 0
    with source.open("rb") as input_stream, destination.open("xb") as output_stream:
        while True:
            chunk = input_stream.read(1024 * 1024)
            if not chunk:
                break
            output_stream.write(chunk)
            digest.update(chunk)
            byte_count += len(chunk)
        output_stream.flush()
        os.fsync(output_stream.fileno())
    return byte_count, digest.hexdigest()


def _hash_file(path: Path) -> tuple[int, str]:
    digest = hashlib.sha256()
    byte_count = 0
    with path.open("rb") as stream:
        while True:
            chunk = stream.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
            byte_count += len(chunk)
    return byte_count, digest.hexdigest()


def _tree_fingerprints(root: Path) -> dict[str, tuple[int, str]]:
    fingerprints: dict[str, tuple[int, str]] = {}
    for path in sorted((item for item in root.rglob("*") if item.is_file())):
        relative = str(PurePosixPath(path.relative_to(root)))
        fingerprints[relative] = _hash_file(path)
    return fingerprints


def _validate_output_separation(source: Path, output: Path) -> None:
    if output == Path(output.anchor):
        raise StagingError(f"filesystem root cannot be an output root: {output}")
    if output == source or output.is_relative_to(source) or source.is_relative_to(output):
        raise StagingError(
            f"source and output trees must be disjoint (source={source}, output={output})"
        )


def _validate_existing_output_ownership(plan: SelectionPlan, output: Path) -> None:
    if not output.exists():
        return
    try:
        if not any(output.iterdir()):
            return
    except OSError as exc:
        raise StagingError(f"cannot inspect existing output root: {exc}") from exc

    manifest_path = output / MANIFEST_NAME
    if not manifest_path.is_file():
        raise StagingError(
            "refusing to replace a non-empty directory without an existing "
            f"{MANIFEST_NAME}: {output}"
        )
    try:
        manifest, _ = _load_json_object(manifest_path)
    except (OSError, UnicodeDecodeError, json.JSONDecodeError, _StrictJsonError) as exc:
        raise StagingError(
            f"refusing to replace an output with an invalid ownership manifest: {exc}"
        ) from exc

    entries = manifest.get("entries")
    sentence_ids = (
        [entry.get("sentence_id") for entry in entries if isinstance(entry, dict)]
        if isinstance(entries, list)
        else []
    )
    if (
        manifest.get("schema_version") != SCHEMA_VERSION
        or manifest.get("signer_id") != plan.signer_id
        or sentence_ids != list(EXPECTED_SENTENCE_IDS)
    ):
        raise StagingError(
            "refusing to replace a directory whose manifest is not a complete "
            f"schema-v{SCHEMA_VERSION} generation for signer {plan.signer_id!r}"
        )


def _remove_owned_tree(path: Path, parent: Path, required_prefix: str) -> None:
    resolved_path = path.resolve()
    resolved_parent = parent.resolve()
    if resolved_path.parent != resolved_parent or not resolved_path.name.startswith(required_prefix):
        raise StagingError(f"refusing to remove unowned temporary directory: {resolved_path}")
    shutil.rmtree(resolved_path)


def _take_distribution(plan: SelectionPlan) -> str:
    counts: dict[str, int] = {}
    for candidate in plan.selected:
        counts[candidate.take_id] = counts.get(candidate.take_id, 0) + 1
    return ", ".join(f"{take_id}={counts[take_id]}" for take_id in sorted(counts))


def _print_warnings(warnings: Sequence[Diagnostic]) -> None:
    for warning in warnings:
        print(
            f"WARNING [{warning.code}] {warning.path}: {warning.message}",
            file=sys.stderr,
        )


def _print_summary(plan: SelectionPlan, *, mode: str, output_root: Path) -> None:
    print("VALIDATION OK")
    print(f"mode: {mode}")
    print(f"source_root: {plan.source_root}")
    print(f"output_root: {output_root.expanduser().resolve()}")
    print(f"signer_id: {plan.signer_id}")
    print("coverage: 31/31 (001-031)")
    print(f"completed_candidates: {plan.completed_candidate_count}")
    print(f"selected_take_distribution: {_take_distribution(plan)}")
    print(f"incomplete_take_directories_skipped: {plan.incomplete_take_count}")
    print(f"source_media_files_excluded: {plan.ignored_media_count}")
    print(f"test_game_subtrees_skipped: {plan.skipped_test_game_count}")
    print(f"selected_pose_bytes: {sum(candidate.pose_bytes for candidate in plan.selected)}")
    print(f"generated_utc: {plan.manifest['generated_utc']}")
    for candidate in plan.selected:
        print(
            f"selected sentence_{candidate.sentence_id}: {candidate.take_id} "
            f"(take_index={candidate.take_index}, completed_utc={candidate.completed_utc})"
        )


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Select the latest completed Take for SignVR sentences 001-031, "
            "validate Pose/metadata integrity, and stage Quest instruction content."
        ),
        epilog=(
            "Exit codes: 0 success; 2 command-line usage; "
            "3 source/content validation failure; 4 staging I/O or publication failure."
        ),
    )
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--signer-id", default="wang")
    parser.add_argument("--output-root", required=True, type=Path)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument(
        "--dry-run",
        action="store_true",
        help="perform complete validation and selection without writing output",
    )
    mode.add_argument(
        "--validate-only",
        action="store_true",
        help="alias-like validation mode that never writes output",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    try:
        plan = build_plan(args.source_root, args.signer_id)
    except ContentValidationError as exc:
        _print_warnings(exc.warnings)
        print(f"VALIDATION FAILED ({len(exc.issues)} issue(s))", file=sys.stderr)
        for issue in exc.issues:
            print(
                f"ERROR [{issue.code}] {issue.path}: {issue.message}",
                file=sys.stderr,
            )
        return EXIT_VALIDATION
    except OSError as exc:
        print(f"STAGING FAILED: source I/O error: {exc}", file=sys.stderr)
        return EXIT_STAGING

    _print_warnings(plan.warnings)
    if args.dry_run or args.validate_only:
        mode = "dry-run" if args.dry_run else "validate-only"
        _print_summary(plan, mode=mode, output_root=args.output_root)
        print("write_performed: false")
        return EXIT_OK

    try:
        result = stage_plan(plan, args.output_root)
    except StagingError as exc:
        print(f"STAGING FAILED: {exc}", file=sys.stderr)
        return EXIT_STAGING

    _print_summary(plan, mode="stage", output_root=args.output_root)
    print(f"write_performed: {'true' if result.changed else 'false (already identical)'}")
    print(f"manifest: {result.output_root / MANIFEST_NAME}")
    return EXIT_OK


if __name__ == "__main__":
    raise SystemExit(main())
