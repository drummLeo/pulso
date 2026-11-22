"""Produz um APK pessoal assinado usando o JDK e as ferramentas oficiais do Android."""
import argparse
import ctypes
from ctypes import wintypes
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parent
LOCAL = Path(os.environ["LOCALAPPDATA"])


def run(*args, env=None):
    subprocess.run([str(a) for a in args], check=True, env=env)


def version(path):
    return tuple(int(n) for n in re.findall(r"\d+", path.name))


def jdk_bin():
    studio = Path(os.environ["ProgramFiles"]) / "Android/Android Studio/jbr/bin"
    candidates = [Path(os.environ.get("JAVA_HOME", ".")) / "bin", studio]
    for binary in candidates:
        if (binary / "javac.exe").is_file() and (binary / "keytool.exe").is_file():
            return binary
    raise SystemExit("Informe um JDK completo em JAVA_HOME ou instale o Android Studio.")


class Blob(ctypes.Structure):
    _fields_ = [("size", wintypes.DWORD), ("data", ctypes.c_void_p)]


def protect(data, decrypt=False):
    source = ctypes.create_string_buffer(data)
    incoming = Blob(len(data), ctypes.cast(source, ctypes.c_void_p))
    outgoing = Blob()
    crypt = ctypes.WinDLL("crypt32", use_last_error=True)
    method = crypt.CryptUnprotectData if decrypt else crypt.CryptProtectData
    if not method(ctypes.byref(incoming), None, None, None, None, 1, ctypes.byref(outgoing)):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        return ctypes.string_at(outgoing.data, outgoing.size)
    finally:
        free = ctypes.windll.kernel32.LocalFree
        free.argtypes = [ctypes.c_void_p]
        free(outgoing.data)


def signing(jdk):
    parent = LOCAL / "PulsoAndroid"
    dest = parent / "Assinatura"
    if not dest.exists():
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="chave-", dir=parent) as tmp:
            staged = Path(tmp) / "Assinatura"
            staged.mkdir()
            password = secrets.token_urlsafe(32)
            env = dict(os.environ, PULSO_ANDROID_SIGN=password)
            run(jdk / "keytool.exe", "-genkeypair", "-keystore", staged / "pulso.p12", "-storetype", "PKCS12",
                "-alias", "pulso", "-keyalg", "RSA", "-keysize", "3072", "-validity", "10000",
                "-dname", "CN=Pulso Android Local", "-storepass:env", "PULSO_ANDROID_SIGN",
                "-keypass:env", "PULSO_ANDROID_SIGN", "-noprompt", env=env)
            (staged / "senha.dpapi").write_bytes(protect(password.encode("ascii")))
            try:
                staged.rename(dest)
            except FileExistsError:
                pass
    key = dest / "pulso.p12"
    if not key.is_file() or not (dest / "senha.dpapi").is_file():
        raise SystemExit("A assinatura local está incompleta. Restaure a chave original para atualizar o aplicativo.")
    password = protect((dest / "senha.dpapi").read_bytes(), decrypt=True).decode("ascii")
    return key, dict(os.environ, PULSO_ANDROID_SIGN=password)


def build(android_jar):
    sdk = Path(os.environ.get("ANDROID_HOME") or os.environ.get("ANDROID_SDK_ROOT") or LOCAL / "Android/Sdk")
    tools = max((p for p in (sdk / "build-tools").iterdir() if (p / "aapt2.exe").is_file()), key=version)
    if android_jar is None:
        platform = max((p for p in (sdk / "platforms").iterdir() if (p / "android.jar").is_file()), key=version)
        android_jar = platform / "android.jar"
    android_jar = Path(android_jar).resolve(strict=True)
    jdk = jdk_bin()
    with tempfile.TemporaryDirectory(prefix="pulso-apk-") as temp:
        work = Path(temp)
        generated, classes, dex = [work / k for k in ("gerados","classes","dex")]
        for folder in (generated,classes,dex):
            folder.mkdir()
        # O AAPT2 do Windows pode perder acentos ao receber caminhos absolutos.
        shutil.copytree(ROOT / "res", work / "res")
        raw = work / "res" / "raw"
        raw.mkdir(exist_ok=True)
        for name in ("app_categories.tsv", "site_porn.txt", "site_gambling.txt"):
            shutil.copyfile(ROOT.parent / "shared" / name, raw / name)
        shutil.copytree(ROOT / "src", work / "src")
        shutil.copyfile(ROOT / "AndroidManifest.xml", work / "AndroidManifest.xml")
        aapt = tools / "aapt2.exe"
        run(aapt, "compile", "--dir", work / "res", "-o", work / "recursos.zip")
        package = work / "base.apk"
        run(aapt, "link", "-o", package, "-I", android_jar, "--java", generated, "--manifest",
            work / "AndroidManifest.xml", work / "recursos.zip")
        sources = sorted((work / "src").rglob("*.java")) + sorted(generated.rglob("*.java"))
        run(jdk / "javac.exe", "-encoding", "UTF-8", "-g:source,lines", "-source", "8", "-target", "8",
            "-Xlint:-options", "-bootclasspath", str(tools / "core-lambda-stubs.jar") + os.pathsep + str(android_jar),
            "-d", classes, *sources)
        run(jdk / "jar.exe", "cf", work / "pulso.jar", "-C", classes, ".")
        java = jdk / "java.exe"
        run(java, "-cp", tools / "lib/d8.jar", "com.android.tools.r8.D8", "--release", "--min-api", "29",
            "--lib", android_jar, "--output", dex, work / "pulso.jar")
        with zipfile.ZipFile(package, "a") as apk:
            for file in sorted(dex.glob("*.dex")):
                apk.write(file,file.name,zipfile.ZIP_DEFLATED)
        aligned, signed = work / "alinhado.apk", work / "assinado.apk"
        run(tools / "zipalign.exe", "-P", "16", "-f", "4", package, aligned)
        key, env = signing(jdk)
        signer = tools / "lib/apksigner.jar"
        run(java, "-jar", signer, "sign", "--ks", key, "--ks-key-alias", "pulso", "--ks-pass", "env:PULSO_ANDROID_SIGN",
            "--v4-signing-enabled", "false", "--out", signed, aligned, env=env)
        run(java, "-jar", signer, "verify", "--verbose", signed)
        with tempfile.NamedTemporaryFile(prefix="Pulso-", suffix=".tmp", dir=ROOT, delete=False) as pending:
            output = Path(pending.name)
        try:
            shutil.copyfile(signed, output)
            output.replace(ROOT / "Pulso-Galaxy.apk")
        finally:
            output.unlink(missing_ok=True)
    print(ROOT / "Pulso-Galaxy.apk")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--android-jar", type=Path)
    build(parser.parse_args().android_jar)
