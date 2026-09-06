#!/usr/bin/env python3
"""Extract ordered action-reference candidates from videos, without a model or a skill."""
from __future__ import annotations

import argparse
import hashlib
import html
import json
import math
import os
import re
import shutil
import subprocess
import tempfile
import time
from pathlib import Path
from urllib.parse import quote

import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFont, ImageOps

from motion import describe, find_segments, measure, summary_indices
from visibility import assess_visibility

VERSION = 6
ROOT = Path(__file__).resolve().parent
EXTENSIONS = {'.mp4', '.mov', '.mkv', '.webm', '.avi', '.m4v'}


def run(command):
    result = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if result.returncode:
        raise RuntimeError(result.stderr[-3000:])
    return result.stderr


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def discover(inputs, output):
    videos = set()
    for source in inputs:
        if source.is_dir():
            videos.update(p.resolve() for p in source.rglob('*') if p.is_file()
                          and p.suffix.lower() in EXTENSIONS and not p.resolve().is_relative_to(output))
        elif source.is_file() and source.suffix.lower() in EXTENSIONS:
            videos.add(source.resolve())
        else:
            raise ValueError(f'Unsupported or missing input: {source}')
    if not videos:
        raise ValueError('No source videos found.')
    return sorted(videos)


def probe(video):
    reader = imageio_ffmpeg.read_frames(str(video))
    try:
        metadata = next(reader)
    finally:
        reader.close()
    if not math.isfinite(metadata['duration']) or metadata['duration'] <= 0:
        raise ValueError('Video needs a finite positive duration.')
    return metadata


def decode_samples(video, directory, args):
    directory.mkdir()
    # Select source frames, not interpolated or duplicated frames. showinfo records
    # their actual times relative to the first decoded video frame, including VFR.
    select = f"select='isnan(prev_selected_t)+gt(floor(t*{args.sample_fps}),floor(prev_selected_t*{args.sample_fps}))'"
    filters = f"setpts=PTS-STARTPTS,{select},scale='trunc(min({args.frame_width},iw)/2)*2':-2,showinfo"
    log = run([args.ffmpeg, '-hide_banner', '-loglevel', 'info', '-nostdin', '-i', str(video),
               '-map', '0:v:0', '-an', '-vf', filters, '-fps_mode', 'vfr', '-q:v', '2',
               str(directory / '%06d.jpg')])
    times = [float(t) for t in re.findall(r'\bpts_time:([\d.eE+\-]+)', log)]
    paths = sorted(directory.glob('*.jpg'))
    if not paths or len(paths) != len(times):
        raise ValueError('Decoded images and source timestamps do not match.')
    if any(b <= a for a, b in zip(times, times[1:])):
        raise ValueError('Source timestamps are not strictly increasing.')
    return [describe(path, timestamp) for path, timestamp in zip(paths, times)]


