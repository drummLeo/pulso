"""Reproduz as listas locais de sites usando revisões públicas fixadas."""
import argparse
import hashlib
from pathlib import Path
import re
import tempfile
from urllib.request import urlopen


SHARED = Path(__file__).resolve().parents[1] / "shared"
REVISION = "8ed495030a7169b626a81b45761b84b8c36bf8e5"
PSL_REVISION = "87a2c1b8de5886a133ff4da21b2aa6fec88d8739"
SOURCE = f"https://raw.githubusercontent.com/ShadowWhisperer/BlockLists/{REVISION}/RAW"
PSL = f"https://raw.githubusercontent.com/publicsuffix/list/{PSL_REVISION}/public_suffix_list.dat"
SOURCES = {
    "porn": (f"{SOURCE}/Adult", "b9ae35bbc234a0429906f6ca7e96907a9fb706d4954cc34f88c946cd3bafbe8b"),
    "gambling": (f"{SOURCE}/Gambling", "7c8862825b71eca171230ae9e091e5eabfbd1f2c5f5a679315753a051af1ceb3"),
}
PSL_SHA256 = "00a0af2ed2379634523b9ca80de3469d1466893915c252a91264e248c92d69d8"
LABEL = re.compile(r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?")


def download(url, digest):
    with urlopen(url, timeout=60) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != digest:
        raise ValueError(f"O arquivo recebido diverge da revisão fixada em {url}")
    return data.decode("utf-8")


class PublicSuffixes:
    def __init__(self, data):
        self.exact, self.wildcards, self.exceptions = set(), set(), set()
        for line in data.splitlines():
            rule = line.partition("//")[0].strip()
            if not rule:
                continue
            target = self.exact
            if rule.startswith("!"):
                rule, target = rule[1:], self.exceptions
            elif rule.startswith("*."):
                rule, target = rule[2:], self.wildcards
            target.add(rule.encode("idna").decode("ascii").lower())

    def is_public(self, domain):
        parent = domain.partition(".")[2]
        return domain not in self.exceptions and (domain in self.exact or parent in self.wildcards)


def domains(data, suffixes, category):
    rules = set()
    for line in data.splitlines():
        name = line.partition("#")[0].strip().lower().rstrip(".")
        labels = name.split(".")
        if (len(labels) >= 2 and len(name) <= 253 and not labels[-1].isdigit()
                and all(LABEL.fullmatch(label) for label in labels) and not suffixes.is_public(name)):
            rules.add(name)
    if not rules:
        raise ValueError(f"Lista de sites vazia em {category}")
    if category == "gambling":
        # O registro brasileiro reserva este sufixo às operadoras de apostas.
        rules.add("bet.br")
    # Uma regra de domínio já inclui todos os seus subdomínios.
    reduced = []
    for name in sorted(rules):
        labels = name.split(".")
        if not any(".".join(labels[i:]) in rules for i in range(1, len(labels) - 1)):
            reduced.append(name)
    return ("\n".join(reduced) + "\n").encode("ascii")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="compara o conteúdo gerado com os arquivos do projeto")
    args = parser.parse_args()
    suffixes = PublicSuffixes(download(PSL, PSL_SHA256))
    outputs = {}
    for category, (url, digest) in SOURCES.items():
        payload = domains(download(url, digest), suffixes, category)
        name = f"site_{category}.txt"
        count = payload.count(b"\n")
        if args.check and (SHARED / name).read_bytes() != payload:
            raise SystemExit(f"{name} diverge das fontes.")
        print(f"{name}: {count} regras")
        outputs[name] = payload
    if args.check:
        return
    pending = {}
    try:
        for name, payload in outputs.items():
            with tempfile.NamedTemporaryFile(dir=SHARED, suffix=".tmp", delete=False) as file:
                pending[name] = Path(file.name)
                file.write(payload)
        for name, source in pending.items():
            source.replace(SHARED / name)
    finally:
        for source in pending.values():
            source.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
