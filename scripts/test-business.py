#!/usr/bin/env python3
"""Installed business workflow; simulated Copilot process, never a model-quality benchmark."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tool')
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    command = [str(Path(args.tool).resolve())] if args.tool else [args.dotnet, str(repo / 'src/BipKb/bin/Debug/net10.0/BipKb.dll')]
    with tempfile.TemporaryDirectory(prefix='bip-business-') as temporary:
        root = Path(temporary)
        kb = root / 'Projet métier'
        package = root / 'node_modules/@github/copilot'
        package.mkdir(parents=True)
        fixture = package / 'business.js'
        shutil.copy(repo / 'tests/BipKb.Tests/Fixtures/copilot-business.js', fixture)
        if os.name == 'nt':
            executable = root / 'copilot.cmd'
            executable.write_text('Wrapper must not be executed')
            (package / 'package.json').write_text(json.dumps({'name': '@github/copilot', 'bin': {'copilot': 'business.js'}}))
        else:
            fixture.chmod(0o700)
            executable = fixture

        def run(*words):
            completed = subprocess.run(command + list(map(str, words)) + ['--workspace', str(kb), '--json'],
                                       capture_output=True, text=True, encoding='utf-8', timeout=60)
            assert completed.returncode == 0, completed.stdout + completed.stderr
            assert not completed.stderr, completed.stderr
            return json.loads(completed.stdout)

        run('init', '--non-interactive')
        config_path = kb / 'bip.json'
        config = json.loads(config_path.read_text(encoding='utf-8'))
        assert config['businessMode']
        config['copilotExecutable'] = str(executable)
        config_path.write_text(json.dumps(config), encoding='utf-8')
        proposal = run('project', 'setup', '--message', 'Comprendre la consultation des contrats é 漢字 👋')
        assert proposal['reply']['ready']
        profile = run('project', 'setup', '--confirm', '--actor', 'Test')
        assert profile['brief']['declarations'][0]['text'].endswith('é 漢字 👋')
        book = root / 'Étude.xlsx'
        shutil.copy(repo / 'tests/BipKb.Tests/Fixtures/study.xlsx', book)
        report = run('--ingest', book, '--message', 'Étude des contrats, comportant des propositions cible')
        assert report['mode'] == 'business' and report['unanalyzedFiles'] == 0
        calls = (package / 'business-calls.jsonl').read_text()
        assert run('--ingest', book)['unchanged'] == 1
        assert calls == (package / 'business-calls.jsonl').read_text()
        assert run('search', 'accès contrat')['hits'][0]['kind'] == 'claim'
        assert 'Cadrage métier' in run('context', 'accès contrat')
        assert run('analysis', report['sourceId'])['published'][0]['business']['complete']
        # MCP must read the index even when no materialized cache is present.
        cache = kb / '.local/materialized.json'
        cache.rename(cache.with_suffix('.saved'))
        messages = [
            {'jsonrpc': '2.0', 'id': 1, 'method': 'tools/call', 'params': {'name': 'project', 'arguments': {}}},
            {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/call', 'params': {'name': 'context', 'arguments': {'query': 'accès contrat'}}},
            {'jsonrpc': '2.0', 'id': 3, 'method': 'tools/call', 'params': {'name': 'analysis', 'arguments': {'id': report['sourceId']}}},
        ]
        started = time.perf_counter()
        mcp = subprocess.run(command + ['mcp', '--workspace', str(kb)], input=''.join(json.dumps(m)+'\n' for m in messages),
                             capture_output=True, text=True, encoding='utf-8', timeout=30)
        assert mcp.returncode == 0 and not mcp.stderr, mcp.stderr
        responses = [json.loads(line) for line in mcp.stdout.splitlines()]
        assert len(responses) == 3 and all(not r['result']['isError'] for r in responses), responses
        print(json.dumps({'status': 'passed', 'model': 'simulated', 'calls': len(calls.splitlines()),
                          'threeMcpCallsAndStartupSeconds': round(time.perf_counter()-started, 3),
                          'unicodePreserved': True, 'cacheNotRequiredForMcp': not cache.exists()}))


if __name__ == '__main__':
    main()
