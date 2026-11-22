"""Testa as categorias no AVD descartável PulsoTeste17, com barras locais sem acesso à rede."""
import argparse
from contextlib import ExitStack
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
from zipfile import ZIP_DEFLATED, ZipFile

from test_android import PKG, Phone, until
from test_agendas_android import create_allowance, daytime
from test_limites_android import enable_service


BROWSERS = ("com.android.chrome", "com.microsoft.emmx", "com.sec.android.app.sbrowser")
ACTIVITY = "br.local.pulso.testbrowser.BrowserActivity"
ANDROID = Path(__file__).resolve().parents[1]
FREE = "https://livre.example.test/"


def domain_samples(apk):
    with ZipFile(apk) as package:
        def table(name):
            path = next(p for p in package.namelist() if Path(p).parent.as_posix() == "res/raw"
                        and Path(p).stem == name)
            return set(package.read(path).decode("ascii").splitlines())
        porn, gambling = table("site_porn"), table("site_gambling")

    def exclusive(own, other):
        for host in sorted(own, key=lambda h: (len(h), h)):
            labels = host.split(".")
            if re.fullmatch(r"[a-z0-9.-]+", host) and not any(".".join(labels[i:]) in other for i in range(len(labels))):
                return host
        raise AssertionError("O APK precisa conter pelo menos um domínio exclusivo de cada categoria.")
    return exclusive(porn, gambling), exclusive(gambling, porn)


def jdk_bin(jdk):
    if jdk is None:
        java = shutil.which("java")
        if os.environ.get("JAVA_HOME"):
            jdk = Path(os.environ["JAVA_HOME"])
        elif java:
            info = subprocess.run([java, "-XshowSettings:properties", "-version"], capture_output=True, text=True, check=True)
            jdk = Path(re.search(r"^\s*java.home = (.+)$", info.stderr, re.M).group(1).strip())
        else:
            jdk = Path(os.environ.get("ProgramFiles", "")) / "Android/Android Studio/jbr"
    return jdk / "bin"


def fixtures(work, sdk, platform, jdk, browsers):
    def version(path):
        return tuple(int(n) for n in re.findall(r"\d+", path.name))
    exe = ".exe" if os.name == "nt" else ""
    build = max((p for p in (sdk/"build-tools").iterdir() if (p/("aapt2"+exe)).is_file()), key=version)
    if platform is None:
        platform = max((p for p in (sdk/"platforms").iterdir() if (p/"android.jar").is_file()), key=version) / "android.jar"
    shutil.copyfile(platform, work/"android.jar")
    jbin = jdk_bin(jdk)
    java, keytool, javac = (jbin/(name+exe) for name in ("java", "keytool", "javac"))
    def run(*args):
        subprocess.run([str(a) for a in args], cwd=work, check=True)
    for folder in ("classes", "dex", "res/values"):
        (work/folder).mkdir(parents=True)
    run(javac, "-encoding", "UTF-8", "-source", "8", "-target", "8", "-Xlint:-options", "-bootclasspath",
        str(build/"core-lambda-stubs.jar")+os.pathsep+"android.jar", "-d", "classes", ANDROID/"tests/browser/BrowserActivity.java")
    with ZipFile(work/"classes.jar", "w") as jar:
        for entry in (work/"classes").rglob("*.class"):
            jar.write(entry, entry.relative_to(work/"classes"))
    run(java, "-cp", build/"lib/d8.jar", "com.android.tools.r8.D8", "--release", "--min-api", "29",
        "--lib", "android.jar", "--output", "dex", "classes.jar")
    (work/"res/values/ids.xml").write_text('<resources><item type="id" name="url_bar"/><item type="id" name="location_bar_edit_text"/></resources>', encoding="ascii")
    run(build/("aapt2"+exe), "compile", "--dir", "res", "-o", "res.zip")
    run(keytool, "-genkeypair", "-keystore", "teste.p12", "-alias", "teste", "-keyalg", "RSA", "-keysize", "2048",
        "-validity", "1", "-storepass", "testebarra", "-keypass", "testebarra", "-dname", "CN=Teste local Pulso", "-noprompt")
    for pkg in browsers:
        (work/"manifest.xml").write_text(
            f'<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="{pkg}">'
            '<uses-sdk android:minSdkVersion="29" android:targetSdkVersion="32"/>'
            '<application android:label="Barra local de teste" android:testOnly="true" android:theme="@android:style/Theme.Material.Light.NoActionBar">'
            f'<activity android:name="{ACTIVITY}" android:exported="true" android:launchMode="singleTop"/>'
            '</application></manifest>', encoding="ascii")
        base = pkg+".base.apk"
        run(build/("aapt2"+exe), "link", "-o", base, "-I", "android.jar", "--manifest", "manifest.xml", "res.zip")
        with ZipFile(work/base, "a") as apk:
            apk.write(work/"dex/classes.dex", "classes.dex", ZIP_DEFLATED)
        run(build/("zipalign"+exe), "-f", "4", base, "alinhado.apk")
        run(java, "-jar", build/"lib/apksigner.jar", "sign", "--ks", "teste.p12", "--ks-pass", "pass:testebarra",
            "--v4-signing-enabled", "false", "--out", pkg+".apk", "alinhado.apk")


def open_tabs(p, pkg, address, other=FREE):
    p.sh("am", "start", "-W", "-n", pkg+"/"+ACTIVITY, "--es", "endereco", address, "--es", "outra", other)
    until(lambda: pkg+"/" in p.activity(), "Barra de teste em primeiro plano")


