---
name: capylulu-pet
description: Generate or repair coherent CapyLulu action atlases using a small subset of assets/character-references as character evidence, preserving the existing runtime atlas and manifest contract. Also use for pet asset validation and packaging.
---

# CapyLulu Pet

Input: per-video JPG groups in `assets/character-references/`. Output: the same application-ready action atlas and sibling `*.pet.json` in `assets/pet-atlases/`.

## Normal workflow

1. **Prepare a small reference set.** Run the helper below once per character/run. It selects up to four groups by default, records provenance, and writes a reusable generation brief. Inspect only those boards; four is a starting budget, not a required count. Prefer groups supplied by the user; add or substitute a group only when identity, a useful angle or a chosen action needs more pose evidence. The index has no verified action labels, so automatic sampling is not semantic ranking.
2. **Lock one character.** Use the selected groups to establish a canonical full-body reference and a short identity lock (head/muzzle/eyes/ears/top detail, proportions, palette/material, chosen outfit). Keep the same canonical image throughout the atlas. For replacements, inspect the existing atlas and representative previews as the quality baseline; preserve its identity/roles and any accepted custom layout. Correct known visual mistakes against the video evidence rather than copying them into the new baseline.
3. **Choose performances, then generate coherent rows.** Read [references/atlas-contract.md](references/atlas-contract.md) once for trigger constraints, exact rows, frame counts and gaze order. Choose concrete actions from the user's intent and the selected visual evidence BEFORE mapping them to helper names. In the existing generation brief, record one short line per requested row: trigger -> chosen performance -> useful reference/pose evidence -> key phases within the available frames. Do not default every atlas to the same waving/running routine; variety is welcome when it suits the character and trigger, not a quota or a fixed menu. Helper names are storage interfaces, not mandatory choreography. Use `imagegen` for actual artwork, attaching the canonical image and only one or two relevant observed views when necessary. Generate each row together as a coherent sequence with anticipation, motion and recovery or a clean loop. Reference frames inspire appearance and actions; they are not a frame-by-frame animation script. Do not splice unrelated frames, outfits or cameras. Keep shared scale, lighting, grounding and anatomy; repair the smallest failing row with the same identity references.
4. **Assemble and accept.** Use the existing deterministic tools listed in the contract. Require structural validation AND visual comparison against references/canonical/baseline at normal pet size. Judge the chosen performance and its trigger compatibility, not whether it literally waves or runs; the old atlas is a quality baseline, not a pose template. Check complete action cycles, first/last transitions, no size popping, no cropped/blank cells, no identity drift, and all 16 gaze directions in order. Reject regressions even when file validation passes. Copy only accepted assets to `assets/pet-atlases/` and concise evidence to `artifacts/pet-qa/<pet-id>/`.

```powershell
python .agents/skills/capylulu-pet/scripts/prepare_references.py --run-dir .pet-work/lulu-next
```

Use `--limit 5` for a larger initial sample, repeat `--group <group-id-or-prefix>` for chosen groups, or use `--references <directory>` for another reference library. For an atlas replacement, pass `--baseline assets/pet-atlases/<name>.webp` to record the existing atlas and manifest. Reuse the same run for the same selection; use a new run to change it. The helper prepares inputs only; it does not generate images or publish assets.

## Keep the work small

- Read `reference-selection.json` and `generation-brief.md`; inspect the selected boards once. An older cached brief does not override this skill's action-choice rules. Add a targeted reference only if needed to resolve a chosen action or important pose; do not reopen all videos, all groups or the full hatch-pet orchestration on every row.
- Reuse existing deterministic extraction, registration, assembly and QA tools; only consult their `--help` when needed. Image synthesis and visual acceptance still require visual judgment.
- A normal run needs one final visual review of the atlas and motion previews. Add focused review for a failed or ambiguous row; do not replace quality checks with automatic approval or add repeated whole-run reviews.
- Build/launch only when delivering changed runtime assets or when explicitly requested. A skill edit or reference preparation alone does not require rebuilding the app.
- Preserve source videos, `assets/character-references/`, `raw_images/`, existing runtime identities and legacy support. Never use a source JPG as a sprite frame or change application loading code to accommodate a malformed atlas.
- Keep temporary inputs/prompts/retries in `.pet-work/<pet-id>/`. Read [references/repository-layout.md](references/repository-layout.md) only when moving, cleaning or packaging assets. Export with normal inherited permissions, not private temporary-directory ACLs.
