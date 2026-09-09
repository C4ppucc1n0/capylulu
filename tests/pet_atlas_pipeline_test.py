"""Regression checks for deterministic row processing; no generation calls."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('pipeline', ROOT/'.agents/skills/pet-action-atlas/scripts/atlas_pipeline.py')
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)


class PipelineTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        _, cls.modules = p.helpers()

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def strip(self, boxes, background=(0,255,0,255), color=(240,140,40,255)):
        im = Image.new('RGBA',(200,120),background)
        draw = ImageDraw.Draw(im)
        for box in boxes:
            draw.rectangle(box,fill=color)
        path=self.root/'row.png'
        im.save(path)
        return path

    def extract(self,path,count=2,**kwargs):
        return p.extract_row(path,{'scale':1,'baseline':180,**kwargs},count,(0,255,0),96,self.modules)

    def test_fixed_scale_and_vertical_motion(self):
        path=self.strip([(20,30,59,89),(120,20,159,79)])
        frames,info=self.extract(path,preserve_y=True)
        self.assertEqual(info['positions'][0][2:],info['positions'][1][2:])
        self.assertEqual(info['positions'][1][1]-info['positions'][0][1],-10)
        self.assertEqual(frames[0].size,(192,208))

    def test_extra_figure_rejected(self):
        with self.assertRaisesRegex(ValueError,'expected 2'):
            self.extract(self.strip([(10,20,39,89),(75,20,104,89),(140,20,169,89)]))

    def test_missing_figure_rejected(self):
        with self.assertRaisesRegex(ValueError,'expected 2'):
            self.extract(self.strip([(10,20,39,89)]))

    def test_source_clipping_rejected(self):
        with self.assertRaisesRegex(ValueError,'touches canvas'):
            self.extract(self.strip([(0,20,39,89),(120,20,159,89)]))

    def test_overflow_rejected(self):
        with self.assertRaisesRegex(ValueError,'overflow'):
            self.extract(self.strip([(20,30,59,89),(120,20,159,79)]),scale=4)

    def test_alpha_preserves_green_character(self):
        path=self.strip([(20,30,59,89),(120,20,159,79)],(0,0,0,0),(0,255,0,255))
        frames,info=self.extract(path)
        self.assertFalse(info['keyed'])
        self.assertEqual(frames[0].getpixel((96,160)),(0,255,0,255))

    def test_init_preserves_config(self):
        run = self.root / 'artifacts/work/init'
        p.init(run)
        content=(run/'pipeline.json').read_bytes()
        with self.assertRaises(ValueError):
            p.init(run)
        self.assertEqual(content,(run/'pipeline.json').read_bytes())
        self.assertEqual(len(json.loads(content)['rows']),11)


class PipelineCliTests(unittest.TestCase):
    def invoke(self, command, run):
        return subprocess.run(
            [sys.executable, '-B', str(Path(p.__file__)), command, '--run', str(run)],
            capture_output=True, text=True)

    def test_init_and_build_accept_artifacts_work_run(self):
        with tempfile.TemporaryDirectory() as temporary:
            run = Path(temporary) / 'artifacts/work/example'
            result = self.invoke('init', run)
            self.assertEqual(result.returncode, 0, result.stderr)
            config = json.loads((run / 'pipeline.json').read_text(encoding='utf-8'))
            self.assertEqual(config['profile'], 'capylulu-v2')
            # An empty run reaches input validation, rather than the directory guard.
            result = self.invoke('build', run)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('Missing strips', json.loads(result.stderr)['error'])
            events = [json.loads(line) for line in (run/'events.jsonl').read_text().splitlines()]
            self.assertEqual(events[-1]['stage'], 'build')
            self.assertEqual(events[-1]['status'], 'failed')

    def test_cli_keeps_shared_roots_and_curated_assets_protected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for relative in ('artifacts', 'artifacts/work', 'artifacts/pet-qa/example',
                             'artifacts/work/../pet-qa/example', 'artifacts/tests/example',
                             'artifacts/pet-qa/nested/artifacts/work/example',
                             'assets/pet-atlases/example', '.agents/skills/example'):
                with self.subTest(run=relative):
                    run = root / relative
                    result = self.invoke('init', run)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn('artifacts/work/<run>', json.loads(result.stderr)['error'])
                    self.assertFalse(run.exists())


class PipelineBuildTests(unittest.TestCase):
    def test_build_snapshots_inputs_and_records_real_cache_hits(self):
        with tempfile.TemporaryDirectory() as temporary:
            run = Path(temporary) / 'artifacts/work/build'
            p.init(run)
            for state, count in p.STATES:
                strip = Image.new('RGBA', (count*80, 130))
                draw = ImageDraw.Draw(strip)
                for frame in range(count):
                    draw.rectangle((frame*80+24, 20, frame*80+54, 110), fill=(240,140,40,255))
                strip.save(run/'decoded'/(state+'.png'))
            first = p.build(run)
            self.assertEqual(first['cache_hits'], 0)
            report = json.loads((Path(first['output'])/'processing.json').read_text())
            self.assertEqual(report['visual_review'], 'pending')
            for item in report['inputs'].values():
                self.assertTrue((run/item['path']).is_file())
                self.assertEqual(p.digest(run/item['path']), item['sha256'])
            second = p.build(run)
            self.assertEqual(second['cache_hits'], 11)
            self.assertEqual(first['output'], second['output'])
            events = [json.loads(line) for line in (run/'events.jsonl').read_text().splitlines()]
            rows = [event for event in events if event['stage'] == 'process_row']
            self.assertEqual([event['cache_hit'] for event in rows], [False]*11 + [True]*11)
            source = run/'decoded/idle.png'
            changed = Image.open(source).convert('RGBA')
            changed.putpixel((30,30),(230,100,40,255))
            changed.save(source)
            third = p.build(run)
            self.assertEqual(third['cache_hits'], 10)
            self.assertNotEqual(third['output'], first['output'])
            self.assertTrue((Path(first['output'])/'atlas.webp').is_file())


if __name__=='__main__':
    unittest.main()
