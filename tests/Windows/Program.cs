using Pulso;
using System.Windows;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args is ["interface", var executable, var preview]) return DesktopFlow.Run(executable, preview);
        if (args is ["limite", var program]) return DesktopFlow.CheckQuota(program);
        if (args is ["janela"])
        {
            var app = new Application();
            return app.Run(new Window { Title = "Pulso · janela de verificação", Width = 230, Height = 110,
                Left = -8000, Top = 5, ShowActivated = false, Content = "Janela usada pelo teste de foco." });
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
        string? command;
        while ((command = Console.ReadLine()) != null && command != "sair")
        {
            var now = DateTimeOffset.Now;
            switch (command)
            {
                case "iniciar":
                case "expirar":
                    bridge.Update(new FocusData { Sites = ["pulso.test"], Active = new("Teste", now,
                        now.AddSeconds(command == "expirar" ? 3 : 120), false) });
                    Console.WriteLine("ok"); break;
                case "parar":
                    bridge.Update(new()); Console.WriteLine("ok"); break;
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
