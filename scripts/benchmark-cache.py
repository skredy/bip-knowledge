#!/usr/bin/env python3
"""Measure lossless local cache compaction on a synthetic corpus, without Copilot."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import time


def shared_hashes(root):
    result = {}
    for name in ('memory', 'decisions'):
        for path in sorted((root / name).rglob('*.json')):
            with path.open('rb') as stream:
                result[str(path.relative_to(root))] = hashlib.file_digest(stream, 'sha256').hexdigest()
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--tool')
    parser.add_argument('--count', type=int, default=22000)
    parser.add_argument('--rebuild', action='store_true')
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    project = repo / 'scripts/index-benchmark/IndexBenchmark.csproj'
    subprocess.run([args.dotnet, 'build', str(project), '-c', 'Release', '--nologo'], check=True, capture_output=True)
    generator = project.parent / 'bin/Release/net10.0/IndexBenchmark.dll'
    tool = [str(Path(args.tool).resolve())] if args.tool else [args.dotnet, str(repo / 'src/BipKb/bin/Release/net10.0/BipKb.dll')]
    with tempfile.TemporaryDirectory(prefix='bip-cache-scale-') as temp:
        root = Path(temp) / 'kb'
        fixture = subprocess.run([args.dotnet, str(generator), str(root), str(args.count)], check=True,
                                 capture_output=True, text=True, timeout=600)
        print(json.dumps({'fixture': json.loads(fixture.stdout)}), flush=True)
        before = shared_hashes(root)
        command = tool + ['cache', 'compact', '--workspace', str(root), '--json'] + (['--rebuild'] if args.rebuild else [])
        start = time.perf_counter()
        result = subprocess.run(command, capture_output=True, text=True, timeout=600)
        assert result.returncode == 0, result.stderr
        compact = json.loads(result.stdout)
        assert compact['verified'] and compact['currentBytes'] < compact['previousBytes'] / 2, compact
        assert compact['currentBytes'] == (root / '.local/materialized.json').stat().st_size
        assert before == shared_hashes(root), 'Shared knowledge was changed'
        metrics = {'compaction': compact, 'seconds': round(time.perf_counter() - start, 3), 'sharedFilesUnchanged': True}
        # Read the compact cache through the regular CLI as well as the verification path.
        detail = subprocess.run(tool + ['analysis', 'bench', '--workspace', str(root), '--json'], capture_output=True, text=True, timeout=120)
        assert detail.returncode == 0, detail.stderr
        assert 'published' in json.loads(detail.stdout)
        repeat = subprocess.run(command, capture_output=True, text=True, timeout=600)
        assert repeat.returncode == 0, repeat.stderr
        assert json.loads(repeat.stdout)['currentBytes'] == compact['currentBytes']
        metrics['idempotent'] = True
        print(json.dumps(metrics, indent=2), flush=True)


if __name__ == '__main__':
    main()
