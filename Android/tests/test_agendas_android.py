"""Agendamento por uso e aviso de aplicativo no AVD descartável PulsoTeste17."""

import argparse
from contextlib import contextmanager
import os
from pathlib import Path
import re
import time

from test_android import PKG, Phone, until
from test_limites_android import CLOCK, enable_service, open_clock, select_targets


def app_notice(phone):
    windows = phone.sh("dumpsys", "window", "windows")
    return any("Window #" in line and "Pulso · Aplicativo em pausa" in line for line in windows.splitlines())


def acknowledge_notice(phone):
    # A UIAutomator dump temporarily suspends the accessibility service on Android 17.
    # Locate its real window and tap the center of the 48 dp button above its 12 dp bottom padding.
    windows = phone.sh("dumpsys", "window", "-a", "windows")
    section = next(part for part in re.split(r"\n\s*Window #\d+ ", windows)
                   if "Pulso · Aplicativo em pausa" in part.split("\n", 1)[0])
    frame = re.search(r"\bframe=\[(\d+),(\d+)\]\[(\d+),(\d+)\]", section)
    assert frame, "O Android precisa informar a posição do aviso."
    left, _, right, bottom = map(int, frame.groups())
    density = int(re.findall(r"density:\s*(\d+)", phone.sh("wm", "density"))[-1])
    phone.sh("input", "tap", str((left + right) // 2), str(bottom - round(36 * density / 160)))
    until(lambda: not app_notice(phone), "Entendi fecha o aviso")


@contextmanager
def daytime(phone):
    # Keep the default 09:00 schedule active regardless of the host time.
    original = phone.sh("getprop", "persist.sys.timezone")
    auto = phone.sh("settings", "get", "global", "auto_time_zone")
    utc_hour = int(phone.sh("date", "-u", "+%H"))
    offset = (12 - utc_hour + 12) % 24 - 12
    zone = "Etc/UTC" if offset == 0 else f"Etc/GMT{-offset:+d}"
    try:
        phone.sh("settings", "put", "global", "auto_time_zone", "0")
        phone.sh("cmd", "alarm", "set-timezone", zone)
        assert phone.sh("getprop", "persist.sys.timezone") == zone
        yield
    finally:
        if original:
            phone.sh("cmd", "alarm", "set-timezone", original)
        if auto == "null":
            phone.sh("settings", "delete", "global", "auto_time_zone")
        else:
            phone.sh("settings", "put", "global", "auto_time_zone", auto)


def create_allowance(phone, name, minutes):
    phone.tap("Novo agendamento")
    phone.type_in("Nome da sessão", name)
    phone.type_in("25", "720", replace=True)
    # The test window includes weekends as well as the default weekdays.
    weekday = int(phone.sh("date", "+%u"))
    if weekday in (6, 7):
        phone.tap("Sáb" if weekday == 6 else "Dom", scroll=True)
    phone.tap("Limitar minutos por hora", scroll=True)
    phone.type_in("1 a 60", str(minutes), replace=True)
    phone.tap("Salvar")
    phone.check_visible(name)


def run(phone, artifacts):
    assert phone.sh("pm", "clear", PKG) == "Success"
    phone.sh("input", "keyevent", "KEYCODE_WAKEUP")
    phone.sh("wm", "dismiss-keyguard")
    phone.home()
    enable_service(phone)
    select_targets(phone, upgraded=False)
    with daytime(phone):
        phone.home()
        phone.tap("Lista")
        phone.tap("Limites por agendamento", scroll=True)
        create_allowance(phone, "Trabalho", 1)
        create_allowance(phone, "Noite", 5)
        phone.tap("Ativar Noite", scroll=True)
        phone.tap("Foco")
        phone.check_visible("Iniciar sessão")
        if int(phone.sh("date", "+%M")) >= 58:
            until(lambda: int(phone.sh("date", "+%M")) < 58, "Início de uma hora completa de teste", timeout=125)
        open_clock(phone)
        time.sleep(3)
        assert CLOCK in phone.activity() and not app_notice(phone), "A agenda deve permitir uso enquanto há saldo."
        until(lambda: app_notice(phone), "Aviso ao esgotar o saldo compartilhado da agenda", timeout=80)
        phone.screenshot(artifacts / "aviso-app-agendado.png")
        assert CLOCK not in phone.activity(), "O app em pausa deve continuar protegido."
        acknowledge_notice(phone)
        open_clock(phone)
        until(lambda: app_notice(phone), "Reabrir o app em pausa volta a mostrar o aviso")
        phone.home()
        phone.tap("Agenda")
        phone.tap("Ativar Trabalho", scroll=True)
        phone.tap("Ativar Noite", scroll=True)
        open_clock(phone)
        time.sleep(4)
        assert CLOCK in phone.activity() and not app_notice(phone), "O segundo agendamento deve ter saldo próprio."
    print("OK  Agenda permite uso, avisa ao esgotar e preserva o saldo de outro agendamento.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, default=Path(os.environ["LOCALAPPDATA"]) / "Android/Sdk/platform-tools/adb.exe")
    parser.add_argument("--serial", default="emulator-5584")
    parser.add_argument("--artifacts", type=Path, required=True)
    args = parser.parse_args()
    args.artifacts.mkdir(parents=True, exist_ok=True)
    run(Phone(args.adb, args.serial), args.artifacts)
