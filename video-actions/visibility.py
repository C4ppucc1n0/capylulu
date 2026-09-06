"""Cheap, advisory tracking of a distinctive foreground color; no object model."""
from __future__ import annotations

import re

import numpy as np
from PIL import Image


def hsv_image(rgb):
    image = Image.fromarray(np.uint8(np.clip(rgb * 255, 0, 255))).resize((48, 48))
    return np.asarray(image.convert('HSV'), dtype=np.float32) / 255


def color_profile(samples, color='auto'):
    if color == 'off':
        return {'status': 'disabled'}
    if color != 'auto':
        if not re.fullmatch(r'#[0-9a-fA-F]{6}', color):
            raise ValueError('--subject-color must be auto, off or #RRGGBB.')
        rgb = tuple(int(color[i:i + 2], 16) for i in (1, 3, 5))
        hue, saturation, value = np.asarray(Image.new('RGB', (1, 1), rgb).convert('HSV'))[0, 0] / 255
        if saturation < .25 or value < .18:
            raise ValueError('Color tracking needs a saturated, visible color; use auto or off for neutral subjects.')
        return {'status': 'estimated', 'source': 'explicit', 'hue': float(hue), 'hex': color.upper(),
                'saturation_floor': max(.28, float(saturation) * .65)}
    candidates = [s for s in samples if not s.blank and s.rgb is not None]
    if not candidates:
        return {'status': 'unavailable', 'reason': 'No color samples.'}
    # Spread evidence across retained content: an opening prop should not decide
    # the tracked color for every subsequent shot. Keep the cost bounded.
    candidates = [candidates[i] for i in np.linspace(0, len(candidates) - 1, min(24, len(candidates)), dtype=int)]
    center = np.zeros((48, 48), dtype=bool)
    center[8:42, 12:36] = True
    border = np.ones((48, 48), dtype=bool)
    border[6:42, 6:42] = False
    scores, centers = [], []
    for sample in candidates:
        hsv = hsv_image(sample.rgb)
        valid = (hsv[:, :, 1] >= .35) & (hsv[:, :, 2] >= .18)
        bins = np.minimum(23, (hsv[:, :, 0] * 24).astype(int))
        inner = np.bincount(bins[valid & center], minlength=24) / center.sum()
        outer = np.bincount(bins[valid & border], minlength=24) / border.sum()
        # Pool adjacent hues so lighting changes do not create a new color.
        inner = inner + np.roll(inner, 1) + np.roll(inner, -1)
        outer = outer + np.roll(outer, 1) + np.roll(outer, -1)
        scores.append(inner - outer)
        centers.append(inner)
    score = np.median(scores, axis=0)
    selected = int(np.argmax(score))
    if score[selected] < .06 or np.median(centers, axis=0)[selected] < .10:
        return {'status': 'unavailable', 'reason': 'No distinctive saturated central color; set --subject-color if known.'}
    hue = (selected + .5) / 24
    saturation = []
    for sample in candidates:
        hsv = hsv_image(sample.rgb)
        delta = np.abs(hsv[:, :, 0] - hue)
        matching = center & (np.minimum(delta, 1 - delta) <= .055) & (hsv[:, :, 1] >= .35)
        saturation.extend(hsv[:, :, 1][matching].tolist())
    return {'status': 'estimated', 'source': 'central_color_contrast', 'hue': hue,
            'saturation_floor': max(.28, float(np.median(saturation)) * .65) if saturation else .35,
            'contrast': round(float(score[selected]), 4)}


