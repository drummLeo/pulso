using Pulso;
using System.Windows;

internal static class Program
{
    [STAThread]
    static int Main(string[] args) => TestProcess.Run(() => Execute(args));

    private static int Execute(string[] args)
    {
        if (args is ["catalogo"]) return InstalledAppsChecks.Run();
        if (args is ["catalogo-contagem"]) return InstalledAppsChecks.CountInstalled();
        if (args is ["catalogo-interface"]) return AppPickerChecks.Run();
        if (args is ["aviso-bandeja"]) return TrayNoticeChecks.Run();
        if (args is ["tema"]) return ThemeChecks.Run();
        if (args is ["tema", var imageFolder]) return ThemeChecks.Run(imageFolder);
        if (args is ["interface", var executable, var preview]) return DesktopFlow.Run(executable, preview);
        if (args is ["limite", var program]) return DesktopFlow.CheckQuota(program);
        if (args is ["limite-agendado", var scheduledProgram]) return DesktopFlow.CheckQuota(scheduledProgram, scheduled: true);
        if (args is ["janela"])
        {
            var app = new Application();
            var window = new Window { Title = "Pulso · janela de verificação", Width = 230, Height = 110,
                Left = -8000, Top = 5, ShowActivated = false, Content = "Janela usada pelo teste de foco." };
            using var foreground = DesktopFlow.ObserveTestWindow(window);
            return app.Run(window);
        }
        if (args.Length is < 2 or > 3 || args[0] != "navegador") return 2;
        int port = args.Length == 3 ? int.Parse(args[2]) : BrowserLink.Port;
        return CheckBrowser(args[1], port).GetAwaiter().GetResult();
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
                    data = data with { Sites = ["pulso.test"], Active = new("Teste", now,
                        now.AddSeconds(command == "expirar" ? 3 : 120), false) };
                    bridge.Update(data);
                    Console.WriteLine("ok"); break;
                case "parar":
                    data = data with { Active = null };
                    bridge.Update(data); Console.WriteLine("ok"); break;
                case "tema system" or "tema light" or "tema dark":
                    data = data with { Theme = command[5..] };
                    bridge.Update(data); Console.WriteLine("ok"); break;
                case "estado": Console.WriteLine(bridge.Connected); break;
                case "aba": Console.WriteLine(bridge.ForegroundSite("chrome") ?? bridge.ForegroundSite("msedge") ?? "-"); break;
                case "medir":
                    bridge.UpdateUsage(["pulso.test", "sub.pulso.test", "liberado.test"], new Dictionary<string, DateTimeOffset>());
                    Console.WriteLine("ok"); break;
                case "cotas":
                    bridge.UpdateUsage(["pulso.test", "sub.pulso.test", "liberado.test"],
                        new Dictionary<string, DateTimeOffset> { ["pulso.test"] = now.AddSeconds(9), ["liberado.test"] = now.AddMinutes(2) });
                    Console.WriteLine("ok"); break;
                case "limpar":
                    bridge.UpdateUsage([], new Dictionary<string, DateTimeOffset>());
                    Console.WriteLine("ok"); break;
                default: return 3;
            }
        }
        await bridge.CloseAsync();
        return 0;
    }
}
