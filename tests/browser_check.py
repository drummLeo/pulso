"""Exercita a extensão empacotada, o servidor real do Pulso e sites HTTP locais."""
import argparse
import contextlib
import http.server
import os
import pathlib
import socket
import subprocess
import tempfile
import threading
import time
from urllib.parse import urlsplit
from playwright.sync_api import sync_playwright


class Page(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        body = b"<!doctype html><title>Pagina local</title><h1>Conteudo de teste</h1>"
        self.send_response(200)
        self.send_header("Content-Type", "text/html")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_):
        pass


def wait_for(check, label, page, timeout=12):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        if check():
            print("OK ", label, flush=True)
            return
        page.wait_for_timeout(120)
    raise AssertionError(label)


def readable_theme(page, mode):
    return page.evaluate("""mode => {
        const body = document.body, message = document.querySelector('p');
        if (!body || !message) return false;
        const css = el => getComputedStyle(el);
        function luminance(color) {
            const rgb = color.match(/[\\d.]+/g).slice(0, 3).map(n => Number(n) / 255)
                .map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4);
            return .2126 * rgb[0] + .7152 * rgb[1] + .0722 * rgb[2];
        }
        const root = document.documentElement;
        const surface = body.classList.contains('popup') ? body : root;
        const bg = luminance(css(surface).backgroundColor), fg = luminance(css(message).color);
        return css(root).colorScheme === mode && (mode === 'dark' ? bg < .2 : bg > .7)
            && (Math.max(bg,fg)+.05) / (Math.min(bg,fg)+.05) >= 4.5;
    }""", mode)


