"""End-to-end checks with small generated videos, no project reference library required."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import imageio_ffmpeg
from PIL import Image

ROOT = Path(__file__).resolve().parent


class CliTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        cls.work = Path(cls.temporary.name)
        cls.source = cls.work / 'two rates.mp4'
        # Vary the source timing: 12 fps in the first second, 6 fps afterwards.
        subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'error',
                        '-f', 'lavfi', '-i', 'testsrc2=duration=2:size=160x120:rate=12',
                        '-vf', r'setpts=if(lt(N\,12)\,N/(12*TB)\,(1+(N-12)/6)/TB)',
                        '-fps_mode', 'vfr', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', str(cls.source)], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def invoke(self, *arguments):
        return subprocess.run([sys.executable, str(ROOT / 'extract.py'), *map(str, arguments)],
                              capture_output=True, text=True, encoding='utf-8')

    def test_manual_ranges_vfr_timestamps_previews_and_cache(self):
        output = self.work / 'manual'
        arguments = [self.source, '--range', '0.25:2.5', '--out-dir', output]
        result = self.invoke(*arguments)
        self.assertEqual(0, result.returncode, result.stderr + result.stdout)
        index = json.loads((output / 'index.json').read_text())
        group = output / index['videos'][0]['group']
        manifest_path = group / 'action-01' / 'manifest.json'
        manifest = json.loads(manifest_path.read_text())
        times = [frame['source_time_seconds'] for frame in manifest['frames']]
        self.assertEqual(sorted(set(times)), times)
        self.assertTrue(all(.25 <= t < 2.5 for t in times))
        self.assertTrue(any(b - a > .15 for a, b in zip(times, times[1:])), 'VFR gaps should not be filled with copies')
        self.assertEqual(8, len(manifest['summary_frames']))
        for frame in manifest['frames'] + manifest['summary_frames']:
            with Image.open(manifest_path.parent / frame['file']) as image:
                image.verify()
        reader = imageio_ffmpeg.read_frames(str(manifest_path.parent / 'preview.mp4'))
        try:
            metadata = next(reader)
            decoded = sum(1 for _ in reader)
        finally:
            reader.close()
        self.assertGreater(decoded, 0)
        self.assertAlmostEqual(2.25, metadata['duration'], delta=.18)
        before = manifest_path.stat().st_mtime_ns
        rerun = self.invoke(*arguments)
        self.assertEqual(0, rerun.returncode, rerun.stderr + rerun.stdout)
        self.assertEqual('skipped', json.loads(rerun.stdout.splitlines()[0])['status'])
        self.assertEqual(before, manifest_path.stat().st_mtime_ns)

    def test_bad_video_does_not_stop_other_inputs(self):
        broken = self.work / 'broken.mp4'
        broken.write_bytes(b'not a video')
        output = self.work / 'mixed'
        result = self.invoke(broken, self.source, '--min-motion', '0', '--out-dir', output)
        self.assertEqual(1, result.returncode)
        videos = json.loads((output / 'index.json').read_text())['videos']
        self.assertEqual({'error', 'created'}, {v['status'] for v in videos})

    def test_out_of_bounds_range_fails_without_publishing_group(self):
        output = self.work / 'invalid-range'
        result = self.invoke(self.source, '--range', '2:99', '--out-dir', output)
        self.assertEqual(1, result.returncode)
        self.assertFalse(list(output.glob('*/manifest.json')))


if __name__ == '__main__':
    unittest.main()
