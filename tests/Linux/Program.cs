using Pulso;

internal static class Program
{
    static int Main(string[] args) => TestProcess.Run(() => Execute(args));

    private static int Execute(string[] args)
    {
        if (args.Length == 0 || args is ["todos"])
        {
            if (LinuxInstalledAppsChecks.Run() != 0) return 1;
            if (LinuxWindowBlockerChecks.Run() != 0) return 1;
            if (LinuxBrowserLinkChecks.Run().GetAwaiter().GetResult() != 0) return 1;
            if (SingleInstanceChecks.Run() != 0) return 1;
            return 0;
        }
        if (args is ["catalogo"]) return LinuxInstalledAppsChecks.Run();
        if (args is ["catalogo-contagem"]) return LinuxInstalledAppsChecks.CountInstalled();
        if (args is ["janelas"]) return LinuxWindowBlockerChecks.Run();
        if (args is ["filtros-navegador"]) return LinuxBrowserLinkChecks.Run().GetAwaiter().GetResult();
        if (args is ["instancia"]) return SingleInstanceChecks.Run();
        if (args is ["kwin-teste"]) return CheckKWin();
        if (args.Length is >= 2 and <= 3 && args[0] == "navegador")
        {
            int port = args.Length == 3 ? int.Parse(args[2]) : BrowserLink.Port;
            return CheckBrowser(args[1], port).GetAwaiter().GetResult();
        }
        return 2;
    }

    private static async Task<int> CheckBrowser(string folder, int port)
    {
        var bridge = new BrowserLink(folder, port);
        var extension = bridge.PrepareExtension();
        await bridge.StartAsync();
        if (bridge.Error != null) throw new InvalidOperationException(bridge.Error);
        Console.WriteLine(extension);
        var data = new FocusData();
        string? command;
        while ((command = Console.ReadLine()) != null && command != "sair")
        {
            var now = DateTimeOffset.Now;
            switch (command)
            {
                case "iniciar":
                case "expirar":
                    data = data with
                    {
                        Sites = ["pulso.test"],
                        Active = new("Teste", now, now.AddSeconds(command == "expirar" ? 3 : 120), false)
                    };
                    bridge.Update(data);
                    Console.WriteLine("ok");
                    break;
                case "parar":
                    data = data with { Active = null };
                    bridge.Update(data);
                    Console.WriteLine("ok");
                    break;
                case "tema system" or "tema light" or "tema dark":
                    data = data with { Theme = command[5..] };
                    bridge.Update(data);
                    Console.WriteLine("ok");
                    break;
                case "estado":
                    Console.WriteLine(bridge.Connected);
                    break;
                case "recarregar-filtros":
                    Console.WriteLine(bridge.RequiresFilterReload ? "sim" : "nao");
                    break;
                case var filter when filter.StartsWith("filtro ", StringComparison.Ordinal):
                    if (filter.Split(' ') is not ["filtro", var category, var setting]
                        || category is not ("porn" or "gambling") || setting is not ("off" or "focus" or "always")) return 3;
                    var mode = setting switch { "always" => SiteFilterMode.Always, "focus" => SiteFilterMode.Focus, _ => SiteFilterMode.Off };
                    data = category == "porn" ? data with { PornFilter = mode } : data with { GamblingFilter = mode };
                    bridge.Update(data);
                    Console.WriteLine("ok");
                    break;
                case "aba":
                    Console.WriteLine(bridge.ForegroundSite("chrome") ?? bridge.ForegroundSite("msedge") ?? "-");
                    break;
                case "aviso-categoria":
                    Console.WriteLine(bridge.UsageTarget("chrome").App == null || bridge.UsageTarget("msedge").App == null ? "sim" : "nao");
                    break;
                case "medir":
                    bridge.UpdateUsage(["pulso.test", "sub.pulso.test", "liberado.test"], new Dictionary<string, DateTimeOffset>());
                    Console.WriteLine("ok");
                    break;
                case "cotas":
                    bridge.UpdateUsage(["pulso.test", "sub.pulso.test", "liberado.test"],
                        new Dictionary<string, DateTimeOffset> { ["pulso.test"] = now.AddSeconds(9), ["liberado.test"] = now.AddMinutes(2) });
                    Console.WriteLine("ok");
                    break;
                case "limpar":
                    bridge.UpdateUsage([], new Dictionary<string, DateTimeOffset>());
                    Console.WriteLine("ok");
                    break;
                default:
                    return 3;
            }
        }
        await bridge.CloseAsync();
        return 0;
    }

    private static int CheckKWin()
    {
        using var backend = KWinWaylandWindowBackend.TryCreate()
            ?? throw new InvalidOperationException("KWinWaylandWindowBackend não disponível nesta sessão.");
        var active = backend.ActiveWindow();
        var windows = backend.ListWindows();
        Console.WriteLine($"OK  KWin conectado: {windows.Count} janela(s) detectada(s), ativa={active}.");
        foreach (var win in windows)
        {
            if (win.SkipTaskbarOrShell) continue;
            string? primary = LinuxWindowMatcher.PrimaryName(win);
            Console.WriteLine($"  handle={win.Handle} pid={win.Pid} visible={win.Visible} primary={primary} exe={win.ExeName} cls={win.WmClassClass} desktop={win.DesktopFileName}");
        }
        return 0;
    }
}
