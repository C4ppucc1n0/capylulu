---
name: capylulu-pet
description: Create, repair, validate, organize, or package CapyLulu pet sprite assets in this repository. Use for pet artwork, action atlases, gaze directions, pet QA, or generated_actions changes; do not use for ordinary desktop UI or application logic changes.
---

# CapyLulu Pet Assets

Keep the pet production workflow, temporary files, QA evidence, and shipped assets separate.

## Repository contract

- Put reusable repository guidance in this skill. Never store generated images or run outputs inside the skill directory.
- Use `.pet-work/<pet-id>/` for prompts, decoded images, extracted frames, repair attempts, and other disposable run data. This directory is ignored by Git.
- Put curated evidence worth retaining in `artifacts/pet-qa/<pet-id>/`. Keep final validation JSON, contact sheets, direction QA, and useful previews; exclude duplicated atlases and per-frame extraction trees.
- Put only application-ready assets in `generated_actions/`: the final sprite atlas and its sibling `*.pet.json` character manifest. For a standard v2 atlas the manifest carries identity only (`id`, `displayName`, `roles`, `spriteVersionNumber`); the application supplies the action table. It is optional entirely, at the cost of falling back to a filename-derived id and no `loafing` role.
- Treat `raw_images/` as source references. Do not modify source artwork unless the user explicitly requests it.
- Treat `dist/` as disposable build output.

Read [references/repository-layout.md](references/repository-layout.md) before moving, packaging, or cleaning pet assets.

## Workflow

1. Confirm the source image, target character, and whether the request changes visuals or only repository organization.
2. For new or repaired v2 artwork, use the available `hatch-pet` workflow and set its run directory under `.pet-work/<pet-id>/`.
3. Preserve the standard physical atlas contract. In CapyLulu terminology, rows 1 and 2 are `drag-right` and `drag-left`; row 7 is `working`. Do not regenerate actions merely because the upstream hatch-pet row identifiers contain `running`.
4. Validate the finished atlas and action manifest before copying them into `generated_actions/`.
5. Copy only curated QA evidence into `artifacts/pet-qa/<pet-id>/`. Leave transient frames and retries in `.pet-work/`.
6. Run `build.ps1`, perform a bounded launch smoke test when no user-owned instance would be interrupted, and report the exact final paths.

## Constraints

- Do not regenerate or restyle pet artwork unless the user asks for a visual change.
- Preserve the manifest `id` when replacing an atlas so saved character selections remain compatible. The atlas filename is not the identity and may change; the application persists `SelectedCharacterId`, which comes from the manifest.
- Preserve old resources without manifests; the application intentionally supports them.
- Only write `actions`, `clickRows`, or `lookRows` into a manifest when the atlas deviates from the standard v2 row order. Copying the default table into every character is what this convention exists to avoid.
- Do not commit `.pet-work/` or `dist/`.
- Do not delete source images, curated QA evidence, or shipped assets without explicit scope or a verified replacement.
