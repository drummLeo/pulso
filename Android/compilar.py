"""Produz um APK ou Android App Bundle (.aab) assinado usando o JDK e as ferramentas oficiais do Android."""
import argparse
import ctypes
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parent
EXE = ".exe" if os.name == "nt" else ""
# BundleConfig.pb padrão (BundleConfig { bundletool { version: "1.18.3" } }).
BUNDLE_CONFIG = b"\x0a\x08\x12\x061.18.3"


def run(*args, env=None):
    subprocess.run([str(a) for a in args], check=True, env=env)


def version(path):
    return tuple(int(n) for n in re.findall(r"\d+", path.name))


def jdk_bin():
    candidates = []
    if os.environ.get("JAVA_HOME"):
        candidates.append(Path(os.environ["JAVA_HOME"]) / "bin")
    if os.name == "nt" and os.environ.get("ProgramFiles"):
        candidates.append(Path(os.environ["ProgramFiles"]) / "Android/Android Studio/jbr/bin")
    which_javac = shutil.which("javac")
    if which_javac:
        candidates.append(Path(which_javac).resolve().parent)
    for binary in candidates:
        if (binary / f"javac{EXE}").is_file() and (binary / f"keytool{EXE}").is_file():
            return binary
    raise SystemExit("Informe um JDK completo em JAVA_HOME ou instale o Android Studio.")


def sdk_root():
    configured = os.environ.get("ANDROID_HOME") or os.environ.get("ANDROID_SDK_ROOT")
    if configured:
        return Path(configured)
    if os.name == "nt":
        return Path(os.environ["LOCALAPPDATA"]) / "Android/Sdk"
    return Path.home() / "Android/Sdk"


def protect(data, decrypt=False):
    from ctypes import wintypes

    class Blob(ctypes.Structure):
        _fields_ = [("size", wintypes.DWORD), ("data", ctypes.c_void_p)]

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


