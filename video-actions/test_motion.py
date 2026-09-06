"""Behavior checks for cut boundaries, ordered sampling, pauses, and end cards."""
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from motion import Sample, describe, end_card_start, find_segments, measure, same_scene_during_fade, split_shot, summary_indices


def sample(t, base=.6, movement=.02, hist_bin=20):
    gray = np.full((96, 96), base, dtype=np.float32)
    x = int(t * 30) % 60
    gray[20:75, x:x + 20] -= .25
    histogram = np.zeros(64)
    histogram[hist_bin] = 1
    return Sample(t, '', gray, histogram, float(gray.mean()), False, movement)


class MotionTests(unittest.TestCase):
    def test_summary_keeps_first_last_and_unique_order(self):
        for count in (8, 11, 37, 120):
            result = summary_indices(count)
            self.assertEqual(8, len(set(result)))
            self.assertEqual(sorted(result), result)
            self.assertEqual((0, count - 1), (result[0], result[-1]))

    def test_summary_does_not_pad_short_action_with_duplicate_frames(self):
        with self.assertRaises(ValueError):
            summary_indices(7)

    def test_exact_minimum_duration_is_not_dropped(self):
        frames = [sample(i / 12, movement=.04) for i in range(12)]
        segments = split_shot(frames, 0, 1, 1, 5)
        self.assertEqual(1, len(segments))
        self.assertEqual(1, segments[0]['end'])

    def test_shot_change_never_appears_inside_candidate(self):
        frames = [sample(i / 12, .7 if i < 24 else .3, hist_bin=20 if i < 24 else 5) for i in range(48)]
        segments, _ = find_segments(frames, 4, keep_end_card=True)
        self.assertGreaterEqual(len(segments), 2)
        self.assertTrue(any(s.cut and s.time == 2 for s in frames))
        self.assertTrue(all(not (s['start'] < 2 < s['end']) for s in segments))

    def test_sustained_pause_splits_two_performances_in_one_shot(self):
        frames = [sample(i / 12) for i in range(60)]
        for frame in frames:
            frame.motion = .001 if 2 <= frame.time <= 2.6 else .04
        segments = split_shot(frames, 0, 5, .8, 5)
        self.assertEqual(2, len(segments))
        self.assertEqual('motion_pause', segments[0]['end_reason'])
        self.assertAlmostEqual(segments[0]['end'], segments[1]['start'])

    def test_long_continuous_action_is_bounded_and_flagged(self):
        frames = [sample(i / 12, movement=.04) for i in range(156)]
        segments = split_shot(frames, 0, 13, .8, 5)
        self.assertTrue(all(.8 <= s['end'] - s['start'] <= 5 for s in segments))
        self.assertEqual(0, segments[0]['start'])
        self.assertEqual(13, segments[-1]['end'])
        self.assertTrue(any(s['end_reason'] == 'duration_limit' for s in segments))

    def test_static_video_produces_no_invented_action(self):
        frame = sample(0)
        frames = [Sample(i / 12, '', frame.gray, frame.histogram, frame.brightness, False) for i in range(36)]
        segments, excluded = find_segments(frames, 3)
        self.assertEqual([], segments)
        self.assertTrue(any(s['reason'] == 'nearly_static' for s in excluded))

    def test_bright_terminal_character_is_retained(self):
        frames = [sample(i / 12) for i in range(48)]
        self.assertIsNone(end_card_start(frames, 4))

    def test_dark_stable_card_and_black_fade_are_excluded(self):
        frames = [sample(i / 12, base=.8) for i in range(48)]
        card = sample(0, base=.15, hist_bin=1)
        for i in range(48, 84):
            fade = i < 51
            frames.append(Sample(i / 12, '', np.zeros((96, 96)) if fade else card.gray,
                                 card.histogram, .0 if fade else card.brightness, fade))
        cutoff = end_card_start(frames, 7)
        self.assertEqual(4, cutoff)
        segments, excluded = find_segments(frames, 7)
        self.assertTrue(all(s['end'] <= cutoff for s in segments))
        self.assertTrue(any(s['reason'] == 'likely_terminal_card_and_fade' for s in excluded))

    def test_continuous_motion_does_not_create_a_cut_per_frame(self):
        frames = [sample(i / 12) for i in range(20)]
        measure(frames)
        self.assertFalse(any(s.cut for s in frames))

    def test_gradual_card_fade_is_removed_back_to_black_transition(self):
        frames = [sample(i / 12, base=.8) for i in range(48)]
        card = sample(0, base=.30)
        for i in range(48, 96):
            gain = min(1, max(0, (i - 51) / 15))
            gray = card.gray * gain
            frames.append(Sample(i / 12, '', gray, card.histogram, float(gray.mean()), gain < .04))
        self.assertEqual(4, end_card_start(frames, 8))
        segments, _ = find_segments(frames, 8)
        self.assertTrue(all(s['end'] <= 4 for s in segments))

    def test_fade_compensation_does_not_match_a_different_composition(self):
        card = sample(0, base=.35)
        other = sample(1, base=.2)
        self.assertFalse(same_scene_during_fade(other, card))
        faded = Sample(1, '', card.gray * .4, card.histogram, card.brightness * .4, False)
        self.assertTrue(same_scene_during_fade(faded, card))

    def test_similar_palette_mirrored_background_creates_a_boundary(self):
        original = sample(0)
        original.gray[:, :12] = .28
        original.gray[:, 84:] = .76
        frames = [Sample(i / 12, '', original.gray if i < 24 else original.gray[:, ::-1],
                         original.histogram, original.brightness, False) for i in range(48)]
        measure(frames)
        self.assertEqual([2], [s.time for s in frames if s.cut])
        self.assertEqual('background_layout_jump', frames[24].cut_reason)

    def test_fast_central_gesture_with_fixed_background_is_not_a_cut(self):
        original = sample(0)
        frames = []
        for i in range(36):
            gray = original.gray.copy()
            gray[25:70, 25:70] = .2 if i == 18 else .7
            frames.append(Sample(i / 12, '', gray, original.histogram, float(gray.mean()), False))
        measure(frames)
        self.assertFalse(any(s.cut for s in frames))

    def test_same_brightness_different_color_scene_is_detected(self):
        frames = [sample(i / 12, hist_bin=2 if i < 12 else 30) for i in range(24)]
        measure(frames)
        self.assertTrue(frames[12].cut)

    def test_extracted_image_description_handles_blank_frames(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'image.jpg'
            Image.new('RGB', (120, 80), 'black').save(path)
            self.assertTrue(describe(path, 0).blank)
            image = Image.new('RGB', (120, 80), '#eddfaa')
            ImageDraw.Draw(image).ellipse((20, 10, 90, 75), fill='#9f5815')
            image.save(path)
            self.assertFalse(describe(path, 0).blank)


if __name__ == '__main__':
    unittest.main()
