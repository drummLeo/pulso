"""Exercita busca, seleção por categoria e cotas no AVD descartável PulsoTeste17 com o APK instalado."""

import argparse
import os
from pathlib import Path
import re
import unicodedata

from test_android import PKG, Phone, until


def text_query(value):
    return "".join(c for c in unicodedata.normalize("NFD", value) if not unicodedata.combining(c)).lower()


def search(phone, value):
    field = next(n for n in phone.nodes() if n.get("class") == "android.widget.EditText")
    phone.touch(field)
    phone.sh("input", "keycombination", "113", "29")
    phone.sh("input", "text", text_query(value).replace(" ", "%s"))
    if "mInputShown=true" in phone.sh("dumpsys", "input_method"):
        phone.sh("input", "keyevent", "KEYCODE_BACK")


def choices(phone):
    return {n.get("text"): n.get("checked") == "true"
            for n in phone.nodes() if n.get("class") == "android.widget.CheckedTextView"}


def pair(phone):
    # As imagens AOSP e Google Play incluem aplicativos diferentes.
    for category in ("Ferramentas", "Outros", "Trabalho e estudo", "Comunicação", "Fotos e design"):
        search(phone, category)
        names = [name for name in choices(phone) if re.fullmatch(r"[a-z0-9][a-z0-9 ._-]*", text_query(name))]
        for first in names:
            for second in names:
                if text_query(first) not in text_query(second) and text_query(second) not in text_query(first):
                    return category, first, second
    raise AssertionError("A imagem de teste precisa de dois aplicativos da mesma categoria com nomes distintos.")


def tap_category(phone, label):
    for _ in range(20):
        nodes = phone.nodes()
        action = next((n for n in nodes if phone.matches(n, label)), None)
        if action is not None:
            phone.touch(action)
            return
        listing = next(n for n in nodes if n.get("class") == "android.widget.ListView")
        left, top, right, bottom = map(int, re.findall(r"\d+", listing.get("bounds")))
        x = str((left + right) // 2)
        phone.sh("input", "swipe", x, str(bottom - 16), x, str(top + (bottom - top) // 3), "340")
    raise AssertionError("Cabeçalho ausente no catálogo: " + label)


def run(phone):
    original = phone.sh("cmd", "uimode", "night").split(":")[-1].strip()
    try:
        assert phone.sh("pm", "clear", PKG) == "Success"
        phone.sh("input", "keyevent", "KEYCODE_WAKEUP")
        phone.sh("wm", "dismiss-keyguard")
        phone.sh("cmd", "uimode", "night", "no")
        phone.home()
        phone.tap("Lista")
        phone.tap("Escolher aplicativos")
        search(phone, PKG)
        phone.check_visible("Nenhum aplicativo encontrado.")
        phone.tap("Limpar busca")
        category, first, second = pair(phone)
        phone.tap(second)
        phone.tap("Salvar lista")
        phone.tap("Limite de " + second, scroll=True)
        phone.type_in("1 a 60", "17")
        phone.tap("Salvar limites")
        phone.check_visible(category + " · 1 app")

        phone.tap("Escolher aplicativos")
        tap_category(phone, "Marcar categoria " + category)
        search(phone, second)
        assert choices(phone)[second], "Marcar a categoria inteira inclui o app da busca."
        phone.tap("Desmarcar resultados de " + category)
        phone.sh("cmd", "uimode", "night", "yes")
        def restored():
            nodes = phone.nodes()
            return (any(n.get("class") == "android.widget.EditText" and text_query(n.get("text")) == text_query(second)
                        for n in nodes)
                    and any(n.get("class") == "android.widget.CheckedTextView" and n.get("text") == second
                            and n.get("checked") == "false" for n in nodes))
        until(restored, "O filtro e a desmarcação sobrevivem à troca de tema.")
        phone.tap("Limpar busca")
        search(phone, category)
        marked = choices(phone)
        assert marked[first] and not marked[second], "A desmarcação filtrada alterou outro app do mesmo grupo."
        phone.tap("Cancelar")
        phone.check_visible(category + " · 1 app")
        phone.tap("Limite de " + second)
        phone.check_visible("17")
        phone.tap("Cancelar")

        phone.tap("Escolher aplicativos")
        search(phone, category)
        marked = choices(phone)
        assert not marked[first] and marked[second], "Cancelar deve restaurar toda a seleção salva."
        search(phone, first)
        phone.tap(first)
        phone.tap("Salvar lista")
        phone.check_visible(category + " · 2 apps")
        phone.tap("Limite de " + second)
        phone.check_visible("17")
        phone.tap("Cancelar")
        print("OK  Seleção individual e por categoria, busca, limpeza, troca de tema, cancelamento e cota preservada.")
    finally:
        phone.sh("cmd", "uimode", "night", original)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, default=Path(os.environ["LOCALAPPDATA"]) / "Android/Sdk/platform-tools/adb.exe")
    parser.add_argument("--serial", default="emulator-5584")
    args = parser.parse_args()
    run(Phone(args.adb, args.serial))
