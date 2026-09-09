"""Compare an existing atlas with a candidate without normalizing away size or pose changes."""
import argparse
import hashlib
import html
import json
from pathlib import Path
import re
import statistics
import sys

from PIL import Image, ImageChops, ImageDraw, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parent))
import atlas_run as runs

CHECKS = ('appearance', 'motion', 'geometry', 'playback')


def geometry(cell):
    alpha = cell.getchannel('A')
    histogram = alpha.histogram()
    # Ignore nearly invisible edge noise when measuring the visible body.
    bbox = alpha.point(lambda value: 255 if value >= 32 else 0).getbbox()
    return {'bbox': list(bbox) if bbox else None,
            'visible_pixels': sum(histogram[1:]),
            'alpha_mass': round(sum(i * n for i, n in enumerate(histogram)) / 255, 3)}


def opaque_preview(image, background='#e8e8e8'):
    return Image.alpha_composite(Image.new('RGBA', image.size, background), image).convert('RGB')


def read_layout(path, size):
    layout = json.loads(runs.read_json(path))
    if (layout['width'], layout['height']) != size or not layout.get('rows'):
        raise ValueError('Layout dimensions must match the original atlas')
    rectangles, row_ids = [], []
    for row in layout['rows']:
        row_id = str(row.get('id', row.get('row')))
        if row_id in row_ids or row_id == 'None' or not row.get('slots'):
            raise ValueError('Layout requires unique row IDs and explicit slots')
        row_ids.append(row_id)
        for slot in row['slots']:
            x0, y0, x1, y1 = slot['rectangle']
            if (not all(isinstance(v, int) for v in (x0, y0, x1, y1)) or
                    not 0 <= x0 < x1 <= size[0] or not 0 <= y0 < y1 <= size[1] or
                    not isinstance(slot.get('occupied'), bool)):
                raise ValueError('Invalid slot rectangle or occupied flag')
            if any(x0 < b[2] and x1 > b[0] and y0 < b[3] and y1 > b[1] for b in rectangles):
                raise ValueError('Layout slots overlap')
            rectangles.append((x0, y0, x1, y1))
    return layout


def render_row(folder, row_id, frames, duration):
    widths = [max(pair[0].width, pair[1].width) for pair in frames]
    height = max(max(pair[0].height, pair[1].height) for pair in frames)
    strip = Image.new('RGB', (sum(widths), height * 2 + 54), '#e8e8e8')
    draw = ImageDraw.Draw(strip)
    draw.text((4, 3), 'INPUT', fill='#222222')
    draw.text((4, height + 28), 'CANDIDATE', fill='#222222')
    x, animation = 0, []
    cell_width = max(widths)
    for index, (before, after) in enumerate(frames, 1):
        strip.paste(opaque_preview(before), (x, 20))
        strip.paste(opaque_preview(after), (x, height + 45))
        draw.text((x + 4, height + 8), str(index), fill='#222222')
        # Every frame uses the same canvas and pixel scale. Never fit the subject.
        preview = Image.new('RGB', (cell_width * 2 + 12, height + 26), '#e8e8e8')
        preview.paste(opaque_preview(before), (0, 24))
        preview.paste(opaque_preview(after), (cell_width + 12, 24))
        label = ImageDraw.Draw(preview)
        label.text((2, 4), f'INPUT {index}', fill='#222222')
        label.text((cell_width + 14, 4), f'OUTPUT {index}', fill='#222222')
        animation.append(preview)
        x += widths[index - 1]
    name = hashlib.sha256(row_id.encode()).hexdigest()[:10]
    strip.save(folder / (name + '.png'))
    animation[0].save(folder / (name + '.gif'), save_all=True, append_images=animation[1:],
                      duration=duration, loop=0, disposal=2)
    return {'contact': name + '.png', 'animation': name + '.gif'}


