using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Pulso;

public sealed partial class DesktopApp : Application
{
    internal static string? DataFolderPath { get; set; }
    internal static Action<Action>? RegisterReopen { get; set; }

    private SingleInstanceGuard? ownGuard;
    private LinuxWindowBlocker? windowBlocker;
    private MainWindow? mainWindow;
    private TrayIcon? trayIcon;
    private PosixSignalRegistration? sigTerm, sigInt;
    private string themePreference = "system";
    private bool shutdownStarted;

    internal WindowIcon? AppIcon { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    internal void SetTheme(string preference)
    {
        themePreference = preference;
        LinuxTheme.Apply(this, preference);
    }

    internal void SetTrayTooltip(string text)
    {
        if (trayIcon != null) trayIcon.ToolTipText = text;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var args = desktop.Args ?? [];
        bool startMinimized = args.Any(a => a is "--minimized" or "--oculto");
        string? customDataDir = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--data-dir")
            {
                customDataDir = args[i + 1];
                break;
            }
        }

        try
        {
            using (var iconStream = AssetLoader.Open(new Uri("avares://Pulso/Assets/pulso.png")))
                AppIcon = new WindowIcon(iconStream);

            string folder = DataFolderPath ?? LinuxPaths.DataFolder(customDataDir);
            if (RegisterReopen == null)
            {
                ownGuard = SingleInstanceGuard.TryAcquire(folder, () =>
                    Dispatcher.UIThread.Post(() => mainWindow?.Reopen()));
                if (ownGuard == null)
                {
                    Environment.ExitCode = 0;
                    return;
                }
            }

            if (PlatformSettings != null)
            {
                PlatformSettings.ColorValuesChanged += (_, _) =>
                {
                    if (themePreference == "system")
                        Dispatcher.UIThread.Post(() => LinuxTheme.Apply(this, themePreference));
                };
            }

            windowBlocker = new LinuxWindowBlocker();
            mainWindow = new MainWindow(new FocusStore(folder), new BrowserLink(folder), windowBlocker, () => ShutdownApp(desktop));
            RegisterReopen?.Invoke(() => Dispatcher.UIThread.Post(() => mainWindow?.Reopen()));
            if (!startMinimized) desktop.MainWindow = mainWindow;

            var menu = new NativeMenu();
            var openItem = new NativeMenuItem("Abrir o Pulso");
            openItem.Click += (_, _) => mainWindow?.Reopen();
            var quitItem = new NativeMenuItem("Sair do Pulso");
            quitItem.Click += (_, _) => _ = mainWindow?.QuitAsync();
            menu.Items.Add(openItem);
            menu.Items.Add(quitItem);

            trayIcon = new TrayIcon
            {
                Icon = AppIcon,
                ToolTipText = "Pulso · Foco no PC",
                Menu = menu,
                IsVisible = true
            };
            trayIcon.Clicked += (_, _) => mainWindow?.Reopen();
            TrayIcon.SetIcons(this, [trayIcon]);

            sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
            {
                ctx.Cancel = true;
                Dispatcher.UIThread.Post(() => _ = ForceExitAsync(desktop));
            });
            sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
            {
                ctx.Cancel = true;
                Dispatcher.UIThread.Post(() => _ = ForceExitAsync(desktop));
            });

            _ = mainWindow.StartBackgroundAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Cleanup();
            desktop.Shutdown(1);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task ForceExitAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (shutdownStarted) return;
        if (mainWindow != null) await mainWindow.ForceShutdownAsync();
        ShutdownApp(desktop);
    }

    private void ShutdownApp(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (shutdownStarted) return;
        shutdownStarted = true;
        Cleanup();
        desktop.Shutdown(0);
    }

    private void Cleanup()
    {
        sigTerm?.Dispose();
        sigInt?.Dispose();
        if (trayIcon != null)
        {
            trayIcon.IsVisible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }
        windowBlocker?.Dispose();
        windowBlocker = null;
        ownGuard?.Dispose();
        ownGuard = null;
    }
}
