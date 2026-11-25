using Pulso;

internal static class LinuxWindowBlockerChecks
{
    private sealed class FakeWindowBackend : ILinuxWindowBackend
    {
        public bool Available { get; set; } = true;
        public ulong Active { get; set; }
        public List<LinuxWindowInfo> Windows { get; } = [];
        public List<ulong> Minimized { get; } = [];

        public ulong ActiveWindow() => Active;
        public IReadOnlyList<LinuxWindowInfo> ListWindows() => Windows;
        public bool Minimize(ulong handle)
        {
            Minimized.Add(handle);
            return true;
        }
        public void Dispose() { }
    }

    public static int Run()
    {
        const int selfPid = 9999;
        var backend = new FakeWindowBackend();
        backend.Windows.AddRange([
            new(1, 101, true, false, false, "chrome", "chrome", "google-chrome", "Google-chrome"),
            new(2, 102, true, false, false, "telegram-desktop", "telegram-deskto", "TelegramDesktop", "TelegramDesktop"),
            new(3, 103, true, false, false, "python3", "python3", "lutris", "Lutris"),
            new(4, 104, false, false, false, "discord", "discord", "discord", "discord"),
            new(5, 105, true, true, false, "steam", "steam", "steam", "steam"),
            new(6, 106, true, false, true, "waybar", "waybar", "waybar", "waybar"),
            new(7, selfPid, true, false, false, "Pulso", "Pulso", "Pulso", "Pulso"),
            new(8, 108, true, false, false, "firefox-esr", "firefox-esr", "Navigator", "firefox"),
            new(9, 109, true, false, false, null, null, "firefox_firefox", "firefox_firefox", "firefox_firefox"),
            new(10, 110, true, false, false, null, null, "com.google.Chrome", "Google-chrome", "com.google.Chrome.desktop")
        ]);

        var blocker = new LinuxWindowBlocker(backend, selfPid);
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "google-chrome", "telegram-desktop", "lutris", "firefox" };

        backend.Active = 2;
        Should(blocker.ForegroundProcess(listed) == "telegram-desktop",
            "Janela ativa com comm truncado em 15 chars casa exatamente com o processo listado.");

        backend.Active = 1;
        Should(blocker.ForegroundProcess(Array.Empty<string>()) == "chrome"
            && blocker.ForegroundProcess(listed) == "google-chrome",
            "Navegador ativo identifica medição de site mesmo fora da lista e preserva nome listado quando presente.");

        backend.Active = 3;
        Should(LinuxWindowMatcher.PrimaryName(backend.Windows[2]) == "lutris"
            && blocker.ForegroundProcess(listed) == "lutris",
            "Interpretador genérico (python3) usa WM_CLASS da janela para identificar o app.");

        backend.Active = 8;
        Should(blocker.ForegroundProcess(listed) == "firefox",
            "Sufixo -esr casa com a regra base firefox.");

        backend.Active = 9;
        Should(blocker.ForegroundProcess(listed) == "firefox"
            && blocker.ForegroundProcess(Array.Empty<string>()) == "firefox"
            && LinuxWindowMatcher.PrimaryName(backend.Windows[8]) == "firefox",
            "Snap firefox_firefox sem /proc/exe casa com firefox na lista e identifica medição de sites.");

        backend.Active = 10;
        Should(blocker.ForegroundProcess(listed) == "google-chrome"
            && blocker.ForegroundProcess(Array.Empty<string>()) == "chrome",
            "Flatpak com.google.Chrome sem /proc/exe casa com google-chrome e identifica medição de Chrome.");

        backend.Active = 5;
        Should(blocker.ForegroundProcess(listed) == null,
            "Janela já minimizada/oculta não gera uso em primeiro plano.");

        backend.Active = 7;
        Should(blocker.ForegroundProcess(listed) == null,
            "A própria janela do Pulso é ignorada na detecção.");

