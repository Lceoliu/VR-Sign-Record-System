# Interaction instruction content staging

`stage_instruction_content.py` validates Host recordings, resolves the latest
completed Recording Take for each sentence `001` through `031`, and creates the
build input defined by `docs/interaction/INTERACTION_CONTRACT_V1.md`.

The output root is deliberately required and has no repository-local default.
Use a machine-local build-content directory so the large Pose recordings are
not accidentally added to the repository.

```powershell
python tools/interaction_content/stage_instruction_content.py `
  --source-root Y:\SignVR-Host-Portable\data\recordings\0824_gaming_wang `
  --signer-id wang `
  --output-root D:\SignVRBuildContent\instruction-content `
  --validate-only
```

Remove `--validate-only` to publish the output. `--dry-run` is an equivalent
no-write workflow with a distinct mode label. A successful publish contains
only the manifest, 31 Pose JSONL files, and their 31 metadata JSON files.
Camera WebM files, visualization MP4 files, and any `test_game` subtree are
excluded.

For data safety, a non-empty existing output directory is replaceable only when
it already has a complete schema-v1 instruction manifest for the same signer.
Filesystem roots, source/output overlap, symlinks, and unrelated non-empty
directories are rejected.

Selection is deterministic: `utc_stopped` wins first and `take_index` breaks a
timestamp tie. If both values tie across multiple candidates, validation fails
instead of choosing by filesystem order. `generated_utc` is the latest selected
completion timestamp, making an unchanged source produce byte-identical output.

Exit codes:

- `0`: validation/staging succeeded
- `2`: invalid command line
- `3`: source or content validation failed
- `4`: staging I/O or transactional publication failed

Tests use only temporary directories:

```powershell
python -m unittest discover -s tools/interaction_content/tests -v
```
