"""Deterministic shot / pause segmentation; these are candidates, not action labels."""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np
from PIL import Image


@dataclass
class Sample:
    time: float
    path: str
    gray: np.ndarray
    histogram: np.ndarray
    brightness: float
    blank: bool
    motion: float = 0.0
    cut: bool = False
    rgb: np.ndarray | None = None
    cut_reason: str | None = None


def describe(path, time):
    with Image.open(path) as image:
        rgb = np.asarray(image.convert('RGB').resize((96, 96)), dtype=np.float32) / 255
    gray = rgb.mean(axis=2)
    # Color distribution catches scene changes; the central crop limits edge watermarks.
    colors = (rgb * 3.999).astype(np.int32)
    bins = colors[:, :, 0] * 16 + colors[:, :, 1] * 4 + colors[:, :, 2]
    histogram = np.bincount(bins.ravel(), minlength=64) / bins.size
    return Sample(time, str(path), gray, histogram, float(gray.mean()),
                  bool((gray < .07).mean() > .97 or (gray > .96).mean() > .99), rgb=rgb)


def difference(a, b):
    return float(np.abs(a.gray - b.gray).mean())


def measure(samples, scene_threshold=.24):
    differences = [0.0] + [difference(a, b) for a, b in zip(samples, samples[1:])]
    border = np.ones((96, 96), dtype=bool)
    border[16:80, 18:78] = False
    layouts = [0.0]
    for a, b in zip(samples, samples[1:]):
        delta = b.gray - a.gray
        # Remove uniform lighting changes and ignore the central performer.
        layouts.append(float(np.abs(delta - np.median(delta))[border].mean()))
    for i, (previous, current) in enumerate(zip(samples, samples[1:]), 1):
        delta = np.abs(current.gray - previous.gray)
        current.motion = float(delta[10:86, 10:86].mean())
        color_change = float(np.abs(current.histogram - previous.histogram).sum() / 2)
        neighbors = differences[max(1, i - 4):i] + differences[i + 1:i + 5]
        local_motion = max(.003, float(np.median(neighbors))) if neighbors else .003
        # Same-character edits can keep most colors. An isolated change relative
        # to neighboring motion is stronger evidence than one global threshold.
        color_cut = (differences[i] >= scene_threshold or color_change > .75
                     or (color_change > .20 and differences[i] > scene_threshold * .30
                         and differences[i] / local_motion > 3.5))
        surrounding_layouts = layouts[max(1, i - 4):i] + layouts[i + 1:i + 5]
        local_layout = max(.006, float(np.median(surrounding_layouts))) if surrounding_layouts else .006
        layout_cut = (differences[i] > scene_threshold * .25 and layouts[i] > scene_threshold * .25
                      and layouts[i] / local_layout > 4.5)
        current.cut = color_cut or layout_cut
        current.cut_reason = 'background_layout_jump' if layout_cut else 'scene_change' if color_cut else None


def same_scene_during_fade(frame, reference):
    """Compare structure after fitting a global brightness gain and offset."""
    x = reference.gray - reference.gray.mean()
    y = frame.gray - frame.gray.mean()
    energy_x, energy_y = float((x * x).sum()), float((y * y).sum())
    if min(energy_x, energy_y) < .001:
        return False
    product = float((x * y).sum())
    gain = product / energy_x
    correlation = product / np.sqrt(energy_x * energy_y)
    residual = float(np.abs(y - gain * x).mean())
    return .025 <= gain <= 1.3 and correlation > .97 and residual < .02


def end_card_start(samples, duration):
    """Conservative heuristic for a stable dark terminal card, including its fade."""
    usable = [sample for sample in samples if not sample.blank]
    if len(usable) < 3:
        return None
    end = usable[-1]
    first = len(usable) - 1
    while first > 0 and difference(usable[first - 1], end) < .05:
        first -= 1
    if (first == 0 or end.brightness >= 115 / 255
            or end.time - usable[first].time < .4):
        return None
    while first > 0 and usable[first - 1].time > duration * .45:
        previous = usable[first - 1]
        fade = (previous.brightness < 30 / 255 or same_scene_during_fade(previous, end)
                or (previous.brightness < end.brightness + 25 / 255 and difference(previous, end) < .12))
        if not fade:
            break
        first -= 1
    if first > 0 and usable[first].time > duration * .45:
        previous = usable[first - 1]
        if previous.brightness - end.brightness > 35 / 255 and difference(previous, end) > .16:
            cutoff = usable[first].time
            # Include intervening almost-black frames in the excluded fade.
            between = [s.time for s in samples if previous.time < s.time < cutoff and s.blank]
            return min(between, default=cutoff)
    return None