def compare(run, candidate, layout_path, *, size_tolerance=.10, position_tolerance=3,
            preview_duration_ms=180):
    if not 0 < size_tolerance < 1 or position_tolerance < 0 or preview_duration_ms <= 0:
        raise ValueError('Invalid comparison tolerances or preview duration')
    data = runs.manifest(run)
    if data['mode'] != 'align' or not data['source']:
        raise ValueError('Initialize an align run with its original --source first')
    source_info = data['source']
    source_path = runs.inside(run, source_info['path'])
    if runs.digest(source_path) != source_info['sha256']:
        raise ValueError('Original source snapshot changed')
    with Image.open(source_path) as opened:
        source = opened.convert('RGBA')
    layout = read_layout(layout_path, source.size)
    saved_layout = runs.snapshot(run, layout_path, 'layout')
    candidate_info = runs.snapshot(run, candidate, 'candidate', folder='pipeline-output/candidates')
    with Image.open(runs.inside(run, candidate_info['path'])) as opened:
        output = opened.convert('RGBA')
    settings = {'size_tolerance': size_tolerance, 'position_tolerance': position_tolerance,
                'preview_duration_ms': preview_duration_ms}
    comparison_id = hashlib.sha256(json.dumps([source_info['sha256'], candidate_info['sha256'],
        saved_layout['sha256'], settings, runs.digest(__file__)], sort_keys=True).encode()).hexdigest()[:20]
    folder = run / 'reviews' / comparison_id
    folder.mkdir(parents=True, exist_ok=True)
    runs.event(run, 'compare', 'started', comparison_id=comparison_id,
               source=source_info['path'], candidate=candidate_info['path'])
    errors, rows, flags = [], [], []
    covered = Image.new('L', source.size)
    coverage = ImageDraw.Draw(covered)
    for row in layout['rows']:
        for slot in row['slots']:
            left, top, right, bottom = slot['rectangle']
            coverage.rectangle((left, top, right-1, bottom-1), fill=255)
    outside = ImageOps.invert(covered)
    if ImageChops.multiply(source.getchannel('A'), outside).getbbox():
        errors.append('Layout omits visible source pixels outside its slots')
    if output.size != source.size:
        errors.append(f'Canvas changed: {source.size} -> {output.size}')
    else:
        if ImageChops.multiply(output.getchannel('A'), outside).getbbox():
            errors.append('Candidate has visible pixels outside the declared slots')
        for row in layout['rows']:
            row_id = str(row.get('id', row.get('row')))
            frames, metrics = [], []
            for index, slot in enumerate(row['slots'], 1):
                pair = [image.crop(slot['rectangle']) for image in (source, output)]
                before, after = [geometry(cell) for cell in pair]
                if bool(before['visible_pixels']) != slot['occupied']:
                    errors.append(f'Layout disagrees with input at row {row_id}, slot {index}')
                if bool(after['visible_pixels']) != slot['occupied']:
                    errors.append(f'Candidate occupancy changed at row {row_id}, slot {index}')
                if not slot['occupied']:
                    continue
                entry = {'slot': index, 'input': before, 'candidate': after}
                b, a = before['bbox'], after['bbox']
                if b and a:
                    entry.update(width_ratio=round((a[2] - a[0]) / (b[2] - b[0]), 4),
                                 height_ratio=round((a[3] - a[1]) / (b[3] - b[1]), 4),
                                 baseline_shift=a[3] - b[3],
                                 center_x_shift=((a[0] + a[2]) - (b[0] + b[2])) / 2)
                    if any(abs(entry[k] - 1) > size_tolerance for k in ('width_ratio', 'height_ratio')):
                        flags.append({'row': row_id, 'slot': index, 'kind': 'size_changed'})
                    if any(abs(entry[k]) > position_tolerance for k in ('baseline_shift', 'center_x_shift')):
                        flags.append({'row': row_id, 'slot': index, 'kind': 'position_changed'})
                else:
                    errors.append(f'No significant visible body at row {row_id}, slot {index}')
                metrics.append(entry)
                frames.append(pair)
            ratios = [item for item in metrics if 'width_ratio' in item]
            item = {'row': row_id, 'frames': metrics,
                    'median_width_ratio': statistics.median([m['width_ratio'] for m in ratios]) if ratios else None,
                    'median_height_ratio': statistics.median([m['height_ratio'] for m in ratios]) if ratios else None}
            if frames:
                durations = row.get('durations_ms')
                if durations is not None and (len(durations) != len(frames) or
                        any(not isinstance(d, int) or d <= 0 for d in durations)):
                    raise ValueError(f'Invalid durations_ms for row {row_id}')
                item.update(render_row(folder, row_id, frames, durations or preview_duration_ms))
                item['timing_source'] = 'layout' if durations else 'review_only'
            rows.append(item)
    report = {'schema_version': 1, 'comparison_id': comparison_id, 'created_at': runs.now(),
              'source': source_info, 'candidate': candidate_info, 'layout': saved_layout,
              'settings': settings, 'structure': 'fail' if errors else 'pass', 'errors': errors,
              'geometry_flags': flags, 'rows': rows, 'visual_review': 'pending',
              'note': 'Geometry flags require review, not automatic resizing. Frame counts cannot prove pose preservation.'}
    runs.save_json(folder / 'comparison.json', report)
    data = runs.manifest(run)
    data.update(status='rejected' if errors else 'pending',
                latest_comparison={'id': comparison_id,
                                   'path': (folder/'comparison.json').relative_to(run).as_posix()})
    data.pop('latest_review', None)
    runs.save_json(run / 'run.json', data)
    assessment = {'comparison_id': comparison_id, 'reviewer': '', 'rows': [
        {'row': row['row'], **{check: {'status': 'pending', 'evidence': ''} for check in CHECKS}} for row in rows]}
    template = folder / 'assessment-template.json'
    if not template.exists():
        runs.save_json(template, assessment)
    cards = ''.join(f'<section><h2>Row {html.escape(row["row"])}</h2>'
                    f'<p>Width ratio: {row["median_width_ratio"]} · Height ratio: {row["median_height_ratio"]}</p>'
                    f'<img class="contact" src="{row["contact"]}" alt="Same-scale input above candidate">'
                    f'<p><img src="{row["animation"]}" alt="Synchronized input and candidate"></p>'
                    f'<p>Timing: {row["timing_source"]}</p></section>' for row in rows if 'contact' in row)
    (folder / 'index.html').write_text('<!doctype html><html lang="en"><meta charset="utf-8">'
        '<meta name="viewport" content="width=device-width, initial-scale=1"><title>Atlas comparison</title>'
        '<style>body{font:16px system-ui;background:#e8e8e8;color:#222;margin:24px;line-height:1.5}'
        'section{margin-block:28px}.contact{max-width:100%;height:auto}img{vertical-align:top}</style>'
        '<h1>Original / candidate comparison</h1><p>Equal pixel scale and original frame order. '
        'Synthetic review timing is labelled review_only; exporting GIFs does not mean playback was reviewed.</p>'
        f'<p>Structure: {report["structure"]} · Geometry flags: {len(flags)} · Visual review: <span id="review-state">pending</span></p>'
        '<p><a href="comparison.json">Measurements</a> · <a href="assessment-template.json">Review template</a></p>'
        + ''.join(f'<p>{html.escape(error)}</p>' for error in errors) + cards + '</html>', encoding='utf-8')
    runs.event(run, 'compare', 'failed' if errors else 'completed', comparison_id=comparison_id,
               report=(folder / 'comparison.json').relative_to(run).as_posix(),
               structure=report['structure'], geometry_flags=len(flags), visual_review='pending')
    return report, folder


