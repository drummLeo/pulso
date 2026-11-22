using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace Pulso;

public partial class DesktopApp : Application
{
    private Mutex? singleton;
    private bool owns;
    private EventWaitHandle? show;
    private RegisteredWaitHandle? listener;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        try
        {
            var folder = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulsoFoco");
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).ToUpperInvariant())))[..16];
            var key = @"Local\PulsoFoco-" + suffix;
            singleton = new Mutex(true, key, out owns);
            show = new EventWaitHandle(false, EventResetMode.AutoReset, key + "-abrir");
            if (!owns) { show.Set(); Shutdown(); return; }
            var window = new MainWindow(new FocusStore(folder), new BrowserLink(folder));
            MainWindow = window;
            listener = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => Dispatcher.BeginInvoke(window.Reopen), null, -1, false);
            window.Show();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            MessageBox.Show("O Pulso não conseguiu abrir.\n\n" + ex.Message, "Pulso", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        listener?.Unregister(null);
        show?.Dispose();
        if (owns) singleton?.ReleaseMutex();
        singleton?.Dispose();
        base.OnExit(e);
    }
}
