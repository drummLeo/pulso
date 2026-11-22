"""Executa o teste WPF do seletor em uma área de trabalho privada do Windows."""
import ctypes
from ctypes import wintypes as win
import msvcrt
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import uuid


class StartupInfo(ctypes.Structure):
    _fields_ = [
        ("cb", win.DWORD), ("reserved", win.LPWSTR), ("desktop", win.LPWSTR), ("title", win.LPWSTR),
        ("x", win.DWORD), ("y", win.DWORD), ("width", win.DWORD), ("height", win.DWORD),
        ("columns", win.DWORD), ("rows", win.DWORD), ("fill", win.DWORD), ("flags", win.DWORD),
        ("show", win.WORD), ("reserved_size", win.WORD), ("reserved_data", win.LPVOID),
        ("stdin", win.HANDLE), ("stdout", win.HANDLE), ("stderr", win.HANDLE),
    ]


class ProcessInfo(ctypes.Structure):
    _fields_ = [("process", win.HANDLE), ("thread", win.HANDLE), ("pid", win.DWORD), ("tid", win.DWORD)]


def run():
    directory = Path(__file__).resolve().parent
    executable = directory / "bin/Release/net10.0-windows/Pulso.Verificacao.exe"
    if not executable.is_file():
        raise SystemExit("Compile antes com dotnet build tests/Windows/WindowsChecks.csproj -c Release.")
    user = ctypes.WinDLL("user32", use_last_error=True)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    user.CreateDesktopW.argtypes = [win.LPCWSTR, win.LPCWSTR, win.LPVOID, win.DWORD, win.DWORD, win.LPVOID]
    user.CreateDesktopW.restype = win.HANDLE
    user.CloseDesktop.argtypes = [win.HANDLE]
    kernel.CreateProcessW.argtypes = [win.LPCWSTR, win.LPWSTR, win.LPVOID, win.LPVOID, win.BOOL, win.DWORD,
        win.LPVOID, win.LPCWSTR, ctypes.POINTER(StartupInfo), ctypes.POINTER(ProcessInfo)]
    kernel.CloseHandle.argtypes = [win.HANDLE]
    kernel.WaitForSingleObject.argtypes = [win.HANDLE, win.DWORD]
    kernel.GetExitCodeProcess.argtypes = [win.HANDLE, ctypes.POINTER(win.DWORD)]
    kernel.TerminateProcess.argtypes = [win.HANDLE, win.UINT]

    desktop_name = "PulsoCatalogo" + uuid.uuid4().hex
    desktop = user.CreateDesktopW(desktop_name, None, None, 0, 0x01FF, None)
    if not desktop:
        raise ctypes.WinError(ctypes.get_last_error())
    process = ProcessInfo()
    try:
        with tempfile.TemporaryFile() as log, open(os.devnull, "rb") as null:
            startup = StartupInfo()
            startup.cb = ctypes.sizeof(startup)
            startup.desktop = "WinSta0\\" + desktop_name
            startup.flags = subprocess.STARTF_USESTDHANDLES
            startup.stdin = msvcrt.get_osfhandle(null.fileno())
            startup.stdout = startup.stderr = msvcrt.get_osfhandle(log.fileno())
            command = ctypes.create_unicode_buffer(subprocess.list2cmdline([str(executable), "catalogo-interface"]))
            for descriptor in (log, null):
                os.set_inheritable(descriptor.fileno(), True)
            if not kernel.CreateProcessW(str(executable), command, None, None, True, subprocess.CREATE_NO_WINDOW,
                    None, str(directory), ctypes.byref(startup), ctypes.byref(process)):
                raise ctypes.WinError(ctypes.get_last_error())
            if kernel.WaitForSingleObject(process.process, 60000) != 0:
                kernel.TerminateProcess(process.process, 1)
                raise TimeoutError("O verificador do catálogo excedeu 60 segundos.")
            status = win.DWORD()
            if not kernel.GetExitCodeProcess(process.process, ctypes.byref(status)):
                raise ctypes.WinError(ctypes.get_last_error())
            log.seek(0)
            output = log.read()
            try:
                message = output.decode("utf-8")
            except UnicodeDecodeError:
                message = output.decode("oem", errors="replace")
            sys.stdout.write(message.replace("\r\n", "\n"))
            return status.value
    finally:
        if process.thread:
            kernel.CloseHandle(process.thread)
        if process.process:
            kernel.CloseHandle(process.process)
        user.CloseDesktop(desktop)


if __name__ == "__main__":
    sys.exit(run())
