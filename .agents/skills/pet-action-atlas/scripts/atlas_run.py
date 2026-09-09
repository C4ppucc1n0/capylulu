"""Keep one pet-atlas run's inputs, requests, results and factual timeline together."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys
import uuid

from PIL import Image


def now():
    return datetime.now(timezone.utc).isoformat()


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def read_json(path):
    return Path(path).read_text(encoding='utf-8-sig')


def work_directory(path):
    run = Path(path).resolve()
    if any(part.casefold() in {'assets', '.agents', '.git'} for part in run.parts):
        raise ValueError('Use artifacts/work/<run>, outside assets and skill code')
    found = False
    for directory in (run, *run.parents):
        if directory.name.casefold() == 'artifacts':
            work = directory / 'work'
            if run == work or not run.is_relative_to(work):
                raise ValueError('Use a task directory under artifacts/work/<run>')
            found = True
    if found:
        return run
    raise ValueError('Use a task directory under artifacts/work/<run>')


def inside(run, relative):
    path = (run / relative).resolve()
    if not path.is_relative_to(run):
        raise ValueError(f'Run path escapes its directory: {relative}')
    return path


def manifest(run):
    return json.loads(read_json(run / 'run.json'))


def event(run, stage, status, **details):
    entry = {'id': uuid.uuid4().hex[:12], 'at': now(), 'stage': stage,
             'status': status, **details}
    with (run / 'events.jsonl').open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(entry, ensure_ascii=False) + '\n')
    timeline = run / 'timeline.md'
    if not timeline.exists():
        timeline.write_text('# 执行轨迹\n\n| UTC 时间 | 阶段 | 状态 | 重点事实 | 记录 |\n| --- | --- | --- | --- | --- |\n', encoding='utf-8')
    facts = '; '.join(f'{key}: {details[key]}' for key in (
        'row', 'request_id', 'mode', 'cache_hit', 'geometry_flags', 'elapsed_seconds', 'error', 'note')
        if key in details and details[key] is not None)
    facts = facts.replace('|', '\\|').replace('\n', ' ')
    pointer = details.get('request') or details.get('report') or details.get('output')
    if not pointer and details.get('request_id'):
        pointer = f'requests/{details["request_id"]}.json'
    if isinstance(details.get('file'), dict):
        pointer = details['file']['path']
        facts = f'{details["file"]["role"]}: {details["file"]["original_name"]}'.replace('|', '\\|')
    if isinstance(details.get('result'), dict):
        pointer = details['result']['path']
    if isinstance(details.get('verdict'), dict):
        pointer = details['verdict']['assessment']
    link = f'[文件](<{pointer}>)' if pointer else ''
    with timeline.open('a', encoding='utf-8') as stream:
        stream.write(f'| {entry["at"]} | {stage} | {status} | {facts} | {link} |\n')
    return entry


def initialize(run, mode):
    run = work_directory(run)
    path = run / 'run.json'
    if path.exists():
        data = manifest(run)
        if data['mode'] != mode:
            raise ValueError('This run has another mode; use a new run directory')
        return data
    run.mkdir(parents=True, exist_ok=True)
    for name in ('inputs', 'requests', 'decoded', 'pipeline-output', 'reviews'):
        (run / name).mkdir(exist_ok=True)
    data = {'schema_version': 1, 'mode': mode, 'created_at': now(), 'files': [],
            'source': None, 'status': 'in_progress'}
    save_json(path, data)
    event(run, 'initialize', 'completed', mode=mode)
    return data


def snapshot(run, source, role, *, folder='inputs'):
    """Copy bytes by hash; record the real image format, even with a wrong suffix."""
    source = Path(source).resolve()
    if not source.is_file():
        raise ValueError(f'Missing input file: {source}')
    sha = digest(source)
    info = {'origin': str(source), 'original_name': source.name, 'role': role,
            'sha256': sha, 'bytes': source.stat().st_size}
    suffix = source.suffix.lower()
    try:
        with Image.open(source) as image:
            info.update(format=image.format, size=list(image.size), mode=image.mode)
            suffix = {'PNG': '.png', 'JPEG': '.jpg', 'WEBP': '.webp',
                      'GIF': '.gif'}.get(image.format, suffix)
    except (OSError, ValueError):
        pass
    destination = inside(run, f'{folder}/{sha}{suffix}')
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        if digest(destination) != sha:
            raise ValueError(f'Previously saved input changed: {destination}')
    else:
        shutil.copyfile(source, destination)
    if digest(destination) != sha:
        raise ValueError(f'Input changed while being copied: {source}')
    info['path'] = destination.relative_to(run).as_posix()
    data = manifest(run)
    if not any(item['path'] == info['path'] and item['role'] == role for item in data['files']):
        data['files'].append(info)
        if role == 'source':
            if data['source'] and data['source']['sha256'] != sha:
                raise ValueError('Source changed; create a new run instead')
            data['source'] = info
        save_json(run / 'run.json', data)
        event(run, 'snapshot', 'completed', file=info)
    return info


def start_request(run, *, row, prompt, images, tool, model=None, reason=None):
    """Save the exact prompt and ordered image payload before calling the image tool."""
    if not row or not tool or not images:
        raise ValueError('A request needs a row, tool and the actual attached image files')
    prompt_info = snapshot(run, prompt, 'prompt', folder='requests/prompts')
    attachments = [snapshot(run, image, 'request_image') for image in images]
    request_id = uuid.uuid4().hex[:12]
    record = {'id': request_id, 'row': row, 'tool': tool, 'model': model,
              'reason': reason, 'started_at': now(), 'status': 'started',
              'prompt': prompt_info, 'images': attachments}
    save_json(run / 'requests' / (request_id + '.json'), record)
    event(run, 'generation', 'started', request_id=request_id, row=row,
          request=f'requests/{request_id}.json')
    return record


def finish_request(run, request_id, *, result=None, error=None):
    if not re.fullmatch(r'[0-9a-f]{12}', request_id):
        raise ValueError('Invalid request ID')
    path = inside(run, f'requests/{request_id}.json')
    record = json.loads(read_json(path))
    if record['status'] != 'started':
        raise ValueError('Request already finished; a retry needs a new request record')
    if bool(result) == bool(error):
        raise ValueError('Supply either a result file or an error explanation')
    record['finished_at'] = now()
    record['elapsed_seconds'] = round((datetime.fromisoformat(record['finished_at']) -
                                       datetime.fromisoformat(record['started_at'])).total_seconds(), 3)
    record['status'] = 'failed' if error else 'completed'
    if error:
        record['error'] = error
    else:
        record['result'] = snapshot(run, result, 'generated_row', folder='decoded')
    save_json(path, record)
    event(run, 'generation', record['status'], request_id=request_id,
          row=record['row'], elapsed_seconds=record['elapsed_seconds'],
          result=record.get('result'), error=error)
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    create = commands.add_parser('init')
    create.add_argument('--mode', choices=['align', 'generate'], required=True)
    create.add_argument('--source', type=Path)
    create.add_argument('--reference', type=Path, action='append', default=[])
    request = commands.add_parser('request')
    request.add_argument('--row', required=True)
    request.add_argument('--prompt', type=Path, required=True)
    request.add_argument('--image', type=Path, action='append', required=True)
    request.add_argument('--tool', required=True)
    request.add_argument('--model')
    request.add_argument('--reason')
    result = commands.add_parser('result')
    result.add_argument('--request', required=True)
    result.add_argument('--file', type=Path)
    result.add_argument('--error')
    for command in (create, request, result):
        command.add_argument('--run', type=Path, required=True)
    args = parser.parse_args()
    run = None
    try:
        run = work_directory(args.run)
        if args.command == 'init':
            if args.mode == 'align' and not args.source:
                raise ValueError('Alignment requires the original source atlas')
            result = initialize(run, args.mode)
            if args.source:
                snapshot(run, args.source, 'source')
            for reference in args.reference:
                snapshot(run, reference, 'reference')
            result = {'run': str(run), 'manifest': str(run / 'run.json')}
        elif args.command == 'request':
            result = start_request(run, row=args.row, prompt=args.prompt, images=args.image,
                                   tool=args.tool, model=args.model, reason=args.reason)
        else:
            result = finish_request(run, args.request, result=args.file, error=args.error)
        print(json.dumps(result, ensure_ascii=False))
        return 0
    except (ValueError, OSError, KeyError) as exc:
        if run and (run/'run.json').exists():
            event(run, args.command, 'failed', error=str(exc))
        print(json.dumps({'error': str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
