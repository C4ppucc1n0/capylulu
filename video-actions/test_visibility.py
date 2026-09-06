"""Visibility hints preserve deliberate occlusion and report uncertain tracking."""
import unittest

import numpy as np

from motion import Sample
from visibility import assess_visibility, color_profile, largest_region


def frame(t, box=(30, 20, 65, 75), color=(.95, .60, .12)):
    rgb = np.empty((96, 96, 3), dtype=np.float32)
    rgb[:] = (.18, .40, .65)
    if box:
        left, top, right, bottom = box
        rgb[top:bottom, left:right] = color
    gray = rgb.mean(axis=2)
    return Sample(t, '', gray, np.zeros(64), float(gray.mean()), False, rgb=rgb)


def segment(end=3):
    return {'start': 0, 'end': end, 'warnings': []}


class VisibilityTests(unittest.TestCase):
    def test_tracks_stable_colored_subject_without_inventing_warnings(self):
        samples = [frame(i / 12) for i in range(36)]
        segments = [segment()]
        profile = assess_visibility(samples, segments)
        self.assertEqual('estimated', profile['status'])
        self.assertEqual('no_issue_detected', segments[0]['reference_quality']['status'])

    def test_exit_is_flagged_and_action_is_preserved(self):
        samples = [frame(i / 12, None if i >= 24 else (30, 20, 65, 75)) for i in range(36)]
        segments = [segment()]
        assess_visibility(samples, segments)
        self.assertEqual(1, len(segments))
        issues = segments[0]['reference_quality']['issues']
        missing = next(i for i in issues if i['code'] == 'subject_visibility_drop')
        self.assertEqual([[2, 3]], missing['ranges_seconds'])

    def test_occlusion_and_return_are_not_automatically_deleted(self):
        samples = [frame(i / 12, None if 12 <= i < 24 else (30, 20, 65, 75)) for i in range(36)]
        segments = [segment()]
        assess_visibility(samples, segments)
        self.assertEqual((0, 3), (segments[0]['start'], segments[0]['end']))
        self.assertTrue(segments[0]['warnings'])

    def test_scale_change_has_its_own_hint(self):
        samples = [frame(i / 12, (40, 30, 55, 50) if i < 18 else (20, 10, 75, 85)) for i in range(36)]
        segments = [segment()]
        assess_visibility(samples, segments, '#f2991f')
        self.assertIn('subject_scale_change', [i['code'] for i in segments[0]['reference_quality']['issues']])

    def test_framing_edge_contact_is_reported(self):
        samples = [frame(i / 12, (0, 20, 35, 75)) for i in range(36)]
        segments = [segment()]
        assess_visibility(samples, segments, '#f2991f')
        self.assertIn('framing_edge_contact', [i['code'] for i in segments[0]['reference_quality']['issues']])

    def test_neutral_subject_is_unassessed_instead_of_approved(self):
        samples = [frame(i / 12, color=(.7, .7, .7)) for i in range(36)]
        segments = [segment()]
        self.assertEqual('unavailable', assess_visibility(samples, segments)['status'])
        self.assertEqual('unassessed', segments[0]['reference_quality']['status'])

    def test_desaturated_warm_background_does_not_extend_subject_to_edge(self):
        samples = [frame(i / 12) for i in range(36)]
        for sample in samples:
            background = sample.rgb[:, :, 2] > .5
            sample.rgb[background] = (.8, .63, .50)
        segments = [segment()]
        assess_visibility(samples, segments)
        self.assertEqual('no_issue_detected', segments[0]['reference_quality']['status'])

    def test_opening_prop_does_not_set_tracking_color_for_later_shots(self):
        samples = [frame(i / 12, color=(.2, .8, .25) if i < 24 else (.95, .60, .12)) for i in range(96)]
        segments = [segment(2), {'start': 2, 'end': 8, 'warnings': []}]
        profile = assess_visibility(samples, segments)
        self.assertLess(profile['hue'], .15)
        self.assertEqual('no_issue_detected', segments[1]['reference_quality']['status'])

    def test_excluded_terminal_frames_do_not_choose_the_tracking_color(self):
        samples = [frame(i / 12, color=(.95, .60, .12) if i < 24 else (.2, .8, .25)) for i in range(96)]
        segments = [segment(2)]
        profile = assess_visibility(samples, segments)
        self.assertLess(profile['hue'], .15)
        self.assertEqual('no_issue_detected', segments[0]['reference_quality']['status'])

    def test_disabling_hints_does_not_remove_candidates(self):
        segments = [segment()]
        self.assertEqual('disabled', assess_visibility([frame(0)], segments, 'off')['status'])
        self.assertEqual(1, len(segments))

    def test_isolated_color_speck_does_not_count_as_subject(self):
        mask = np.zeros((48, 48), dtype=bool)
        mask[20, 20] = True
        self.assertEqual(0, largest_region(mask)['area'])

    def test_invalid_explicit_color_is_rejected(self):
        with self.assertRaises(ValueError):
            color_profile([frame(0)], 'orange-ish')


if __name__ == '__main__':
    unittest.main()
