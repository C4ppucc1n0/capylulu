---
name: video-character-reference
description: Automatically turn each source video into a compact JPG reference group for character appearance and action references. Use for batch video reference extraction.
---

# Video Character Reference

Run one command; the program handles sampling, selection, JPG export, timestamps, and indexes. Do not read its implementation or manually select frames during normal use.

```powershell
python .agents/skills/video-character-reference/scripts/video_references.py video
```

- Requires Python 3.10+, Pillow 10.1+, and FFmpeg/FFprobe. Use an available Python runtime; pass `--ffmpeg` and `--ffprobe` with executable paths if PATH is stale.
- Accepts video files or directories. Default: up to 6 distinct frames per video; `--frames 4` makes smaller groups.
- Output defaults to `assets/character-references/` under the project root. Use `--out-dir` only when another location is requested.
- Each group contains `frames/*.jpg`, `reference-board.jpg`, and `manifest.json`. Open `index.html` to browse; `index.json` reports created, skipped, and failed inputs.
- Unchanged files and settings are skipped. Changed inputs create new groups; earlier groups remain. New batches extend the index. One failed video does not stop others.
- Exported files inherit the output folder's permissions; private temporary directories must not be moved into the final library.
- Read the summary and report output paths and flagged cases. Inspect only flagged boards or a small sample when useful; full manual review is optional.
- Selection uses image differences, edge sharpness, and time coverage. Exclude platform end cards (Douyin, Xiaohongshu) including their fade-in; do not reserve a slot for the video's final frame. Automatic detection is heuristic and its cutoff is recorded. Near-duplicates may yield fewer images; short actions may be missed, especially in long clips. Labels are timestamps, not inferred action names or verified angles.
- Preserve source videos. Save references outside `assets/pet-atlases/`. Identity prose, semantic labeling, and AI-generated turnarounds are separate, on-request tasks.