def signing(jdk, keystore=None, alias="pulso", pass_env="PULSO_ANDROID_SIGN"):
    if keystore is not None:
        key = Path(keystore).resolve(strict=True)
        if not os.environ.get(pass_env):
            raise SystemExit(f"Defina a variável de ambiente {pass_env} com a senha da chave {key}.")
        return key, alias, pass_env, dict(os.environ)
    if os.name != "nt":
        raise SystemExit(
            f"Informe --keystore e defina a variável de ambiente {pass_env} com a senha da chave fora do Windows."
        )
    parent = Path(os.environ["LOCALAPPDATA"]) / "PulsoAndroid"
    dest = parent / "Assinatura"
    if not dest.exists():
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="chave-", dir=parent) as tmp:
            staged = Path(tmp) / "Assinatura"
            staged.mkdir()
            password = secrets.token_urlsafe(32)
            env = dict(os.environ, PULSO_ANDROID_SIGN=password)
            run(jdk / f"keytool{EXE}", "-genkeypair", "-keystore", staged / "pulso.p12", "-storetype", "PKCS12",
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
    return key, "pulso", "PULSO_ANDROID_SIGN", dict(os.environ, PULSO_ANDROID_SIGN=password)


def pack_aab(base_proto, dex_dir, destination):
    with zipfile.ZipFile(base_proto, "r") as proto, zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as bundle:
        bundle.writestr("BundleConfig.pb", BUNDLE_CONFIG)
        for info in proto.infolist():
            target = "base/manifest/AndroidManifest.xml" if info.filename == "AndroidManifest.xml" else f"base/{info.filename}"
            bundle.writestr(target, proto.read(info.filename))
        for dex_file in sorted(dex_dir.glob("*.dex")):
            bundle.write(dex_file, f"base/dex/{dex_file.name}")


def build(android_jar=None, aab=False, keystore=None, alias="pulso", pass_env="PULSO_ANDROID_SIGN", output_path=None):
    sdk = sdk_root()
    tools = max((p for p in (sdk / "build-tools").iterdir() if (p / f"aapt2{EXE}").is_file()), key=version)
    if android_jar is None:
        platform = max((p for p in (sdk / "platforms").iterdir() if (p / "android.jar").is_file()), key=version)
        android_jar = platform / "android.jar"
    android_jar = Path(android_jar).resolve(strict=True)
    jdk = jdk_bin()
    if aab and not (jdk / f"jarsigner{EXE}").is_file():
        raise SystemExit("O JDK informado não contém jarsigner para assinar o Android App Bundle (.aab).")
    target_path = Path(output_path).resolve() if output_path else ROOT / ("Pulso-Galaxy.aab" if aab else "Pulso-Galaxy.apk")
    with tempfile.TemporaryDirectory(prefix="pulso-apk-") as temp:
        work = Path(temp)
        generated, classes, dex = [work / k for k in ("gerados", "classes", "dex")]
        for folder in (generated, classes, dex):
            folder.mkdir()
        # O AAPT2 do Windows pode perder acentos ao receber caminhos absolutos.
        shutil.copytree(ROOT / "res", work / "res")
        raw = work / "res" / "raw"
        raw.mkdir(exist_ok=True)
        for name in ("app_categories.tsv", "site_porn.txt", "site_gambling.txt"):
            shutil.copyfile(ROOT.parent / "shared" / name, raw / name)
        shutil.copytree(ROOT / "src", work / "src")
        shutil.copyfile(ROOT / "AndroidManifest.xml", work / "AndroidManifest.xml")
        aapt = tools / f"aapt2{EXE}"
        run(aapt, "compile", "--dir", work / "res", "-o", work / "recursos.zip")
        package = work / ("base_proto.apk" if aab else "base.apk")
        link_args = [aapt, "link"]
        if aab:
            link_args.append("--proto-format")
        link_args.extend([
            "-o", package, "-I", android_jar, "--java", generated,
            "--manifest", work / "AndroidManifest.xml", work / "recursos.zip",
        ])
        run(*link_args)
        sources = sorted((work / "src").rglob("*.java")) + sorted(generated.rglob("*.java"))
        run(jdk / f"javac{EXE}", "-encoding", "UTF-8", "-g:source,lines", "-source", "8", "-target", "8",
            "-Xlint:-options", "-bootclasspath", str(tools / "core-lambda-stubs.jar") + os.pathsep + str(android_jar),
            "-d", classes, *sources)
        run(jdk / f"jar{EXE}", "cf", work / "pulso.jar", "-C", classes, ".")
        java = jdk / f"java{EXE}"
        run(java, "-cp", tools / "lib/d8.jar", "com.android.tools.r8.D8", "--release", "--min-api", "29",
            "--lib", android_jar, "--output", dex, work / "pulso.jar")
        key, key_alias, key_pass_env, env = signing(jdk, keystore=keystore, alias=alias, pass_env=pass_env)
        if aab:
            unsigned, signed = work / "pulso.aab", work / "assinado.aab"
            pack_aab(package, dex, unsigned)
            jarsigner = jdk / f"jarsigner{EXE}"
            run(jarsigner, "-keystore", key, "-storetype", "PKCS12",
                "-storepass:env", key_pass_env, "-keypass:env", key_pass_env,
                "-sigalg", "SHA256withRSA", "-digestalg", "SHA-256",
                "-signedjar", signed, unsigned, key_alias, env=env)
            run(jarsigner, "-verify", signed)
        else:
            with zipfile.ZipFile(package, "a") as apk:
                for file in sorted(dex.glob("*.dex")):
                    apk.write(file, file.name, zipfile.ZIP_DEFLATED)
            aligned, signed = work / "alinhado.apk", work / "assinado.apk"
            run(tools / f"zipalign{EXE}", "-P", "16", "-f", "4", package, aligned)
            signer = tools / "lib/apksigner.jar"
            run(java, "-jar", signer, "sign", "--ks", key, "--ks-key-alias", key_alias,
                "--ks-pass", f"env:{key_pass_env}", "--v4-signing-enabled", "false",
                "--out", signed, aligned, env=env)
            run(java, "-jar", signer, "verify", "--verbose", signed)
        target_path.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(prefix="Pulso-", suffix=".tmp", dir=target_path.parent, delete=False) as pending:
            output = Path(pending.name)
        try:
            shutil.copyfile(signed, output)
            output.replace(target_path)
        finally:
            output.unlink(missing_ok=True)
    print(target_path)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--android-jar", type=Path)
    parser.add_argument("--aab", action="store_true", help="gera um Android App Bundle (.aab) para envio ao Google Play")
    parser.add_argument("--keystore", type=Path, help="caminho opcional para um arquivo PKCS12 (.p12) de assinatura")
    parser.add_argument("--key-alias", default="pulso", help="alias da chave dentro do keystore (padrão: pulso)")
    parser.add_argument("--key-pass-env", default="PULSO_ANDROID_SIGN", help="variável de ambiente com a senha do keystore")
    parser.add_argument("--output", type=Path, help="caminho de saída do pacote gerado")
    args = parser.parse_args()
    build(args.android_jar, aab=args.aab, keystore=args.keystore, alias=args.key_alias,
          pass_env=args.key_pass_env, output_path=args.output)
