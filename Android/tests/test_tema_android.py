"""Verifica o tema e o estado das telas no AVD descartável PulsoTeste17 com o APK atual instalado."""

import argparse
import os
from pathlib import Path
import struct
import subprocess
from test_android import PKG, Phone, until


def background_is_dark(phone):
    raw = subprocess.check_output([*phone.prefix, "exec-out", "screencap"], timeout=12)
    width, height, _ = struct.unpack_from("<III", raw)
    start = len(raw) - width * height * 4
    if start not in (12, 16):
        raise AssertionError("O emulador não devolveu uma captura RGBA.")
    offset = start + (height // 2 * width + width // 50) * 4
    return max(raw[offset:offset + 3]) < 110


def choose(phone, label):
    phone.tap("Tema")
    phone.tap(label)


def checked(phone, label):
    return any(phone.matches(n, label) and n.get("checked") == "true" for n in phone.nodes())


def run(phone):
    original = phone.sh("cmd", "uimode", "night").split(":")[-1].strip()
    try:
        assert phone.sh("pm", "clear", PKG) == "Success"
        phone.sh("cmd", "uimode", "night", "no")
        phone.home()
        phone.tap("Tema")
        assert checked(phone, "Sistema")
        phone.tap("Cancelar")
        phone.tap("Lista")
        choose(phone, "Escuro")
        until(lambda: background_is_dark(phone), "Escuro aplica a paleta com o celular no modo claro")
        phone.check_visible("Lista de bloqueio")
        phone.sh("am", "force-stop", PKG)
        phone.home()
        phone.tap("Tema")
        assert checked(phone, "Escuro")
        phone.tap("Claro")
        until(lambda: not background_is_dark(phone), "Claro aplica a paleta clara")
        phone.sh("cmd", "uimode", "night", "yes")
        phone.tap("Tema")
        assert checked(phone, "Claro")
        phone.tap("Cancelar")
        assert not background_is_dark(phone)
        choose(phone, "Sistema")
        until(lambda: background_is_dark(phone), "Sistema acompanha o tema escuro atual")
        phone.tap("Lista")
        phone.tap("Adicionar site", scroll=True)
        phone.type_in("exemplo.com", "tema.example")
        phone.sh("cmd", "uimode", "night", "no")
        phone.check_visible("tema.example")
        phone.tap("Adicionar")
        phone.check_visible("tema.example")
        until(lambda: not background_is_dark(phone), "Sistema atualiza a Lista e preserva o domínio digitado")
    finally:
        phone.sh("cmd", "uimode", "night", original)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, default=Path(os.environ["LOCALAPPDATA"]) / "Android/Sdk/platform-tools/adb.exe")
    parser.add_argument("--serial", default="emulator-5584")
    args = parser.parse_args()
    run(Phone(args.adb, args.serial))
