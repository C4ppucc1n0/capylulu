#!/usr/bin/env python3
"""Prepare a small, reusable video-reference selection; never generate or publish sprites."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]


def inside(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root):
        raise ValueError(f'Reference path escapes its library: {relative}')
    return path


def prepare(library, run, limit=4, groups=None, baseline=None):
    library, run = library.resolve(), run.resolve()
    if run.is_relative_to(library) or run.is_relative_to(ROOT / 'assets'):
        raise ValueError('Choose a run directory outside source references and runtime assets')
    config = {'library': str(library), 'limit': limit, 'groups': groups or [],
              'baseline': str(baseline.resolve()) if baseline else None}
    selection_path = run / 'reference-selection.json'
    if selection_path.exists():
        existing = json.loads(selection_path.read_text(encoding='utf-8'))
        if existing['config'] != config:
            raise ValueError('This run already has a different selection; use a new --run-dir')
        for entry in existing['selected']:
            if not Path(entry['board']).is_file():
                raise ValueError('A selected board is missing; use a new --run-dir')
        if not (run / 'generation-brief.md').is_file():
            raise ValueError('The generation brief is missing; use a new --run-dir')
        return existing
    index = json.loads((library / 'index.json').read_text(encoding='utf-8'))
    available = []
    for item in index['videos']:
        if item.get('status') == 'error' or not item.get('group'):
            continue
        directory = inside(library, item['group'])
        if (directory / 'reference-board.jpg').is_file() and (directory / 'manifest.json').is_file():
            available.append(item)
    available.sort(key=lambda item: item['group'])
    if not available:
        raise ValueError('No complete video reference groups found')
    if groups:
        selected = []
        for group in groups:
            matches = [item for item in available if item['group'] == group]
            if not matches:
                matches = [item for item in available if item['group'].startswith(group)]
            if len(matches) != 1:
                raise ValueError(f'Group prefix must identify exactly one available group: {group}')
            if matches[0] not in selected:
                selected.append(matches[0])
    else:
        count = min(limit, len(available))
        positions = [round(i * (len(available) - 1) / max(1, count - 1)) for i in range(count)]
        selected = [available[position] for position in positions]
    baseline_info = None
    if baseline:
        baseline = baseline.resolve()
        if not baseline.is_file() or baseline.suffix.lower() not in {'.png', '.webp'}:
            raise ValueError('Baseline must be an existing PNG/WebP atlas')
        manifest = baseline.with_suffix('.pet.json')
        baseline_info = {'atlas': str(baseline), 'sha256': hashlib.sha256(baseline.read_bytes()).hexdigest(),
                         'manifest': json.loads(manifest.read_text(encoding='utf-8')) if manifest.exists() else None}
    records = []
    # Check all selected manifests before writing; no frame trees or source video reads.
    for item in selected:
        directory = inside(library, item['group'])
        manifest = json.loads((directory / 'manifest.json').read_text(encoding='utf-8'))
        for frame in manifest.get('frames', []):
            inside(directory, frame['file'])
        records.append({'group': item['group'], 'source_video': manifest.get('source_video', item.get('source')),
                        'source_manifest': str(directory / 'manifest.json'),
                        'board': str(run / 'references' / (item['group'] + '.jpg')),
                        'frames': manifest.get('frames', []), 'selection_notes': manifest.get('notes', [])})
    (run / 'references').mkdir(parents=True, exist_ok=True)
    for record in records:
        target = Path(record['board'])
        if target.exists():
            raise ValueError('Run has partial existing references; use a new --run-dir')
        shutil.copyfile(inside(library, record['group']) / 'reference-board.jpg', target)
    packet = {'config': config, 'selection_method': 'explicit' if groups else 'evenly_spaced_groups_not_semantic_ranking',
              'selected': records, 'baseline': baseline_info}
    brief = '''# Generation brief

Inspect only the selected boards listed in reference-selection.json. They ground character identity and useful views; their timestamps are not animation timing and their labels are not verified action labels.

Establish ONE canonical full-body image, shared outfit/material/palette and compact identity lock. Use real observed details to resolve uncertainty. Discard slate frames, scene backgrounds and watermarks; do not copy props or outfits merely because they occur in another video. Add a reference group only to resolve an important character feature or missing pose evidence for a chosen action.

Choose concrete performances from the user's intent, trigger context and selected visual evidence before mapping them to helper filenames. In this brief, add one short line per requested row: trigger -> chosen performance -> reference/pose evidence -> key phases within the fixed frame count. Avoid the same default waving/running routine for every atlas; examples and helper names are not a whitelist. Add only a targeted reference if the chosen action needs more pose evidence. Deliberate small props may support a compatible action if they remain coherent and fit the cells; do not copy scenery or change the outfit.

For each requested action, attach the canonical image first and only the relevant observed views as supplements. Generate the entire row as one coherent sequence at constant camera/scale/lighting, with anticipation, motion and recovery (or seamless loop). Preserve the same head, muzzle, eyes, ears, top detail, short limbs and costume across all rows. Do not stitch unrelated video poses into an animation.

Follow capylulu-pet/references/atlas-contract.md for trigger constraints, physical rows, frame counts, neutral cell and clockwise gaze order. Keep helper filenames unchanged: waving is an interaction slot, not a compulsory wave; non-directional running is working, not locomotion. Preserve directional movement, lift/reversed-drop compatibility and exact gaze semantics. Use deterministic assembly. Judge the chosen performance and trigger rather than literal helper names; compare identity and motion quality, not identical poses, to the prior accepted atlas. Do not ship a regression because structure alone passes.
'''
    (run / 'generation-brief.md').write_text(brief, encoding='utf-8')
    selection_path.write_text(json.dumps(packet, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return packet


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-dir', required=True, type=Path)
    parser.add_argument('--references', default=ROOT / 'assets' / 'character-references', type=Path)
    parser.add_argument('--limit', default=4, type=int, choices=range(1, 9))
    parser.add_argument('--group', action='append')
    parser.add_argument('--baseline', type=Path)
    args = parser.parse_args()
    result = prepare(args.references, args.run_dir, args.limit, args.group, args.baseline)
    print(json.dumps({'selection': str(args.run_dir.resolve() / 'reference-selection.json'),
                      'brief': str(args.run_dir.resolve() / 'generation-brief.md'),
                      'boards': [item['board'] for item in result['selected']]}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, KeyError) as error:
        raise SystemExit(str(error)) from error