def record_review(run, comparison_path, assessment_path):
    comparison_path = Path(comparison_path)
    if comparison_path.is_file():
        comparison_path = comparison_path.resolve()
    comparison_path = runs.inside(run, comparison_path)
    report = json.loads(runs.read_json(comparison_path))
    review = json.loads(runs.read_json(assessment_path))
    if review['comparison_id'] != report['comparison_id']:
        raise ValueError('Review belongs to another candidate/comparison')
    if runs.manifest(run).get('latest_comparison', {}).get('id') != report['comparison_id']:
        raise ValueError('A newer comparison exists; review the current candidate')
    for role in ('source', 'candidate', 'layout'):
        if runs.digest(runs.inside(run, report[role]['path'])) != report[role]['sha256']:
            raise ValueError(f'{role} snapshot changed after comparison')
    if not review.get('reviewer', '').strip():
        raise ValueError('Review must identify its reviewer')
    expected = {row['row'] for row in report['rows']}
    if len(review['rows']) != len(expected) or {row['row'] for row in review['rows']} != expected:
        raise ValueError('Review must cover every compared row exactly once')
    statuses = []
    for row in review['rows']:
        for check in CHECKS:
            value = row[check]
            if value['status'] not in {'pass', 'fail', 'pending'}:
                raise ValueError(f'Unknown review status: {value["status"]}')
            if value['status'] != 'pending' and not value.get('evidence', '').strip():
                raise ValueError(f'{check} needs observed evidence, not just a pass/fail label')
            statuses.append(value['status'])
    status = ('rejected' if report['structure'] == 'fail' or 'fail' in statuses else
              'pending' if not statuses or 'pending' in statuses else 'accepted')
    saved = runs.snapshot(run, assessment_path, 'visual_assessment', folder='reviews/assessments')
    verdict = {'comparison_id': report['comparison_id'], 'candidate_sha256': report['candidate']['sha256'],
               'status': status, 'recorded_at': runs.now(), 'assessment': saved['path']}
    runs.save_json(comparison_path.parent / 'verdict.json', verdict)
    preview = comparison_path.parent / 'index.html'
    if preview.exists():
        body = preview.read_text(encoding='utf-8')
        body = re.sub(r'(<span id="review-state">)[^<]*(</span>)',
                      lambda match: match[1] + status + match[2], body)
        preview.write_text(body, encoding='utf-8')
    runs.event(run, 'visual_review', status, verdict=verdict)
    data = runs.manifest(run)
    data.update(status=status, latest_review=verdict)
    runs.save_json(run / 'run.json', data)
    return verdict


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    comparison = commands.add_parser('compare')
    comparison.add_argument('--candidate', type=Path, required=True)
    comparison.add_argument('--layout', type=Path, required=True)
    comparison.add_argument('--size-tolerance', type=float, default=.10)
    comparison.add_argument('--position-tolerance', type=int, default=3)
    comparison.add_argument('--preview-duration-ms', type=int, default=180)
    review = commands.add_parser('review')
    review.add_argument('--comparison', type=Path, required=True)
    review.add_argument('--assessment', type=Path, required=True)
    for command in (comparison, review):
        command.add_argument('--run', type=Path, required=True)
    args = parser.parse_args()
    run = None
    try:
        run = runs.work_directory(args.run)
        if args.command == 'compare':
            result, folder = compare(run, args.candidate, args.layout, size_tolerance=args.size_tolerance,
                position_tolerance=args.position_tolerance, preview_duration_ms=args.preview_duration_ms)
            print(json.dumps({'comparison': str(folder / 'comparison.json'), 'preview': str(folder / 'index.html'),
                'structure': result['structure'], 'geometry_flags': len(result['geometry_flags']),
                'visual_review': 'pending'}, ensure_ascii=False))
            return 1 if result['errors'] else 0
        result = record_review(run, args.comparison, args.assessment)
        print(json.dumps(result, ensure_ascii=False))
        return 0
    except (ValueError, OSError, KeyError) as exc:
        if run and (run / 'run.json').exists():
            runs.event(run, args.command, 'failed', error=str(exc))
        print(json.dumps({'error': str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
