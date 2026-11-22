using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Pulso;

public partial class MainWindow : Window
{
    private readonly FocusStore store;
    private readonly BrowserLink bridge;
    private FocusData data;
    private HashSet<string> blocked = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DateTimeOffset> pausedSites = new();
    private string[] watchedSites = [];
    private readonly UsageMeter meter = new();
    private readonly Stopwatch uptime = Stopwatch.StartNew();
    private WindowsUsageSession? windowsSession;
    private bool usageDirty;
    private TimeSpan nextCheckpoint;
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Forms.NotifyIcon tray;
    private readonly Dictionary<string, (Button Button, TextBlock Label)> nav = new();
    private string page = "home";
    private string lastStatus = "";
    private bool closing, trayHint;
    private DateTimeOffset noticeUntil, nextSaveTry;
    private const string HomeId = "home", RulesId = "rules", SchedulesId = "schedules", HistoryId = "history", BrowserId = "browser";
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static TimeZoneInfo Zone => TimeZoneInfo.Local;

    public MainWindow(FocusStore store, BrowserLink bridge)
    {
        this.store = store;
        this.bridge = bridge;
        data = store.Load();
        InitializeComponent();
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Width, work.Width);
        Height = Math.Min(Height, work.Height);
        AddNav(NavItems, HomeId, "\uE916", "Visão geral");
        AddNav(NavItems, RulesId, "\uE8D7", "Lista de bloqueio");
        AddNav(NavItems, SchedulesId, "\uE787", "Agendamentos");
        AddNav(NavItems, HistoryId, "\uE81C", "Histórico");
        AddNav(BrowserNav, BrowserId, "\uE774", "Navegadores");
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir o Pulso", null, (_, _) => Dispatcher.Invoke(Reopen));
        menu.Items.Add("Sair do Pulso", null, (_, _) => Dispatcher.Invoke(Quit));
        using var stream = Application.GetResourceStream(new Uri("pulso.ico", UriKind.Relative))!.Stream;
        using var original = new System.Drawing.Icon(stream);
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(original, 32, 32), Text = "Pulso · Foco no PC", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reopen);
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            windowsSession = new WindowsUsageSession(source, meter.Reset, () => CheckpointUsage(true));
            windowsSession.WatchForeground(data.Usage.Enabled);
        };
        Closing += ClosingWindow;
        Application.Current.SessionEnding += (_, _) =>
        {
            closing = true;
            ticker.Stop();
            if (data.Active != null) Commit(FocusClock.Stop(data, DateTimeOffset.Now, Zone));
            CheckpointUsage(true);
        };
        Closed += (_, _) =>
        {
            ticker.Stop();
            windowsSession?.Dispose();
            CheckpointUsage(true);
            tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); menu.Dispose();
        };
        Navigate(HomeId);
        ApplyState();
        ticker.Tick += (_, _) => Tick();
        Loaded += async (_, _) =>
        {
            Tick();
            ticker.Start();
            await bridge.StartAsync();
            RefreshStatus();
        };
    }

    private void AddNav(StackPanel parent, string id, string glyph, string label)
    {
        var button = Ui.Button(label, () => Navigate(id));
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 2, 0, 2);
        button.Padding = new Thickness(8, 8, 8, 8);
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Width = 140 };
        var icon = Ui.Text(glyph);
        icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
        icon.FontSize = 16; icon.Width = 24; icon.VerticalAlignment = VerticalAlignment.Center;
        var text = Ui.Text(label);
        text.FontSize = 13; text.Margin = new Thickness(5, 0, 0, 0);
        content.Children.Add(icon); content.Children.Add(text);
        button.Content = content;
        parent.Children.Add(button);
        nav[id] = (button, text);
    }

    private void Navigate(string id)
    {
        page = id;
        foreach (var (key, item) in nav)
        {
            item.Button.Background = Ui.Paint(key == id ? "#415175" : "#232E4D");
            item.Button.Foreground = Ui.Paint(key == id ? "#FFFFFF" : "#DBDFF1");
        }
        NoticeBox.Visibility = Visibility.Collapsed;
        (PageTitle.Text, PageSubtitle.Text) = id switch
        {
            RulesId => ("Lista de bloqueio", "Escolha os aplicativos e sites para suas sessões de foco e configure os limites de uso."),
            SchedulesId => ("Agendamentos", "Programe sessões que se repetem nos dias e horários escolhidos."),
            HistoryId => ("Histórico", "Consulte as sessões registradas neste computador."),
            BrowserId => ("Navegadores", "Conecte o Chrome ou o Edge para medir o tempo de uso e colocar sites em pausa."),
            _ => ("Sessão de foco", DateLine())
        };
        Scroller.ScrollToTop();
        Home.Visibility = id == HomeId ? Visibility.Visible : Visibility.Collapsed;
        ContentPage.Visibility = id == HomeId ? Visibility.Collapsed : Visibility.Visible;
        RenderPage();
        Responsive();
        RefreshStatus();
    }
    private void RenderPage()
    {
        ContentPage.Children.Clear();
        usageLabels.Clear();
        siteState = browserState = historyToday = null;
        siteInput = null;
        switch (page)
        {
            case RulesId: RulesPage(); break;
            case SchedulesId: SchedulesPage(); break;
            case HistoryId: HistoryPage(); break;
            case BrowserId: BrowserPage(); break;
        }
    }
    private static string DateLine()
    {
        string s = DateTime.Now.ToString("dddd, d 'de' MMMM", Pt);
        return char.ToUpper(s[0], Pt) + s[1..];
    }
    public void Reopen()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    private void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        Hide();
        if (trayHint) return;
        trayHint = true;
        tray.ShowBalloonTip(3000, "Pulso na área de notificação", "As sessões e os limites continuam ativos. Dê dois cliques no ícone para voltar.", Forms.ToolTipIcon.Info);
    }
    private async void Quit()
    {
        if (closing) return;
        if (data.Active != null)
        {
            Reopen();
            if (!FormWindow.Confirm(this, "Encerrar a sessão e sair?", "A contagem de uso e os agendamentos serão retomados quando você reabrir o Pulso.", "Encerrar e sair")) return;
            if (!Commit(FocusClock.Stop(data, DateTimeOffset.Now, Zone))) return;
        }
        if (!CheckpointUsage(true)) return;
        closing = true;
        ticker.Stop();
        await bridge.CloseAsync();
        Application.Current.Shutdown();
    }
    private void ResizeShell(object sender, SizeChangedEventArgs e) => Responsive();
    private void Responsive()
    {
        if (DialFrame == null || ActualWidth <= 0) return;
        bool compact = ActualWidth < 835;
        RailColumn.Width = new GridLength(compact ? 66 : 196);
        Wordmark.Visibility = RailState.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in nav.Values)
        {
            item.Label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ((StackPanel)item.Button.Content).Width = compact ? 19 : 140;
            item.Button.Padding = new Thickness(compact ? 4 : 8, 8, compact ? 4 : 8, 8);
        }
        PageFrame.Margin = compact ? new Thickness(19, 21, 19, 20) : new Thickness(30, 27, 30, 27);
        DialFrame.Width = DialFrame.Height = ActualWidth < 730 ? 154 : ActualWidth < 940 ? 176 : 206;
    }

    private bool Commit(FocusData next)
    {
        if (ReferenceEquals(data, next)) return true;
        if (next.Apps != data.Apps || next.Sites != data.Sites) next = UsageClock.Reconcile(next);
        next = UsageClock.Advance(next, DateTimeOffset.Now, Zone);
        if (!WriteState(next)) return false;
        data = next;
        meter.Reset();
        ApplyState();
        return true;
    }

    private bool WriteState(FocusData next, bool checkpoint = false)
    {
        nextCheckpoint = uptime.Elapsed.Add(TimeSpan.FromSeconds(10));
        try
        {
            store.Save(next);
            usageDirty = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify(checkpoint ? "Não foi possível salvar o tempo de uso. O Pulso tentará novamente."
                : "Não foi possível salvar a alteração. Verifique o espaço e o acesso à pasta de dados.", true);
            return false;
        }
    }

    private bool CheckpointUsage(bool force) =>
        !usageDirty || (!force && uptime.Elapsed < nextCheckpoint) || WriteState(data, true);

    private void ApplyState()
    {
        watchedSites = UsageClock.WatchedSites(data);
        windowsSession?.WatchForeground(data.Usage.Enabled);
        bridge.Update(data);
        Enforce(DateTimeOffset.Now, true);
        RefreshStatus();
    }

    private void SampleUsage(DateTimeOffset now)
    {
        if (!data.Usage.Enabled) return;
        string? app = windowsSession?.CanMeasure == true ? WindowBlocker.ForegroundProcess() : null;
        string? site = app != null && watchedSites.Length > 0 ? bridge.ForegroundSite(app) : null;
        var next = meter.Sample(data, now, uptime.Elapsed, app, site, Zone);
        if (!ReferenceEquals(next, data)) { data = next; usageDirty = true; }
    }

    private bool Enforce(DateTimeOffset now, bool settingsChanged = false)
    {
        bool focus = data.Active is { } session && now >= session.Start && now < session.End;
        if (!settingsChanged && !focus && !data.Usage.Enabled && blocked.Count == 0 && pausedSites.Count == 0) return false;
        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sites = new Dictionary<string, DateTimeOffset>();
        if (data.Usage.Enabled)
        {
            var period = UsagePeriod.At(now, Zone);
            foreach (var name in data.Apps)
                if (UsageClock.BlockedUntil(data, UsageClock.AppKey(name), period).HasValue) apps.Add(name);
            foreach (var domain in data.Sites)
                if (UsageClock.BlockedUntil(data, UsageClock.SiteKey(domain), period) is { } end) sites[domain] = end;
        }
        if (focus) apps.UnionWith(data.Apps);
        bool changed = !blocked.SetEquals(apps) || sites.Count != pausedSites.Count
            || sites.Any(pair => !pausedSites.TryGetValue(pair.Key, out var end) || end != pair.Value);
        if (settingsChanged || changed) bridge.UpdateUsage(watchedSites, sites);
        blocked = apps;
        pausedSites = sites;
        WindowBlocker.Apply(blocked);
        return changed;
    }
    private void Tick()
    {
        var now = DateTimeOffset.Now;
        SampleUsage(now);
        var next = FocusClock.Tick(data, now, Zone);
        if (!ReferenceEquals(next, data) && now >= nextSaveTry)
        {
            var previous = data.Active;
            if (Commit(next))
            {
                if (previous != null && previous.End <= now && next.Active == null)
                    tray.ShowBalloonTip(2800, "Sessão concluída", "Seu tempo de foco terminou.", Forms.ToolTipIcon.Info);
                if (page is HistoryId or SchedulesId) RenderPage();
            }
            else nextSaveTry = now.AddSeconds(5);
        }
        CheckpointUsage(Enforce(now));
        if (IsVisible) RefreshStatus();
        if (NoticeBox.Visibility == Visibility.Visible && now >= noticeUntil) NoticeBox.Visibility = Visibility.Collapsed;
    }
    private void Notify(string message, bool error = false)
    {
        NoticeText.Text = message;
        NoticeBox.Background = Ui.Paint(error ? "#FBE8E9" : "#E5EAF9");
        NoticeText.Foreground = Ui.Paint(error ? "#8E2F3E" : "#2E426B");
        NoticeBox.Visibility = Visibility.Visible;
        noticeUntil = DateTimeOffset.Now.AddSeconds(error ? 14 : 5);
    }
    private static string Count(int n, string one, string many) => n + " " + (n == 1 ? one : many);
    private static string MinutesText(TimeSpan duration)
    {
        int m = Math.Max(0, (int)duration.TotalMinutes);
        return m < 60 ? m + " min" : m % 60 == 0 ? m / 60 + " h" : $"{m / 60} h {m % 60:00} min";
    }
    private string Coverage()
    {
        if (data.Sites.Length > 0 && bridge.RequiresReload) return "Recarregue a extensão na tela Navegadores para medir os sites.";
        if (data.Sites.Length > 0 && bridge.Connected.Length == 0) return "Sites aguardam a conexão do navegador.";
        if (data.Sites.Length + data.Apps.Length == 0) return "Adicione o que deseja colocar em pausa.";
        if (data.Usage.Enabled) return "Limites de uso ativos na lista.";
        return "Esta lista vale para todas as sessões.";
    }
    private void RefreshStatus()
    {
        var now = DateTimeOffset.Now;
        var s = data.Active;
        bool active = s != null && s.End > now;
        string browsers = bridge.Connected;
        string connection = bridge.Error != null ? "Conexão indisponível" : browsers.Length == 0 ? "Aguardando extensão" : browsers + (browsers.Contains(" e ") ? " conectados" : " conectado");
        RailState.Text = connection;
        const string reload = "Para medir sites, clique em Recarregar na extensão Pulso em chrome://extensions ou edge://extensions.";
        if (browserState != null) browserState.Text = bridge.Error ?? bridge.ExtensionWarning ?? (bridge.RequiresReload ? reload : connection);
        if (siteState != null) siteState.Text = bridge.Error ?? bridge.ExtensionWarning ?? (bridge.RequiresReload ? reload :
            browsers.Length == 0 ? "Conecte a extensão para medir e bloquear sites." : "Extensão conectada · " + browsers);
        if (page == RulesId) RefreshUsageLabels(now);
        if (historyToday != null) historyToday.Text = MinutesText(FocusClock.Today(data, now, Zone));
        var status = active ? "Em foco" : blocked.Count > 0 || pausedSites.Count > 0 ? "Limite atingido" : "Em espera";
        StatusText.Text = status;
        StatusDot.Fill = Ui.Paint(active ? "#3D835E" : "#71809A");
        if (tray != null && lastStatus != status) { tray.Text = "Pulso · " + status; lastStatus = status; }
        if (page != HomeId) return;
        PageSubtitle.Text = DateLine();
        HeroTitle.Text = active ? s!.Title : "Foco rápido";
        HeroHint.Text = active ? (s!.Scheduled ? "Sessão agendada" : "Sessão manual") + " até " + s.End.ToLocalTime().ToString("HH:mm") + "." : "Escolha a duração da sessão.";
        Presets.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        string action = active ? "Encerrar sessão" : "Iniciar sessão";
        ActionText.Text = action;
        ActionIcon.Text = active ? "\uE71A" : "\uE768";
        AutomationProperties.SetName(SessionButton, action);
        long seconds = active ? (long)Math.Ceiling((s!.End - now).TotalSeconds) : data.Minutes * 60;
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        TimerText.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        TimerText.FontSize = t.TotalHours >= 1 ? DialFrame.Width < 160 ? 28 : 35 : DialFrame.Width < 160 ? 37 : 46;
        TimerLabel.Text = active ? "RESTANTE" : "DURAÇÃO";
        Dial.Set(active ? Math.Clamp((now - s!.Start).TotalSeconds / (s.End - s.Start).TotalSeconds, 0, 1) : Math.Clamp(data.Minutes / 60d, 0, 1));
        foreach (Button b in Presets.Children)
        {
            bool custom = data.Minutes is not (15 or 25 or 50);
            bool selected = b == PCustom ? custom : (string?)b.Tag == data.Minutes.ToString();
            b.Background = Ui.Paint(selected ? "#DFE4FC" : "#394763");
            b.Foreground = Ui.Paint(selected ? "#293458" : "#F0F1FB");
            if (b == PCustom) b.Content = custom ? data.Minutes + " min" : "Outro";
        }
        HomeTargets.Text = Count(data.Apps.Length, "app", "apps") + "  ·  " + Count(data.Sites.Length, "site", "sites");
        HomeCoverage.Text = Coverage();
        HomeMinutes.Text = MinutesText(FocusClock.Today(data, now, Zone));
        int sessions = data.History.Count(e => e.End.LocalDateTime.Date == now.LocalDateTime.Date);
        HomeSessions.Text = Count(sessions, "sessão encerrada", "sessões encerradas");
        var next = FocusClock.Next(data, now, Zone);
        HomeNext.Text = next == null ? "Nenhum horário programado." : next.Routine.Name + " · " + next.Start.ToLocalTime().ToString("ddd, dd/MM 'às' HH:mm", Pt);
    }
    private void PresetClick(object sender, RoutedEventArgs e)
    {
        if (data.Active == null && sender is Button { Tag: string value } && int.TryParse(value, out int minutes))
            Commit(data with { Minutes = minutes });
    }
    private void CustomMinutes(object sender, RoutedEventArgs e)
    {
        var dialog = new FormWindow(this, "Duração da sessão", "Usar duração");
        var edit = dialog.Field("Duração em minutos (1 a 720)", data.Minutes.ToString(), 3);
        dialog.Loaded += (_, _) => { edit.Focus(); edit.SelectAll(); };
        dialog.Accept.Click += (_, _) =>
        {
            if (!int.TryParse(edit.Text, out var duration) || duration is < 1 or > 720) { dialog.Invalid("Digite um número entre 1 e 720.", edit); return; }
            if (data.Active != null) { dialog.DialogResult = false; return; }
            if (Commit(data with { Minutes = duration })) dialog.DialogResult = true;
        };
        dialog.ShowDialog();
    }
    private void ToggleSession(object sender, RoutedEventArgs e)
    {
        if (data.Active == null)
        {
            if (Commit(FocusClock.Start(data, DateTimeOffset.Now, data.Minutes)))
            {
                if (data.Sites.Length > 0 && bridge.Connected.Length == 0) Notify("Sessão iniciada. Conecte o navegador para ativar o bloqueio de sites.");
                else if (data.Sites.Length + data.Apps.Length == 0) Notify("Cronômetro iniciado. Você pode adicionar itens em Lista de bloqueio.");
            }
            return;
        }
        var original = data.Active;
        if (FormWindow.Confirm(this, "Encerrar a sessão?", "O tempo decorrido ficará no histórico. Os limites de uso configurados continuam valendo.", "Encerrar sessão")
            && ReferenceEquals(original, data.Active)) Commit(FocusClock.Stop(data, DateTimeOffset.Now, Zone));
    }
    private void GoRules(object s, RoutedEventArgs e) => Navigate(RulesId);
    private void GoSchedule(object s, RoutedEventArgs e) => Navigate(SchedulesId);
    private void GoHistory(object s, RoutedEventArgs e) => Navigate(HistoryId);
}
