"""Repository asset layout and reference preparation checks (stdlib only)."""
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
LIBRARY = ROOT / 'assets/character-references'
spec = importlib.util.spec_from_file_location(
    'prepare_references', ROOT / '.agents/skills/capylulu-pet/scripts/prepare_references.py')
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.paths = []

    def handle_starttag(self, tag, attrs):
        for key, value in attrs:
            if key in {'href', 'src'} and value:
                self.paths.append(value)


class AssetLayoutTests(unittest.TestCase):
    def test_explicit_runtime_includes_resolve_without_reference_images(self):
        project = ROOT / 'src/CapyLulu/CapyLulu.csproj'
        tree = ET.parse(project)
        asset_files = []
        for entry in tree.findall('.//EmbeddedResource'):
            pattern = entry.get('Include', '').replace('\\', '/')
            if '../../assets/' not in pattern:
                continue
            self.assertNotIn('**', pattern)
            files = list(project.parent.glob(pattern))
            # PNG and WebP are both supported; a given checkout may use only one.
            self.assertTrue((project.parent / pattern).parent.is_dir(), pattern)
            self.assertTrue(entry.findtext('LogicalName').startswith(
                ('CapyLulu.GeneratedActions.', 'CapyLulu.GifResources.')))
            asset_files.extend(file.resolve() for file in files)
        expected = {file.resolve() for directory in ('pet-atlases', 'animations')
                    for file in (ROOT / 'assets' / directory).rglob('*') if file.is_file()}
        self.assertEqual(expected, set(asset_files))
        self.assertEqual(len(asset_files), len(set(asset_files)))
        self.assertFalse(any(file.is_relative_to(LIBRARY) for file in asset_files))

    def test_reference_index_links_and_frames_survive_move(self):
        parser = Links()
        parser.feed((LIBRARY / 'index.html').read_text(encoding='utf-8'))
        self.assertTrue(parser.paths)
        for link in parser.paths:
            url = urlsplit(link)
            self.assertFalse(url.scheme, link)
            path = (LIBRARY / unquote(url.path)).resolve()
            self.assertTrue(path.is_relative_to(LIBRARY), link)
            self.assertTrue(path.is_file(), link)
        index = json.loads((LIBRARY / 'index.json').read_text(encoding='utf-8'))
        for group in index['videos']:
            if 'group' not in group:
                continue
            directory = LIBRARY / group['group']
            manifest = json.loads((directory / 'manifest.json').read_text(encoding='utf-8'))
            for frame in manifest['frames']:
                self.assertTrue((directory / frame['file']).is_file())

    def test_prepare_default_paths_baseline_and_cached_selection(self):
        baseline = next((ROOT / 'assets/pet-atlases').glob('*.webp'))
        with tempfile.TemporaryDirectory() as temporary:
            run = Path(temporary) / 'run'
            completed = subprocess.run(
                [sys.executable, str(Path(helper.__file__)), '--run-dir', str(run), '--baseline', str(baseline)],
                check=True, capture_output=True)
            self.assertTrue(completed.stdout)
            result = json.loads((run / 'reference-selection.json').read_text(encoding='utf-8'))
            self.assertEqual(str(LIBRARY.resolve()), result['config']['library'])
            self.assertEqual(4, len(result['selected']))
            self.assertEqual(str(baseline.resolve()), result['baseline']['atlas'])
            manifest = json.loads(baseline.with_suffix('.pet.json').read_text(encoding='utf-8'))
            self.assertEqual(manifest, result['baseline']['manifest'])
            for selected in result['selected']:
                self.assertEqual(Path(selected['board']).read_bytes(),
                                 (LIBRARY / selected['group'] / 'reference-board.jpg').read_bytes())
            self.assertEqual(result, helper.prepare(LIBRARY, run, baseline=baseline))

    def test_work_directories_cannot_pollute_any_asset_category(self):
        for category in ('pet-atlases', 'animations', 'character-references'):
            with self.assertRaisesRegex(ValueError, 'outside source references and runtime assets'):
                helper.prepare(LIBRARY, ROOT / 'assets' / category / 'forbidden-test-run')


if __name__ == '__main__':
    unittest.main()
