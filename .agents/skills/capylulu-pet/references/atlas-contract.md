# Runtime contract and acceptance

This file describes the existing contract, not a new asset format. Authoritative consumers are `src/CapyLulu/SpriteSheet.cs`, `PetActions.cs`, `CharacterCatalog.cs` and `CapyLulu.csproj`.

## Physical layout (zero-based coordinates)

Standard v2: lossless transparent PNG/WebP, **1536 x 2288**, **8 columns x 11 rows**, **192 x 208** per cell. Preserve row positions and left-to-right frame order. No scene background, text, watermarks, guides, detached effects or ground shadows.

| Row | CapyLulu meaning | Helper strip name | Used columns |
| --- | --- | --- | --- |
| 0 | idle | idle | 0–5; column 6 is the neutral reference |
| 1 | drag right | running-right | 0–7 |
| 2 | drag left | running-left | 0–7 |
| 3 | click/greeting | waving | 0–3 |
| 4 | lift/drop/lift-drop gesture | jumping | 0–4 |
| 5 | flick/shake reaction | failed | 0–7 |
| 6 | waiting | waiting | 0–5 |
| 7 | working (not locomotion) | running | 0–5 |
| 8 | review | review | 0–5 |
| 9 | gaze A | look-row-9 | 0–7 |
| 10 | gaze B | look-row-10 | 0–7 |

The four currently shipped atlases all contain the neutral cell at row 0, column 6. Preserve it; do not erase it as an unused slot. Row 0 column 7 and the other unused row tails must be transparent. Do not copy a standard nine-row intermediate into the runtime directory.

Gaze is clockwise in screen coordinates: `000 up`, `022.5`, `045`, `067.5`, `090 right`, `112.5`, `135`, `157.5` in row 9; `180 down`, `202.5`, `225`, `247.5`, `270 left`, `292.5`, `315`, `337.5` in row 10. These are gaze directions, not a whole-body turnaround. Preserve the canonical body/camera and make eyes/head readable at display size. Generate and verify four cardinal anchors first; generate row 9 coherently from them, then row 10 from the same anchors plus accepted row 9. Repair a failing gaze row as a whole, not as an isolated pasted cell.

Each atlas has a sibling `<atlas-basename>.pet.json`. For standard v2 keep only the existing identity fields: `id`, `displayName`, `roles`, `spriteVersionNumber: 2`. Preserve the current `id` and roles on replacement. Do not write the helper's geometry/direction report as this runtime manifest. Custom `actions`, `clickRows`, `lookRows` overrides are retained only for an existing or explicitly requested nonstandard layout. Legacy assets without manifests and 288 x 312 cells remain supported; do not migrate them as a side effect.

## Reuse deterministic helpers

The installed `hatch-pet/scripts` directory contains the established tools. Resolve it from the available skill location; no need to activate its unrelated branding, custom-pet installation or worker orchestration. Use `--help` for the selected tool rather than loading every script.

| Stage | Existing helper | Required result |
| --- | --- | --- |
| Extract coherent generated strips | `extract_strip_frames.py` | Correct frame count; stable row scale/anchor; selected chroma key |
| Check extraction | `inspect_frames.py` | No errors; visually resolve warnings and clipping |
| Assemble standard rows | `compose_atlas.py` | Nine-row intermediate from named frame folders |
| Register/add gaze and neutral | `assemble_extended_atlas.py` | Full 8 x 11 atlas; row 9 registered before generating row 10 |
| Clean keyed edges | `despill_chroma_edges.py` | Successful report using the same recorded key; preserve alpha |
| Validate atlas | `validate_atlas.py --require-v2` | Correct dimensions, occupied/unused cells, transparency and no key leakage |
| Review motion and appearance | `make_contact_sheet.py`, `render_animation_previews.py` | Normal-size atlas overview and per-row loops |
| Review gaze | `make_direction_qa_sheet.py`, `measure_direction_continuity.py` | Neutral + ordered 16 views; inspect warnings |

Generate rows with a shared flat chroma key absent from the character palette when using keyed extraction. Use the same key in extraction, registration, cleanup and validation. Do not ask the image model to produce exact final atlas geometry; assemble it in code. Do not independently auto-fit every frame and erase intended jump height or introduce size popping. Use an equal-slot extraction fallback only after checking its frame registration visually. If existing helpers are unavailable, stop before publishing a candidate and report the missing tool; do not substitute an unvalidated assembly path.

## Quality gate

Compare the candidate to the observed reference set, the canonical image, and the previous accepted atlas/preview when replacing an asset. Record a concise `qa/review.json` containing selected references, baseline path if any, structural results, identity/appearance verdict, per-row motion verdicts and 16 ordered gaze verdicts with visual reasons. Technical validation cannot prove appearance quality.

Block release for: changed character features/proportions or outfit between frames, clipped anatomy, empty required slots, detached fragments, flickering scale/position, broken loop or reversed drag cadence, incorrect action meaning, wrong/ambiguous cardinal gaze, wrong-quadrant diagonals or a visible reversal in the gaze loop. Review metric warnings visually; a numerical pass does not excuse an obvious defect. Do not lower thresholds to pass a candidate. Keep the prior runtime atlas until its replacement passes. Recheck affected rows after repairs and ensure the final assembled file still validates.

Retain final validation, contact sheet, per-row previews and gaze review in `artifacts/pet-qa/<pet-id>/`. After accepting and copying an atlas/manifest pair into `assets/pet-atlases/`, run the repository's offline consumer check:

```powershell
.\.dotnet\dotnet.exe run --project tests\CapyLulu.Validation\CapyLulu.Validation.csproj --configuration Debug
```

Then use `build.ps1` and a bounded launch smoke test when the task delivers runtime assets and no user-owned instance would be interrupted. Do not install into a separate Codex pet directory; this project's consumer is CapyLulu.
