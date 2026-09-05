#!/usr/bin/env python3
"""Batch videos into compact JPG reference groups. Requires Pillow and FFmpeg."""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import math
import re
import shutil
import subprocess
import tempfile
from pathlib import Path
from urllib.parse import quote

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps, ImageStat

VERSION = 3
EXTENSIONS = {'.mp4', '.mov', '.mkv', '.webm', '.avi', '.m4v'}


def run(command):
    result = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if result.returncode:
        raise RuntimeError(result.stderr[-2000:])
    return result.stdout, result.stderr


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def executable(value):
    found = shutil.which(value)
    if not found:
        raise ValueError(f'Executable not found: {value}; use --ffmpeg / --ffprobe with full paths')
    return found


def discover(inputs):
    videos = set()
    for source in inputs:
        if source.is_dir():
            videos.update(p.resolve() for p in source.rglob('*') if p.is_file() and p.suffix.lower() in EXTENSIONS)
        elif source.is_file() and source.suffix.lower() in EXTENSIONS:
            videos.add(source.resolve())
        else:
            raise ValueError(f'Unsupported or missing input: {source}')
    if not videos:
        raise ValueError('No videos found')
    return sorted(videos)


def probe(video, ffprobe):
    raw, _ = run([ffprobe, '-v', 'error', '-select_streams', 'v:0', '-show_entries',
                  'format=duration:stream=width,height,avg_frame_rate', '-of', 'json', str(video)])
    data = json.loads(raw)
    stream = data.get('streams', [None])[0]
    duration = float(data.get('format', {}).get('duration', 0))
    if not stream or not math.isfinite(duration) or duration <= 0:
        raise ValueError('Missing video stream or positive duration')
    return dict(stream, duration_seconds=duration)