def free_page(p, pkg):
    # A ausência imediata de cobertura pode preceder o primeiro evento da barra.
    until(lambda: not p.cover(), "Página liberada")
    end = time.monotonic()+2.3
    while time.monotonic() < end:
        assert pkg+"/" in p.activity() and not p.cover(), "Uma página permitida recebeu bloqueio."
        time.sleep(.25)


def choose(p, category, before, after):
    p.tap(category+" · "+before, scroll=True)
    p.tap(after)


def exercise(p, browsers, porn, gambling, artifacts):
    p.sh("input", "keyevent", "KEYCODE_WAKEUP")
    p.sh("wm", "dismiss-keyguard")
    p.home()
    enable_service(p)
    p.tap("Lista")
    choose(p, "Pornografia", "Desativado", "Sempre")
    choose(p, "Apostas", "Desativado", "Durante o foco")
    blocked = "https://sub."+porn+"/pagina"
    metered = "https://"+gambling+"/"
    for pkg in browsers:
        open_tabs(p, pkg, FREE+porn, blocked)
        free_page(p, pkg)
        switch = next(n for n in p.nodes() if p.matches(n, "Outra aba"))
        p.touch(switch)
        until(p.cover, "Sempre cobre a segunda aba, inclusive fora de uma sessão em "+pkg)
        if artifacts:
            p.screenshot(artifacts/(pkg+"-sempre.png"))
        p.touch(switch)
        free_page(p, pkg)
    pkg = browsers[0]
    open_tabs(p, pkg, blocked+"?pagina="+"a"*4500)
    until(p.cover, "Um endereço longo preserva o bloqueio pelo domínio")
    open_tabs(p, pkg, metered)
    free_page(p, pkg)
    p.home(); p.tap("Foco"); p.tap("Iniciar sessão")
    open_tabs(p, pkg, metered)
    until(p.cover, "Apostas pausa durante a sessão rápida")
    if artifacts:
        p.screenshot(artifacts/"apostas-durante-o-foco.png")
    p.home(); p.tap("Foco"); p.tap("Encerrar sessão"); p.tap("Encerrar sessão")
    open_tabs(p, pkg, metered)
    free_page(p, pkg)
    with daytime(p):
        p.home(); p.tap("Agenda")
        create_allowance(p, "Janela", 5)
        open_tabs(p, pkg, metered)
        until(p.cover, "Uma agenda com saldo disponível já pausa a categoria")
        p.home(); p.tap("Agenda"); p.tap("Ativar Janela", scroll=True)
        open_tabs(p, pkg, metered)
        free_page(p, pkg)
    p.sh("am","force-stop",PKG); p.home(); p.tap("Lista")
    choose(p, "Pornografia", "Sempre", "Desativado")
    choose(p, "Apostas", "Durante o foco", "Desativado")
    p.tap("Adicionar site", scroll=True); p.type_in("exemplo.com","manual.test"); p.tap("Adicionar")
    p.tap("Foco"); p.tap("Iniciar sessão")
    enable_service(p)
    open_tabs(p, pkg, "https://manual.test/")
    until(p.cover, "A regra manual continua bloqueando depois de desativar as categorias")
    open_tabs(p, pkg, blocked)
    free_page(p, pkg)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sdk_default = os.environ.get("ANDROID_HOME") or os.environ.get("ANDROID_SDK_ROOT") or Path(os.environ.get("LOCALAPPDATA", "."))/"Android/Sdk"
    parser.add_argument("--sdk", type=Path, default=Path(sdk_default))
    parser.add_argument("--jdk", type=Path, help="Diretório do JDK")
    parser.add_argument("--android-jar", type=Path)
    parser.add_argument("--adb", type=Path)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--apk", type=Path, default=ANDROID/"Pulso-Galaxy.apk")
    parser.add_argument("--artifacts", type=Path, help="Pasta opcional para capturas de tela")
    args = parser.parse_args()
    p = Phone(args.adb or args.sdk/"platform-tools"/("adb.exe" if os.name=="nt" else "adb"), args.serial)
    # Phone has verified the emulator serial and the exact disposable AVD name before any mutation.
    installed = set(p.sh("pm", "list", "packages", "-u").splitlines())
    browsers = [pkg for pkg in BROWSERS if "package:"+pkg not in installed]
    if not browsers:
        raise SystemExit("O teste requer ao menos um pacote de navegador ausente. Use o AVD AOSP descartável.")
    porn, gambling = domain_samples(args.apk)
    if args.artifacts:
        args.artifacts.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="pulso-barras-") as temp, ExitStack() as cleanup:
        work = Path(temp)
        fixtures(work, args.sdk.resolve(), args.android_jar, args.jdk, browsers)
        for pkg in browsers:
            assert p.call("install", "-t", str(work/(pkg+".apk"))).endswith("Success")
            cleanup.callback(p.call, "uninstall", pkg)
        assert p.call("install", "-r", str(args.apk.resolve())).endswith("Success")
        original = p.sh("settings", "get", "secure", "enabled_accessibility_services")
        if original in ("null", ""):
            cleanup.callback(p.sh,"settings","delete","secure","enabled_accessibility_services")
        else:
            cleanup.callback(p.sh,"settings","put","secure","enabled_accessibility_services",original)
        assert p.sh("pm","clear",PKG) == "Success"
        exercise(p, browsers, porn, gambling, args.artifacts)
    print("OK  Categorias, atualização da barra, persistência e regras manuais no AVD local.")


if __name__=="__main__":
    main()
