"""CapyLulu v2 post-processing. No image API, visual approval or runtime publishing."""
import argparse
import hashlib
import html
import importlib
import json
import math
import os
from pathlib import Path
import sys
import time

from PIL import Image

STATES = [('idle', 6), ('running-right', 8), ('running-left', 8),
          ('waving', 4), ('jumping', 5), ('failed', 8), ('waiting', 6),
          ('running', 6), ('review', 6), ('look-row-9', 8), ('look-row-10', 8)]
CELL = (192, 208)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')


def helpers(folder=None):
    root = Path(folder) if folder else Path(os.environ.get('CODEX_HOME', Path.home()/'.codex'))/'skills/hatch-pet/scripts'
    names = ['extract_strip_frames', 'assemble_extended_atlas',
             'despill_chroma_edges', 'render_animation_previews', 'validate_atlas']
    missing = [str(root/(n+'.py')) for n in names if not (root/(n+'.py')).is_file()]
    if missing:
        raise ValueError('Missing helpers; use --helpers: ' + ', '.join(missing))
    sys.path.insert(0, str(root.resolve()))
    return root, [importlib.import_module(n) for n in names]


def init(run):
    run.mkdir(parents=True, exist_ok=True)
    config = run/'pipeline.json'
    if config.exists():
        raise ValueError('pipeline.json exists; edit it instead of overwriting')
    rows = {}
    for state, count in STATES:
        rows[state] = {'source': f'decoded/{state}.png', 'height': 170,
                       'baseline': 199, 'preserve_y': state in {'jumping', 'running-right', 'running-left'}}
    write_json(config, {'profile': 'capylulu-v2', 'chroma_key': '#00FF00',
                        'threshold': 96, 'rows': rows})
    return {'config': str(config), 'next': 'Supply decoded strips; calibrate whole-row scales, then build.'}


def extract_row(source, spec, count, key, threshold, modules):
    extract, extended = modules[:2]
    with Image.open(source) as opened:
        rgba = opened.convert('RGBA')
        # Genuine alpha is authoritative; do not erase a green accessory from it.
        keyed = rgba.getchannel('A').getextrema()[0] == 255
        strip = extract.remove_chroma_background(rgba, key, threshold) if keyed else rgba
    components = extract.connected_components(strip)
    largest = max((c['area'] for c in components), default=0)
    seeds = sorted([c for c in components if c['area'] >= max(120, largest*.20)], key=lambda c:c['center_x'])
    if len(seeds) != count:
        raise ValueError(f'{source.name}: expected {count} separate figures; inspect background/overlap, no slot fallback')
    groups = [[seed] for seed in seeds]
    seed_ids = {id(seed) for seed in seeds}
    for component in components:
        if id(component) not in seed_ids and component['area'] >= max(12,largest*.002):
            nearest = min(range(count),key=lambda n:abs(seeds[n]['center_x']-component['center_x']))
            groups[nearest].append(component)
    bounds = [extract.component_bounds(g) for g in groups]
    if any(b[0] <= 0 or b[1] <= 0 or b[2] >= strip.width or b[3] >= strip.height for b in bounds):
        raise ValueError(f'{source.name}: source figure touches canvas edge')
    cells = [extract.component_group_image(strip, g, padding=0) for g in groups]
    scale = float(spec['scale']) if 'scale' in spec else float(spec.get('height', 170))/cells[0].height
    if not math.isfinite(scale) or scale <= 0:
        raise ValueError('scale must be finite and positive')
    frames, positions = [], []
    for cell, box in zip(cells, bounds):
        anchor = extended.cell_geometry(cell).lower_center_x
        bottom = spec.get('baseline', 199)
        if spec.get('preserve_y', False):
            bottom += round((box[3]-bounds[0][3])*scale)
        w, h = round(cell.width*scale), round(cell.height*scale)
        x = round(96-anchor*scale) + spec.get('offset_x', 0)
        y = bottom-h
        if min(w, h) < 1 or x < 5 or y < 5 or x+w > 187 or y+h > 203:
            raise ValueError(f'{source.name}: fixed-scale overflow {(x,y,w,h)}; adjust whole row, never auto-fit frames')
        frame = Image.new('RGBA', CELL)
        frame.alpha_composite(cell.resize((w,h), Image.Resampling.LANCZOS), (x,y))
        frames.append(frame)
        positions.append([x,y,w,h])
    return frames, {'scale': scale, 'positions': positions, 'keyed': keyed}


