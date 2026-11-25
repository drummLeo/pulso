using Pulso;

internal static class LinuxInstalledAppsChecks
{
    public static int Run()
    {
        var temp = Directory.CreateTempSubdirectory("pulso-linux-catalogo-");
        string? lockedDir = null;
        try
        {
            string root = temp.FullName;
            string customData = LinuxPaths.DataFolder(Path.Combine(root, "custom"));
            Should(customData == Path.GetFullPath(Path.Combine(root, "custom")), "Caminho personalizado de dados é preservado.");

            string xdgDataHome = Path.Combine(root, "xdg-home");
            string xdgPath = LinuxPaths.DataFolder(null, k => k == "XDG_DATA_HOME" ? xdgDataHome : null, Path.Combine(root, "home"));
            Should(xdgPath == Path.Combine(xdgDataHome, "PulsoFoco"), "XDG_DATA_HOME define o diretório de dados do usuário.");

            string fallbackData = LinuxPaths.DataFolder(null, _ => "", Path.Combine(root, "home"));
            Should(fallbackData == Path.Combine(root, "home", ".local", "share", "PulsoFoco"),
                "Sem XDG_DATA_HOME, o diretório padrão fica em ~/.local/share/PulsoFoco.");

            string userHome = Path.Combine(root, "user-home");
            string userDataHome = Path.Combine(root, "user-data");
            string sys1 = Path.Combine(root, "sys1");
            string sys2 = Path.Combine(root, "sys2");
            var appDirs = LinuxPaths.ApplicationDirectories(userDataHome, $"{sys1}:{sys2}:{sys1}:relativo/invalido", userHome);
            Should(appDirs.SequenceEqual([
                Path.Combine(userDataHome, "applications"),
                Path.Combine(sys1, "applications"),
                Path.Combine(sys2, "applications")
            ]), "Diretórios de aplicativos seguem prioridade XDG sem duplicatas nem caminhos relativos.");

            Should(Targets.App("plasmashell") == null && Targets.App("gnome-shell") == null && Targets.App("kwin_wayland") == null
                && Targets.App("pulso-linux") == null && Targets.App("foo/bar") == null && Targets.App("foo\\bar") == null
                && Targets.App("telegram-desktop") == "telegram-desktop",
                "Targets.App rejeita componentes críticos do Linux e separadores de caminho.");

            string userApps = Directory.CreateDirectory(Path.Combine(userDataHome, "applications")).FullName;
            string sys1Apps = Directory.CreateDirectory(Path.Combine(sys1, "applications")).FullName;
            string sys2Apps = Directory.CreateDirectory(Path.Combine(sys2, "applications")).FullName;
            string nestedSys = Directory.CreateDirectory(Path.Combine(sys1Apps, "kde")).FullName;

            // Sombreamento: usuário oculta entrada do sistema
            File.WriteAllText(Path.Combine(userApps, "oculto.desktop"), """
                [Desktop Entry]
                Type=Application
                Name=Oculto pelo Usuário
                Exec=/usr/bin/oculto
                NoDisplay=true
                """);
            File.WriteAllText(Path.Combine(sys1Apps, "oculto.desktop"), """
                [Desktop Entry]
                Type=Application
                Name=Deveria Ser Ocultado
                Exec=/usr/bin/oculto
                """);

            // Sombreamento: usuário substitui nome do sistema
            File.WriteAllText(Path.Combine(userApps, "editor.desktop"), """
                [Desktop Entry]
                Type=Application
                Name=Editor do Usuário
                Name[pt_BR]=Editor Favorito
                Exec="/opt/Meu Editor/meu-editor" %F
                Categories=TextEditor;
                """);
            File.WriteAllText(Path.Combine(sys1Apps, "editor.desktop"), """
                [Desktop Entry]
                Type=Application
                Name=Editor do Sistema
                Exec=/usr/bin/meu-editor %u
                Categories=TextEditor;
                """);

            // Env wrapper + subdiretório + Desktop Action ignorado
            File.WriteAllText(Path.Combine(nestedSys, "ide.desktop"), """
                [Desktop Entry]
                Type=Application
                Name=Estúdio Código
                Exec=env -u WAYLAND_DISPLAY QT_SCALE_FACTOR=1 /usr/bin/kate %U
                Categories=KDE;Development;IDE;
                OnlyShowIn=KDE;GNOME;

                [Desktop Action NewWindow]
                Name=Nova Janela
                Exec=/usr/bin/kate --new
                """);

            // Flatpak com --command e StartupWMClass
            File.WriteAllText(Path.Combine(sys1Apps, "com.heroicgameslauncher.hgl.desktop"), """
                [Desktop Entry]
                Type=Application
                Name=Heroic Games
                Exec=/usr/bin/flatpak run --branch=stable --arch=x86_64 --command=heroic-run --file-forwarding com.heroicgameslauncher.hgl @@u %u @@
                StartupWMClass=heroic
                Categories=Game;
                """);

            // Flatpak Chrome com --command=/app/bin/chrome
            File.WriteAllText(Path.Combine(sys2Apps, "com.google.Chrome.desktop"), """
                [Desktop Entry]
                Type=Application
                Name=Google Chrome
                Exec=/usr/bin/flatpak run --command=/app/bin/chrome com.google.Chrome %U
                StartupWMClass=google-chrome
                Categories=Network;WebBrowser;
                """);

            // Snap + gamemoderun + caminho Steam
            File.WriteAllText(Path.Combine(sys2Apps, "spotify.desktop"), """
                [Desktop Entry]
                Type=Application
                Name=Spotify
                Exec=snap run spotify %U
                Categories=Audio;Music;
                """);
            File.WriteAllText(Path.Combine(sys2Apps, "estrelas.desktop"), """
                [Desktop Entry]
                Type=Application
                Name=Estrelas
                Exec=gamemoderun /home/teste/.local/share/Steam/steamapps/common/Estrelas/estrelas
                """);

            // Entradas filtradas (Terminal, Hidden, URL/protocolo, TryExec ausente, NotShowIn, /usr/bin/false, instalador, interpretador)
            File.WriteAllText(Path.Combine(sys1Apps, "terminal.desktop"), "[Desktop Entry]\nType=Application\nName=Htop\nExec=htop\nTerminal=true\n");
            File.WriteAllText(Path.Combine(sys1Apps, "hidden.desktop"), "[Desktop Entry]\nType=Application\nName=Escondido\nExec=escondido\nHidden=true\n");
            File.WriteAllText(Path.Combine(sys1Apps, "jogo-steam.desktop"), "[Desktop Entry]\nType=Application\nName=Balatro\nExec=steam steam://rungameid/2379780\nCategories=Game;\n");
            File.WriteAllText(Path.Combine(sys1Apps, "webapp.desktop"), "[Desktop Entry]\nType=Application\nName=WebApp\nExec=google-chrome --app=https://exemplo.invalid\n");
            File.WriteAllText(Path.Combine(sys1Apps, "ausente.desktop"), "[Desktop Entry]\nType=Application\nName=Ausente\nTryExec=binario-inexistente\nExec=binario-inexistente\n");
            File.WriteAllText(Path.Combine(sys1Apps, "gnome-only.desktop"), "[Desktop Entry]\nType=Application\nName=Só GNOME\nExec=gnome-tool\nOnlyShowIn=GNOME;\n");
            File.WriteAllText(Path.Combine(sys1Apps, "not-kde.desktop"), "[Desktop Entry]\nType=Application\nName=Não KDE\nExec=outro-app\nNotShowIn=KDE;\n");
            File.WriteAllText(Path.Combine(sys1Apps, "falso.desktop"), "[Desktop Entry]\nType=Application\nName=Falso\nExec=/usr/bin/false\n");
            File.WriteAllText(Path.Combine(sys1Apps, "instalador.desktop"), "[Desktop Entry]\nType=Application\nName=Instalador\nExec=/opt/app/OficinaSetup-x64\n");
            File.WriteAllText(Path.Combine(sys1Apps, "script.desktop"), "[Desktop Entry]\nType=Application\nName=Script\nExec=python3 /opt/script.py\n");

            var catalog = new LinuxInstalledAppCatalog(CancellationToken.None, "KDE", cmd => cmd != "binario-inexistente");
            catalog.ReadApplicationsDirectory(userApps, 3);
            catalog.ReadApplicationsDirectory(sys1Apps, 2);
            catalog.ReadApplicationsDirectory(sys2Apps, 1);

            var scan = catalog.Result();
            Should(!scan.Partial, "Leitura normal de diretórios válidos não marca parcialidade.");
            Should(scan.Apps.Count == 6, $"Esperados 6 aplicativos descobertos, obtidos {scan.Apps.Count}.");

            DiscoveredApp Find(string process) => scan.Apps.Single(a => a.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase));
            Should(Find("meu-editor") is { DisplayName: "Editor Favorito", CategoryId: "work" },
                "Entrada do usuário sombreia a do sistema, usa Name[pt_BR] e mapeia TextEditor para Trabalho e estudo.");
            Should(Find("kate") is { DisplayName: "Estúdio Código", CategoryId: "development" },
                "Wrapper env é desembrulhado e Categories=Development;IDE prioriza Desenvolvimento sobre TextEditor.");
            Should(Find("heroic") is { DisplayName: "Heroic Games", CategoryId: "games" },
                "Flatpak com script wrapper usa StartupWMClass e cai na categoria Jogos.");
            Should(Find("chrome") is { DisplayName: "Google Chrome", CategoryId: "browsers" },
                "Flatpak com --command=/app/bin/chrome extrai o executável correto.");
            Should(Find("spotify") is { DisplayName: "Spotify", CategoryId: "audio" },
                "Snap extrai o executável e categoriza música corretamente.");
            Should(Find("estrelas") is { DisplayName: "Estrelas", CategoryId: "games" },
                "Gamemoderun é desembrulhado e caminho Steam identifica categoria Jogos.");

            Should(AppCategories.ForLinux("telegram-desktop") == "communication"
                && AppCategories.ForLinux("antigravity") == "development"
                && AppCategories.ForLinux("okular") == "reading"
                && AppCategories.ForLinux("libreoffice") == "work"
                && AppCategories.ForLinux("kmymoney") == "shopping"
                && AppCategories.ForLinux("desconhecido", null, null, ["AudioVideo", "Video"]) == "video"
                && AppCategories.ForLinux("desconhecido", null, null, ["AudioVideo", "Audio"]) == "audio"
                && AppCategories.ForLinux("desconhecido") == "other",
                "Classificação Linux combina tabela compartilhada, aliases Linux, caminhos e Categories Freedesktop.");

            // Teste de diretório ilegível -> Partial = true
            lockedDir = Directory.CreateDirectory(Path.Combine(root, "sem-permissao")).FullName;
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(lockedDir, UnixFileMode.None);
            if (!OperatingSystem.IsWindows() && Environment.UserName != "root")
            {
                var partialCatalog = new LinuxInstalledAppCatalog(CancellationToken.None, "KDE", _ => true);
                partialCatalog.ReadApplicationsDirectory(lockedDir, 1);
                Should(partialCatalog.Result().Partial, "Diretório sem permissão de leitura sinaliza Partial = true.");
            }

            try
            {
                LinuxInstalledApps.Scan(new CancellationToken(canceled: true));
                throw new InvalidOperationException("Varredura cancelada deve lançar OperationCanceledException.");
            }
            catch (OperationCanceledException) { }

            Console.WriteLine("OK  Catálogo Linux verifica XDG, .desktop, sombreamento, wrappers, categorias, parcialidade e cancelamento.");
            return 0;
        }
        finally
        {
            if (lockedDir != null && Directory.Exists(lockedDir))
            {
                try
                {
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(lockedDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch { }
            }
            if (temp.Parent?.FullName.TrimEnd(Path.DirectorySeparatorChar) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                && temp.Name.StartsWith("pulso-linux-catalogo-", StringComparison.Ordinal))
                temp.Delete(true);
        }
    }

    public static int CountInstalled()
    {
        var scan = Task.Run(() => LinuxInstalledApps.Scan(CancellationToken.None)).GetAwaiter().GetResult();
        Should(scan.Apps.All(a => Targets.App(a.ProcessName) == a.ProcessName && !string.IsNullOrWhiteSpace(a.DisplayName))
            && scan.Apps.Select(a => a.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == scan.Apps.Count,
            "O catálogo Linux real deve conter processos válidos e únicos.");
        Console.WriteLine($"Aplicativos Linux {scan.Apps.Count}, categorias {scan.Apps.Select(a => a.CategoryId).Distinct().Count()}, parcial {scan.Partial}.");
        return 0;
    }

    private static void Should(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
