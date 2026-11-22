"""Exercita os limites pela interface do Pulso no AVD descartável PulsoTeste17."""

import argparse
from contextlib import contextmanager
from http.server import ThreadingHTTPServer
import os
from pathlib import Path
import re
import subprocess
import threading
import time
import xml.etree.ElementTree as ET

from test_android import CHROME, PKG, SERVICE, Page, Phone, test_editor as run_legacy_editor, until


SITE = "127.0.0.1"
GROUP = "Editar limite compartilhado"
LEGACY_ROUTINE = "Leitura anterior"
CLOCK = "com.google.android.deskclock"
CLOCK_ACTIVITY = CLOCK + "/com.android.deskclock.DeskClock"


def install(p, apk, version):
    assert p.call("install", "-r", str(apk)).endswith("Success")
    package = p.sh("dumpsys", "package", PKG)
    assert re.search(r"^\s*versionName=" + re.escape(version) + r"\s*$", package, re.M), version


def service_bound(p):
    # O comando uiautomator dump do Android 17 suspende os demais serviços enquanto lê a tela.
    return any("Bound services:" in line and "label=Pulso" in line
               for line in p.sh("dumpsys", "accessibility").splitlines())


def enable_service(p):
    original = p.sh("settings", "get", "secure", "enabled_accessibility_services")
    existing = [] if original in ("null", "") else original.split(":")
    if SERVICE not in existing:
        p.sh("settings", "put", "secure", "enabled_accessibility_services", ":".join([*existing, SERVICE]))
    until(lambda: service_bound(p), "Serviço de bloqueio habilitado no AVD")


def prepare_legacy(p, apk, artifacts):
    install(p, apk, "1.0")
    run_legacy_editor(p, artifacts)
    p.home()
    p.tap("Foco")
    p.tap("Iniciar sessão")
    p.tap("Encerrar sessão")
    p.tap("Encerrar sessão")
    p.tap("Histórico")
    p.check_visible("Encerrada antes do fim")
    p.tap("Agenda")
    p.tap("Novo agendamento")
    p.type_in("Nome da sessão", "Leitura%santerior")
    p.tap("Salvar")
    p.tap("Ativar " + LEGACY_ROUTINE)
    p.check_visible("Agendamento desativado")
    p.tap("Foco")
    enable_service(p)
    print("OK  1.0 preparada com duração de 18 min, Chrome, domínio, histórico, rotina desativada e serviço", flush=True)


def check_upgrade(p):
    p.home()
    p.tap("Foco")
    p.check_visible("18 min")
    p.check_visible("Iniciar sessão")
    assert SERVICE in p.sh("settings", "get", "secure", "enabled_accessibility_services").split(":")
    until(lambda: service_bound(p), "A permissão e o vínculo de Acessibilidade sobreviveram à atualização")
    p.tap("Histórico")
    p.check_visible("Foco rápido")
    p.check_visible("Encerrada antes do fim")
    p.tap("Agenda")
    p.check_visible(LEGACY_ROUTINE)
    p.check_visible("Agendamento desativado")
    p.tap("Lista")
    p.check_visible("Chrome")
    p.check_visible(SITE)
    print("OK  Dados criados na 1.0 continuam visíveis nas respectivas telas da versão atual", flush=True)


@contextmanager
def browser(p, artifacts):
    flags = artifacts / "chrome-command-line"
    flags.write_text("chrome --disable-fre --no-first-run --disable-background-networking --metrics-recording-only\n", encoding="ascii")
    p.call("push", str(flags), "/data/local/tmp/chrome-command-line")
    p.sh("am", "set-debug-app", "--persistent", CHROME)
    p.sh("am", "force-stop", CHROME)
    try:
        with ThreadingHTTPServer(("127.0.0.1", 0), Page) as http:
            threading.Thread(target=http.serve_forever, daemon=True).start()
            port = str(http.server_port)
            p.call("reverse", "tcp:" + port, "tcp:" + port)
            try:
                free = "http://localhost:" + port + "/livre"
                metered = "http://" + SITE + ":" + port + "/contado"
                p.open_url(free)
                def loaded():
                    nodes = p.nodes()
                    if p.dismiss_chrome_prompt(nodes):
                        return False
                    return any("Pagina de teste local" in p.label(n) for n in nodes)
                until(loaded, "Chrome exibe uma página servida localmente pelo teste", timeout=45)
                yield metered, free
            finally:
                p.call("reverse", "--remove", "tcp:" + port)
                http.shutdown()
    finally:
        p.sh("am", "clear-debug-app")
        p.sh("rm", "-f", "/data/local/tmp/chrome-command-line")


def open_clock(p):
    p.sh("am", "start", "-W", "-n", CLOCK_ACTIVITY)


