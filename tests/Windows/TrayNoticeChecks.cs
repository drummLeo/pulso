using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Pulso;

internal static class TrayNoticeChecks
{
    public static int Run()
    {
        var folder = Directory.CreateTempSubdirectory("pulso-aviso-teste-");
        var previousProfile = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", folder.FullName);
        System.Reflection.Assembly.SetEntryAssembly(typeof(MainWindow).Assembly);
        var app = new DesktopApp();
        app.InitializeComponent();
        try
        {
            PumpUntil(() => app.MainWindow is MainWindow { IsLoaded: true }, "A instância de teste precisa iniciar no perfil temporário.");
            app.MainWindow.Hide();
            const string target = "JanelaTeste";
            var now = DateTimeOffset.Now;
            if (now.Minute == 59 && now.Second >= 53)
            {
                Thread.Sleep(TimeSpan.FromSeconds(61 - now.Second));
                now = DateTimeOffset.Now;
            }
            var routine = new Routine { Name = "Trabalho", Days = 127, Minutes = 120,
                StartMinute = Math.Max(0, now.Hour * 60 + now.Minute - 1), HourMinutes = 1 };
            var data = UsageClock.Advance(new FocusData { Apps = [target], Routines = [routine] }, now, TimeZoneInfo.Local);
            data.Usage.ScheduleTicks[routine.Id] = TimeSpan.TicksPerMinute;
            var store = new FocusStore(folder.FullName);
            store.Save(data);
            if (UsageClock.BlockFor(store.Load(), UsageClock.AppKey(target), now, TimeZoneInfo.Local)?.Title != routine.Name)
                throw new InvalidOperationException("O perfil de teste precisa iniciar com o saldo do agendamento esgotado.");
            bool attempt = false;
            int samples = 0;
            var main = new MainWindow(store, new BrowserLink(folder.FullName, 0), blocked =>
            {
                samples++;
                if (!attempt || !blocked.Contains(target)) return null;
                attempt = false;
                return target;
            });
            main.Show();
            PumpUntil(() => main.IsLoaded && samples >= 2, "A tela precisa iniciar o monitor de janelas.");
            main.Close();
            if (main.IsVisible) throw new InvalidOperationException("A janela principal precisa estar recolhida na bandeja.");
            Window? Notice() => app.Windows.OfType<Window>().SingleOrDefault(w => w.Title == "Aplicativo em pausa · Pulso");
            attempt = true;
            PumpUntil(() => Notice() != null, "O primeiro bloqueio na bandeja precisa abrir o aviso.");
            var notice = Notice()!;
            var details = Descendants(notice).OfType<TextBlock>().Select(t => t.Text).ToArray();
            if (!notice.Topmost || !details.Any(t => t.Contains(target)) || !details.Any(t => t.Contains(routine.Name))
                || !details.Any(t => t.Contains("às ")) || main.IsVisible)
                throw new InvalidOperationException("O aviso na bandeja precisa mostrar o app, o agendamento e a liberação.");
            Click(Descendants(notice).OfType<Button>().Single(b => Equals(b.Content, "Entendi")));
            PumpUntil(() => Notice() == null, "Entendi precisa dispensar o aviso.");
            attempt = true;
            PumpUntil(() => Notice() != null, "Uma nova tentativa precisa abrir outro aviso.");
            var rail = (StackPanel)main.FindName("NavItems");
            Click(rail.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Agendamentos"));
            var page = (StackPanel)main.FindName("ContentPage");
            Click(Descendants(page).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Desativar Trabalho"));
            PumpUntil(() => Notice() == null, "Desativar a agenda precisa fechar o aviso.");
            if (main.IsVisible) throw new InvalidOperationException("O aviso reabriu a janela principal.");
            Console.WriteLine("OK  Aviso na bandeja exibe app, motivo e liberação, aceita Entendi e desaparece ao desativar a agenda.");
            return 0;
        }
        finally
        {
            app.Shutdown();
            Environment.SetEnvironmentVariable("LOCALAPPDATA", previousProfile);
            if (folder.Parent?.FullName.TrimEnd('\\') == Path.GetTempPath().TrimEnd('\\') && folder.Name.StartsWith("pulso-aviso-teste-"))
                folder.Delete(true);
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }

    private static void PumpUntil(Func<bool> condition, string failure)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed.TotalSeconds > 6) throw new TimeoutException(failure);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
