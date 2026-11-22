"""Exercita a extensão empacotada, o servidor real do Pulso e sites HTTP locais."""
import argparse
import contextlib
import ctypes
import http.server
import os
import pathlib
import re
import socket
import subprocess
import tempfile
import threading
import time
from urllib.parse import urlsplit
from playwright.sync_api import sync_playwright


class Page(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        self.server.paths.add(urlsplit(self.path).path)
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


def check_categories(ctx, command, srv, extension_url):
    # Only this private copy of the packaged lists contains the .test fixtures.
    adult_host = "sub.adulto-pulso.test"
    gaming_host = "loja.exemplo-pulso.bet.br"
    adult_url = f"http://{adult_host}:{srv.server_port}/retomar?teste=local"
    def url(host, path):
        return f"http://{host}:{srv.server_port}/{path}"

    def paused(page):
        return urlsplit(page.url).path == "/pausa.html"

    def active(page, category):
        return paused(page) and category in page.locator("#detail").inner_text()

    def released(page):
        return page.locator("h1").inner_text() == "A pausa terminou."

    adult = ctx.new_page()
    gaming = ctx.new_page()
    other = ctx.new_page()
    panel = ctx.new_page()
    try:
        panel.goto(extension_url + "/status.html")
        adult.goto(adult_url)
        gaming.goto(url(gaming_host, "antes-apostas"))
        assert adult.title() == gaming.title() == "Pagina local"
        assert command("filtro porn always") == "ok"
        wait_for(lambda: active(adult, "Pornografia"), "Filtro Sempre pausa uma aba de pornografia já aberta", adult)
        assert not any(c.isdigit() for c in adult.locator("#detail").inner_text())
        assert gaming.title() == "Pagina local"
        wait_for(lambda: "Pornografia" in panel.locator("#status").inner_text(), "Painel identifica a categoria ativa", panel)
        adult.bring_to_front()
        wait_for(lambda: command("aviso-categoria") == "sim", "Aba ativa em pausa informa o aviso sem site cadastrado", adult)
        assert command("aba") == "-"
        gaming.bring_to_front()
        wait_for(lambda: command("aviso-categoria") == "nao", "Aviso em segundo plano libera a contagem do navegador", gaming)
        other.goto(url("adulto-pulso.test", "rede-proibida"))
        assert paused(other) and "/rede-proibida" not in srv.paths
        for allowed in ["naoadulto-pulso.test", "adulto-pulso.test.liberado.test", "liberado.test"]:
            other.goto(url(allowed, "adulto-pulso.test/apostas/pornografia"))
            assert other.title() == "Pagina local"
        other.evaluate("""url => new Promise(done => {
            const frame = document.createElement("iframe");
            frame.onload = frame.onerror = () => done();
            frame.src = url;
            document.body.append(frame);
        })""", url(adult_host, "quadro-proibido"))
        assert "/quadro-proibido" not in srv.paths

        assert command("filtro gambling focus") == "ok"
        gaming.reload()
        assert gaming.title() == "Pagina local"
        assert command("iniciar") == "ok"
        wait_for(lambda: active(gaming, "Apostas"), "Foco pausa subdomínios da categoria Apostas", gaming)
        assert any(c.isdigit() for c in gaming.locator("#detail").inner_text())
        assert active(adult, "Pornografia")
        compiled = ctx.service_workers[0].evaluate("""async () => {
            const api = chrome.declarativeNetRequest, rules = await api.getSessionRules();
            return [rules.length, rules.filter(r => r.condition.regexFilter).length,
                api.MAX_NUMBER_OF_SESSION_RULES, api.MAX_NUMBER_OF_REGEX_RULES];
        }""")
        assert 0 < compiled[0] < compiled[2] and 0 < compiled[1] < compiled[3], compiled
        print(f"OK  Catálogos e lista manual ativos em {compiled[0]} regras, {compiled[1]} com regex", flush=True)
        assert command("filtro porn off") == "ok"
        wait_for(lambda: released(adult), "Revisão libera somente Pornografia", adult)
        adult.get_by_role("button", name="Voltar").click()
        wait_for(lambda: adult.url == adult_url and adult.title() == "Pagina local", "Voltar retoma a URL da aba liberada", adult)
        other.goto(url(gaming_host, "nova-aposta"))
        assert paused(other) and "/nova-aposta" not in srv.paths
        assert command("parar") == "ok"
        wait_for(lambda: released(gaming), "Encerrar o foco libera o filtro de Apostas", gaming)

        assert command("filtro gambling always") == "ok"
        assert command("filtro porn focus") == "ok"
        wait_for(lambda: active(gaming, "Apostas"), "Apostas em Sempre funciona fora do foco", gaming)
        assert not any(c.isdigit() for c in gaming.locator("#detail").inner_text())
        assert command("expirar") == "ok"
        wait_for(lambda: active(adult, "Pornografia"), "Pornografia configurada para o foco acompanha o início da sessão", adult)
        wait_for(lambda: released(adult), "Prazo do filtro de foco libera a aba automaticamente", adult)
        assert active(gaming, "Apostas")
        assert command("filtro gambling off") == "ok"
        assert command("filtro porn always") == "ok"
        assert command("cotas") == "ok"
        other.goto(url("sobreposto.pulso.test", "sobreposto"))
        wait_for(lambda: active(other, "Pornografia"), "Categoria Sempre cobre um domínio com cota em andamento", other)
        other.bring_to_front()
        wait_for(lambda: command("aviso-categoria") == "sim" and command("aba") == "-",
                 "Aviso de categoria também suspende a medição do site manual sobreposto", other)
        assert command("filtro porn off") == "ok"
        wait_for(lambda: other.locator("h1").inner_text() == "Limite de uso atingido.",
                 "Remover a categoria preserva a cota manual sobreposta", other)
        wait_for(lambda: command("aviso-categoria") == "nao" and command("aba") == "pulso.test",
                 "Pausa exclusivamente por cota recupera o domínio manual", other)
        assert command("limpar") == "ok"
        assert command("parar") == "ok"
        wait_for(lambda: released(other), "Limpar a cota libera o domínio sobreposto", other)
    finally:
        for page in [adult, gaming, other, panel]:
            page.close()


def reload_extension(ctx, tab):
    worker_url = ctx.service_workers[0].url
    session = ctx.new_cdp_session(tab)
    def workers():
        return {target["targetId"] for target in session.send("Target.getTargets")["targetInfos"]
                if target["type"] == "service_worker" and target["url"] == worker_url}
    previous = workers()
    assert previous
    manager = ctx.new_page()
    try:
        manager.goto("chrome://extensions")
        # Unpacked extensions require developer mode on a normal reload, even
        # when the test initially loaded them with the DevTools protocol.
        toggle = manager.locator("#devMode:visible, #dev-switch:visible").first
        if not toggle.evaluate("el => !!el.checked"):
            toggle.click()
        if manager.url.startswith("edge:"):
            card = manager.locator("edge-card").filter(has_text="Pulso")
            card.locator("fluent-button").filter(has_text=re.compile(r"\b(Recarregar|Reload)\b", re.I)).click()
        else:
            manager.locator("extensions-item#" + urlsplit(worker_url).hostname).locator("#dev-reload-button").click()
        wait_for(lambda: bool(workers() - previous), "Cartão do navegador recarregou a extensão", manager, 20)
    finally:
        manager.close()
        session.detach()
    return worker_url.rsplit("/", 1)[0]


def check_lease(ctx, tab, command, proc, srv, extension_url):
    # Suspend only the child fixture. The browser keeps its existing loopback
    # socket open, so the extension must enforce its own deadline.
    panel = ctx.new_page()
    panel.goto(extension_url + "/status.html")
    try:
        assert command("filtro gambling always") == "ok"
        wait_for(lambda: "Apostas" in panel.locator("#status").inner_text(), "Começou a vigência do filtro para testar o prazo da conexão", panel)
        start = time.monotonic()
        panel.wait_for_timeout(8500)
        child_handle = ctypes.c_void_p(int(proc._handle))
        if proc.poll() is not None or ctypes.windll.ntdll.NtSuspendProcess(child_handle) != 0:
            raise RuntimeError("Não foi possível suspender o fixture de teste.")
        try:
            # Keep the worker awake as when a user checks the popup. The real
            # clock passes the original deadline after at least two heartbeats.
            while time.monotonic() < start + 41:
                assert panel.evaluate("chrome.runtime.sendMessage('estado')")["connected"]
                panel.wait_for_timeout(950)
            tab.goto(f"http://teste-conexao.bet.br:{srv.server_port}/prazo-renovado")
            assert urlsplit(tab.url).path == "/pausa.html" and "/prazo-renovado" not in srv.paths
            print("OK  Atualizações do PC renovam o prazo sem interromper Sempre", flush=True)
            wait_for(lambda: not panel.evaluate("chrome.runtime.sendMessage('estado')")["connected"]
                     and tab.locator("h1").inner_text() == "A pausa terminou.",
                     "Canal silencioso libera Sempre ao vencer o prazo local", panel, 16)
            tab.goto(f"http://teste-conexao.bet.br:{srv.server_port}/prazo-liberado")
            assert tab.title() == "Pagina local" and "/prazo-liberado" in srv.paths
        finally:
            if ctypes.windll.ntdll.NtResumeProcess(child_handle) != 0:
                raise RuntimeError("Não foi possível retomar o fixture de teste.")
        wait_for(lambda: urlsplit(tab.url).path == "/pausa.html",
                 "O mesmo filtro volta após a recuperação do canal", tab, 25)
    finally:
        panel.close()


def run(fixture, executable, load_via_protocol=False, check_timeout=False):
    with tempfile.TemporaryDirectory(prefix="pulso-chromium-") as root, contextlib.ExitStack() as cleanup:
        # No redirects, extensions, data or websites from the user's browser profile are used.
        with socket.socket() as reservation:
            reservation.bind(("127.0.0.1", 0))
            local_port = reservation.getsockname()[1]
        def start():
            return subprocess.Popen([fixture, "navegador", str(pathlib.Path(root) / "dados"), str(local_port)],
                                    stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8")
        proc = start()
        def stop():
            if proc.poll() is None:
                proc.communicate("sair\n", timeout=8)
        cleanup.callback(stop)
        extension = proc.stdout.readline().strip()
        assert pathlib.Path(extension, "manifest.json").is_file(), extension
        porn_file = pathlib.Path(extension, "site_porn.txt")
        assert "bet.br" in pathlib.Path(extension, "site_gambling.txt").read_text(encoding="ascii").splitlines()
        porn_domains = set(porn_file.read_text(encoding="ascii").splitlines())
        porn_domains.update(["adulto-pulso.test", "sobreposto.pulso.test"])
        porn_file.write_text("\n".join(sorted(porn_domains)) + "\n", encoding="ascii")
        def command(value):
            proc.stdin.write(value + "\n")
            proc.stdin.flush()
            return proc.stdout.readline().strip()
        with http.server.ThreadingHTTPServer(("127.0.0.1", 0), Page) as srv:
            srv.paths = set()
            threading.Thread(target=srv.serve_forever, daemon=True).start()
            try:
                with sync_playwright() as p:
                    options = {"executable_path": executable} if executable else {"channel": "chromium"}
                    loading = ["--enable-unsafe-extension-debugging"] if load_via_protocol else [
                        "--disable-extensions-except=" + extension, "--load-extension=" + extension]
                    with p.chromium.launch_persistent_context(str(pathlib.Path(root) / "perfil"), headless=True,
                            ignore_default_args=["--disable-extensions"],
                            args=["--no-proxy-server", "--disable-background-networking",
                                  "--host-resolver-rules=MAP * 127.0.0.1",
                                  *loading], **options) as ctx:
                        page = ctx.new_page()
                        if load_via_protocol:
                            session = ctx.browser.new_browser_cdp_session() if ctx.browser else ctx.new_cdp_session(page)
                            session.send("Extensions.loadUnpacked", {"path": extension})
                        port = srv.server_port
                        pending = f"http://sub.pulso.test:{port}/antes"
                        wait_for(lambda: command("estado") in {"Chrome", "Edge"}, "Extensão empacotada conectou ao Pulso", page)
                        print("Navegador:", command("estado"), flush=True)
                        assert command("recarregar-filtros") == "nao"
                        check_categories(ctx, command, srv, ctx.service_workers[0].url.rsplit("/", 1)[0])
                        page.goto(pending)
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
                        gaming_url = f"http://reconectar-pulso.bet.br:{port}/retorno"
                        tab.goto(gaming_url)
                        proc = start()
                        assert proc.stdout.readline().strip() == extension
                        assert command("filtro gambling always") == "ok"
                        wait_for(lambda: urlsplit(tab.url).path == "/pausa.html" and "Apostas" in tab.locator("#detail").inner_text(),
                                 "Reabrir o PC reconecta a extensão e pausa a aba já aberta", tab, 20)
                        tab.goto(f"http://liberado.test:{port}/recarregar")
                        extension_url = reload_extension(ctx, tab)
                        wait_for(lambda: command("estado") in {"Chrome", "Edge"} and command("recarregar-filtros") == "nao",
                                 "Extensão recarregada autentica novamente", tab)
                        tab.goto(gaming_url)
                        assert urlsplit(tab.url).path == "/pausa.html" and "Apostas" in tab.locator("#detail").inner_text()
                        if check_timeout:
                            assert command("filtro gambling off") == "ok"
                            wait_for(lambda: tab.locator("h1").inner_text() == "A pausa terminou.", "Filtro liberado antes de testar o prazo", tab)
                            check_lease(ctx, tab, command, proc, srv, extension_url)
            finally:
                srv.shutdown()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("fixture")
    parser.add_argument("--chromium", default=os.environ.get("PULSO_TEST_CHROMIUM"))
    parser.add_argument("--via-protocolo", action="store_true")
    parser.add_argument("--prazo-conexao", action="store_true", help="Verifica também o prazo real de 40 s com o fixture Windows suspenso.")
    args = parser.parse_args()
    run(args.fixture, args.chromium, args.via_protocolo, args.prazo_conexao)