def candidates(video, duration, work, ffmpeg):
    # One decode pass; at most about 160 small candidates even for a long video.
    interval = max(0.5, duration / 160)
    filters = (f"select='isnan(prev_selected_t)+gte(t-prev_selected_t,{interval:.6f})',"
               'scale=192:192:force_original_aspect_ratio=decrease,showinfo')
    _, log = run([ffmpeg, '-hide_banner', '-loglevel', 'info', '-i', str(video), '-map', '0:v:0',
                  '-an', '-vf', filters, '-fps_mode', 'vfr', '-q:v', '4', str(work / '%04d.jpg')])
    times = [float(t) for t in re.findall(r'\bpts_time:([\d.eE+\-]+)', log)]
    paths = sorted(work.glob('*.jpg'))
    if not paths or len(paths) != len(times):
        raise ValueError('Could not match candidate images to video timestamps')
    result = []
    for path, timestamp in zip(paths, times):
        with Image.open(path) as image:
            gray = image.convert('L')
            center = gray.crop((gray.width // 10, gray.height // 10, gray.width * 9 // 10, gray.height * 9 // 10))
            hist = gray.histogram()
            count = gray.width * gray.height
            blank = sum(hist[:18]) / count > 0.96 or sum(hist[242:]) / count > 0.98
            result.append({'time': timestamp, 'signature': gray.resize((32, 32)),
                           'sharpness': ImageStat.Stat(center.filter(ImageFilter.FIND_EDGES)).mean[0],
                           'brightness': ImageStat.Stat(gray).mean[0],
                           'dark': sum(hist[:48]) / count, 'blank': blank})
    return result


def difference(a, b):
    return ImageStat.Stat(ImageChops.difference(a['signature'], b['signature'])).mean[0] / 255


def choose(frames, count):
    notes = []
    usable = [f for f in frames if not f['blank']]
    if not usable:
        return [], ['No usable candidates; source is nearly blank']
    # End cards often contain crisp text/QR edges: sharpness is NOT a veto.
    # Find a contiguous, stable terminal scene, then include its fade-in.
    end = usable[-1]
    first = len(usable) - 1
    while first > 0 and difference(usable[first - 1], end) < 0.05:
        first -= 1
    if first > 0 and len(usable) - first >= 2 and end['brightness'] < 115:
        while first > 0 and usable[first - 1]['time'] > frames[-1]['time'] * 0.45:
            previous = usable[first - 1]
            fade = previous['brightness'] < 30 or (previous['brightness'] < end['brightness'] + 25 and difference(previous, end) < 0.12)
            if not fade:
                break
            first -= 1
        if first > 0 and usable[first]['time'] > frames[-1]['time'] * 0.45:
            previous = usable[first - 1]
            if previous['brightness'] - end['brightness'] > 35 and difference(previous, end) > 0.16:
                cutoff = usable[first]['time']
                usable = usable[:first]
                notes.append(f'Likely platform end card and fade excluded from {cutoff:.3f}s onward')
    sharp = max(f['sharpness'] for f in usable) or 1
    selected = []
    start, stop = usable[0]['time'], usable[-1]['time']
    for bin_index in range(count):
        bucket = [f for f in usable if min(count - 1, int((f['time'] - start) / max(stop - start, 0.001) * count)) == bin_index]
        if not bucket:
            continue
        def score(f):
            novelty = min((difference(f, prior) for prior in selected), default=0.15)
            return min(novelty / 0.15, 1) * 0.65 + f['sharpness'] / sharp * 0.35
        best = max(bucket, key=score)
        if not selected or min(difference(best, p) for p in selected) >= 0.025:
            selected.append(best)
    if len(selected) < count:
        notes.append(f'Only {len(selected)} distinct usable frames; not padded with duplicates')
    if frames[-1]['time'] > 80:
        notes.append('Long video sampled sparsely; brief actions may need a shorter clip')
    return sorted(selected, key=lambda f: f['time']), notes


def board(group, records):
    columns = min(3, len(records))
    cell, margin, gap = 320, 20, 16
    rows = math.ceil(len(records) / columns)
    canvas = Image.new('RGB', (margin * 2 + columns * (cell + gap) - gap,
                               64 + rows * (cell + 44)), '#f4f1e9')
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default(size=18)
    draw.text((margin, 20), 'VIDEO REFERENCES | source frames', font=font, fill='#252525')
    for i, record in enumerate(records):
        x, y = margin + (i % columns) * (cell + gap), 64 + (i // columns) * (cell + 44)
        with Image.open(group / record['file']) as image:
            fitted = ImageOps.contain(image.convert('RGB'), (cell, cell), Image.Resampling.LANCZOS)
        canvas.paste(fitted, (x + (cell - fitted.width) // 2, y + (cell - fitted.height) // 2))
        draw.text((x, y + cell + 8), f"{i + 1:02d} | {record['timestamp_seconds']:.3f}s", font=font, fill='#252525')
    canvas.save(group / 'reference-board.jpg', quality=88, optimize=True)


def process(video, args):
    stat = video.stat()
    fingerprint = {'source': str(video), 'size': stat.st_size, 'mtime_ns': stat.st_mtime_ns,
                   'frames': args.frames, 'version': VERSION}
    digest = hashlib.sha256(json.dumps(fingerprint, sort_keys=True).encode()).hexdigest()[:12]
    stem = re.sub(r'[^\w.-]', '_', video.stem)[:64]
    group = args.out_dir / f'{stem}-{digest}'
    manifest_path = group / 'manifest.json'
    if manifest_path.exists():
        old = json.loads(manifest_path.read_text(encoding='utf-8'))
        if old.get('fingerprint') == fingerprint:
            paths = [group / r['file'] for r in old['frames']] + [group / 'reference-board.jpg']
            for path in paths:
                with Image.open(path) as image:
                    image.verify()
            return {'source': str(video), 'group': group.name, 'status': 'skipped', 'notes': old['notes']}
    if group.exists():
        raise ValueError(f'Existing group is incomplete or unrecognized: {group}; use a new output directory')
    metadata = probe(video, args.ffprobe)
    with tempfile.TemporaryDirectory(prefix='.reference-work-', dir=args.out_dir) as temporary:
        work = Path(temporary)
        selection, notes = choose(candidates(video, metadata['duration_seconds'], work, args.ffmpeg), args.frames)
        if not selection:
            raise ValueError('; '.join(notes))
        staged = work / 'group'
        (staged / 'frames').mkdir(parents=True)
        records = []
        for i, chosen in enumerate(selection, 1):
            filename = f'frames/{i:02d}.jpg'
            run([args.ffmpeg, '-hide_banner', '-loglevel', 'error', '-ss', str(chosen['time']),
                 '-i', str(video), '-map', '0:v:0', '-frames:v', '1', '-q:v', '2', str(staged / filename)])
            with Image.open(staged / filename) as image:
                image.verify()
            records.append({'file': filename, 'timestamp_seconds': chosen['time'], 'origin': 'source_frame'})
        board(staged, records)
        write_json(staged / 'manifest.json', {'fingerprint': fingerprint, 'source_video': str(video),
                   'video_metadata': metadata, 'selection': 'automatic_visual_heuristics',
                   'review_status': 'unreviewed', 'notes': notes, 'frames': records})
        # Moving a TemporaryDirectory child preserves its private Windows ACL.
        # Copy into newly created directories so exported assets inherit the
        # chosen output root's permissions and are readable in Explorer/browser.
        shutil.copytree(staged, group, copy_function=shutil.copyfile)
    return {'source': str(video), 'group': group.name, 'status': 'created', 'notes': notes}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('inputs', nargs='+', type=Path, help='Video files or directories')
    parser.add_argument('--out-dir', type=Path, default=Path(__file__).resolve().parents[4] / 'assets' / 'character-references',
                        help='Output directory (default: project root/assets/character-references)')
    parser.add_argument('--frames', type=int, choices=range(4, 7), default=6, help='Maximum frames per video (default: 6)')
    parser.add_argument('--ffmpeg', default='ffmpeg')
    parser.add_argument('--ffprobe', default='ffprobe')
    args = parser.parse_args()
    args.ffmpeg, args.ffprobe = executable(args.ffmpeg), executable(args.ffprobe)
    videos = discover(args.inputs)
    args.out_dir = args.out_dir.resolve()
    args.out_dir.mkdir(parents=True, exist_ok=True)
    index = args.out_dir / 'index.json'
    previous = []
    if index.exists():
        data = json.loads(index.read_text(encoding='utf-8'))
        if data.get('producer') != 'video-character-reference':
            raise ValueError('Output index belongs to another tool; choose a new output directory')
        previous = data['videos']
    elif (args.out_dir / 'index.html').exists():
        raise ValueError('Existing HTML index is unrecognized; choose a new output directory')
    results = []
    for video in videos:
        try:
            result = process(video, args)
        except (OSError, ValueError, RuntimeError, KeyError, IndexError) as error:
            result = {'source': str(video), 'status': 'error', 'error': str(error)}
        results.append(result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
    by_source = {r['source']: r for r in previous}
    by_source.update({r['source']: r for r in results})
    write_json(index, {'producer': 'video-character-reference', 'videos': list(by_source.values())})
    sections = ['<!doctype html><meta charset="utf-8"><title>Video reference groups</title>',
                '<style>body{font:16px system-ui;max-width:1100px;margin:32px auto;padding:16px}img{max-width:100%}section{margin-bottom:40px}</style>',
                '<h1>Video reference groups</h1><p>Automatically selected source frames. Labels are timestamps; selection has not been visually reviewed.</p>']
    for record in by_source.values():
        sections.append(f"<section><h2>{html.escape(Path(record['source']).name)}</h2>")
        if 'group' in record:
            link = quote(record['group'])
            sections.append(f'<a href="{link}/manifest.json">Timestamp manifest</a><br><a href="{link}/reference-board.jpg"><img loading="lazy" src="{link}/reference-board.jpg"></a>')
        sections.append(f"<p>{html.escape(record.get('error', '; '.join(record.get('notes', []))))}</p></section>")
    (args.out_dir / 'index.html').write_text('\n'.join(sections), encoding='utf-8')
    print(json.dumps({'processed': sum(r['status'] == 'created' for r in results),
                      'skipped': sum(r['status'] == 'skipped' for r in results),
                      'failed': sum(r['status'] == 'error' for r in results), 'index': str(index)}))
    return int(any(r['status'] == 'error' for r in results))


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        raise SystemExit(str(error)) from error
