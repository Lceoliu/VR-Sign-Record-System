# W2 content staging completion report

Date: 2026-08-25
Worker: W2
Baseline supplied by Orchestrator: `35fcc93`

## 1. Scope completed

- Added a repeatable, standard-library-only Python CLI under
  `tools/interaction_content` with required `--source-root`, optional
  `--signer-id` (default `wang`), required `--output-root`, and mutually
  exclusive `--dry-run` / `--validate-only` no-write modes.
- Scans the inspected Host layout
  `<round>/sentence_NNN/take_NNN/take_NNN.{meta.json,pose.jsonl}`. Metadata is
  parsed and cross-checked against sentence/Take directories; selection does
  not infer completion from filenames or modification times.
- Requires `capture_status == "completed"`, non-simulated metadata, a valid
  zoned RFC 3339 `utc_stopped`, matching sentence/Take/index fields, and an
  intact non-empty JSONL Pose. When quality fields are present, JSONL frame
  count, valid-frame count, and ratio must match metadata.
- Resolves each sentence with the exact V1 ordering key
  `(utc_stopped, take_index)`. Timestamp precision is retained to nanoseconds.
  Multiple candidates tied on both fields fail with
  `AMBIGUOUS_LATEST_TAKE`; filesystem order is never a fallback.
- Emits the V1 `instruction-content-manifest.json` for all ordered sentence IDs
  `001` through `031`, including phase, signer, Take, completion UTC, relative
  Pose/metadata paths, Pose byte count, and SHA-256.
- Stages exactly 31 Pose JSONL files and 31 metadata JSON files. Camera WebM,
  Pose visualization MP4, and every `test_game` subtree are excluded.
- Uses a same-parent temporary generation, verifies complete-tree fingerprints,
  renames an existing generation to an invocation-owned backup, publishes the
  verified directory by rename, verifies again, and restores the previous
  generation on a handled publication/verification failure. An identical
  rerun is detected by complete fingerprints and performs no replacement.
- Refuses filesystem roots, source/output overlap, symlinks, and unrelated
  non-empty output directories. A non-empty output is replaceable only when its
  manifest identifies a complete schema-v1 generation for the same signer.
- Added standard-library `unittest` fixtures covering all required selection,
  validation, hashing, idempotency, no-write, exclusion, and rollback behavior.

Deliberately not completed:

- No real recording artifacts were staged or copied; real `Y:` validation was
  read-only.
- No large generated content was written into the repository.
- No Unity scene, Unity runtime, Host/Recorder runtime, OpenXR setting,
  `docs/interaction/PROGRESS.md`, or repository history was changed.
- No Quest/device validation was claimed; this is a build-time Python tool.

## 2. Files created

- `tools/interaction_content/__init__.py`
- `tools/interaction_content/stage_instruction_content.py`
- `tools/interaction_content/README.md`
- `tools/interaction_content/tests/__init__.py`
- `tools/interaction_content/tests/test_stage_instruction_content.py`
- `docs/interaction/reports/W2-content-staging.md`

## 3. Commands and exact results

Syntax/help check:

```powershell
python -m py_compile tools/interaction_content/stage_instruction_content.py tools/interaction_content/tests/test_stage_instruction_content.py
python tools/interaction_content/stage_instruction_content.py --help
```

Result: exit `0`; CLI help lists both no-write modes and exit codes `0`, `2`,
`3`, and `4`.

Python 3.10 fixture suite:

```powershell
python -B -m unittest discover -s tools/interaction_content/tests -v
```

Final result: exit `0`; `Ran 12 tests in 2.788s`; `OK`.

Python 3.14 compatibility suite:

```powershell
py -3.14 -B -m unittest discover -s tools/interaction_content/tests -v
```

Final result: exit `0`; `Ran 12 tests in 2.588s`; `OK`.

Covered assertions:

- completion timestamp precedence at one-nanosecond resolution;
- Take-index tie-break and equal-key ambiguity failure;
- exact `001-031` phase ranges;
- missing sentence, missing Pose, non-completed status, simulated/metadata
  mismatch paths, corrupt metadata JSON, and corrupt Pose JSONL failures;
- manifest SHA-256/byte counts and copied bytes;
- exclusion of `test_game`, WebM, and MP4;
- byte-identical/no-op second staging run;
- `--dry-run` and `--validate-only` creating no output;
- rollback to a previous complete generation after forced post-publish
  verification failure, plus preservation of unrelated non-empty output.

Real data validate-only command:

```powershell
python -B tools/interaction_content/stage_instruction_content.py `
  --source-root 'Y:\SignVR-Host-Portable\data\recordings\0824_gaming_wang' `
  --signer-id wang `
  --output-root 'C:\Users\woshica\AppData\Local\Temp\signvr-w2-content-validation-do-not-create' `
  --validate-only
```

