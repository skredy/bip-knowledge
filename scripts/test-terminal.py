#!/usr/bin/env python3
"""Linux terminal regression test. Uses only stdlib and a simulated Copilot process.

Run after building: python3 scripts/test-terminal.py
Or test an installed package: python3 scripts/test-terminal.py --tool /path/to/bip-kb
"""
import argparse
import codecs
import errno
import json
import os
from pathlib import Path
import select
import shutil
import signal
import subprocess
import tempfile
import time


class Terminal:
    def __init__(self, command):
        import pty
        self.master, slave = pty.openpty()
        self.process = subprocess.Popen(command, stdin=slave, stdout=slave, stderr=slave, start_new_session=True)
        os.close(slave)
        self.decoder = codecs.getincrementaldecoder("utf-8")("replace")
        self.buffer = ""
        self.transcript = ""

    def read(self):
        if not select.select([self.master], [], [], 0.1)[0]:
            return
        try:
            data = os.read(self.master, 65536)
        except OSError as error:
            if error.errno == errno.EIO:
                return
            raise
        text = self.decoder.decode(data)
        self.buffer += text
        self.transcript += text

    def expect(self, text, timeout=30):
        deadline = time.monotonic() + timeout
        while text not in self.buffer:
            self.read()
            if time.monotonic() > deadline or (self.process.poll() is not None and text not in self.buffer):
                raise AssertionError(f"Missing terminal prompt: {text}\n{self.transcript[-3500:]}")
        self.buffer = self.buffer.split(text, 1)[1]

    def answer(self, prompt, value):
        self.expect(prompt)
        os.write(self.master, (value + "\n").encode())

    def finish(self, expected=0):
        deadline = time.monotonic() + 40
        while self.process.poll() is None:
            self.read()
            if time.monotonic() > deadline:
                raise AssertionError("Terminal process timed out\n" + self.transcript[-3500:])
        self.read()
        assert self.process.returncode == expected, self.transcript[-3500:]
        return self.transcript

    def __enter__(self):
        return self

    def __exit__(self, *_):
        if self.process.poll() is None:
            os.killpg(self.process.pid, signal.SIGTERM)
            self.process.wait(timeout=5)
        os.close(self.master)