        backend.Active = 2;
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "google-chrome", "telegram-desktop", "discord", "steam" };
        string? interrupted = blocker.Apply(selected);
        Should(interrupted == "telegram-desktop"
            && backend.Minimized.SequenceEqual([1UL, 2UL, 10UL]),
            "Apply minimiza apenas janelas visíveis não minimizadas (inclusive Flatpak) e retorna a janela ativa interrompida.");

        backend.Minimized.Clear();
        backend.Active = 8;
        Should(blocker.Apply(selected) == null && backend.Minimized.SequenceEqual([1UL, 2UL, 10UL]),
            "Apply retorna null quando apenas janelas em segundo plano são minimizadas.");

        backend.Minimized.Clear();
        backend.Active = 9;
        var firefoxSelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "firefox" };
        Should(blocker.Apply(firefoxSelected) == "firefox" && backend.Minimized.SequenceEqual([8UL, 9UL]),
            "Apply minimiza janelas Wayland/Snap do Firefox e retorna firefox.");

        var openApps = blocker.OpenApps();
        Should(openApps.SequenceEqual(["chrome", "firefox", "firefox-esr", "lutris", "steam", "telegram-desktop"]),
            "OpenApps lista janelas abertas únicas (inclusive minimizadas) ignorando invisíveis, docks e o próprio Pulso.");

        var snapshot = KWinSnapshotParser.Parse("""
            {
              "active": "{ff-1}",
              "windows": [
                {"id": "{ff-1}", "pid": 401, "minimized": false, "skip": false, "name": "firefox_firefox", "cls": "firefox", "desktop": "firefox_firefox"},
                {"id": "{ch-2}", "pid": 402, "minimized": true, "skip": false, "name": "google-chrome", "cls": "Google-chrome", "desktop": "com.google.Chrome"},
                {"id": "{dk-3}", "pid": 403, "minimized": false, "skip": true, "name": "plasmashell", "cls": "plasmashell", "desktop": "org.kde.plasmashell"}
              ]
            }
            """, pid => pid == 402 ? ("chrome", "chrome") : (null, null));
        Should(snapshot.Windows.Count == 3
            && snapshot.ActiveHandle == snapshot.Windows[0].Handle
            && snapshot.Windows[0] is { Visible: true, Hidden: false, DesktopFileName: "firefox_firefox" }
            && snapshot.Windows[1] is { Visible: false, Hidden: true, ExeName: "chrome" }
            && snapshot.Windows[2].SkipTaskbarOrShell,
            "KWinSnapshotParser interpreta janelas ativas, minimizadas, docks e desktopFileName do Wayland.");

        var waylandMixed = LinuxSessionInfo.Detect(k => k switch
        {
            "XDG_SESSION_TYPE" => "wayland",
            "WAYLAND_DISPLAY" => "wayland-0",
            "XDG_CURRENT_DESKTOP" => "KDE",
            _ => null
        }, hasX11: true, hasKWinWayland: false);
        Should(waylandMixed is { IsWayland: true, HasX11: true, Desktop: "KDE", HasFullWindowControl: false },
            "Sessão Wayland sem KWin detecta ausência de controle completo de janelas nativas.");

        var kdeWayland = LinuxSessionInfo.Detect(k => k switch
        {
            "XDG_SESSION_TYPE" => "wayland",
            "WAYLAND_DISPLAY" => "wayland-0",
            "XDG_CURRENT_DESKTOP" => "KDE",
            _ => null
        }, hasX11: true, hasKWinWayland: true);
        Should(kdeWayland is { IsWayland: true, HasKWinWayland: true, HasFullWindowControl: true },
            "Sessão KDE Wayland com KWin ativo habilita controle completo de janelas.");

        var x11Only = LinuxSessionInfo.Detect(k => k switch
        {
            "XDG_SESSION_TYPE" => "x11",
            "DISPLAY" => ":0",
            "XDG_CURRENT_DESKTOP" => "XFCE",
            _ => null
        }, hasX11: true);
        Should(x11Only is { IsWayland: false, HasX11: true, Desktop: "XFCE", HasFullWindowControl: true },
            "Sessão X11 pura possui controle completo de janelas.");

        Console.WriteLine("OK  Bloqueador de janelas Linux verifica correspondência de processos, minimização, janelas abertas e sessão X11/Wayland.");
        return 0;
    }

    private static void Should(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
