#!/usr/bin/env python3
"""Compare guided request counts on a synthetic workbook; no real Copilot/model latency."""
import argparse
import json
from pathlib import Path
import random
import shutil
import string
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tool', required=True)
    parser.add_argument('--baseline-tool')
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    with tempfile.TemporaryDirectory(prefix='bip-guided-benchmark-') as temp:
        root = Path(temp)
        file = root / 'endpoints.xlsx'
        sheet = ET.Element('worksheet', xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main')
        ET.SubElement(sheet, 'dimension', ref='A1:C566')
        data = ET.SubElement(sheet, 'sheetData')
        rng = random.Random(23)
        for n in range(1, 567):
            row = ET.SubElement(data, 'row', r=str(n))
            values = ['Endpoint historique', 'DTO cible', 'Conditions documentées'] if n == 1 else [f'GET /commandes/{n}', f'DTO{n}', ''.join(rng.choices(string.ascii_letters+string.digits, k=220))]
            for column, value in zip('ABC', values):
                ET.SubElement(ET.SubElement(ET.SubElement(row, 'c', r=f'{column}{n}', t='inlineStr'), 'is'), 't').text = value
        with zipfile.ZipFile(repo / 'tests/BipKb.Tests/Fixtures/study.xlsx') as template, zipfile.ZipFile(file, 'w', zipfile.ZIP_DEFLATED) as target:
            for name in template.namelist():
                target.writestr(name, ET.tostring(sheet) if name == 'xl/worksheets/sheet1.xml' else template.read(name))
        fixture = root / 'copilot-guided.js'
        shutil.copyfile(repo / 'tests/BipKb.Tests/Fixtures/copilot-guided.js', fixture)
        fixture.chmod(0o700)
        descriptor = root / 'context.json'
        descriptor.write_text(json.dumps({'role': 'legacy', 'context': {'project': 'Migration', 'domain': 'Commandes', 'description': 'Étude synthétique', 'status': 'historical', 'notes': ''}}))

        def measure(tool, name):
            workspace = root / name

            def run(*words):
                result = subprocess.run([str(Path(tool).resolve()), *map(str, words), '--workspace', str(workspace), '--json'], capture_output=True, text=True, timeout=180)
                if result.returncode:
                    raise RuntimeError(result.stderr)
                return json.loads(result.stdout)

            run('init')
            config_file = workspace / 'bip.json'
            config = json.loads(config_file.read_text())
            config['copilotExecutable'] = str(fixture)
            config_file.write_text(json.dumps(config))
            log = root / 'guided-calls.jsonl'
            log.unlink(missing_ok=True)
            if name == 'candidate':
                (root / 'stdin-enabled').touch()
            run('--ingest', file, '--source', 'benchmark', '--context-file', descriptor, '--analyze', '--max-calls', 100)
            calls = [json.loads(line) for line in log.read_text().splitlines()]
            ids = {proof for call in calls for proof in call['ids']}
            summary = run('analysis', 'benchmark')['published'][0]['exploration']
            assert summary['analyzedUnits'] == summary['totalUnits'] == len(ids) == 566
            return {'calls': len(calls), 'units': len(ids), 'maxPromptCharacters': max(c['promptCharacters'] for c in calls)}, ids

        metrics = {'xlsxBytes': file.stat().st_size, 'model': 'simulated; request counts only'}
        previous = None
        if args.baseline_tool:
            metrics['baseline'], previous = measure(args.baseline_tool, 'baseline')
        metrics['candidate'], current = measure(args.tool, 'candidate')
        if previous is not None:
            assert previous == current
        assert metrics['candidate']['calls'] <= 5
        print(json.dumps(metrics, indent=2))


if __name__ == '__main__':
    main()
