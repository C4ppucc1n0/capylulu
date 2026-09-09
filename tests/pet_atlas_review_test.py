"""Run provenance and original/candidate fidelity checks using local image fixtures."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = ROOT / '.agents/skills/pet-action-atlas/scripts'
sys.path.insert(0, str(SCRIPTS))
import atlas_run as runs
import atlas_compare as review


class AtlasReviewTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.run = self.root / 'artifacts/work/example'
        self.source = self.root / 'input.webp'
        image = Image.new('RGBA', (64, 64))
        draw = ImageDraw.Draw(image)
        for x, y in ((0, 0), (32, 0), (0, 32)):
            draw.rectangle((x+7, y+3, x+25, y+28), fill=(240, 150, 30, 255))
        # Deliberately mislabeled suffix: input format must be inspected, not guessed.
        image.save(self.source, format='PNG')
        self.candidate = self.root / 'candidate.png'
        image.save(self.candidate)
        self.layout = self.root / 'layout.json'
        runs.save_json(self.layout, {'width': 64, 'height': 64, 'rows': [
            {'row': r+1, 'slots': [
                {'rectangle': [c*32, r*32, (c+1)*32, (r+1)*32],
                 'occupied': r == 0 or c == 0} for c in range(2)]} for r in range(2)]})
        runs.initialize(self.run, 'align')
        runs.snapshot(self.run, self.source, 'source')

    def compare(self, **options):
        return review.compare(self.run, self.candidate, self.layout, **options)

    def assessment(self, folder, status='pass'):
        data = json.loads(runs.read_json(folder/'assessment-template.json'))
        data['reviewer'] = 'synthetic fixture test'
        for row in data['rows']:
            for check in review.CHECKS:
                row[check] = {'status': status, 'evidence': 'Fixture observation for review-record validation'}
        path = self.root/'assessment.json'
        runs.save_json(path, data)
        return path

    def test_source_snapshot_has_real_format_and_rejects_replacement(self):
        original = runs.manifest(self.run)['source']
        self.assertEqual(original['format'], 'PNG')
        saved = runs.inside(self.run, original['path'])
        self.assertEqual(saved.suffix, '.png')
        self.assertEqual(saved.read_bytes(), self.source.read_bytes())
        changed = self.root/'changed.png'
        Image.new('RGBA', (64,64), 'red').save(changed)
        with self.assertRaisesRegex(ValueError, 'Source changed'):
            runs.snapshot(self.run, changed, 'source')
        self.assertEqual(runs.manifest(self.run)['source'], original)

    def test_request_preserves_prompt_order_results_and_failure_history(self):
        prompt = self.root/'prompt.txt'
        prompt.write_text('Keep frame 2 eyes half closed.\nOnly correct muzzle thickness.', encoding='utf-8')
        request = runs.start_request(self.run, row='row-01', prompt=prompt,
            images=[self.source,self.candidate], tool='fixture-tool', reason='check exact payload')
        self.assertEqual([item['original_name'] for item in request['images']], ['input.webp','candidate.png'])
        self.assertEqual(runs.inside(self.run,request['prompt']['path']).read_bytes(),prompt.read_bytes())
        failed = runs.finish_request(self.run,request['id'],error='fixture connection failure')
        self.assertEqual(failed['status'],'failed')
        self.assertGreaterEqual(failed['elapsed_seconds'],0)
        retry = runs.start_request(self.run,row='row-01',prompt=prompt,images=[self.source],tool='fixture-tool',reason='retry after failure')
        result = runs.finish_request(self.run,retry['id'],result=self.candidate)
        self.assertEqual(result['status'],'completed')
        self.assertTrue(runs.inside(self.run,result['result']['path']).is_file())
        with self.assertRaisesRegex(ValueError,'already finished'):
            runs.finish_request(self.run,retry['id'],result=self.candidate)
        events=[json.loads(line) for line in (self.run/'events.jsonl').read_text().splitlines()]
        self.assertEqual([e['status'] for e in events if e['stage']=='generation'], ['started','failed','started','completed'])
        self.assertIn(request['id'],(self.run/'timeline.md').read_text(encoding='utf-8'))

    def test_identical_atlas_is_structurally_valid_but_not_visually_approved(self):
        report,folder=self.compare()
        self.assertEqual(report['structure'],'pass')
        self.assertEqual(report['geometry_flags'],[])
        self.assertEqual(report['visual_review'],'pending')
        self.assertEqual(runs.manifest(self.run)['status'],'pending')
        self.assertTrue((folder/'index.html').is_file())
        with Image.open(folder/report['rows'][0]['contact']) as contact:
            self.assertEqual(contact.width,64)

    def test_size_regression_is_visible_even_when_occupancy_is_preserved(self):
        source=Image.open(self.source).convert('RGBA')
        candidate=Image.new('RGBA',source.size)
        for x,y in ((0,0),(32,0),(0,32)):
            cell=source.crop((x,y,x+32,y+32)).resize((24,24),Image.Resampling.NEAREST)
            candidate.alpha_composite(cell,(x+4,y+7))
        candidate.save(self.candidate)
        report,_=self.compare()
        self.assertEqual(report['structure'],'pass')
        self.assertEqual(len([f for f in report['geometry_flags'] if f['kind']=='size_changed']),3)
        self.assertLess(report['rows'][0]['median_width_ratio'],.85)

    def test_blank_or_filled_slots_fail_structure(self):
        candidate=Image.open(self.candidate).convert('RGBA')
        candidate.paste((0,0,0,0),(0,0,32,32))
        ImageDraw.Draw(candidate).rectangle((40,40,50,55),fill='red')
        candidate.save(self.candidate)
        report,folder=self.compare()
        self.assertEqual(report['structure'],'fail')
        self.assertEqual(len([e for e in report['errors'] if 'occupancy' in e]),2)
        verdict=review.record_review(self.run,folder/'comparison.json',self.assessment(folder))
        self.assertEqual(verdict['status'],'rejected')

    def test_wrong_canvas_and_overlapping_layout_are_rejected(self):
        Image.new('RGBA',(65,64)).save(self.candidate)
        report,_=self.compare()
        self.assertEqual(report['structure'],'fail')
        layout=json.loads(runs.read_json(self.layout))
        layout['rows'][0]['slots'][1]['rectangle']=[0,0,32,32]
        runs.save_json(self.layout,layout)
        with self.assertRaisesRegex(ValueError,'overlap'):
            self.compare()

    def test_visible_content_cannot_hide_outside_the_declared_slots(self):
        layout=json.loads(runs.read_json(self.layout))
        layout['rows'][1]['slots'].pop()
        runs.save_json(self.layout,layout)
        candidate=Image.open(self.candidate).convert('RGBA')
        candidate.putpixel((50,50),(255,0,0,255))
        candidate.save(self.candidate)
        report,_=self.compare()
        self.assertEqual(report['structure'],'fail')
        self.assertIn('outside the declared slots',' '.join(report['errors']))

    def test_semantic_failure_rejects_an_otherwise_valid_atlas(self):
        _,folder=self.compare()
        assessment=self.assessment(folder)
        data=json.loads(runs.read_json(assessment))
        data['rows'][0]['motion']={'status':'fail','evidence':'Frame 2 support foot changed in visual comparison'}
        runs.save_json(assessment,data)
        verdict=review.record_review(self.run,folder/'comparison.json',assessment)
        self.assertEqual(verdict['status'],'rejected')
        self.assertIn('id="review-state">rejected', (folder/'index.html').read_text(encoding='utf-8'))

    def test_incomplete_playback_or_evidence_cannot_pass(self):
        _,folder=self.compare()
        assessment=self.assessment(folder)
        data=json.loads(runs.read_json(assessment))
        data['rows'][0]['playback']={'status':'pending','evidence':''}
        runs.save_json(assessment,data)
        self.assertEqual(review.record_review(self.run,folder/'comparison.json',assessment)['status'],'pending')
        data['rows'][0]['playback']={'status':'pass','evidence':''}
        runs.save_json(assessment,data)
        with self.assertRaisesRegex(ValueError,'observed evidence'):
            review.record_review(self.run,folder/'comparison.json',assessment)

    def test_new_candidate_invalidates_old_approval(self):
        _,folder=self.compare()
        assessment=self.assessment(folder)
        self.assertEqual(review.record_review(self.run,folder/'comparison.json',assessment)['status'],'accepted')
        candidate=Image.open(self.candidate).convert('RGBA')
        candidate.putpixel((10,10),(10,20,30,255))
        candidate.save(self.candidate)
        self.compare()
        self.assertEqual(runs.manifest(self.run)['status'],'pending')
        self.assertNotIn('latest_review',runs.manifest(self.run))
        with self.assertRaisesRegex(ValueError,'newer comparison'):
            review.record_review(self.run,folder/'comparison.json',assessment)

    def test_snapshot_tampering_invalidates_review(self):
        report,folder=self.compare()
        assessment=self.assessment(folder)
        runs.inside(self.run,report['candidate']['path']).write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError,'snapshot changed'):
            review.record_review(self.run,folder/'comparison.json',assessment)


if __name__=='__main__':
    unittest.main()