def select_targets(p, upgraded):
    p.home()
    p.tap("Lista")
    p.tap("Escolher aplicativos", scroll=True)
    if upgraded:
        p.type_in("Buscar aplicativo", CHROME)
        p.tap("Chrome")
    p.type_in("Buscar aplicativo", CLOCK, replace=upgraded)
    choices = [n for n in p.nodes() if n.get("class") == "android.widget.CheckedTextView"]
    assert len(choices) == 1, "A busca precisa encontrar o relógio deste AVD."
    label = choices[0].get("text")
    p.touch(choices[0])
    p.tap("Salvar lista")
    if not upgraded:
        p.tap("Adicionar site", scroll=True)
        p.type_in("exemplo.com", SITE)
        p.tap("Adicionar")
    return label


def edit_limit(p, control, *, hour=None, day=None, disable=False):
    p.home()
    p.tap("Lista")
    p.tap(control, scroll=True)
    if disable:
        p.tap("Desativar limites")
        return
    for index, minutes in enumerate((hour, day)):
        if minutes is None:
            continue
        fields = [n for n in p.nodes() if n.get("class") == "android.widget.EditText"]
        assert len(fields) == 2, "O editor deve oferecer as cotas horária e diária."
        p.touch(fields[index])
        p.sh("input", "keycombination", "113", "29")
        p.sh("input", "keyevent", "KEYCODE_DEL")
        p.sh("input", "text", str(minutes))
        p.sh("input", "keyevent", "KEYCODE_BACK")
    p.tap("Salvar limites")


