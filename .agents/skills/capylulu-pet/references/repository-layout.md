# Pet asset repository layout

## `.agents/skills/capylulu-pet`

Reusable instructions for Codex. This directory contains workflow guidance and references only. It must not contain generated sprites, QA screenshots, temporary prompts, or packaged executables.

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

## `generated_actions`

Runtime product assets embedded into CapyLulu during publishing. Each character uses an application-ready PNG or WebP atlas. A v2 character may include a sibling `*.pet.json` manifest with action rows and look-direction rows.

This is the authoritative location for assets that ship in the EXE.

## `raw_images`

Original character references. They are inputs, not runtime dependencies.

## `dist`

Local publish output produced by `build.ps1`. It is disposable and ignored by Git.