def run(fixture, executable, load_via_protocol=False):
    with tempfile.TemporaryDirectory(prefix="pulso-chromium-") as root, contextlib.ExitStack() as cleanup:
        # No redirects, extensions, data or websites from the user's browser profile are used.
        with socket.socket() as reservation:
            reservation.bind(("127.0.0.1", 0))
            local_port = reservation.getsockname()[1]
        proc = subprocess.Popen([fixture, "navegador", str(pathlib.Path(root) / "dados"), str(local_port)],
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8")
        def stop():
            if proc.poll() is None:
                proc.communicate("sair\n", timeout=8)
        cleanup.callback(stop)
        extension = proc.stdout.readline().strip()
        assert pathlib.Path(extension, "manifest.json").is_file(), extension
        def command(value):
            proc.stdin.write(value + "\n")
            proc.stdin.flush()
            return proc.stdout.readline().strip()
        with http.server.ThreadingHTTPServer(("127.0.0.1", 0), Page) as srv:
            threading.Thread(target=srv.serve_forever, daemon=True).start()
            try:
                with sync_playwright() as p:
                    options = {"executable_path": executable} if executable else {"channel": "chromium"}
                    loading = ["--enable-unsafe-extension-debugging"] if load_via_protocol else [
                        "--disable-extensions-except=" + extension, "--load-extension=" + extension]
                    with p.chromium.launch_persistent_context(str(pathlib.Path(root) / "perfil"), headless=True,
                            ignore_default_args=["--disable-extensions"],
                            args=["--no-proxy-server", "--disable-background-networking",
                                  "--host-resolver-rules=MAP *.pulso.test 127.0.0.1, MAP pulso.test 127.0.0.1, MAP liberado.test 127.0.0.1, MAP fimpulso.test 127.0.0.1",
                                  *loading], **options) as ctx:
                        page = ctx.new_page()
                        if load_via_protocol:
                            session = ctx.browser.new_browser_cdp_session() if ctx.browser else ctx.new_cdp_session(page)
                            session.send("Extensions.loadUnpacked", {"path": extension})
                        port = srv.server_port
                        pending = f"http://sub.pulso.test:{port}/antes"
                        page.goto(pending)
                        wait_for(lambda: command("estado") in {"Chrome", "Edge"}, "Extensão empacotada conectou ao Pulso", page)
                        print("Navegador:", command("estado"), flush=True)
                        assert page.title() == "Pagina local"
                        popup = ctx.new_page()
                        popup.emulate_media(color_scheme="dark")
                        popup.goto(ctx.service_workers[0].url.rsplit("/", 1)[0] + "/status.html")
                        wait_for(lambda: readable_theme(popup, "dark"), "Tema Sistema segue o navegador escuro no painel da extensão", popup)
                        popup.emulate_media(color_scheme="light")
                        wait_for(lambda: readable_theme(popup, "light"), "Painel aberto acompanha mudança do sistema para Claro", popup)
                        popup.emulate_media(color_scheme="dark")
                        wait_for(lambda: readable_theme(popup, "dark"), "Painel aberto acompanha mudança do sistema para Escuro", popup)
                        assert command("tema light") == "ok"
                        wait_for(lambda: readable_theme(popup, "light"), "Tema Claro do PC prevalece no painel já aberto", popup)
                        page.emulate_media(color_scheme="light")
                        assert command("tema dark") == "ok"
                        wait_for(lambda: readable_theme(popup, "dark"), "Painel acompanha a troca para Escuro", popup)
                        popup.close()
                        page.bring_to_front()
                        unauthorized = ctx.request.get(f"http://127.0.0.1:{local_port}/foco")
                        assert unauthorized.status == 403
                        unauthorized.dispose()
                        print("OK  Acesso HTTP comum não recebe a lista de foco", flush=True)
                        command("iniciar")
                        wait_for(lambda: page.url.startswith("chrome-extension:") and page.title().startswith("Site em pausa"), "Aba já aberta entra em pausa", page)
                        wait_for(lambda: readable_theme(page, "dark"), "Tela de pausa usa o tema Escuro do PC", page)
                        assert command("tema system") == "ok"
                        wait_for(lambda: readable_theme(page, "light"), "Trocar o tema mantém a pausa ativa", page)
                        assert page.locator("h1").inner_text() == "Este site está em pausa."
                        tab = ctx.new_page()
                        tab.goto(f"http://pulso.test:{port}/nova")
                        assert urlsplit(tab.url).path == "/pausa.html", tab.url
                        print("OK  Nova navegação ao domínio é bloqueada", flush=True)
                        tab.goto(f"http://outro.pulso.test:{port}/sub")
                        assert urlsplit(tab.url).path == "/pausa.html"
                        for host in ["liberado.test", "fimpulso.test"]:
                            tab.goto(f"http://{host}:{port}/")
                            assert tab.title() == "Pagina local"
                        print("OK  Subdomínios entram na regra e domínios parecidos ficam acessíveis", flush=True)
                        command("parar")
                        wait_for(lambda: page.locator("h1").inner_text() == "A pausa terminou.", "Fim manual libera a navegação", page)
                        page.get_by_role("button", name="Voltar").click()
                        wait_for(lambda: page.url == pending and page.title() == "Pagina local", "A aba anterior pode ser retomada", page)
                        command("medir")
                        page.bring_to_front()
                        wait_for(lambda: command("aba") == "sub.pulso.test", "Limites identificam o domínio mais específico da aba ativa", page)
                        tab.bring_to_front()
                        wait_for(lambda: command("aba") == "-", "A aba listada em segundo plano não gera uso", tab)
                        tab.goto(f"http://liberado.test:{port}/outra")
                        wait_for(lambda: command("aba") == "liberado.test", "Troca de endereço muda a atribuição do uso", tab)
                        command("cotas")
                        wait_for(lambda: tab.locator("h1").inner_text() == "Limite de uso atingido.", "Site limitado mostra o motivo fora do foco", tab)
                        tab.goto(f"http://liberado.test:{port}/nova?privado=teste")
                        assert urlsplit(tab.url).path == "/pausa.html" and urlsplit(tab.url).fragment == "liberado.test", tab.url
                        wait_for(lambda: command("aba") == "liberado.test", "Pausa de navegação nova mantém o alvo sem expor caminho ou consulta", tab)
                        page.bring_to_front()
                        wait_for(lambda: page.locator("h1").inner_text() == "Limite de uso atingido.", "Cota do domínio inclui seus subdomínios", page)
                        wait_for(lambda: page.locator("h1").inner_text() == "A pausa terminou.", "Liberou somente o domínio cuja cota expirou", page, 16)
                        wait_for(lambda: command("aba") == "-", "Aviso de pausa encerrada não consome o novo saldo do site", page)
                        assert tab.locator("h1").inner_text() == "Limite de uso atingido."
                        command("limpar")
                        page.get_by_role("button", name="Voltar").click()
                        wait_for(lambda: page.url == pending, "Aba das cotas retoma o endereço anterior", page)
                        command("expirar")
                        wait_for(lambda: urlsplit(page.url).path == "/pausa.html", "Nova sessão instala a regra", page)
                        wait_for(lambda: page.locator("h1").inner_text() == "A pausa terminou.", "Vencimento libera a regra automaticamente", page)
                        tab.goto(f"http://pulso.test:{port}/liberado")
                        assert tab.title() == "Pagina local"
                        command("iniciar")
                        wait_for(lambda: urlsplit(tab.url).path == "/pausa.html", "Regra reaplicada", tab)
                        assert command("tema dark") == "ok"
                        wait_for(lambda: readable_theme(tab, "dark"), "Tema Escuro está salvo na extensão", tab)
                        stop()
                        wait_for(lambda: tab.locator("h1").inner_text() == "A pausa terminou.", "Fechar o aplicativo libera o navegador", tab)
                        tab.reload()
                        wait_for(lambda: readable_theme(tab, "dark"), "Extensão mantém a aparência com o PC desconectado", tab)
                        tab.goto(f"http://pulso.test:{port}/fim")
                        assert tab.title() == "Pagina local"
            finally:
                srv.shutdown()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("fixture")
    parser.add_argument("--chromium", default=os.environ.get("PULSO_TEST_CHROMIUM"))
    parser.add_argument("--via-protocolo", action="store_true")
    args = parser.parse_args()
    run(args.fixture, args.chromium, args.via_protocolo)