def make_board(images, labels, path, title):
    cell, gap, margin, columns = 240, 12, 16, 4
    rows = math.ceil(len(images) / columns)
    canvas = Image.new('RGB', (2 * margin + columns * (cell + gap) - gap, 58 + rows * (cell + 30)), '#f4f1e9')
    draw, font = ImageDraw.Draw(canvas), ImageFont.load_default(size=15)
    draw.text((margin, 18), title, fill='#202522', font=font)
    for i, (image, label) in enumerate(zip(images, labels)):
        x, y = margin + (i % columns) * (cell + gap), 58 + (i // columns) * (cell + 30)
        fitted = ImageOps.contain(image.convert('RGB'), (cell, cell), Image.Resampling.LANCZOS)
        canvas.paste(fitted, (x + (cell - fitted.width) // 2, y + (cell - fitted.height) // 2))
        draw.text((x, y + cell + 5), label, fill='#202522', font=font)
    canvas.save(path, quality=90)


def export_action(video, samples, segment, folder, args, number):
    chosen = [s for s in samples if segment['start'] <= s.time < segment['end']]
    indices = summary_indices(len(chosen), args.summary_frames)
    (folder / 'frames').mkdir(parents=True)
    (folder / 'summary').mkdir()
    records = []
    for i, sample in enumerate(chosen, 1):
        relative = f'frames/{i:04d}.jpg'
        shutil.copyfile(sample.path, folder / relative)
        records.append({'file': relative, 'source_time_seconds': round(sample.time, 6),
                        'clip_time_seconds': round(sample.time - segment['start'], 6)})
    summary, images = [], []
    for number_in_summary, index in enumerate(indices, 1):
        relative = f'summary/{number_in_summary:02d}.jpg'
        shutil.copyfile(chosen[index].path, folder / relative)
        summary.append({**records[index], 'file': relative, 'dense_frame': index + 1})
        with Image.open(folder / relative) as image:
            images.append(image.copy())
    make_board(images, [f"{i + 1:02d} | {r['source_time_seconds']:.3f}s" for i, r in enumerate(summary)],
               folder / 'contact-sheet.jpg', f"ACTION {number:02d} | {segment['start']:.3f} - {segment['end']:.3f}s")
    # Decode trim uses the same normalized video time origin as frame extraction.
    # Preview keeps source playback speed and excludes audio / subtitle streams.
    duration = segment['end'] - segment['start']
    # A constant-rate viewing copy preserves elapsed time, including VFR gaps.
    # Hold the final picture to the requested boundary instead of letting the
    # encoder truncate the final source frame's presentation interval.
    filters = (f"setpts=PTS-STARTPTS,trim=start={segment['start']:.6f}:end={segment['end']:.6f},"
               f"setpts=PTS-{segment['start']:.6f}/TB,fps=24:start_time=0,"
               "scale='trunc(min(640,iw)/2)*2':-2,"
               f"tpad=stop_mode=clone:stop_duration={duration:.6f},trim=duration={duration:.6f}")
    run([args.ffmpeg, '-hide_banner', '-loglevel', 'error', '-nostdin', '-i', str(video),
         '-map', '0:v:0', '-an', '-vf', filters, '-c:v', 'libx264', '-preset', 'veryfast',
         '-crf', '23', '-pix_fmt', 'yuv420p', '-fps_mode', 'cfr', '-movflags', '+faststart', str(folder / 'preview.mp4')])
    manifest = {'id': folder.name, 'source_video': str(video),
                'start_seconds': round(segment['start'], 6), 'end_seconds': round(segment['end'], 6),
                'duration_seconds': round(segment['end'] - segment['start'], 6),
                'selection': {k: v for k, v in segment.items() if k not in ('start', 'end', 'reference_quality')},
                'reference_quality': segment['reference_quality'],
                'review_status': 'unreviewed_candidate', 'summary_sampling': 'uniform_in_source_frame_order',
                'frames': records, 'summary_frames': summary,
                'preview': 'preview.mp4', 'contact_sheet': 'contact-sheet.jpg'}
    write_json(folder / 'manifest.json', manifest)
    return {'id': folder.name, 'start_seconds': manifest['start_seconds'], 'end_seconds': manifest['end_seconds'],
            'dense_frames': len(records), 'summary_frames': len(summary), 'warnings': segment['warnings'],
            'reference_quality': {k: v for k, v in segment['reference_quality'].items() if k != 'samples'}}


def cached_complete(group, manifest):
    for action in manifest['actions']:
        folder = group / action['id']
        if not (folder / 'manifest.json').is_file():
            return False
        data = json.loads((folder / 'manifest.json').read_text(encoding='utf-8'))
        paths = [data['preview'], data['contact_sheet']] + [r['file'] for r in data['frames'] + data['summary_frames']]
        if not all((folder / p).is_file() and (folder / p).stat().st_size > 0 for p in paths):
            return False
    return (group / 'timeline.jpg').is_file() and (group / 'analysis.json').is_file()


def process(video, args):
    started = time.perf_counter()
    settings = {name: getattr(args, name) for name in ('sample_fps', 'frame_width', 'summary_frames',
                 'min_duration', 'max_duration', 'min_motion', 'scene_threshold', 'keep_end_card', 'ranges', 'subject_color')}
    settings['ranges'] = [list(pair) for pair in args.ranges] if args.ranges else None
    with video.open('rb') as stream:
        source_hash = hashlib.file_digest(stream, 'sha256').hexdigest()
    fingerprint = {'version': VERSION, 'source': str(video), 'sha256': source_hash, 'settings': settings}
    digest = hashlib.sha256(json.dumps(fingerprint, sort_keys=True).encode()).hexdigest()[:12]
    stem = re.sub(r'[^\w.-]', '_', video.stem)[:64]
    group = args.out_dir / f'{stem}-{digest}'
    manifest_path = group / 'manifest.json'
    if manifest_path.is_file():
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        if manifest.get('fingerprint') == fingerprint and cached_complete(group, manifest):
            return {'source': str(video), 'group': group.name, 'status': 'skipped', 'actions': manifest['actions'],
                    'elapsed_seconds': round(time.perf_counter() - started, 3)}
    if group.exists():
        raise ValueError(f'Incomplete existing output: {group}. Use a new --out-dir to regenerate.')
    metadata = probe(video)
    with tempfile.TemporaryDirectory(prefix='.action-work-', dir=args.out_dir) as temporary:
        work = Path(temporary)
        samples = decode_samples(video, work / 'sampled', args)
        staged = work / 'result'
        staged.mkdir()
        if args.ranges:
            measure(samples, args.scene_threshold)
            segments, excluded = [], []
            for start, end in args.ranges:
                if start < 0 or end <= start or end > metadata['duration'] + .001:
                    raise ValueError(f'Range {start}:{end} is outside video duration {metadata["duration"]}.')
                warnings = ['Manually selected time range; action boundaries are not inferred.']
                if any(s.cut and start < s.time < end for s in samples):
                    warnings.append('This range crosses a detected scene change.')
                segments.append({'start': start, 'end': end, 'start_reason': 'manual', 'end_reason': 'manual', 'warnings': warnings})
        else:
            segments, excluded = find_segments(samples, metadata['duration'], minimum=args.min_duration,
                maximum=args.max_duration, min_motion=args.min_motion, scene_threshold=args.scene_threshold,
                keep_end_card=args.keep_end_card, summary_frames=args.summary_frames)
        subject_profile = assess_visibility(samples, segments, args.subject_color)
        actions = []
        for number, segment in enumerate(segments, 1):
            actions.append(export_action(video, samples, segment, staged / f'action-{number:02d}', args, number))
        timeline_indices = summary_indices(len(samples), min(16, len(samples))) if len(samples) > 1 else [0]
        images = []
        for index in timeline_indices:
            with Image.open(samples[index].path) as image:
                images.append(image.copy())
        make_board(images, [f'{samples[i].time:.3f}s' for i in timeline_indices], staged / 'timeline.jpg', 'SOURCE TIMELINE | includes rejected regions')
        write_json(staged / 'analysis.json', {'excluded': excluded, 'samples': [
            {'time_seconds': round(s.time, 6), 'motion': round(s.motion, 6), 'scene_cut': s.cut, 'cut_reason': s.cut_reason,
             'blank': s.blank, 'brightness': round(s.brightness, 6)} for s in samples]})
        write_json(staged / 'manifest.json', {'producer': 'video-actions', 'fingerprint': fingerprint,
            'source_video': str(video), 'video_metadata': metadata, 'actions': actions, 'excluded': excluded,
            'subject_profile': subject_profile,
            'notes': ['Heuristic candidates, not verified semantic actions.',
                      'Timestamps are relative to the first decoded video frame. JPG samples are not synthesized or duplicated.',
                      'Color-region visibility is advisory, not a semantic object detector. No-issue status is not visual approval.',
                      'Source backgrounds, watermarks and outfits are retained. These are references, not runtime sprites.'],
            'elapsed_seconds': round(time.perf_counter() - started, 3)})
        # Copy into new directories to inherit normal output ACLs on Windows.
        shutil.copytree(staged, group, copy_function=shutil.copyfile)
    return {'source': str(video), 'group': group.name, 'status': 'created', 'actions': actions,
            'elapsed_seconds': round(time.perf_counter() - started, 3)}


def write_index(output, videos):
    write_json(output / 'index.json', {'producer': 'video-actions', 'videos': videos})
    parts = ['<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width">',
             '<title>视频动作候选</title><style>body{font:16px system-ui;background:#f4f1e9;color:#202522;max-width:1180px;margin:32px auto;padding:0 20px}h1{font-size:30px}h2{overflow-wrap:anywhere}a{color:#276c55}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:20px}article{background:white;padding:16px;border-radius:12px}video,img{width:100%;border-radius:6px}video{max-height:320px;background:#181c19}.warning{color:#875212}section{margin:36px 0}p{line-height:1.6}summary{cursor:pointer}small{color:#58615c}</style>',
             '<h1>视频动作候选</h1><p>每段保留连续采样帧、摘要帧及原速预览。候选按镜头与运动变化切分，请检查动作起止是否完整。</p>']
    for video in videos:
        parts.append(f'<section><h2>{html.escape(Path(video["source"]).name)}</h2>')
        if video['status'] == 'error':
            parts.append(f'<p class="warning">{html.escape(video["error"])}</p></section>')
            continue
        group = quote(video['group'])
        parts.append(f'<p>{len(video["actions"])} 个候选 · <a href="{group}/manifest.json">来源与参数</a> · <a href="{group}/analysis.json">切分数据</a></p>')
        parts.append(f'<details><summary>查看整段视频时间线（含过滤区域）</summary><img loading="lazy" src="{group}/timeline.jpg"></details><div class="grid">')
        for action in video['actions']:
            base = f'{group}/{quote(action["id"])}'
            parts.append(f'<article><h3>{action["id"]} · {action["start_seconds"]:.2f}–{action["end_seconds"]:.2f}s</h3><video controls loop muted playsinline preload="none" poster="{base}/summary/01.jpg" src="{base}/preview.mp4"></video>')
            parts.append(f'<p>{action["dense_frames"]} 张连续采样帧 · {action["summary_frames"]} 帧摘要<br><a href="{base}/contact-sheet.jpg">查看摘要大图</a> · <a href="{base}/manifest.json">帧时间戳</a></p><img loading="lazy" src="{base}/contact-sheet.jpg">')
            status = action.get('reference_quality', {}).get('status')
            if status == 'review_suggested':
                parts.append('<p class="warning"><strong>参考适用性：需要检查</strong></p>')
            elif status in ('unassessed', 'disabled'):
                parts.append('<p><small>尚未评估角色可见性，请查看预览确认。</small></p>')
            if action['warnings']:
                parts.append('<p class="warning">' + html.escape(' '.join(action['warnings'])) + '</p>')
            parts.append('</article>')
        parts.append('</div></section>')
    (output / 'index.html').write_text('\n'.join(parts) + '</html>\n', encoding='utf-8')


def parse_range(value):
    try:
        start, end = map(float, value.split(':'))
        if not math.isfinite(start) or not math.isfinite(end) or start < 0 or end <= start:
            raise ValueError()
        return start, end
    except ValueError:
        raise argparse.ArgumentTypeError('Use START:END in seconds, with 0 <= START < END.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('inputs', type=Path, nargs='+', help='Video files or directories (recursive)')
    parser.add_argument('--out-dir', type=Path, default=ROOT.parent / 'reference-library' / 'actions',
                        help='Library directory (default: project reference-library/actions)')
    parser.add_argument('--sample-fps', type=int, choices=range(2, 31), default=12, help='Maximum source-frame samples per second')
    parser.add_argument('--frame-width', type=int, default=960, help='Maximum exported frame width; never upscaled')
    parser.add_argument('--summary-frames', type=int, choices=range(2, 33), default=8)
    parser.add_argument('--min-duration', type=float, default=.8)
    parser.add_argument('--max-duration', type=float, default=5.0)
    parser.add_argument('--min-motion', type=float, default=.0025)
    parser.add_argument('--scene-threshold', type=float, default=.24)
    parser.add_argument('--keep-end-card', action='store_true', help='Disable heuristic terminal-card filtering')
    parser.add_argument('--range', type=parse_range, action='append', dest='ranges', help='Override automatic segmentation; repeat for one source video')
    parser.add_argument('--ffmpeg', help='Override bundled FFmpeg executable')
    parser.add_argument('--subject-color', default='auto', help='Advisory foreground tracking: auto, off or a quoted #RRGGBB color')
    args = parser.parse_args()
    if (not all(math.isfinite(v) for v in (args.min_duration, args.max_duration, args.min_motion, args.scene_threshold))
            or not 0 < args.min_duration <= args.max_duration or not 0 <= args.min_motion < 1
            or not 0 < args.scene_threshold <= 1 or args.frame_width < 32 or args.frame_width % 2):
        parser.error('Invalid thresholds, durations or frame width (must be even and >= 32).')
    if args.ffmpeg:
        os.environ['IMAGEIO_FFMPEG_EXE'] = str(Path(shutil.which(args.ffmpeg) or args.ffmpeg).resolve())
    args.ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    args.out_dir = args.out_dir.resolve()
    videos = discover(args.inputs, args.out_dir)
    if args.ranges and len(videos) != 1:
        parser.error('--range requires exactly one video.')
    # Runtime assets must remain isolated from source frames and generated previews.
    if args.out_dir.is_relative_to(ROOT.parent / 'assets' / 'pet-atlases'):
        parser.error('Choose an output directory outside assets/pet-atlases.')
    args.out_dir.mkdir(parents=True, exist_ok=True)
    index = args.out_dir / 'index.json'
    previous = []
    if index.exists():
        data = json.loads(index.read_text(encoding='utf-8'))
        if data.get('producer') != 'video-actions':
            raise ValueError('Existing output index belongs to another tool; choose another --out-dir.')
        previous = data['videos']
    elif (args.out_dir / 'index.html').exists():
        raise ValueError('Existing HTML index is unrecognized; choose another --out-dir.')
    results = []
    for video in videos:
        try:
            result = process(video, args)
        except (OSError, ValueError, RuntimeError, KeyError) as error:
            result = {'source': str(video), 'status': 'error', 'error': str(error)}
        results.append(result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
    merged = {r['source']: r for r in previous}
    merged.update({r['source']: r for r in results})
    write_index(args.out_dir, list(merged.values()))
    print(json.dumps({'created': sum(r['status'] == 'created' for r in results),
                      'skipped': sum(r['status'] == 'skipped' for r in results),
                      'failed': sum(r['status'] == 'error' for r in results),
                      'actions': sum(len(r.get('actions', [])) for r in results),
                      'index': str(args.out_dir / 'index.html')}, ensure_ascii=False))
    return int(any(r['status'] == 'error' for r in results))


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (OSError, ValueError, RuntimeError) as error:
        raise SystemExit(str(error))
