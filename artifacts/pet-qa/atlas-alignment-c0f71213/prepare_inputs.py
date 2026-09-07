"""Prepare lossless input crops and evidence for this atlas alignment run."""
from pathlib import Path
import hashlib
import json
import shutil

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parents[3]
RUN = Path(__file__).resolve().parent
SOURCE = Path('C:/Users/shihaowa/AppData/Local/Temp/codex-clipboard-c0f71213-77ce-4a49-9c1d-9c95991c1153.png')
EXPECTED_SHA = '69323f1ba4ce35bc5228a62c5336d7f495abe96e1b5cbce39bf2729b92a16007'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


assert sha(SOURCE) == EXPECTED_SHA
for directory in ('input/rows', 'input/frames', 'references', 'prompts'):
    (RUN / directory).mkdir(parents=True, exist_ok=True)
shutil.copy2(SOURCE, RUN / 'input/original.png')
atlas = Image.open(SOURCE).convert('RGBA')
assert atlas.size == (1050, 1280)
xs = [0, 131, 262, 394, 525, 656, 788, 919, 1050]
ys = [0, 142, 284, 427, 569, 711, 853, 995, 1138, 1280]
counts = [6, 8, 8, 4, 5, 8, 6, 6, 6]
reassembled = Image.new('RGBA', atlas.size)
layout_rows = []
for row in range(9):
    row_box = (0, ys[row], 1050, ys[row + 1])
    strip = atlas.crop(row_box)
    strip.save(RUN / f'input/rows/row-{row + 1:02d}.png')
    reassembled.paste(strip, (0, ys[row]))
    slots = []
    for col in range(8):
        rect = [xs[col], ys[row], xs[col + 1], ys[row + 1]]
        frame = atlas.crop(rect)
        occupied = frame.getchannel('A').getbbox() is not None
        assert occupied == (col < counts[row]), (row, col)
        relative = f'input/frames/row-{row + 1:02d}-frame-{col + 1:02d}.png'
        if occupied:
            frame.save(RUN / relative)
        slots.append({'column': col + 1, 'rectangle': rect, 'occupied': occupied,
                      'file': relative if occupied else None})
    layout_rows.append({'row': row + 1, 'rectangle': list(row_box),
                        'effective_frames': counts[row], 'slots': slots})
assert np.array_equal(np.asarray(atlas), np.asarray(reassembled))
save_json(RUN / 'input/layout.json', {
    'source_sha256': EXPECTED_SHA, 'width': 1050, 'height': 1280,
    'rows': layout_rows, 'frame_count': sum(counts),
    'frame_durations': None, 'runtime_contract': None,
    'notes': '按可见透明间隔记录裁切坐标；不是已确认的运行时网格。帧时长未知。',
    'lossless_row_reassembly_verified': True,
})

records = [json.loads(line) for line in (ROOT / 'reference-library/semantics/index.jsonl').read_text(encoding='utf-8').splitlines() if line.strip()]
index = json.loads((ROOT / 'reference-library/actions/index.json').read_text(encoding='utf-8'))
groups = {video['group'] for video in index['videos']}
selection = [
    (1, 0, 'front-body.jpg', '正面全身比例与四肢'),
    (0, 3, 'side-body.jpg', '侧面口鼻厚度与肢体连接'),
    (2, 5, 'front-face.jpg', '眼睛、口鼻、耳朵与头顶果实'),
]
evidence_log = []
for record_index, frame_index, name, use in selection:
    record = records[record_index]
    entry = record['evidence'][frame_index]
    source = ROOT / entry['file']
    manifest_path = ROOT / record['manifest']
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    group_dir = manifest_path.parent.parent
    group_manifest = json.loads((group_dir / 'manifest.json').read_text(encoding='utf-8'))
    assert record['availability'] == 'current' and group_dir.name in groups
    assert group_manifest['fingerprint']['sha256'] == record['source_sha256']
    assert abs(manifest['start_seconds'] - record['start_seconds']) < 0.000001
    assert abs(manifest['end_seconds'] - record['end_seconds']) < 0.000001
    assert sha(source) == entry['sha256']
    shutil.copy2(source, RUN / 'references' / name)
    evidence_log.append({'file': f'references/{name}', 'source': entry['file'],
                         'sha256': entry['sha256'], 'clip_id': record['clip_id'],
                         'source_time_seconds': entry['source_time_seconds'], 'use': use,
                         'identity_basis': 'visual comparison; character_id is null',
                         'semantic_review_status': record['annotation']['review_status']})
save_json(RUN / 'references/selection.json', evidence_log)

font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 23)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 17)
board = Image.new('RGB', (1280, 660), '#edf0f2')
draw = ImageDraw.Draw(board)
draw.text((24, 15), '校准依据 · 输入图与素材库参考（尚未重绘）', font=font, fill='#17212d')
panels = [
    ('原图代表帧｜动作与服装道具', atlas.crop((0, 0, 131, 142))),
    ('正面全身｜比例与四肢', Image.open(RUN / 'references/front-body.jpg')),
    ('侧面｜口鼻轮廓与连接', Image.open(RUN / 'references/side-body.jpg')),
    ('头部｜眼睛、耳朵与果实', Image.open(RUN / 'references/front-face.jpg')),
]
for i, (label, picture) in enumerate(panels):
    tile = Image.new('RGBA', (300, 440), 'white')
    fitted = ImageOps.contain(picture.convert('RGBA'), (296, 430), Image.Resampling.LANCZOS)
    tile.alpha_composite(fitted, ((300 - fitted.width) // 2, (440 - fitted.height) // 2))
    x = 20 + i * 315
    board.paste(tile.convert('RGB'), (x, 92))
    draw.text((x, 62), label, font=small, fill='#17212d')
draw.text((24, 552), '纠正：较大的椭圆眼与浅色眼缘、宽圆口鼻、短粗四肢、柔和低高光材质。', font=font, fill='#17212d')
draw.text((24, 592), '保留：原动作与表情阶段、57 帧及空槽、爪印短裤、持冰淇淋的左右关系。', font=font, fill='#17212d')
board.save(RUN / 'reference-review.jpg', quality=93)
print(json.dumps({'run': str(RUN), 'frames': sum(counts), 'row_counts': counts,
                  'references': len(selection), 'lossless_reassembly': True}, ensure_ascii=False))