def split_shot(samples, start, end, minimum, maximum):
    """Split at sustained pauses first; long continuous takes get flagged windows."""
    if not samples:
        return []
    values = np.array([s.motion for s in samples])
    smooth = np.convolve(np.pad(values, (1, 1), mode='edge'), np.ones(3) / 3, mode='valid')
    threshold = max(.0015, float(np.quantile(smooth, .65)) * .30)
    pauses = []
    run_start = None
    for i, value in enumerate(smooth):
        if value <= threshold and run_start is None:
            run_start = i
        if run_start is not None and (value > threshold or i == len(samples) - 1):
            last = i - 1 if value > threshold else i
            if samples[last].time - samples[run_start].time >= .20:
                pauses.append((samples[run_start].time + samples[last].time) / 2)
            run_start = None
    result = []
    cursor, start_reason = start, 'shot_start'
    while end - cursor >= minimum - 1e-8:
        valid = [p for p in pauses if cursor + minimum <= p <= min(end - minimum, cursor + maximum)]
        if valid:
            boundary, reason = valid[0], 'motion_pause'
        elif end - cursor > maximum:
            # Keep enough material for the final window; never cross a shot boundary.
            target = cursor + min(maximum, (end - cursor) / 2) if end - cursor < 2 * maximum else cursor + maximum
            choices = [(abs(s.time - target) + float(m) * 3, s.time)
                       for s, m in zip(samples, smooth)
                       if cursor + minimum <= s.time <= min(cursor + maximum, end - minimum)
                       and abs(s.time - target) <= .65]
            boundary = min(choices)[1] if choices else target
            reason = 'duration_limit'
        else:
            boundary, reason = end, 'shot_end'
        result.append({'start': cursor, 'end': boundary,
                       'start_reason': start_reason, 'end_reason': reason})
        cursor, start_reason = boundary, reason
        if reason == 'shot_end':
            break
    if result and 0 < end - cursor <= minimum:
        result[-1]['end'] = end
        result[-1]['end_reason'] = 'shot_end'
    return result


def find_segments(samples, duration, *, minimum=.8, maximum=5.0,
                  min_motion=.0025, scene_threshold=.24, keep_end_card=False, summary_frames=8):
    measure(samples, scene_threshold)
    cutoff = None if keep_end_card else end_card_start(samples, duration)
    usable_end = cutoff if cutoff is not None else duration
    excluded = []
    if cutoff is not None:
        excluded.append({'start': cutoff, 'end': duration, 'reason': 'likely_terminal_card_and_fade'})
    shots, current = [], []
    for sample in samples:
        if sample.time >= usable_end:
            break
        if sample.blank or sample.cut:
            if current:
                shots.append((current, current[0].time, sample.time))
                current = []
        if not sample.blank:
            current.append(sample)
    if current:
        shots.append((current, current[0].time, usable_end))
    segments = []
    for shot_id, (shot, start, end) in enumerate(shots, 1):
        if end - start < minimum:
            excluded.append({'start': start, 'end': end, 'reason': 'shot_too_short'})
            continue
        for segment in split_shot(shot, start, end, minimum, maximum):
            selected = [s for s in shot if segment['start'] <= s.time < segment['end']]
            # Exclude incoming cut energy from motion scoring of the new shot.
            motion = float(np.mean([s.motion for s in selected[1:]])) if len(selected) > 1 else 0
            reason = 'too_few_source_samples' if len(selected) < summary_frames else 'nearly_static' if motion < min_motion else None
            if reason:
                excluded.append({**segment, 'reason': reason})
                continue
            warnings = []
            if 'duration_limit' in (segment['start_reason'], segment['end_reason']):
                warnings.append('Continuous motion split by duration; verify the action boundaries.')
            segments.append({**segment, 'shot': shot_id, 'motion_score': round(motion, 6),
                             'warnings': warnings})
    return segments, excluded


def summary_indices(count, requested=8):
    if count < requested:
        raise ValueError(f'Only {count} sampled source frames; need {requested}. Widen the range or increase --sample-fps.')
    return [round(i * (count - 1) / (requested - 1)) for i in range(requested)]
