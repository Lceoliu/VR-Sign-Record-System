from __future__ import annotations

import contextlib
import hashlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import tools.interaction_content.stage_instruction_content as staging_module

from tools.interaction_content.stage_instruction_content import (
    ContentValidationError,
    EXIT_OK,
    EXIT_VALIDATION,
    EXPECTED_SENTENCE_IDS,
    MANIFEST_NAME,
    StagingError,
    build_plan,
    main,
    phase_for_sentence,
    stage_plan,
)


def _timestamp_for(sentence_id: str) -> str:
    return f"2026-08-25T00:00:{int(sentence_id):02d}Z"


def _write_take(
    source_root: Path,
    sentence_id: str,
    take_index: int,
    *,
    completed_utc: str | None = None,
    round_id: str = "round_001",
    capture_status: str = "completed",
    metadata_updates: dict | None = None,
    pose_bytes: bytes | None = None,
) -> Path:
    take_id = f"take_{take_index:03d}"
    take_directory = (
        source_root / round_id / f"sentence_{sentence_id}" / take_id
    )
    take_directory.mkdir(parents=True, exist_ok=True)
    if pose_bytes is None:
        pose_record = {
            "sample_index": 0,
            "pose_valid": True,
            "fixture": f"{round_id}:{sentence_id}:{take_index}",
        }
        pose_bytes = (
            json.dumps(pose_record, ensure_ascii=False, separators=(",", ":")) + "\n"
        ).encode("utf-8")
    (take_directory / f"{take_id}.pose.jsonl").write_bytes(pose_bytes)

    metadata = {
        "sentence_id": f"sentence_{sentence_id}",
        "sentence_text": f"fixture sentence {sentence_id}",
        "take_id": take_id,
        "take_index": take_index,
        "capture_status": capture_status,
        "utc_stopped": completed_utc or _timestamp_for(sentence_id),
        "pose_file": (
            f"sentence_{sentence_id}__take-{take_index:03d}__{take_id}.pose.jsonl"
        ),
        "pose_frame_count": 1,
        "valid_pose_frame_count": 1,
        "valid_pose_ratio": 1.0,
        "editor_simulation": False,
        "pose_source_simulated": False,
    }
    if metadata_updates:
        metadata.update(metadata_updates)
    (take_directory / f"{take_id}.meta.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    return take_directory


def _write_full_source(source_root: Path, *, missing: set[str] | None = None) -> None:
    missing = missing or set()
    for sentence_id in EXPECTED_SENTENCE_IDS:
        if sentence_id not in missing:
            _write_take(source_root, sentence_id, 1)


def _issue_codes(error: ContentValidationError) -> set[str]:
    return {issue.code for issue in error.issues}


def _file_snapshot(root: Path) -> dict[str, str]:
    return {
        path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in root.rglob("*")
        if path.is_file()
    }


class PhaseMappingTests(unittest.TestCase):
    def test_all_31_sentence_ids_map_to_frozen_phase_ranges(self) -> None:
        expected = {
            **{f"{number:03d}": 1 for number in range(1, 4)},
            **{f"{number:03d}": 2 for number in range(4, 13)},
            **{f"{number:03d}": 3 for number in range(13, 16)},
            **{f"{number:03d}": 4 for number in range(16, 19)},
            **{f"{number:03d}": 5 for number in range(19, 26)},
            **{f"{number:03d}": 6 for number in range(26, 32)},
        }
        self.assertEqual(
            {sentence_id: phase_for_sentence(sentence_id) for sentence_id in EXPECTED_SENTENCE_IDS},
            expected,
        )
        for invalid in ("000", "032", "not-a-sentence"):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                phase_for_sentence(invalid)


class SelectionTests(unittest.TestCase):
    def test_completion_time_wins_then_take_index_breaks_a_tie(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "recordings"
            _write_full_source(source)

            # One nanosecond later wins even though its Take index is lower.
            _write_take(
                source,
                "001",
                2,
                completed_utc="2026-08-25T01:00:00.123456701Z",
            )
            _write_take(
                source,
                "001",
                3,
                completed_utc="2026-08-25T01:00:00.123456700Z",
            )

            # Equal completion timestamps are decided by the greater Take index.
            _write_take(
                source,
                "002",
                2,
                completed_utc="2026-08-25T01:00:01.5000000Z",
            )
            _write_take(
                source,
                "002",
                3,
                completed_utc="2026-08-25T01:00:01.5Z",
            )

            plan = build_plan(source)
            selected = {candidate.sentence_id: candidate for candidate in plan.selected}
            self.assertEqual(selected["001"].take_id, "take_002")
            self.assertEqual(selected["002"].take_id, "take_003")

    def test_equal_completion_and_take_index_across_rounds_is_ambiguous(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "recordings"
            _write_full_source(source)
            _write_take(
                source,
                "001",
                1,
                completed_utc=_timestamp_for("001"),
                round_id="round_002",
            )

            with self.assertRaises(ContentValidationError) as caught:
                build_plan(source)
            self.assertIn("AMBIGUOUS_LATEST_TAKE", _issue_codes(caught.exception))

    def test_test_game_subtree_is_never_a_candidate(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "recordings"
            _write_full_source(source)
            _write_take(
                source / "test_game",
                "001",
                9,
                completed_utc="2026-08-25T23:59:59Z",
            )

            plan = build_plan(source)
            self.assertEqual(plan.selected[0].take_id, "take_001")
            self.assertEqual(plan.skipped_test_game_count, 1)


class ValidationFailureTests(unittest.TestCase):
    def test_missing_sentence_is_reported(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "recordings"
            _write_full_source(source, missing={"031"})
            with self.assertRaises(ContentValidationError) as caught:
                build_plan(source)
            self.assertIn("MISSING_SENTENCE", _issue_codes(caught.exception))
            self.assertTrue(
                any(issue.path == "sentence_031" for issue in caught.exception.issues)
            )

    def test_corrupt_metadata_and_pose_jsonl_are_reported(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "metadata-corrupt"
            _write_full_source(source)
            metadata = (
                source
                / "round_001"
                / "sentence_004"
                / "take_001"
                / "take_001.meta.json"
            )
            metadata.write_text("{not-json\n", encoding="utf-8")
            with self.assertRaises(ContentValidationError) as caught:
                build_plan(source)
            self.assertIn("INVALID_METADATA_JSON", _issue_codes(caught.exception))

        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "pose-corrupt"
            _write_full_source(source)
            pose = (
                source
                / "round_001"
                / "sentence_005"
                / "take_001"
                / "take_001.pose.jsonl"
            )
            pose.write_bytes(b'{"sample_index": 0\n')
            with self.assertRaises(ContentValidationError) as caught:
                build_plan(source)
            self.assertIn("INVALID_POSE_JSONL", _issue_codes(caught.exception))

    def test_status_missing_file_and_metadata_mismatch_are_failures(self) -> None:
        cases = (
            ("non-completed", "TAKE_NOT_COMPLETED"),
            ("simulated", "SIMULATED_TAKE_FORBIDDEN"),
            ("missing-pose", "MISSING_POSE"),
            ("sentence-mismatch", "SENTENCE_METADATA_MISMATCH"),
        )
        for mutation, expected_code in cases:
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                source = Path(temporary) / "recordings"
                _write_full_source(source)
                take_directory = (
                    source / "round_001" / "sentence_006" / "take_001"
                )
                metadata_path = take_directory / "take_001.meta.json"
                if mutation == "missing-pose":
                    (take_directory / "take_001.pose.jsonl").unlink()
                else:
                    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
                    if mutation == "non-completed":
                        metadata["capture_status"] = "interrupted_by_retake"
                    elif mutation == "simulated":
                        metadata["pose_source_simulated"] = True
                    else:
                        metadata["sentence_id"] = "sentence_007"
                    metadata_path.write_text(
                        json.dumps(metadata, ensure_ascii=False) + "\n",
                        encoding="utf-8",
                    )

                with self.assertRaises(ContentValidationError) as caught:
                    build_plan(source)
                self.assertIn(expected_code, _issue_codes(caught.exception))


class StagingTests(unittest.TestCase):
    def test_manifest_hashes_and_output_excludes_media(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "recordings"
            output = root / "build-content"
            _write_full_source(source)
            first_take = source / "round_001" / "sentence_001" / "take_001"
            (first_take / "take_001.camera.webm").write_bytes(b"camera")
            (first_take / "take_001.pose.30fps.mp4").write_bytes(b"visualization")

            plan = build_plan(source)
            result = stage_plan(plan, output)
            self.assertTrue(result.changed)
            self.assertEqual(plan.ignored_media_count, 2)

            manifest = json.loads((output / MANIFEST_NAME).read_text(encoding="utf-8"))
            self.assertEqual(manifest["schema_version"], 1)
            self.assertEqual(len(manifest["entries"]), 31)
            self.assertEqual(
                [entry["sentence_id"] for entry in manifest["entries"]],
                list(EXPECTED_SENTENCE_IDS),
            )
            entry = manifest["entries"][0]
            source_pose = first_take / "take_001.pose.jsonl"
            self.assertEqual(entry["pose_bytes"], len(source_pose.read_bytes()))
            self.assertEqual(
                entry["pose_sha256"], hashlib.sha256(source_pose.read_bytes()).hexdigest()
            )
            self.assertEqual((output / Path(entry["pose_path"])).read_bytes(), source_pose.read_bytes())
            self.assertTrue((output / Path(entry["metadata_path"])).is_file())
            self.assertEqual(entry["phase_id"], 1)

            staged_files = [path for path in output.rglob("*") if path.is_file()]
            self.assertEqual(len(staged_files), 63)
            self.assertFalse(
                any(path.suffix.casefold() in {".mp4", ".webm"} for path in staged_files)
            )

    def test_repeated_staging_is_byte_identical_and_no_op(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "recordings"
            output = root / "build-content"
            _write_full_source(source)

            first_plan = build_plan(source)
            first_result = stage_plan(first_plan, output)
            first_snapshot = _file_snapshot(output)
            second_plan = build_plan(source)
            second_result = stage_plan(second_plan, output)

            self.assertTrue(first_result.changed)
            self.assertFalse(second_result.changed)
            self.assertEqual(first_plan.manifest_bytes, second_plan.manifest_bytes)
            self.assertEqual(first_snapshot, _file_snapshot(output))

    def test_failed_publication_verification_restores_previous_output(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "recordings"
            output = root / "build-content"
            _write_full_source(source)

            foreign_output = root / "foreign-content"
            foreign_output.mkdir()
            (foreign_output / "user-file.txt").write_text("preserve me", encoding="utf-8")
            plan = build_plan(source)
            with self.assertRaisesRegex(StagingError, "refusing to replace"):
                stage_plan(plan, foreign_output)
            self.assertEqual(
                (foreign_output / "user-file.txt").read_text(encoding="utf-8"),
                "preserve me",
            )

            stage_plan(plan, output)
            previous_snapshot = _file_snapshot(output)
            changed_pose = (
                source
                / "round_001"
                / "sentence_001"
                / "take_001"
                / "take_001.pose.jsonl"
            )
            changed_pose.write_bytes(
                b'{"sample_index":0,"pose_valid":true,"fixture":"changed"}\n'
            )
            changed_plan = build_plan(source)

            real_fingerprints = staging_module._tree_fingerprints
            call_count = 0

            def fail_third_verification(path: Path) -> dict[str, tuple[int, str]]:
                nonlocal call_count
                call_count += 1
                if call_count == 3:
                    return {}
                return real_fingerprints(path)

            with mock.patch.object(
                staging_module,
                "_tree_fingerprints",
                side_effect=fail_third_verification,
            ), self.assertRaises(StagingError):
                stage_plan(changed_plan, output)

            self.assertEqual(_file_snapshot(output), previous_snapshot)

    def test_dry_run_and_validate_only_never_write_output(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "recordings"
            _write_full_source(source)

            for flag in ("--dry-run", "--validate-only"):
                output = root / flag.removeprefix("--")
                stdout = io.StringIO()
                stderr = io.StringIO()
                with self.subTest(flag=flag), contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                    exit_code = main(
                        [
                            "--source-root",
                            str(source),
                            "--output-root",
                            str(output),
                            flag,
                        ]
                    )
                self.assertEqual(exit_code, EXIT_OK)
                self.assertFalse(output.exists())
                self.assertIn("write_performed: false", stdout.getvalue())

    def test_validation_failure_exit_code_does_not_write(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "recordings"
            output = root / "build-content"
            _write_full_source(source, missing={"031"})
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                exit_code = main(
                    [
                        "--source-root",
                        str(source),
                        "--output-root",
                        str(output),
                        "--dry-run",
                    ]
                )
            self.assertEqual(exit_code, EXIT_VALIDATION)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
