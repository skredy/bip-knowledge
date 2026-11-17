#!/usr/bin/env python3
"""Synthetic dense corpus: cold/warm MCP timings without Copilot or private documents."""
import argparse
import json
import hashlib
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time


def measure(command, root, timeout):
    process = subprocess.Popen(command + ['mcp', '--workspace', str(root)], stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
    lines = queue.Queue()
    def reader():
        for line in process.stdout:
            lines.put(line)
    thread = threading.Thread(target=reader, daemon=True)
    thread.start()
    metrics = {}
    calls = [('initialize', 'initialize', {}),
             ('searchCold', 'tools/call', {'name': 'search', 'arguments': {'query': 'REF00000001'}}),
             ('searchWarm', 'tools/call', {'name': 'search', 'arguments': {'query': 'REF00000002'}}),
             ('context', 'tools/call', {'name': 'context', 'arguments': {'query': 'REF00000001'}}),
             ('evidence', 'tools/call', {'name': 'evidence', 'arguments': {'id': hashlib.sha256(b'proof1').hexdigest()}})]
    try:
        for n, (label, method, params) in enumerate(calls, 1):
            start = time.perf_counter()
            process.stdin.write(json.dumps({'jsonrpc': '2.0', 'id': n, 'method': method, 'params': params}) + '\n')
            process.stdin.flush()
            try:
                raw = lines.get(timeout=timeout)
            except queue.Empty:
                metrics[label] = {'exceededSeconds': timeout}
                break
            response = json.loads(raw)
            assert response['id'] == n and 'error' not in response, response
            if method == 'tools/call':
                assert not response['result']['isError'], response
                text = response['result']['content'][0]['text']
                assert text and ('REF000000' in text), text
            metrics[label] = {'seconds': round(time.perf_counter() - start, 3), 'responseBytes': len(raw.encode())}
        status = Path(f'/proc/{process.pid}/status')
        if status.exists():
            for line in status.read_text().splitlines():
                if line.startswith('VmHWM:'):
                    metrics['peakRssKiB'] = int(line.split()[1])
    finally:
        process.kill()
        process.wait(timeout=10)
        thread.join(timeout=1)
        process.stdin.close()
        process.stdout.close()
        process.stderr.close()
    return metrics


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--tool')
    parser.add_argument('--baseline-tool')
    parser.add_argument('--count', type=int, default=22000)
    parser.add_argument('--timeout', type=int, default=30)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    project = repo / 'scripts/index-benchmark/IndexBenchmark.csproj'
    subprocess.run([args.dotnet, 'build', str(project), '-c', 'Release', '--nologo'], check=True, capture_output=True)
    generator = project.parent / 'bin/Release/net10.0/IndexBenchmark.dll'
    command = [str(Path(args.tool).resolve())] if args.tool else [args.dotnet, str(repo / 'src/BipKb/bin/Release/net10.0/BipKb.dll')]
    with tempfile.TemporaryDirectory(prefix='bip-mcp-scale-') as temp:
        root = Path(temp) / 'kb'
        generated = subprocess.run([args.dotnet, str(generator), str(root), str(args.count)], check=True,
                                   capture_output=True, text=True, timeout=600)
        metrics = {'fixture': json.loads(generated.stdout)}
        print(json.dumps(metrics), flush=True)
        start = time.perf_counter()
        subprocess.run(command + ['reindex', '--workspace', str(root), '--json'], check=True, capture_output=True, timeout=600)
        metrics['reindexSeconds'] = round(time.perf_counter() - start, 3)
        metrics['candidate'] = measure(command, root, args.timeout)
        print(json.dumps({'candidate': metrics['candidate']}), flush=True)
        if args.baseline_tool:
            metrics['baseline'] = measure([str(Path(args.baseline_tool).resolve())], root, args.timeout)
        print(json.dumps(metrics, indent=2), flush=True)


if __name__ == '__main__':
    main()
