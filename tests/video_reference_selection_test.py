"""Regression checks for platform end-card filtering (Python + Pillow)."""
import importlib.util
import unittest
from pathlib import Path

from PIL import Image, ImageDraw, ImageStat

SCRIPT = Path(__file__).resolve().parents[1] / '.agents/skills/video-character-reference/scripts/video_references.py'
spec = importlib.util.spec_from_file_location('video_references', SCRIPT)
references = importlib.util.module_from_spec(spec)
spec.loader.exec_module(references)


def frame(time, image, sharpness=10):
    gray = image.convert('L').resize((32, 32))
    return {'time': time, 'signature': gray, 'brightness': ImageStat.Stat(gray).mean[0],
            'sharpness': sharpness, 'blank': False}


def subject(time, background=180):
    image = Image.new('L', (32, 32), background)
    ImageDraw.Draw(image).ellipse((time * 2, 5, time * 2 + 10, 26), fill=80)
    return frame(time, image)


class EndCardTests(unittest.TestCase):
    def test_crisp_text_does_not_prevent_end_card_removal(self):
        card = Image.new('L', (32, 32), 20)
        draw = ImageDraw.Draw(card)
        for y in range(8, 24, 3):
            draw.line((5, y, 27, y), fill=255)
        frames = [subject(t) for t in range(6)] + [frame(t, card, sharpness=40) for t in range(6, 10)]
        selected, notes = references.choose(frames, 6)
        self.assertTrue(selected)
        self.assertTrue(all(f['time'] < 6 for f in selected))
        self.assertTrue(any('end card' in note for note in notes))

    def test_brown_end_card_fade_is_also_removed(self):
        card = Image.new('L', (32, 32), 75)
        draw = ImageDraw.Draw(card)
        draw.rectangle((10, 10, 22, 13), fill=200)
        frames = [subject(t) for t in range(6)]
        frames += [frame(6, Image.new('L', (32, 32), 19))]
        frames += [frame(t, card) for t in range(7, 10)]
        selected, _ = references.choose(frames, 6)
        self.assertTrue(all(f['time'] < 6 for f in selected))

    def test_bright_still_character_at_end_is_retained(self):
        frames = [subject(t) for t in range(6)]
        image = Image.new('L', (32, 32), 190)
        ImageDraw.Draw(image).rectangle((3, 3, 29, 29), fill=140)
        frames += [frame(t, image) for t in range(6, 10)]
        selected, notes = references.choose(frames, 6)
        self.assertTrue(any(f['time'] >= 6 for f in selected))
        self.assertFalse(any('end card' in note for note in notes))


if __name__ == '__main__':
    unittest.main()
