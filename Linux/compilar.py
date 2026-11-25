"""Compila e empacota a versão Linux do Pulso em pasta e arquivo .tar.gz."""
import argparse
import os
from pathlib import Path
import platform
import shutil
import stat
import subprocess
import tarfile
import tempfile


ROOT = Path(__file__).resolve().parent
PROJECT = ROOT / "Pulso.Linux.csproj"
DIST = ROOT / "dist"
ICON_SRC = ROOT.parent / "src" / "Browser" / "pulso.png"
README_SRC = ROOT / "Leia-me.txt"


def find_dotnet():
    candidate = shutil.which("dotnet")
    if candidate:
        return candidate
    local = Path.home() / ".dotnet" / "dotnet"
    if local.is_file():
        return str(local)
    raise SystemExit("SDK do .NET 10 não encontrado. Instale o dotnet ou adicione ~/.dotnet ao PATH.")


def default_rid():
    machine = platform.machine().lower()
    if machine in ("x86_64", "amd64"):
        return "linux-x64"
    if machine in ("aarch64", "arm64"):
        return "linux-arm64"
    raise SystemExit(f"Arquitetura não suportada automaticamente: {machine}. Informe --rid.")


def desktop_entry(exec_cmd="pulso"):
    return (
        "[Desktop Entry]\n"
        "Version=1.0\n"
        "Type=Application\n"
        "Name=Pulso\n"
        "GenericName=Foco e bloqueio de distrações\n"
        "Comment=Sessões de foco, limites de uso e bloqueio de aplicativos e sites no Linux\n"
        f"Exec={exec_cmd}\n"
        "Icon=pulso\n"
        "Terminal=false\n"
        "Categories=Utility;\n"
        "StartupWMClass=Pulso\n"
    )


def installer_script():
    return """#!/usr/bin/env bash
set -euo pipefail

ORIGEM="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIXO="${HOME}/.local"
DESINSTALAR=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix)
      if [[ $# -lt 2 ]]; then
        echo "Informe um caminho após --prefix." >&2
        exit 2
      fi
      PREFIXO="$2"
      shift 2
      ;;
    --desinstalar)
      DESINSTALAR=1
      shift
      ;;
    -h|--help)
      cat <<EOF
Uso: ./instalar.sh [--prefix CAMINHO] [--desinstalar]

Instala o Pulso para o usuário atual (padrão: ~/.local).
EOF
      exit 0
      ;;
    *)
      echo "Opção desconhecida: $1" >&2
      exit 2
      ;;
  esac
done

LIB_DIR="${PREFIXO}/lib/pulso"
BIN_DIR="${PREFIXO}/bin"
DATA_DIR="${XDG_DATA_HOME:-${HOME}/.local/share}"
if [[ "${PREFIXO}" != "${HOME}/.local" ]]; then
  DATA_DIR="${PREFIXO}/share"
fi
ICON_DEST="${DATA_DIR}/icons/hicolor/128x128/apps/pulso.png"
DESKTOP_DEST="${DATA_DIR}/applications/pulso.desktop"
BIN_LINK="${BIN_DIR}/pulso"

if [[ "${DESINSTALAR}" -eq 1 ]]; then
  rm -f "${BIN_LINK}" "${ICON_DEST}" "${DESKTOP_DEST}"
  rm -rf "${LIB_DIR}"
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "${DATA_DIR}/applications" >/dev/null 2>&1 || true
  fi
  echo "Pulso removido de ${PREFIXO}."
  exit 0
fi

mkdir -p "${LIB_DIR}" "${BIN_DIR}" "$(dirname "${ICON_DEST}")" "$(dirname "${DESKTOP_DEST}")"
rm -rf "${LIB_DIR:?}/"*
cp -a "${ORIGEM}/app/." "${LIB_DIR}/"
chmod +x "${LIB_DIR}/Pulso"
ln -sf "${LIB_DIR}/Pulso" "${BIN_LINK}"
cp -f "${ORIGEM}/pulso.png" "${ICON_DEST}"

cat > "${DESKTOP_DEST}" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Pulso
GenericName=Foco e bloqueio de distrações
Comment=Sessões de foco, limites de uso e bloqueio de aplicativos e sites no Linux
Exec=${BIN_LINK}
Icon=pulso
Terminal=false
Categories=Utility;
StartupWMClass=Pulso
EOF

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "${DATA_DIR}/applications" >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t -f "${DATA_DIR}/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "Pulso instalado em ${LIB_DIR}."
echo "Executável: ${BIN_LINK}"
echo "Atalho: ${DESKTOP_DEST}"
"""


