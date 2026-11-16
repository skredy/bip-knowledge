#!/usr/bin/env python3
"""Linux CLI regression for guided exploration. Copilot is a Node process test double."""
import argparse
import base64
import json
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tool")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--stdin", action="store_true", help="Advertise stdin and exercise wide initial document reads")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    command = [str(Path(args.tool).resolve())] if args.tool else [args.dotnet, str(repo / "src/BipKb/bin/Debug/net10.0/BipKb.dll")]
    with tempfile.TemporaryDirectory(prefix="bip-guided-cli-") as temp:
        root = Path(temp)
        kb = root / "kb"
        fixture = root / "copilot-guided.js"
        shutil.copyfile(repo / "tests/BipKb.Tests/Fixtures/copilot-guided.js", fixture)
        fixture.chmod(0o700)
        if args.stdin:
            (root / "stdin-enabled").touch()

        def run(*words, workspace=kb, expected=0):
            result = subprocess.run(command+list(map(str, words))+["--workspace", str(workspace), "--json"], capture_output=True, text=True, timeout=90)
            assert result.returncode == expected, result.stdout+result.stderr
            assert "[ingestion " not in result.stderr
            try:
                return json.loads(result.stdout if expected == 0 else result.stderr)
            except json.JSONDecodeError as error:
                raise AssertionError(result.stdout+result.stderr) from error

        run("init")
        config_file = kb / "bip.json"
        config = json.loads(config_file.read_text())
        config["copilotExecutable"] = str(fixture)
        config_file.write_text(json.dumps(config))
        descriptor = root / "context.json"
        descriptor.write_text(json.dumps({"role": "current", "context": {"project": "iBip", "domain": "Commandes", "description": "Étude synthétique", "status": "observed", "notes": ""}}))
        for extension in ("xls", "xlsx", "drawio", "png", "sql"):
            folder = root / "src" / extension
            folder.mkdir(parents=True)
            source = folder / ("study."+extension)
            if extension in ("xls", "xlsx"):
                shutil.copyfile(repo / "tests/BipKb.Tests/Fixtures" / source.name, source)
            elif extension == "drawio":
                source.write_text('<mxGraphModel><root><mxCell id="a" value="Commande"><mxGeometry x="10" y="20"/></mxCell><mxCell id="b" value="Client"/><mxCell id="r" source="a" target="b"/></root></mxGraphModel>')
            elif extension == "png":
                source.write_bytes(base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="))
            else:
                source.write_text("CREATE TABLE dbo.Commande(Id int PRIMARY KEY, Statut int);")
            words = ["--ingest", source, "--source", extension, "--context-file", descriptor, "--analyze"]
            if extension == "xls" and not args.stdin:
                before = len(list((kb / "memory/changes").glob("*.json")))
                failure = run(*words, "--max-calls", 1, expected=1)
                assert "en pause" in failure["error"]
                status = run("analysis", extension)
                assert status["published"] == [] and status["local"][0]["state"] == "paused"
                assert len(list((kb / "memory/changes").glob("*.json"))) == before
                report = run(*words, "--max-calls", 1)
                assert len((root / "guided-calls.jsonl").read_text().splitlines()) == 2
            else:
                report = run(*words)
            assert report["mode"] == "guided" and report["unanalyzedFiles"] == 0
            status = run("analysis", extension)
            summary = status["published"][0]["exploration"]
            if args.stdin:
                assert summary["steps"] == 1
                assert status["local"][0]["profile"] == "stdin-v1"
            assert summary["analyzedUnits"]+summary["emptyUnits"] == summary["totalUnits"]
            assert status["local"][0]["state"] == "published"
            calls = (root / "guided-calls.jsonl").read_text()
            assert run(*words)["unchanged"] == 1
            assert calls == (root / "guided-calls.jsonl").read_text()
        assert "Compréhension proposée" in run("context", "Commande")
        shared = root / "shared"
        run("export", "--output", shared)
        run("import", workspace=shared)
        assert run("analysis", "xls")["published"] == run("analysis", "xls", workspace=shared)["published"]
        for record in (shared / "memory").rglob("*.json"):
            assert str(root) not in record.read_text()
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
        server = subprocess.Popen(command+["serve", "--workspace", str(kb), "--port", str(port)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            for attempt in range(100):
                try:
                    with opener.open(f"http://127.0.0.1:{port}/?view=ingestions", timeout=2) as response:
                        html = response.read().decode("utf-8")
                    break
                except urllib.error.URLError:
                    if server.poll() is not None:
                        raise AssertionError("Local web server exited before displaying the published plans")
                    time.sleep(.1)
            else:
                raise AssertionError("Local web server did not start")
            assert "Compréhension des sources" in html and "Comprendre les sections avant de conclure." in html
            assert "Couverture et limites" in html and "study.drawio" in html
        finally:
            server.terminate()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait(timeout=5)
        print("PASS: guided CLI XLS/XLSX/Draw.io/image/SQL, overview, pause/resume, coverage, unchanged re-ingestion, context, export/import, published plans in local web UI. Copilot simulated; no real visual/model validation.")


if __name__ == "__main__":
    main()
