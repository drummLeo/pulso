"""Teste de interface no AVD descartável do Pulso. Recusa aparelhos físicos."""
import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import re
import subprocess
import threading
import time
import xml.etree.ElementTree as ET

PKG = "br.local.pulso"
COMPONENT = PKG + "/.MainActivity"
SERVICE = PKG + "/br.local.pulso.FocusService"
CHROME = "com.android.chrome"


class Page(BaseHTTPRequestHandler):
    def do_GET(self):
        content = b"<html><title>Teste Pulso</title><h1>Pagina de teste local</h1></html>"
        self.send_response(200)
        self.send_header("Content-Type", "text/html")
        self.send_header("Content-Length", str(len(content)))
        self.end_headers()
        self.wfile.write(content)

    def log_message(self, *_):
        pass


class Phone:
    def __init__(self, adb, serial):
        if not serial.startswith("emulator-"):
            raise ValueError("O teste requer um emulador.")
        self.prefix = [str(adb), "-s", serial]
        if self.call("emu", "avd", "name").splitlines()[0] != "PulsoTeste17":
            raise ValueError("O teste requer o AVD descartável PulsoTeste17.")

    def call(self, *args, timeout=20):
        return subprocess.check_output(self.prefix + list(args), timeout=timeout).decode("utf-8", "replace").strip()

    def sh(self, *args):
        return self.call("shell", *args)

    def activity(self):
        text = self.sh("dumpsys", "activity", "activities")
        for line in text.splitlines():
            if "topResumedActivity=" in line or "mResumedActivity:" in line:
                return line
        return ""

    def nodes(self):
        destination = "/data/local/tmp/pulso-ui.xml"
        for _ in range(3):
            response = self.sh("uiautomator", "dump", destination)
            if "dumped" in response:
                return list(ET.fromstring(self.call("exec-out", "cat", destination)).iter("node"))
            time.sleep(.25)
        raise RuntimeError("A árvore da interface de teste ficou indisponível.")

    @staticmethod
    def label(node):
        return [node.get("text"), node.get("content-desc")]

    def matches(self, node, label):
        return any(value and value.casefold()==label.casefold() for value in self.label(node))

    def touch(self, node):
        x1, y1, x2, y2 = map(int, re.findall(r"\d+", node.get("bounds")))
        self.sh("input", "tap", str((x1+x2)//2), str((y1+y2)//2))
        time.sleep(.23)

    def tap(self, label, scroll=False):
        for i in range(5):
            nodes = self.nodes()
            matches = [n for n in nodes if self.matches(n,label)]
            if matches:
                self.touch(matches[-1])
                return
            if scroll and i < 3:
                self.sh("input","swipe","500","1730","500","730","270")
            else:
                time.sleep(.25)
        raise AssertionError("Controle ausente: " + label + " | " + str([n.get("text") for n in nodes if n.get("text")]))

    def type_in(self, hint_or_value, value, replace=False):
        nodes = self.nodes()
        match = next((n for n in nodes if n.get("class") == "android.widget.EditText" and hint_or_value in self.label(n)), None)
        if match is None:
            match = next(n for n in nodes if n.get("class") == "android.widget.EditText")
        self.touch(match)
        if replace:
            self.sh("input","keycombination","113","29")
        self.sh("input","text",value)
        self.sh("input","keyevent","4")

    def home(self):
        self.sh("am","start","-W","--activity-clear-top","-n",COMPONENT)
        time.sleep(.4)

    def check_visible(self, label):
        if not any(self.matches(n,label) for n in self.nodes()):
            raise AssertionError("Texto ausente: " + label)

    def open_url(self, url):
        self.sh("am","start","-a","android.intent.action.VIEW","-d",url,"-p",CHROME)
        time.sleep(.5)

    def screenshot(self,path):
        path.write_bytes(subprocess.check_output(self.prefix+["exec-out","screencap","-p"],timeout=12))

    def cover(self):
        return any("Window #" in l and "Site em pausa" in l for l in self.sh("dumpsys","window","windows").splitlines())

    def dismiss_chrome_prompt(self, nodes):
        labels = ("No thanks", "Agora não", "Não, obrigado", "Stay signed out", "Use without an account",
                  "Usar sem uma conta", "Use Chrome without an account", "Got it", "Entendi")
        action = next((n for label in labels for n in nodes if self.matches(n,label)),None)
        if action is not None:
            self.touch(action)
            return True
        return False


def until(fn, label, timeout=10):
    end=time.monotonic()+timeout
    while time.monotonic()<end:
        if fn():
            print("OK ",label,flush=True)
            return
        time.sleep(.3)
    raise AssertionError(label)


def toggle_chrome(phone):
    if not any(phone.matches(node, "Chrome") for node in phone.nodes()):
        phone.type_in("Buscar aplicativo", CHROME)
    phone.tap("Chrome")


def test_editor(p, artifacts):
    assert p.sh("pm","clear",PKG) == "Success"
    p.sh("input","keyevent","KEYCODE_WAKEUP")
    p.sh("wm","dismiss-keyguard")
    p.home(); p.check_visible("Iniciar sessão")
    p.screenshot(artifacts/"inicio.png")
    print("OK  APK assinado abre no Android 17",flush=True)
    p.tap("Lista");p.tap("Escolher aplicativos")
    toggle_chrome(p);p.tap("Salvar lista")
    p.tap("Adicionar site");p.type_in("exemplo.com","invalido");p.tap("Adicionar")
    p.check_visible("Adicionar site")
    p.type_in("invalido","127.0.0.1",replace=True);p.tap("Adicionar")
    p.check_visible("127.0.0.1")
    p.tap("Agenda");p.tap("Novo agendamento")
    p.type_in("Nome da sessão","Leitura");p.tap("Salvar")
    p.check_visible("Leitura");p.tap("Ativar Leitura");p.check_visible("Agendamento desativado")
    p.tap("Editar");p.type_in("Leitura","Estudo",replace=True);p.tap("Salvar");p.check_visible("Estudo")
    p.tap("Excluir");p.tap("Excluir");p.check_visible("Nenhum agendamento")
    print("OK  Lista, validação de domínio e ciclo completo de agendamento",flush=True)
    p.tap("Foco");p.tap("Outro");p.type_in("25","18",replace=True);p.tap("Usar duração")
    p.check_visible("18 min")
    p.tap("Configurar acessibilidade",scroll=True);p.tap("Abrir Acessibilidade")
    until(lambda:"com.android.settings/" in p.activity(),"O próprio app abre as configurações de Acessibilidade")
    p.sh("input","keyevent","KEYCODE_BACK")


def test_blocking(p, artifacts,web_only=False):
    original = p.sh("settings","get","secure","enabled_accessibility_services")
    existing = [] if original in ("null","") else original.split(":")
    p.sh("settings","put","secure","enabled_accessibility_services",":".join(dict.fromkeys(existing+[SERVICE])))
    time.sleep(1.1)
    try:
        if not web_only:
            p.home();p.tap("Foco");p.tap("Iniciar sessão")
            p.open_url("http://127.0.0.1/")
            until(lambda: any(x in p.activity() for x in ("launcher","nexuslauncher","quickstep")), "Abrir um app bloqueado retorna ao launcher")
            p.home();p.tap("Foco");p.tap("Encerrar sessão");p.tap("Encerrar sessão")
            p.tap("Histórico");p.check_visible("Foco rápido");p.check_visible("Encerrada antes do fim")
            p.tap("Lista");p.tap("Escolher aplicativos");toggle_chrome(p);p.tap("Salvar lista")
            print("OK  Encerramento manual, liberação do app e histórico",flush=True)
        flags=artifacts/"chrome-command-line"
        flags.write_text("chrome --disable-fre --no-first-run --disable-background-networking --metrics-recording-only\n",encoding="ascii")
        p.call("push",str(flags),"/data/local/tmp/chrome-command-line")
        p.sh("am","set-debug-app","--persistent",CHROME)
        p.sh("am","force-stop",CHROME)
        with ThreadingHTTPServer(("127.0.0.1",0),Page) as http:
            threading.Thread(target=http.serve_forever,daemon=True).start()
            port=str(http.server_port)
            p.call("reverse","tcp:"+port,"tcp:"+port)
            try:
                allowed="http://localhost:"+port+"/livre"
                blocked="http://127.0.0.1:"+port+"/pausa"
                p.open_url(allowed)
                ready = 0
                for _ in range(8):
                    nodes=p.nodes()
                    if p.dismiss_chrome_prompt(nodes):
                        ready = 0
                    elif any(n.get("resource-id","").endswith(":id/url_bar") for n in nodes):
                        ready += 1
                        if ready == 2: break
                    time.sleep(.5)
                else: raise AssertionError("Chrome não apresentou o endereço da página local.")
                p.home();p.tap("Foco")
                if any(p.matches(n,"Encerrar sessão") for n in p.nodes()):
                    p.tap("Encerrar sessão");p.tap("Encerrar sessão")
                p.tap("Iniciar sessão")
                p.open_url(blocked)
                try:
                    until(p.cover,"Página da lista recebe a cobertura no Chrome real")
                except AssertionError:
                    if p.dismiss_chrome_prompt(p.nodes()):
                        until(p.cover,"Página da lista recebe a cobertura depois do aviso do navegador")
                    else:
                        p.screenshot(artifacts/"falha-site.png")
                        (artifacts/"falha-janelas.txt").write_text(p.sh("dumpsys","window","windows"),encoding="utf-8")
                        print([(n.get("resource-id"),n.get("text"),n.get("focused")) for n in p.nodes() if n.get("text")],flush=True)
                        raise
                p.screenshot(artifacts/"site-em-pausa.png")
                p.open_url(allowed)
                until(lambda: not p.cover() and CHROME in p.activity(),"Outro endereço permanece acessível na mesma sessão")
                p.open_url(blocked);until(p.cover,"Regra reaplicada a uma nova navegação")
                p.home();p.tap("Foco");p.tap("Encerrar sessão");p.tap("Encerrar sessão")
                p.open_url(blocked)
                until(lambda: not p.cover() and CHROME in p.activity(),"Domínio liberado ao encerrar")
            finally:
                p.call("reverse","--remove","tcp:"+port)
                http.shutdown()
        p.sh("am","force-stop",PKG);p.home();p.tap("Histórico");p.check_visible("Foco rápido")
        print("OK  Histórico preservado depois de reabrir o processo",flush=True)
    finally:
        p.sh("am","clear-debug-app")
        p.sh("rm","-f","/data/local/tmp/chrome-command-line")
        if original in ("null",""): p.sh("settings","delete","secure","enabled_accessibility_services")
        else: p.sh("settings","put","secure","enabled_accessibility_services",original)


if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--adb",type=Path,default=Path(os.environ["LOCALAPPDATA"])/"Android/Sdk/platform-tools/adb.exe")
    parser.add_argument("--serial",default="emulator-5584")
    parser.add_argument("--artifacts",type=Path,required=True)
    parser.add_argument("--etapa",choices=["tudo","bloqueio","sites"],default="tudo",help="Retoma uma etapa no mesmo AVD já preparado.")
    args=parser.parse_args()
    args.artifacts.mkdir(exist_ok=True,parents=True)
    phone=Phone(args.adb,args.serial)
    apk=Path(__file__).resolve().parents[1]/"Pulso-Galaxy.apk"
    assert phone.call("install","-r",str(apk)).endswith("Success")
    if args.etapa=="tudo":
        test_editor(phone,args.artifacts)
    test_blocking(phone,args.artifacts,args.etapa=="sites")
