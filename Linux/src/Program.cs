using System.Globalization;
using Avalonia;

namespace Pulso;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;

        string? customDataDir = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--data-dir")
            {
                customDataDir = args[i + 1];
                break;
            }
        }

        string folder = LinuxPaths.DataFolder(customDataDir);
        Action? reopen = null;
        bool reopenPending = false;
        object gate = new();

        using var guard = SingleInstanceGuard.TryAcquire(folder, () =>
        {
            Action? target;
            lock (gate)
            {
                target = reopen;
                if (target == null) reopenPending = true;
            }
            target?.Invoke();
        });
        if (guard == null) return 0;

        DesktopApp.DataFolderPath = folder;
        DesktopApp.RegisterReopen = handler =>
        {
            bool runNow;
            lock (gate)
            {
                reopen = handler;
                runNow = reopenPending;
                reopenPending = false;
            }
            if (runNow) handler();
        };

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            DesktopApp.RegisterReopen = null;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DesktopApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