def build(rid=None, self_contained=False, output=None, install=False, prefix=None):
    dotnet = find_dotnet()
    target_rid = rid or default_rid()
    suffix = "-standalone" if self_contained else ""
    package_name = f"pulso-{target_rid}{suffix}"

    out_root = Path(output).resolve() if output else DIST
    out_root.mkdir(parents=True, exist_ok=True)
    package_dir = out_root / package_name
    archive_path = out_root / f"{package_name}.tar.gz"

    with tempfile.TemporaryDirectory(prefix="pulso-linux-stage-", dir=out_root) as tmp:
        stage = Path(tmp) / package_name
        app_dir = stage / "app"
        app_dir.mkdir(parents=True)

        env = dict(os.environ)
        dotnet_dir = str(Path(dotnet).resolve().parent)
        env.setdefault("DOTNET_ROOT", dotnet_dir)
        env["PATH"] = dotnet_dir + os.pathsep + env.get("PATH", "")

        cmd = [
            dotnet, "publish", str(PROJECT),
            "-c", "Release",
            "-r", target_rid,
            "--self-contained" if self_contained else "--no-self-contained",
            "-o", str(app_dir),
        ]
        subprocess.run(cmd, check=True, env=env)

        executable = app_dir / "Pulso"
        if executable.exists():
            executable.chmod(executable.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

        shutil.copyfile(ICON_SRC, stage / "pulso.png")
        if README_SRC.is_file():
            shutil.copyfile(README_SRC, stage / "Leia-me.txt")
        (stage / "pulso.desktop").write_text(desktop_entry(), encoding="utf-8")

        installer = stage / "instalar.sh"
        installer.write_text(installer_script(), encoding="utf-8", newline="\n")
        installer.chmod(0o755)

        if package_dir.exists():
            shutil.rmtree(package_dir)
        shutil.move(str(stage), str(package_dir))

    with tempfile.NamedTemporaryFile(prefix=f"{package_name}-", suffix=".tar.gz.tmp", dir=out_root, delete=False) as pending:
        tmp_archive = Path(pending.name)
    try:
        with tarfile.open(tmp_archive, "w:gz") as tar:
            tar.add(package_dir, arcname=package_name)
        tmp_archive.chmod(0o644)
        tmp_archive.replace(archive_path)
    finally:
        tmp_archive.unlink(missing_ok=True)

    print(f"Pasta: {package_dir}")
    print(f"Pacote: {archive_path}")

    if install:
        install_cmd = [str(package_dir / "instalar.sh")]
        if prefix:
            install_cmd.extend(["--prefix", prefix])
        subprocess.run(install_cmd, check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", help="identificador de runtime .NET (padrão: linux-x64 ou linux-arm64 conforme a máquina)")
    parser.add_argument("--self-contained", action="store_true", help="inclui o runtime do .NET 10 no pacote (standalone)")
    parser.add_argument("--output", type=Path, help="pasta de saída para o pacote (padrão: Linux/dist)")
    parser.add_argument("--install", action="store_true", help="executa instalar.sh após compilar")
    parser.add_argument("--prefix", help="prefixo de instalação passado para instalar.sh (padrão: ~/.local)")
    args = parser.parse_args()
    build(rid=args.rid, self_contained=args.self_contained, output=args.output, install=args.install, prefix=args.prefix)
