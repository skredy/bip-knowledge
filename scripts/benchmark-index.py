#!/usr/bin/env python3
"""Dense proof-graph benchmark. Generates public synthetic data; never uses Copilot."""
import argparse
import json
from pathlib import Path
import sqlite3
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--count", type=int, default=20000)
    parser.add_argument("--tool")
    parser.add_argument("--baseline-tool", help="Optional installed 0.3.9 tool to compare")
    parser.add_argument("--baseline-timeout", type=int, default=120)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    project = repo / "scripts/index-benchmark/IndexBenchmark.csproj"
    subprocess.run([args.dotnet, "build", str(project), "-c", "Release", "--nologo"], check=True, capture_output=True)
    generator = project.parent / "bin/Release/net10.0/IndexBenchmark.dll"
    tool = [str(Path(args.tool).resolve())] if args.tool else [args.dotnet, str(repo / "src/BipKb/bin/Release/net10.0/BipKb.dll")]
    with tempfile.TemporaryDirectory(prefix="bip-dense-index-") as temp:
        root = Path(temp) / "kb"
        data = subprocess.run([args.dotnet, str(generator), str(root), str(args.count)], check=True, capture_output=True, text=True, timeout=600)
        metrics = json.loads(data.stdout)
        print(json.dumps({"fixture": metrics}), flush=True)
        if args.baseline_tool:
            start = time.perf_counter()
            try:
                baseline = subprocess.run([str(Path(args.baseline_tool).resolve()), "search", "REF00000001", "--workspace", str(root), "--json"], capture_output=True, text=True, timeout=args.baseline_timeout)
                metrics["baselineSeconds"] = round(time.perf_counter()-start, 3)
                metrics["baselineExitCode"] = baseline.returncode
                if baseline.returncode:
                    raise RuntimeError(baseline.stderr)
            except subprocess.TimeoutExpired:
                metrics["baselineExceededSeconds"] = args.baseline_timeout
            print(json.dumps({"baseline": {k: v for k, v in metrics.items() if k.startswith("baseline")}}), flush=True)
        start = time.perf_counter()
        indexed = subprocess.run(tool + ["reindex", "--workspace", str(root), "--json"], capture_output=True, text=True, timeout=600)
        if indexed.returncode:
            raise RuntimeError(indexed.stderr)
        metrics["reindexSeconds"] = round(time.perf_counter()-start, 3)
        index = Path(json.loads(indexed.stdout)["index"])
        with sqlite3.connect(index) as db:
            for name in ("docs", "edges", "proof_members", "node_proofs"):
                metrics[name] = db.execute(f"select count(*) from {name}").fetchone()[0]
        metrics["indexBytes"] = index.stat().st_size
        start = time.perf_counter()
        result = subprocess.run(tool + ["search", "REF00000001", "--workspace", str(root), "--json"], check=True, capture_output=True, text=True)
        assert json.loads(result.stdout)["hits"]
        metrics["searchSeconds"] = round(time.perf_counter()-start, 3)
        print(json.dumps(metrics, indent=2), flush=True)


if __name__ == "__main__":
    main()
