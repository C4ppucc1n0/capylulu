"""Regression checks for deterministic row processing; no generation calls."""
import importlib.util
import json
from pathlib import Path
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
        p.init(self.root)
        content=(self.root/'pipeline.json').read_bytes()
        with self.assertRaises(ValueError):
            p.init(self.root)
        self.assertEqual(content,(self.root/'pipeline.json').read_bytes())
        self.assertEqual(len(json.loads(content)['rows']),11)


if __name__=='__main__':
    unittest.main()
