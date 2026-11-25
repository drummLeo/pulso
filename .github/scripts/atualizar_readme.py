"""Atualiza o resumo público do Pulso a partir dos manifestos e do histórico Git."""
import argparse
from datetime import date
from html import escape
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
README = ROOT / "README.md"
START = "<!-- pulso:resumo:inicio -->"
END = "<!-- pulso:resumo:fim -->"


def resumo():
    pc = ET.parse(ROOT / "src/Pulso.csproj").findtext("PropertyGroup/Version")
    linux = ET.parse(ROOT / "Linux/Pulso.Linux.csproj").findtext("PropertyGroup/Version")
    manifest = ET.parse(ROOT / "Android/AndroidManifest.xml").getroot()
    android = manifest.get("{http://schemas.android.com/apk/res/android}versionName")
    extension = json.loads((ROOT / "src/Browser/manifest.json").read_text(encoding="utf-8"))["version"]
    if not all((pc, linux, android, extension)):
        raise ValueError("Falta uma versão nos manifestos de distribuição.")

    repo = os.environ.get("GITHUB_REPOSITORY", "drummLeo/pulso")
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", repo):
        raise ValueError("Identificador de repositório inválido.")
    history = subprocess.check_output(
        ["git", "-c", "i18n.logOutputEncoding=UTF-8", "log", "-5", "--format=%H%x09%cs%x09%s",
         "--", ".", ":(exclude)README.md"], cwd=ROOT, encoding="utf-8",
    )
    lines = [f"**Windows** {pc} · **Linux** {linux} · **Android** {android} · **Extensão para Chrome e Edge** {extension}", ""]
    for entry in history.strip("\n").split("\n"):
        if not entry:
            continue
        sha, day, title = entry.split("\t", 2)
        plain = re.sub(r"([\\`*_\[\]|~])", r"\\\1", escape(" ".join(title.split()), quote=False))
        when = date.fromisoformat(day).strftime("%d/%m/%Y")
        lines.append(f"- {when} — {plain} · [{sha[:7]}](https://github.com/{repo}/commit/{sha})")
    return "\n".join(lines)


def atualizar(check):
    original = README.read_bytes().decode("utf-8")
    if original.count(START) != 1 or original.count(END) != 1 or original.index(START) >= original.index(END):
        raise ValueError("O README precisa conter um único par de marcadores do resumo, em ordem.")
    before, _, tail = original.partition(START)
    _, _, after = tail.partition(END)
    newline = "\r\n" if "\r\n" in original else "\n"
    block = ("\n\n" + resumo() + "\n\n").replace("\n", newline)
    updated = before + START + block + END + after
    if updated == original:
        print("README atualizado.")
        return 0
    if check:
        print("O resumo do README precisa ser atualizado.")
        return 1
    descriptor, name = tempfile.mkstemp(dir=ROOT, prefix=".readme-", suffix=".tmp")
    draft = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as output:
            output.write(updated.encode("utf-8"))
        draft.chmod(README.stat().st_mode)
        draft.replace(README)
    finally:
        draft.unlink(missing_ok=True)
    print("Versões e alterações recentes atualizadas no README.")
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="sinaliza se o resumo está desatualizado")
    raise SystemExit(atualizar(parser.parse_args().check))