def build(run, helper_dir=None):
    started = time.perf_counter()
    cfg = json.loads((run/'pipeline.json').read_text(encoding='utf-8'))
    if cfg.get('profile') != 'capylulu-v2' or set(cfg['rows']) != {n for n,_ in STATES}:
        raise ValueError('Only explicit capylulu-v2 with all 11 named rows is supported')
    root, modules = helpers(helper_dir)
    extract, _, despill, previews, validator = modules
    key = extract.parse_hex_color(cfg['chroma_key'])
    sources = {name: (run/spec['source']).resolve() for name,spec in cfg['rows'].items()}
    missing = [str(p) for p in sources.values() if not p.is_file()]
    if missing:
        raise ValueError('Missing strips: ' + ', '.join(missing))
    for state,count in STATES:
        durations = cfg['rows'][state].get('durations_ms',previews.ROW_DURATIONS.get(state,[180]*count))
        if len(durations) != count or any(not isinstance(d,int) or d <= 0 for d in durations):
            raise ValueError(f'{state}: durations_ms must contain {count} positive integers')
    dependencies = [Path(__file__)] + sorted(root.glob('*.py'))
    version = ''.join(digest(p) for p in dependencies)
    atlas = Image.new('RGBA', (1536,2288))
    reports, hits = [], 0
    cache = run/'pipeline-cache'
    cache.mkdir(exist_ok=True)
    for row, (state,count) in enumerate(STATES):
        spec = cfg['rows'][state]
        stamp = hashlib.sha256((version+digest(sources[state])+json.dumps([count,spec,key,cfg.get('threshold',96)],sort_keys=True)).encode()).hexdigest()
        cached, meta = cache/(stamp+'.png'), cache/(stamp+'.json')
        info = json.loads(meta.read_text()) if meta.exists() else None
        if cached.exists() and info and info.get('sha256') == digest(cached):
            with Image.open(cached) as im:
                strip = im.convert('RGBA')
            hits += 1
        else:
            frames, info = extract_row(sources[state],spec,count,key,cfg.get('threshold',96),modules)
            strip = Image.new('RGBA', (192*count,208))
            for col,frame in enumerate(frames):
                strip.alpha_composite(frame,(col*192,0))
            strip.save(cached)
            info['sha256'] = digest(cached)
            write_json(meta,info)
        atlas.alpha_composite(strip,(0,row*208))
        reports.append({'row': row, 'state':state, 'source_sha256':digest(sources[state]), **info})
    atlas.alpha_composite(atlas.crop((0,0,192,208)),(1152,0))
    original = atlas
    atlas, cleanup = despill.decontaminate_image(atlas,chroma_key=key)
    for info in reports:
        if not info['keyed']:
            box = (0,info['row']*208,1536,(info['row']+1)*208)
            atlas.paste(original.crop(box),box[:2])
    cleanup['alpha_input_rows_preserved'] = [r['row'] for r in reports if not r['keyed']]
    # Content-addressed candidate runs keep prior outputs available on failure.
    stamp = hashlib.sha256((version+json.dumps(cfg,sort_keys=True)+''.join(digest(p) for p in sources.values())).encode()).hexdigest()[:16]
    out = run/'pipeline-output'/stamp
    out.mkdir(parents=True,exist_ok=True)
    atlas_path = out/'atlas.webp'
    atlas.save(atlas_path,lossless=True,quality=100,method=6,exact=True)
    import subprocess
    result = subprocess.run([sys.executable,str(root/'validate_atlas.py'),str(atlas_path),
                             '--require-v2','--chroma-key',cfg['chroma_key'],
                             '--json-out',str(out/'validation.json')],capture_output=True,text=True)
    if result.returncode:
        raise ValueError(f'Structural validation failed; see {out}: {result.stdout} {result.stderr}')
    sheet = Image.new('RGB',atlas.size,'#ece6da')
    sheet.paste(atlas,mask=atlas.getchannel('A'))
    sheet.save(out/'contact-sheet.jpg',quality=94)
    cards = []
    for row,(state,count) in enumerate(STATES):
        frames = [atlas.crop((c*192,row*208,(c+1)*192,(row+1)*208)) for c in range(count)]
        durations = cfg['rows'][state].get('durations_ms',previews.ROW_DURATIONS.get(state,[180]*count))
        if len(durations) != count or any(not isinstance(d,int) or d <= 0 for d in durations):
            raise ValueError(f'{state}: durations_ms must contain {count} positive integers')
        if state == 'jumping':
            frames += list(reversed(frames[:-1]))
            durations = durations + list(reversed(durations[:-1]))
        previews.save_preview(frames,durations,out/(state+'.gif'))
        cards.append(f'<article><h2>{html.escape(state)}</h2><img width="192" height="208" src="{state}.gif" alt="{state}"></article>')
    gaze = [atlas.crop((n%8*192,(9+n//8)*208,(n%8+1)*192,(10+n//8)*208)) for n in range(16)]
    previews.save_preview(gaze,[180]*16,out/'gaze-clockwise.gif')
    from PIL import ImageDraw
    directions = Image.new('RGB',(1536,480),'#ece6da')
    draw = ImageDraw.Draw(directions)
    for n,cell in enumerate(gaze):
        directions.paste(cell,(n%8*192,n//8*240+26),cell)
        draw.text((n%8*192+8,n//8*240+5),f'{n*22.5:g} deg',fill='#302a21')
    directions.save(out/'gaze-contact.jpg',quality=94)
    (out/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>Atlas preview</title>'
        '<style>body{font:16px system-ui;background:#ece6da;margin:24px}main{display:flex;flex-wrap:wrap;gap:16px}article{background:#fff8ed;padding:12px;border-radius:12px}h2{font-size:16px}.sheet{max-width:100%}</style>'
        '<h1>CapyLulu v2 · candidate</h1><p>Technical checks are not visual approval. Lift/drop preview plays forward then reverse.</p>'
        '<a href="atlas.webp">Atlas</a> · <a href="validation.json">Validation</a><main>'+''.join(cards)+
        '</main><h2>Clockwise gaze</h2><img src="gaze-clockwise.gif" alt="16 directions"><p><img class="sheet" src="gaze-contact.jpg" alt="Ordered gaze"></p><img class="sheet" src="contact-sheet.jpg" alt="All frames">',encoding='utf-8')
    report = {'output':str(out),'cache_hits':hits,'processed_rows':len(STATES)-hits,
              'elapsed_seconds':round(time.perf_counter()-started,3),
              'structural':'pass','visual_review':'pending','rows':reports,
              'edge_cleanup':cleanup,'config':cfg,'atlas_sha256':digest(atlas_path)}
    write_json(out/'processing.json',report)
    return {k:report[k] for k in ['output','cache_hits','processed_rows','elapsed_seconds','structural','visual_review']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command',choices=['init','build'])
    parser.add_argument('--run',type=Path,required=True,help='Work directory; never a runtime asset directory')
    parser.add_argument('--helpers',type=Path,help='Installed hatch-pet/scripts directory')
    args = parser.parse_args()
    try:
        run = args.run.resolve()
        if any(p in {'assets','artifacts','.agents'} for p in run.parts):
            raise ValueError('Use a disposable work directory such as .pet-work/<run>')
        result = init(run) if args.command == 'init' else build(run,args.helpers)
        print(json.dumps(result,ensure_ascii=False))
    except (ValueError,KeyError,OSError) as exc:
        print(json.dumps({'error':str(exc)},ensure_ascii=False),file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
