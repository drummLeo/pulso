using System.IO;
using System.Runtime.InteropServices;
using Pulso;

internal static class InstalledAppsChecks
{
    internal static int Run()
    {
        var folder = Directory.CreateTempSubdirectory("pulso-catalogo-teste-");
        try
        {
            string programs = Directory.CreateDirectory(Path.Combine(folder.FullName, "Menu", "Apps")).FullName;
            string installs = Directory.CreateDirectory(Path.Combine(folder.FullName, "Apps")).FullName;
            string Executable(string relative, string source = "winver.exe")
            {
                string path = Path.Combine(installs, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(Path.Combine(Environment.SystemDirectory, source), path);
                return path;
            }
            string office = Executable("WINWORD.exe");
            string browser = Executable("chrome.exe");
            string game = Executable(@"Biblioteca\steamapps\common\Estrelas\Estrelas.exe");
            Shortcut(programs, "Oficina Fictícia", office);
            Shortcut(programs, "Página de exemplo", browser, "https://example.invalid/");
            File.WriteAllText(Path.Combine(programs, "Exemplo.url"), "[InternetShortcut]\r\nURL=https://example.invalid/");
            Shortcut(programs, "Auxiliar", Executable("rundll32.exe"));
            Shortcut(programs, "Instalação", Executable("setup.exe"));
            Shortcut(programs, "Assistente de instalação", Executable("OficinaSetup-x64.exe"));
            Shortcut(programs, "Console", Executable("ConsoleFicticio.exe", "where.exe"));
            Shortcut(programs, "Alvo ausente", Path.Combine(installs, "Ausente.exe"));

            string updater = Executable(@"Conversas\Update.exe");
            Executable(@"Conversas\app-1.2.0\Discord.exe", "where.exe");
            Executable(@"Conversas\app-1.10.0\Discord.exe");
            Shortcut(programs, "Conversas", updater, "--processStart \"Discord.exe\"");
            Executable("ForaDaPasta.exe");
            Shortcut(programs, "Fora da pasta", updater, @"--processStart ..\ForaDaPasta.exe");

            string package = Directory.CreateDirectory(Path.Combine(installs, "Pacote")).FullName;
            Executable(@"Pacote\bin\LeitorLocal.exe");
            Executable(@"Pacote\bin\Ignorado.exe");
            File.WriteAllText(Path.Combine(package, "AppxManifest.xml"), """
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Applications>
                    <Application Id="Leitor" Executable="bin\LeitorLocal.exe"/>
                    <Application Id="Auxiliar" Executable="bin\Ignorado.exe"/>
                    <Application Id="Fora" Executable="..\ForaDaPasta.exe"/>
                  </Applications>
                </Package>
                """);

            const string core = "Windows.UI.Core.CoreWindow";
            const string frame = "ApplicationFrameHost";
            string? Name(int pid) => pid switch { 1 => frame, 2 => "UsoFicticio", 3 => "OutroApp", 4 => "explorer", _ => null };
            string? Owner(int pid, params (int Pid, string ClassName)[] children) =>
                WindowBlocker.ResolveWindowOwner(IntPtr.Zero, pid, Name, _ => children);
            Should(Owner(1, (1, core), (3, "OutraClasse"), (2, core), (2, core)) == "UsoFicticio" &&
                Owner(1, (3, core)) == "OutroApp", "Cada janela hospedada usa apenas o PID de sua própria CoreWindow.");
            Should(Owner(1, (2, core), (3, core)) == null && Owner(1, (3, "OutraClasse")) == null && Owner(1, (4, core)) == null,
                "Um host sem aplicativo inequívoco e permitido não pode ser contado nem minimizado.");
            Should(Owner(3, (2, core)) == "OutroApp", "Uma janela comum conserva o processo proprietário.");

            var catalog = new InstalledAppCatalog(CancellationToken.None);
            catalog.ReadAppPath('"' + office + '"');
            catalog.ReadAppPath(game);
            catalog.ReadAppPath(@"\\example.invalid\apps\Remoto.exe");
            catalog.ReadAppPath("https://example.invalid/Remoto.exe");
            catalog.ReadStartMenu(Path.Combine(folder.FullName, "Menu"));
            catalog.ReadPackageApplication(package, "Leitor", "Kindle");
            catalog.ReadPackageApplication(package, "Fora", "Fora");
            Should(!catalog.Result().Partial, "Entradas obsoletas e atalhos de internet são ignorados normalmente.");
            var apps = catalog.Result().Apps;
            Should(apps.Count == 4, "Somente os quatro executáveis interativos previstos entram na lista.");
            DiscoveredApp Find(string process) => apps.Single(a => a.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase));
            Should(Find("WINWORD") is { DisplayName: "Oficina Fictícia", CategoryId: "work" },
                "Atalho e App Paths para o mesmo processo preservam o nome do menu em um único item.");
            Should(Find("Discord") is { DisplayName: "Conversas", CategoryId: "communication" },
                "O atalho do atualizador aponta para o executável da versão instalada.");
            Should(Find("Estrelas").CategoryId == "games", "O caminho de uma biblioteca Steam identifica um jogo sem regra nominal.");
            Should(Find("LeitorLocal") is { DisplayName: "Kindle", CategoryId: "reading" },
                "A entrada selecionada no manifesto preserva o executável e o nome da loja.");
            Should(apps.Select(a => a.CategoryId).SequenceEqual(["communication", "games", "reading", "work"]),
                "A apresentação segue a ordem da tabela de categorias.");
            Should(AppCategories.ForWindows("cHrOmE.ExE", "Discord") == "browsers" &&
                AppCategories.ForWindows("Desconhecido", "Exemplo") == "other" &&
                AppCategories.Label("categoria-antiga") == "Outros" && AppCategories.Order("categoria-antiga") == AppCategories.Order("other"),
                "As regras ignoram caixa e extensão, priorizam o processo e aceitam uma categoria desconhecida.");

            var unavailable = new InstalledAppCatalog(CancellationToken.None);
            unavailable.ReadStartMenu(Path.Combine(folder.FullName, "MenuAusente"));
            Should(unavailable.Result().Partial, "Um menu de programas indisponível sinaliza a cobertura parcial.");
            string broken = Directory.CreateDirectory(Path.Combine(folder.FullName, "PacoteInvalido")).FullName;
            File.WriteAllText(Path.Combine(broken, "AppxManifest.xml"), "<Package>");
            catalog.ReadPackageApplication(broken, "App", "Exemplo");
            Should(catalog.Result() is { Partial: true, Apps.Count: 4 },
                "Uma fonte ilegível sinaliza resultado parcial e conserva os outros itens.");
            try
            {
                InstalledApps.Scan(new CancellationToken(canceled: true));
                throw new InvalidOperationException("A varredura cancelada deve terminar sem retornar um catálogo.");
            }
            catch (OperationCanceledException) { }
            Console.WriteLine("OK  Catálogo verifica fontes locais, executáveis, deduplicação, categorias, varredura parcial e cancelamento.");
            return 0;
        }
        finally
        {
            if (folder.Parent?.FullName.TrimEnd('\\') == Path.GetTempPath().TrimEnd('\\') && folder.Name.StartsWith("pulso-catalogo-teste-"))
                folder.Delete(true);
        }
    }

    internal static int CountInstalled()
    {
        var scan = Task.Run(() => InstalledApps.Scan(CancellationToken.None)).GetAwaiter().GetResult();
        Should(scan.Apps.All(a => Targets.App(a.ProcessName) == a.ProcessName && !string.IsNullOrWhiteSpace(a.DisplayName))
            && scan.Apps.Select(a => a.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == scan.Apps.Count,
            "O catálogo deve conter processos válidos e únicos.");
        Console.WriteLine($"Aplicativos {scan.Apps.Count}, categorias {scan.Apps.Select(a => a.CategoryId).Distinct().Count()}, parcial {scan.Partial}.");
        return 0;
    }

    private static void Shortcut(string folder, string name, string target, string arguments = "")
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(Path.Combine(folder, name + ".lnk"));
            try
            {
                shortcut.TargetPath = target;
                shortcut.Arguments = arguments;
                shortcut.Save();
            }
            finally { Marshal.ReleaseComObject(shortcut); }
        }
        finally { Marshal.ReleaseComObject(shell); }
    }

    private static void Should(bool value, string failure)
    {
        if (!value) throw new InvalidOperationException(failure);
    }
}