def largest_region(mask):
    """Four-connected regions at 48x48; small isolated specks are ignored."""
    unseen = mask.copy()
    best = []
    height, width = mask.shape
    for y, x in zip(*np.nonzero(mask)):
        if not unseen[y, x]:
            continue
        unseen[y, x] = False
        stack, component = [(int(y), int(x))], []
        while stack:
            row, column = stack.pop()
            component.append((row, column))
            for nr, nc in ((row - 1, column), (row + 1, column), (row, column - 1), (row, column + 1)):
                if 0 <= nr < height and 0 <= nc < width and unseen[nr, nc]:
                    unseen[nr, nc] = False
                    stack.append((nr, nc))
        if len(component) > len(best):
            best = component
    if len(best) < 5:
        return {'area': 0.0, 'bbox': None, 'edge_contact': False}
    ys, xs = zip(*best)
    bbox = [min(xs) / width, min(ys) / height, (max(xs) + 1) / width, (max(ys) + 1) / height]
    edge = min(xs) <= 1 or min(ys) <= 1 or max(xs) >= width - 2 or max(ys) >= height - 2
    return {'area': round(len(best) / mask.size, 6), 'bbox': [round(v, 4) for v in bbox], 'edge_contact': bool(edge)}


def track_region(sample, profile):
    if sample.rgb is None:
        return None
    hsv = hsv_image(sample.rgb)
    distance = np.abs(hsv[:, :, 0] - profile['hue'])
    distance = np.minimum(distance, 1 - distance)
    mask = (distance <= .055) & (hsv[:, :, 1] >= profile['saturation_floor']) & (hsv[:, :, 2] >= .18)
    return largest_region(mask)


def time_ranges(samples, flags, end):
    ranges, start = [], None
    for i, (sample, flag) in enumerate(zip(samples, flags)):
        if flag and start is None:
            start = sample.time
        if start is not None and (not flag or i == len(samples) - 1):
            stop = end if flag else sample.time
            if stop - start >= .25:
                ranges.append([round(start, 6), round(stop, 6)])
            start = None
    return ranges


def assess_visibility(samples, segments, color='auto'):
    retained = [s for s in samples if any(segment['start'] <= s.time < segment['end'] for segment in segments)]
    profile = color_profile(retained, color)
    if profile['status'] != 'estimated':
        for segment in segments:
            segment['reference_quality'] = {'status': 'unassessed' if profile['status'] == 'unavailable' else 'disabled',
                                            'issues': [], 'method': 'foreground_color_region'}
        return profile
    regions = {s.time: track_region(s, profile) for s in samples}
    for segment in segments:
        selected = [s for s in samples if segment['start'] <= s.time < segment['end'] and regions[s.time] is not None]
        if not selected:
            segment['reference_quality'] = {'status': 'unassessed', 'issues': [], 'method': 'foreground_color_region'}
            continue
        areas = np.array([regions[s.time]['area'] for s in selected])
        reference_area = float(np.quantile(areas, .9))
        low = areas < max(.004, reference_area * .12)
        missing_ranges = time_ranges(selected, low, segment['end'])
        edge_ranges = time_ranges(selected, [regions[s.time]['edge_contact'] for s in selected], segment['end'])
        issues = []
        if missing_ranges:
            issues.append({'code': 'subject_visibility_drop', 'ranges_seconds': missing_ranges,
                           'message': '参考色块持续减少或消失，可能离场、被遮挡或跟踪失败；请检查这些区间。'})
        if edge_ranges:
            issues.append({'code': 'framing_edge_contact', 'ranges_seconds': edge_ranges,
                           'message': '参考色块持续接触画面边缘，可能有出框或近景裁切；请检查构图。'})
        # Small but still detected regions matter for walking away / zooming out.
        visible = areas[areas >= .004]
        scale_ratio = None
        if len(visible) >= 8:
            scale_ratio = float(np.sqrt(np.quantile(visible, .9) / max(.004, np.quantile(visible, .1))))
            if scale_ratio >= 1.6:
                issues.append({'code': 'subject_scale_change', 'message': '参考色块面积明显变化，可能靠近、远离、遮挡或镜头缩放；生成时需固定角色尺度。'})
        segment['reference_quality'] = {'status': 'review_suggested' if issues else 'no_issue_detected',
            'method': 'foreground_color_region', 'issues': issues,
            'low_visibility_fraction': round(float(low.mean()), 4),
            'apparent_scale_ratio': round(scale_ratio, 3) if scale_ratio is not None else None,
            'samples': [{'time_seconds': round(s.time, 6), **regions[s.time]} for s in selected]}
        segment['warnings'].extend(issue['message'] for issue in issues)
    return profile
