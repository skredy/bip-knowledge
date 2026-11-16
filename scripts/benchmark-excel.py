#!/usr/bin/env python3
"""Compare XLSX analysis call counts using a simulated Copilot process (Linux).

No real model, private workbook, or timing prediction. All generated nonempty rows must be sent.
"""
import argparse
from collections import Counter
import json
from pathlib import Path
import random
import string
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--tool")
    parser.add_argument("--baseline-tool")
    parser.add_argument("--rows", type=int, default=600)
    args = parser.parse_args()
    if not 1 <= args.rows <= 10000:
        parser.error("rows must be 1..10000")
    repo = Path(__file__).resolve().parent.parent
    candidate = [str(Path(args.tool).resolve())] if args.tool else [args.dotnet, str(repo / "src/BipKb/bin/Debug/net10.0/BipKb.dll")]
    with tempfile.TemporaryDirectory(prefix="bip-excel-benchmark-") as temp:
        root = Path(temp)
        corpus = root / "src"
        corpus.mkdir()
        workbook = corpus / "endpoints.xlsx"
        ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
        worksheet = ET.Element("worksheet", xmlns=ns)
        ET.SubElement(worksheet, "dimension", ref=f"A1:L{args.rows+1}")
        data = ET.SubElement(worksheet, "sheetData")

        def cell(row, address, text):
            ET.SubElement(ET.SubElement(ET.SubElement(row, "c", r=address, t="inlineStr"), "is"), "t").text = text

        header = ET.SubElement(data, "row", r="1")
        for col in string.ascii_uppercase[:12]:
            cell(header, col+"1", f"{col} : Correspondance documentée entre endpoint historique et cible ; conserver les conditions et exceptions")
        rng = random.Random(23)
        for i in range(2, args.rows+2):
            row = ET.SubElement(data, "row", r=str(i))
            cell(row, f"A{i}", f"GET /commandes/{i}")
            cell(row, f"B{i}", f"DTO{i} ; statut actif")
            cell(row, f"C{i}", "Note synthétique " + "".join(rng.choices(string.ascii_letters+string.digits, k=200)))
        template = repo / "tests/BipKb.Tests/Fixtures/study.xlsx"
        with zipfile.ZipFile(template) as source, zipfile.ZipFile(workbook, "w", zipfile.ZIP_DEFLATED) as target:
            for name in source.namelist():
                target.writestr(name, ET.tostring(worksheet) if name == "xl/worksheets/sheet1.xml" else source.read(name))
        fixture = root / "copilot-simulated"
        fixture.write_text('''#!/usr/bin/env python3
import json, pathlib, sys
args=sys.argv[1:]
if '--help' in args:
 print('--prompt --silent --deny-tool --available-tools --disable-builtin-mcps --no-custom-instructions --no-auto-update --stream --no-color --no-ask-user --output-format');sys.exit(0)
data=json.loads(args[args.index('--prompt')+1].split('UNTRUSTED_DATA\\n',1)[1])
assert '--allow-all' not in args and '--deny-tool' in args
entry={'phase':data['phase'],'ids':[u['id'] for u in data['units']],'promptCharacters':len(args[args.index('--prompt')+1])}
with pathlib.Path(__file__).with_suffix('.calls').open('a') as log: log.write(json.dumps(entry)+'\\n')
print(json.dumps({'type':'assistant.message','data':{'content':'{"findings":[]}'}}))
print(json.dumps({'type':'result','exitCode':0}))
''', encoding="utf-8")
        fixture.chmod(0o700)
        descriptor = root / "context.json"
        descriptor.write_text(json.dumps({"role": "legacy", "context": {"project": "Migration", "domain": "Commandes", "description": "Étude synthétique des endpoints", "status": "historical", "notes": ""}}))

        def measure(command, name):
            workspace = root / name

            def run(*words):
                result = subprocess.run(command+list(map(str, words))+["--workspace", str(workspace), "--json"], capture_output=True, text=True, timeout=600)
                if result.returncode:
                    raise RuntimeError(result.stderr)
                return json.loads(result.stdout)

            run("init")
            config_file = workspace / "bip.json"
            config = json.loads(config_file.read_text())
            config["copilotExecutable"] = str(fixture)
            config_file.write_text(json.dumps(config))
            log = fixture.with_suffix(".calls")
            if log.exists():
                log.unlink()
            run("--ingest", workbook, "--source", "benchmark", "--context-file", descriptor, "--analyze")
            calls = [json.loads(line) for line in log.read_text().splitlines()]
            ids = [i for call in calls if call["phase"] == "extract" for i in call["ids"]]
            assert len(ids) == len(set(ids)) == args.rows+1
            return {"calls": dict(Counter(call["phase"] for call in calls)), "rowsWithHeader": len(ids), "maxPromptCharacters": max(call["promptCharacters"] for call in calls)}, set(ids)

        metrics = {"xlsxBytes": workbook.stat().st_size, "dataRows": args.rows, "copilot": "simulated; call counts only, not model latency or business accuracy"}
        baseline_ids = None
        if args.baseline_tool:
            metrics["baseline"], baseline_ids = measure([str(Path(args.baseline_tool).resolve())], "baseline")
        metrics["candidate"], ids = measure(candidate, "candidate")
        if baseline_ids is not None:
            assert baseline_ids == ids
        assert metrics["candidate"]["calls"].get("reconcile", 0) == 0
        print(json.dumps(metrics, indent=2))


if __name__ == "__main__":
    main()
