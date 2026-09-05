# Pet asset repository layout

`assets/` groups accepted atlases, animation media and character-reference inputs in separate subdirectories. Only the first two are embedded into the EXE; reference JPGs and indexes must never enter runtime packaging. See `docs/asset-layout.md` for migration and build compatibility.

## `.agents/skills/capylulu-pet`

Reusable instructions for Codex. This directory contains workflow guidance, references and reusable scripts only. It must not contain generated sprites, QA screenshots, temporary prompts, or packaged executables.

## `assets/character-references`

Primary character-reference input: one JPG reference group per source video, with `index.json`, `reference-board.jpg` and per-group `manifest.json`. Sample a small subset for one character/run. These are visual evidence, not animation frames or runtime dependencies. Keep the library unchanged while generating a pet; copy only selected inputs into the run directory. Files must retain normal inherited access permissions.

## `.pet-work/<pet-id>`

Disposable work area for one pet-generation or repair run. Typical contents include prompts, canonical references copied for the run, decoded image-generation outputs, extracted frames, repair candidates, registration intermediates, and verbose per-row diagnostics.

Everything here may be removed after the final atlas and selected QA evidence have been copied elsewhere.

## `artifacts/pet-qa/<pet-id>`

Curated evidence for reviewing a shipped pet. Retain only useful artifacts such as:

- final atlas validation;
- contact sheets and animation previews;
- direction sheets, semantic verdicts, blind-review results, and continuity reports;
- concise review or despill reports needed to explain acceptance.

Do not retain duplicate final atlases, raw image-generation outputs, or extracted frame trees here.

## `assets/pet-atlases`

Runtime product assets embedded into CapyLulu during publishing. Each character uses an application-ready PNG or WebP atlas alongside a sibling `*.pet.json` manifest.

A standard v2 manifest carries identity only — `id`, `displayName`, `roles`, `spriteVersionNumber` — because the v2 row order is a fixed convention the application already knows. Add `actions`, `clickRows`, or `lookRows` only to override an atlas that deviates from it; whatever is omitted is filled in from the convention, and an atlas with fewer than 11 rows is not given look-direction rows.

This is the authoritative location for pet atlases and character manifests that ship in the EXE. Keep filenames, identity fields and atlas geometry stable when moving directories; embedded logical names remain independent of filesystem paths.

## `assets/animations`

Runtime animation GIFs and companion cover images. Preserve `match-game/block/` and `match-game/celebrate/` subdirectories. These are embedded media, not character-reference inputs or generated pet atlases. GIF export and acceptance follow `docs/gif-extraction-standards.md`.

## `raw_images`

Original character references. They are inputs, not runtime dependencies.

## `dist`

Local publish output produced by `build.ps1`. It is disposable and ignored by Git.
