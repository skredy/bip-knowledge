#!/usr/bin/env python3
"""Reproducible local-ingestion benchmark on synthetic C#/SQL, with Copilot unavailable."""
import argparse
import json
from pathlib import Path
import sqlite3
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--count", type=int, default=2212)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--tool")
    args = parser.parse_args()
    if not 1 <= args.count <= 10000:
        parser.error("count must be between 1 and 10000")
    repo = Path(__file__).resolve().parent.parent
    command = [str(Path(args.tool).resolve())] if args.tool else [args.dotnet, str(repo / "src/BipKb/bin/Debug/net10.0/BipKb.dll")]
    with tempfile.TemporaryDirectory(prefix="bip-index-benchmark-") as temp:
        root = Path(temp)
        corpus, kb = root / "src", root / "kb"
        corpus.mkdir()
        total_bytes = 0
        for i in range(args.count):
            if i % 50 == 0:
                content = f"CREATE TABLE dbo.Commande{i}(Id int PRIMARY KEY, Statut int);\nGO\nCREATE PROCEDURE dbo.Lister{i} AS SELECT * FROM dbo.Commande{i} WHERE Statut=1;"
                suffix = ".sql"
            else:
                content = f"namespace Migration; public class Commande{i} {{\n" + "\n".join(f"public int Champ{p} {{ get; set; }}" for p in range(8)) + "\npublic bool EstValide() => Champ0 > 0;\n}"
                suffix = ".cs"
            (corpus / f"commande{i:05}{suffix}").write_text(content, encoding="utf-8")
            total_bytes += len(content.encode("utf-8"))

        def run(*words):
            start = time.perf_counter()
            result = subprocess.run(command + list(map(str, words)) + ["--workspace", str(kb), "--json"], capture_output=True, text=True, timeout=900)
            if result.returncode:
                raise RuntimeError(result.stderr)
            return json.loads(result.stdout), time.perf_counter() - start

        run("init")
        config_file = kb / "bip.json"
        config = json.loads(config_file.read_text())
        config["copilotExecutable"] = str(root / "copilot-not-installed")
        config_file.write_text(json.dumps(config))
        descriptor = root / "context.json"
        descriptor.write_text(json.dumps({"role": "current", "context": {"project": "Migration", "domain": "Commandes", "description": "Code et schéma synthétiques pour mesure locale", "status": "observed", "notes": ""}}))
        report, first = run("--ingest", corpus, "--source", "benchmark", "--context-file", descriptor)
        assert report["mode"] == "index" and report["files"] == args.count
        unchanged, second = run("--ingest", corpus)
        assert unchanged["unchanged"] == args.count
        search, query = run("search", "Commande1")
        assert search["hits"]
        with sqlite3.connect(kb / ".local" / "search-v5.db") as db:
            documents = db.execute("select count(*) from docs").fetchone()[0]
            edges = db.execute("select count(*) from edges").fetchone()[0]
        print(json.dumps({"files": args.count, "sourceBytes": total_bytes, "indexedDocuments": documents, "indexedEdges": edges,
                          "firstIngestionSeconds": round(first, 3), "unchangedIngestionSeconds": round(second, 3),
                          "searchProcessSeconds": round(query, 3), "searchReportedMilliseconds": search["milliseconds"],
                          "copilotCalls": 0, "corpus": "synthetic C#/SQL; not a prediction for a production repository"}, indent=2))


if __name__ == "__main__":
    main()