Result: exit `0`, `VALIDATION OK`, and the requested output path was absent
both before and after the command.

The final implementation was also rerun with the same arguments and
`--dry-run`. Result: exit `0`, `VALIDATION OK`, `write_performed: false`, and
the output path remained absent before and after.

## 4. Real data validation result

Inspected source resolved by Windows to:

```text
\\10.19.136.199\slr\SignVR-Host-Portable\data\recordings\0824_gaming_wang
```

Validated structure and result:

- one `round_001` with sentence directories `sentence_001` through
  `sentence_031`;
- coverage: `31/31`;
- valid completed candidates: `33`;
- final Take distribution: `take_001=29`, `take_002=1`, `take_004=1`;
- `sentence_001` selected `take_004`, completed
  `2025-06-23T06:23:26.666633Z`;
- `sentence_016` selected `take_002`, completed
  `2025-06-23T06:29:37.558038Z`;
- all other sentences selected `take_001`;
- selected Pose total: `178,835,352` bytes;
- deterministic manifest `generated_utc`:
  `2025-06-23T06:36:44.420551Z`;
- `test_game` subtrees: `0`;
- source media excluded: `65` (`34` camera WebM and `31` visualization MP4).

Four partial/reserved Take directories have no metadata/Pose pair and were
reported as explicit non-fatal warnings:

- `round_001/sentence_001/take_001` (empty);
- `round_001/sentence_016/take_001` (camera-only);
- `round_001/sentence_031/take_002` (empty);
- `round_001/sentence_031/take_003` (empty).

These cannot enter selection because a candidate begins only with a readable
canonical metadata file. By contrast, an explicit non-`completed` metadata
status, metadata without its Pose, Pose without metadata, corrupt JSON/JSONL,
or any identity mismatch is fatal.

The inspected Host data also confirms that metadata `pose_file` retains the
Quest-local filename (for example
`sentence_001__take-004__take_004.pose.jsonl`) while the Host stores the upload
as `take_004.pose.jsonl`. The tool validates that the metadata value is a safe
Pose basename but intentionally reads the Host-normalized file named after the
Take directory, matching the existing Host upload/storage contract.

## 5. Generated content strategy and prerequisites

- The CLI has no default output location. A caller must explicitly provide an
  external/local build-content path.
- A real non-dry run would create one manifest plus 62 source artifacts under
  `<output-root>/wang/sentence_NNN/`; these are build inputs and are not intended
  for version control.
- No third-party Python packages are required. The implementation was exercised
  with Python 3.10.18 and Python 3.14.4.
- Exit codes are `0` success, `2` CLI usage, `3` validation/content failure, and
  `4` staging/publication I/O failure.

## 6. Contract deviations and integration assumptions

No incompatible V1 schema deviation was introduced. The manifest fields match
the frozen instruction-content contract; metadata is copied but metadata
byte/hash fields are not added because V1 specifies `pose_bytes` and
`pose_sha256` only.

Two explicit implementation interpretations should be reviewed:

1. `generated_utc` is the greatest completion timestamp in the selected source
   snapshot rather than wall-clock execution time. This keeps unchanged input
   byte-for-byte deterministic and idempotent while remaining a valid UTC
   content-generation watermark.
2. Empty/camera-only directories without metadata/Pose are treated as abandoned
   reservations and produce warnings, not a dataset-wide failure. This is
   necessary for the inspected real source. Any explicit malformed or
   non-completed metadata-bearing Take remains fatal.

On Windows, replacing an existing non-empty directory requires two atomic
renames (old to backup, staged to output); there is no single directory-exchange
primitive in the Python standard library. Handled failures restore the backup,
and a forced rollback is tested. Sudden process/host power loss between the two
renames can leave the complete backup beside an absent output rather than a
half-written output tree; an operator can restore that clearly named backup.

## 7. Recommended Orchestrator review

1. Review the warning-versus-error policy for the four real partial Take
   directories and the deterministic `generated_utc` interpretation above.
2. Run the 12-test suite from the integrated branch with the project-supported
   Python version.
3. Repeat the documented real-data `--validate-only` command and confirm the
   `31/31`, `33`, `29/1/1`, and `178,835,352` results.
4. Run one non-dry staging operation into an external disposable build-content
   directory, inspect the 63-file tree, then rerun and confirm
   `write_performed: false (already identical)`.
5. Validate the downstream Unity build consumer against POSIX manifest paths
   such as `wang/sentence_001/take_004.pose.jsonl`; do not add camera or
   visualization media to Quest StreamingAssets/content.
6. Preserve this tool's explicit output-root requirement so generated recording
   data is never silently written into the repository.