def read_seconds(p, period, cap):
    p.home()
    p.tap("Lista")
    pattern = re.compile(re.escape(period) + r"\s*·\s*(?:(\d+) min(?: (\d+) s)?|(\d+) s) de " + str(cap) + r" min")
    for _ in range(7):
        nodes = p.nodes()
        for node in nodes:
            for line in node.get("text", "").splitlines():
                match = pattern.fullmatch(line)
                if match:
                    return 60 * int(match[1] or 0) + int(match[2] or match[3] or 0)
        scroll = next((node for node in nodes if node.get("scrollable") == "true"), None)
        if scroll is None:
            break
        left, top, right, bottom = map(int, re.findall(r"\d+", scroll.get("bounds")))
        center = str((left + right) // 2)
        p.sh("input", "swipe", center, str(bottom - 45), center, str(top + 80), "320")
    raise AssertionError("Saldo ausente em " + period + ": " + str([n.get("text") for n in nodes if n.get("text")]))


def assert_waiting(p):
    p.home()
    p.tap("Foco")
    p.check_visible("Em espera")
    p.check_visible("Iniciar sessão")


def is_launcher(p):
    activity = p.activity()
    return "launcher" in activity or "quickstep" in activity


def assert_free_site(p, url):
    def visible():
        return not p.cover() and CHROME in p.activity() and any("Pagina de teste local" in p.label(n) for n in p.nodes())

    p.open_url(url)
    try:
        until(visible, "Conteúdo local acessível no Chrome")
    except AssertionError:
        # O Chrome x86_64 desta imagem pode encerrar em onTrimMemory entre duas atividades.
        try:
            p.sh("pidof", CHROME)
        except subprocess.CalledProcessError as error:
            if error.returncode != 1:
                raise
        else:
            raise
        p.open_url(url)
        until(visible, "Página livre após o Android reiniciar o processo do Chrome", timeout=20)
    until(lambda: service_bound(p), "Serviço ativo depois de consultar a árvore da página")
    time.sleep(1)
    assert not p.cover(), "O domínio livre recebeu a cobertura depois da leitura da tela."


def run_shared(p, metered, free, artifacts):
    edit_limit(p, GROUP, day=1)
    start = read_seconds(p, "Hoje", 1)
    assert start == 0, start
    open_clock(p)
    until(lambda: CLOCK in p.activity(), "App da lista aberto com saldo")
    time.sleep(13)
    app = read_seconds(p, "Hoje", 1)
    assert 8 <= app <= 30, app
    assert_free_site(p, metered)
    time.sleep(10)
    combined = read_seconds(p, "Hoje", 1)
    assert app + 8 <= combined < 54, (app, combined)
    print(f"OK  Grupo diário na tela: {app} s após o app, {combined} s após alternar para o domínio", flush=True)
    p.sh("am", "start", "-W", "-a", "android.settings.SETTINGS")
    time.sleep(7)
    assert_free_site(p, free)
    time.sleep(7)
    paused = read_seconds(p, "Hoje", 1)
    assert abs(paused - combined) <= 1, (combined, paused)
    print(f"OK  Saldo do grupo em segundo plano e em outro domínio: {combined} s → {paused} s", flush=True)
    p.open_url(metered)
    until(p.cover, "A soma alternada esgotou o minuto compartilhado e cobriu o domínio", timeout=75 - combined)
    p.screenshot(artifacts / "limite-compartilhado-site.png")
    assert read_seconds(p, "Hoje", 1) == 60
    open_clock(p)
    until(lambda: is_launcher(p), "O mesmo limite compartilhado bloqueia o outro item, um app")
    assert_free_site(p, free)
    assert_waiting(p)
    edit_limit(p, GROUP, day=2)
    assert read_seconds(p, "Hoje", 2) == 60
    assert_free_site(p, metered)
    open_clock(p)
    time.sleep(2)
    assert CLOCK in p.activity()
    edit_limit(p, GROUP, disable=True)
    print("OK  Aumento da cota libera site e app e mantém o minuto consumido", flush=True)


def run_individual(p, app_label, metered, free, artifacts):
    app_control = "Limite de " + app_label
    site_control = "Limite de " + SITE
    # Evita atravessar a troca da hora durante a medição de uma cota horária real.
    minute, second = map(int, p.sh("date", "+%M:%S").split(":"))
    until_hour = 3600 - minute * 60 - second
    if until_hour < 140:
        assert_waiting(p)
        time.sleep(until_hour + 1)
    edit_limit(p, app_control, hour=1)
    edit_limit(p, site_control, day=1)
    assert read_seconds(p, "Nesta hora", 1) == 0
    assert read_seconds(p, "Hoje", 1) == 0
    charged_hour = p.sh("date", "+%Y%m%d%H%z")
    began = time.monotonic()
    open_clock(p)
    until(lambda: CLOCK in p.activity(), "Relógio inicia com cota individual horária")
    until(lambda: is_launcher(p), "O limite individual de um minuto devolve o Relógio ao launcher", timeout=78)
    elapsed = time.monotonic() - began
    assert 53 <= elapsed <= 82, elapsed
    assert read_seconds(p, "Nesta hora", 1) == 60
    assert read_seconds(p, "Hoje", 1) == 0
    print(f"OK  Relógio bloqueado em {elapsed:.1f} s e domínio independente permaneceu em 0 s", flush=True)
    assert_free_site(p, metered)
    time.sleep(8)
    spent = read_seconds(p, "Hoje", 1)
    assert 6 <= spent < 35, spent
    p.open_url(metered)
    until(p.cover, "O domínio atingiu seu próprio minuto diário e recebeu a cobertura", timeout=78 - spent)
    p.screenshot(artifacts / "limite-individual-site.png")
    assert read_seconds(p, "Hoje", 1) == 60
    assert_free_site(p, free)
    edit_limit(p, app_control, hour=2)
    before = p.sh("date", "+%Y%m%d%H%z")
    seconds = read_seconds(p, "Nesta hora", 2)
    after = p.sh("date", "+%Y%m%d%H%z")
    # O minuto anterior é mantido ao editar e volta a zero se o relógio virou a hora.
    assert seconds in {60 if t == charged_hour else 0 for t in (before, after)}, (charged_hour, before, after, seconds)
    open_clock(p)
    time.sleep(2)
    assert CLOCK in p.activity()
    p.open_url(metered)
    until(p.cover, "Editar só o app mantém o bloqueio individual do domínio")
    edit_limit(p, site_control, disable=True)
    assert_free_site(p, metered)
    assert_waiting(p)
    print("OK  Liberados pelo editor nas telas com o foco em espera", flush=True)


def run_limits(p, artifacts, upgraded):
    if upgraded:
        check_upgrade(p)
    else:
        assert p.sh("pm", "clear", PKG) == "Success"
        p.sh("input", "keyevent", "KEYCODE_WAKEUP")
        p.sh("wm", "dismiss-keyguard")
        p.home()
        enable_service(p)
    app_label = select_targets(p, upgraded)
    open_clock(p)
    until(lambda: CLOCK in p.activity(), "Relógio disponível no Android 17")
    assert_waiting(p)
    with browser(p, artifacts) as (metered, free):
        run_shared(p, metered, free, artifacts)
        run_individual(p, app_label, metered, free, artifacts)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, default=Path(os.environ["LOCALAPPDATA"]) / "Android/Sdk/platform-tools/adb.exe")
    parser.add_argument("--serial", default="emulator-5584")
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--preparar-v1", type=Path, help="Instala esta cópia 1.0 e deixa dados de migração no AVD.")
    parser.add_argument("--verificar-atualizacao", action="store_true", help="Verifica dados deixados por --preparar-v1.")
    args = parser.parse_args()
    args.artifacts.mkdir(parents=True, exist_ok=True)
    phone = Phone(args.adb, args.serial)
    if args.preparar_v1:
        prepare_legacy(phone, args.preparar_v1, args.artifacts)
    else:
        android = Path(__file__).resolve().parents[1]
        version = ET.parse(android / "AndroidManifest.xml").getroot().attrib["{http://schemas.android.com/apk/res/android}versionName"]
        install(phone, android / "Pulso-Galaxy.apk", version)
        run_limits(phone, args.artifacts, args.verificar_atualizacao)