def main():
    if os.name != "posix":
        raise SystemExit("This terminal test needs a Unix PTY. Cross-platform form tests run with dotnet test.")
    parser = argparse.ArgumentParser()
    parser.add_argument("--tool")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    command = [str(Path(args.tool).resolve())] if args.tool else [args.dotnet, str(repo / "src/BipKb/bin/Debug/net10.0/BipKb.dll")]
    with tempfile.TemporaryDirectory(prefix="bip-terminal-") as temp:
        root = Path(temp)
        kb = root / "kb"
        corpus = root / "Étude SQL"
        corpus.mkdir()
        (corpus / "facture.sql").write_text("CREATE TABLE dbo.Facture(Id int PRIMARY KEY, Statut int);", encoding="utf-8")
        (corpus / "Facture.cs").write_text("public record FactureDto(int Id, int Statut);", encoding="utf-8")
        shutil.copy(repo / "tests/BipKb.Tests/Fixtures/study.xls", corpus / "etude.xls")

        def cli(*words, workspace=kb, expected=0, analyze=True):
            options = ["--analyze", "--analysis-mode", "batches"] if analyze and words[0] == "--ingest" else []
            result = subprocess.run(command + list(map(str, words)) + options + ["--workspace", str(workspace), "--json"], capture_output=True, text=True, timeout=40)
            assert result.returncode == expected, result.stdout + result.stderr
            assert '[ingestion ' not in result.stderr, result.stderr
            assert '[index ' not in result.stderr, result.stderr
            return json.loads(result.stdout if expected == 0 else result.stderr)

        def terminal(*words):
            options = ["--analyze", "--analysis-mode", "batches"] if words[0] == "--ingest" else []
            return Terminal(command + list(map(str, words)) + options + ["--workspace", str(kb)])

        cli("init")
        fixture = root / "copilot-fixture"
        fixture.write_text('''#!/usr/bin/env python3
import json, pathlib, sys
args=sys.argv[1:]
root=pathlib.Path(__file__).parent
if '--help' in args:
 print('--prompt --silent --deny-tool --available-tools --disable-builtin-mcps --no-custom-instructions --no-auto-update --stream --attachment --no-color --no-ask-user --output-format');sys.exit(0)
if '--version' in args:
 print('terminal-test-fixture');sys.exit(0)
assert '--allow-all' not in args and 'shell' in args and 'write' in args
assert args[args.index('--available-tools')+1]=='--prompt'
assert args[args.index('--output-format')+1]=='json'
with (root/'calls').open('a') as log: log.write('analysis\\n')
if (root/'fail').exists():
 (root/'fail').unlink();print('Simulated provider failure',file=sys.stderr);sys.exit(1)
prompt=args[args.index('--prompt')+1]
data=json.loads(prompt.split('UNTRUSTED_DATA\\n',1)[1])
if (root/'malformed').exists():
 (root/'malformed').unlink()
 print(json.dumps({'type':'assistant.message','data':{'content':'{"findings": ['}}))
 print(json.dumps({'type':'result','exitCode':0}));sys.exit(0)
response=json.dumps({'findings':[] if data['phase']=='reconcile' else [{'entity':'Facture','kind':'rule','statement':u['text'],'contribution':'new','evidenceIds':[u['id']]} for u in data['units']]})
print(json.dumps({'type':'assistant.message','data':{'content':response}}))
print(json.dumps({'type':'result','exitCode':0}))
''', encoding="utf-8")
        fixture.chmod(0o700)
        config_path = kb / "bip.json"
        config = json.loads(config_path.read_text(encoding="utf-8"))
        config["businessMode"] = False  # Explicit regression for workspaces using the previous workflow.
        config["copilotExecutable"] = str(fixture)
        config_path.write_text(json.dumps(config), encoding="utf-8")
        # The old location may be unusable (for example under a synced workspace).
        # The analysis must use its separate system temporary directory instead.
        (kb / ".local" / "copilot").write_text("legacy runtime location is blocked", encoding="utf-8")

        with terminal("project", "setup") as session:
            session.answer("Nom du projet", "Migration BIP")
            session.answer("Ce que vous voulez réaliser", "Recréer les endpoints à partir du modèle cible")
            session.answer("Technologies et organisation du code", ".NET et SQL Server")
            session.answer("Applications ou bases de données concernées", "Ancien BIP ; nouvelle API")
            session.answer("Sujets métier à organiser", "Facturation ; Clients")
            session.answer("Règles de développement à respecter", "")
            session.answer("Enregistrer ce profil ?", "OUI")
            session.finish()

        description = "Étude de migration et correspondances SQL. " * 20
        notes = "Vérifier les exceptions et filtres métier. " * 15
        (root / "fail").touch()
        with terminal("--ingest", corpus) as session:
            session.answer("Identifiant court de la source", "Étude SQL")
            session.expect("Identifiant de source invalide")
            session.answer("Identifiant court de la source", "etude.sql")
            session.expect("Identifiant de source invalide")
            session.answer("Identifiant court de la source", "etude-sql")
            session.answer("Projet auquel rattacher ces fichiers", "")
            session.answer("Sujet métier principal", "Facturation")
            session.answer("Rôle : legacy / target / current", "ancien")
            session.expect("Rôle invalide")
            session.answer("Rôle : legacy / target / current", "target")
            session.answer("Statut : proposal / validated / observed / historical", "unknown")
            session.expect("Statut invalide")
            session.answer("Statut : proposal / validated / observed / historical", "validated")
            session.answer("Que contient cet ensemble, et à quoi sert-il ?", "x" * 1201)
            session.expect("1201 caractères saisis")
            session.answer("Que contient cet ensemble, et à quoi sert-il ?", description)
            session.answer("Précisions, limites ou correspondances connues", notes)
            session.answer("Enregistrer ce contexte et analyser ?", "ouii")
            session.expect("Répondez oui ou non")
            session.answer("Enregistrer ce contexte et analyser ?", "OUI")
            session.expect("Contexte enregistré")
            session.expect("Copilot CLI failed")
            session.finish(expected=1)

        saved = json.loads(config_path.read_text(encoding="utf-8"))["sources"][0]
        assert saved["id"] == "etude-sql" and saved["context"]["description"] == description.strip()
        assert saved["context"]["notes"] == notes.strip()
        (root / "malformed").touch()
        with terminal("--ingest", corpus) as session:
            session.expect("Contexte réutilisé")
            transcript = session.finish()
            assert "Que contient cet ensemble" not in transcript and '"files": 3' in transcript
            assert '[ingestion ' in transcript and 'Fichier 1/3' in transcript
            assert 'attente de Copilot' in transcript and 'Rapprochement entre fichiers' in transcript
            assert 'Ingestion terminée' in transcript
            assert 'nouvelle tentative de format (2/2)' in transcript
        calls = (root / "calls").read_text()
        assert cli("--ingest", corpus)["unchanged"] == 3
        assert (root / "calls").read_text() == calls
        assert cli("search", "Facture")["hits"]
        assert "Preuve" in cli("context", "Préparer la consultation des factures")

        with terminal("--ingest", corpus / "Facture.cs", "--describe") as session:
            session.answer("Projet auquel rattacher ces fichiers", "")
            session.answer("Sujet métier principal", "")
            session.answer("Rôle : legacy / target / current", "current")
            session.answer("Statut : proposal / validated / observed / historical", "observed")
            session.answer("Que contient cet ensemble, et à quoi sert-il ?", "Exemple de DTO actuel")
            session.answer("Précisions, limites ou correspondances connues", "")
            session.answer("Enregistrer ce contexte et analyser ?", "oui")
            session.finish()
        updated = json.loads(config_path.read_text(encoding="utf-8"))["sources"][0]
        assert updated["role"] == "target" and updated["overrides"]["Facture.cs"]["role"] == "current"
        assert updated["context"]["description"] == description.strip()
        before = config_path.read_text(encoding="utf-8")
        with terminal("--ingest", corpus / "Facture.cs", "--describe") as session:
            session.answer("Projet auquel rattacher ces fichiers", "")
            session.answer("Sujet métier principal", "")
            session.answer("Rôle : legacy / target / current", "")
            session.answer("Statut : proposal / validated / observed / historical", "")
            session.answer("Que contient cet ensemble, et à quoi sert-il ?", "Ne pas enregistrer")
            session.answer("Précisions, limites ou correspondances connues", "")
            session.answer("Enregistrer ce contexte et analyser ?", "NON")
            session.finish(expected=130)
        assert config_path.read_text(encoding="utf-8") == before
        assert "Identifiant de source invalide" in cli("--ingest", corpus, "--source", "Nom libre", expected=1)["error"]

        other = root / "shared"
        cli("export", "--output", other)
        cli("import", workspace=other)
        cli("source", "bind", "etude-sql", corpus, workspace=other)
        assert cli("--ingest", corpus, workspace=other)["unchanged"] == 3
        assert cli("search", "Facture", workspace=other)["hits"]
        # Default ingestion must work even when Copilot cannot be launched, then allow targeted analysis.
        config = json.loads(config_path.read_text(encoding="utf-8"))
        config["copilotExecutable"] = str(root / "copilot-not-installed")
        config_path.write_text(json.dumps(config), encoding="utf-8")
        local_file = corpus / "local.md"
        local_file.write_text("Une facture peut contenir un reliquat.", encoding="utf-8")
        before_calls = (root / "calls").read_text()
        indexed = cli("--ingest", local_file, analyze=False)
        assert indexed["mode"] == "index" and indexed["unanalyzedFiles"] == 1
        assert (root / "calls").read_text() == before_calls
        assert cli("search", "reliquat")["hits"]
        # Reindex uses only published memory, even when the original file and Copilot are unavailable.
        memory_before = {str(p.relative_to(kb)): p.read_bytes() for p in (kb / "memory").rglob("*.json")}
        original = local_file.read_text(encoding="utf-8")
        local_file.unlink()
        rebuilt = cli("reindex")
        assert rebuilt["status"] == "indexed" and Path(rebuilt["index"]).name == "search-v6.db"
        assert (root / "calls").read_text() == before_calls
        assert {str(p.relative_to(kb)): p.read_bytes() for p in (kb / "memory").rglob("*.json")} == memory_before
        assert cli("search", "reliquat")["hits"]
        local_file.write_text(original, encoding="utf-8")
        config["businessMode"] = False  # Explicit regression for workspaces using the previous workflow.
        config["copilotExecutable"] = str(fixture)
        config_path.write_text(json.dumps(config), encoding="utf-8")
        analyzed = cli("--ingest", local_file)
        assert analyzed["mode"] == "analysis" and analyzed["unanalyzedFiles"] == 0
        assert (root / "calls").read_text() != before_calls
        print("PASS: project setup, SQL/XLS/C# ingestion, invalid fields, confirmation, analysis failure, retry without retyping, unchanged import, file override, cancellation, search/context, export/import/remap.")
        print("Copilot was simulated. Terminal coverage is Linux; xUnit form tests are included in Windows CI.")


if __name__ == "__main__":
    main()
