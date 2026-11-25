using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace Pulso;

public sealed partial class MainWindow : Window
{
    private readonly FocusStore store;
    private readonly BrowserLink bridge;
    private readonly LinuxWindowBlocker windowBlocker;
    private readonly Func<IReadOnlySet<string>, string?> minimizeBlockedApps;
    private readonly Func<CancellationToken, AppScan> scanInstalledApps;
    private readonly Action onShutdown;
    private FocusData data;
    private HashSet<string> blocked = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DateTimeOffset> pausedSites = new();
    private string[] watchedSites = [];
    private bool measuringUsage, quotaWindowOpen;
    private DateTimeOffset? siteFocusEnd;
    private SiteFilterPolicy siteFilters;
    private readonly UsageMeter meter = new();
    private readonly Stopwatch uptime = Stopwatch.StartNew();
    private readonly LinuxUsageSession usageSession;
    private bool usageDirty;
    private TimeSpan nextCheckpoint;
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Dictionary<string, (Button Button, TextBlock Label)> nav = new();
    private string page = "home";
    private string lastStatus = "";
    private bool closing, trayHint, hasTray, started;
    private DateTimeOffset noticeUntil, nextSaveTry;
    private const string HomeId = "home", RulesId = "rules", SchedulesId = "schedules", HistoryId = "history", BrowserId = "browser";
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static TimeZoneInfo Zone => TimeZoneInfo.Local;

    internal MainWindow(FocusStore store, BrowserLink bridge, LinuxWindowBlocker windowBlocker, Action onShutdown,
        Func<IReadOnlySet<string>, string?>? minimizeBlockedApps = null, Func<CancellationToken, AppScan>? scanInstalledApps = null)
    {
        this.store = store;
        this.bridge = bridge;
        this.windowBlocker = windowBlocker;
        this.onShutdown = onShutdown;
        this.minimizeBlockedApps = minimizeBlockedApps ?? windowBlocker.Apply;
        this.scanInstalledApps = scanInstalledApps ?? LinuxInstalledApps.Scan;

        data = store.Load();
        if (Application.Current is DesktopApp app)
        {
            app.SetTheme(data.Theme);
            Icon = app.AppIcon;
        }
        InitializeComponent();
        ThemeIcon.Data = StreamGeometry.Parse(Icons.Theme);
        MarkThemeChoice();

        AddNav(NavItems, HomeId, Icons.Home, "Visão geral");
        AddNav(NavItems, RulesId, Icons.Rules, "Lista de bloqueio");
        AddNav(NavItems, SchedulesId, Icons.Schedules, "Agendamentos");
        AddNav(NavItems, HistoryId, Icons.History, "Histórico");
        AddNav(BrowserNav, BrowserId, Icons.Browser, "Navegadores");

        usageSession = new LinuxUsageSession(
            () => Dispatcher.UIThread.Post(meter.Reset),
            () => Dispatcher.UIThread.Post(() => CheckpointUsage(true)));
        usageSession.Watch(measuringUsage);

        SizeChanged += (_, _) => Responsive();
        ticker.Tick += (_, _) => Tick();
        Opened += (_, _) => Responsive();

        Navigate(HomeId);
        ApplyState();
    }

    internal async Task StartBackgroundAsync()
    {
        if (started) return;
        started = true;
        hasTray = await Task.Run(LinuxDesktopServices.HasStatusNotifierWatcher);
        QuitRailButton.IsVisible = !hasTray;
        Responsive();
        Tick();
        ticker.Start();
        await bridge.StartAsync();
        RefreshStatus();
    }

    private void AddNav(StackPanel parent, string id, string iconPath, string label)
    {
        var button = Ui.Button(label, () => Navigate(id), "RailButton");
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Width = 140 };
        var icon = Icons.Create(iconPath, 16);
        icon.Margin = new Thickness(4, 0, 4, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var text = Ui.Text(label);
        text.FontSize = 13;
        text.Margin = new Thickness(5, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(icon);
        content.Children.Add(text);
        button.Content = content;
        parent.Children.Add(button);
        nav[id] = (button, text);
    }

    private void Navigate(string id)
    {
        page = id;
        foreach (var (key, item) in nav) item.Button.Classes.Set("selected", key == id);
        NoticeBox.IsVisible = false;
        (PageTitle.Text, PageSubtitle.Text) = id switch
        {
            RulesId => ("Lista de bloqueio", "Escolha os aplicativos e sites para suas sessões de foco e configure os limites de uso."),
            SchedulesId => ("Agendamentos", "Escolha os dias e horários para bloquear a lista ou limitar o tempo de uso."),
            HistoryId => ("Histórico", "Consulte as sessões registradas neste computador."),
            BrowserId => ("Navegadores", "Conecte o Chrome, o Edge ou o Firefox para medir o tempo de uso e colocar sites em pausa."),
            _ => ("Sessão de foco", DateLine())
        };
        Scroller.Offset = Vector.Zero;
        Home.IsVisible = id == HomeId;
        ContentPage.IsVisible = id != HomeId;
        RenderPage();
        Responsive();
        RefreshStatus();
    }

    private void RenderPage()
    {
        ContentPage.Children.Clear();
        usageLabels.Clear();
        scheduleLabels.Clear();
        siteState = browserState = historyToday = null;
        siteInput = null;
        installedAppsButton = null;
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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!closing)
        {
            e.Cancel = true;
            Hide();
            if (!trayHint)
            {
                trayHint = true;
                LinuxDesktopServices.SendNotification("Pulso em segundo plano",
                    "As sessões e os limites continuam ativos. Use o ícone da bandeja ou reabra o Pulso para voltar.");
            }
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ticker.Stop();
        BlockNoticeWindow.Dismiss();
        usageSession.Dispose();
        CheckpointUsage(true);
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Q && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = QuitAsync();
            return;
        }
        base.OnKeyDown(e);
    }

    private void QuitClick(object? sender, RoutedEventArgs e) => _ = QuitAsync();

    internal async Task QuitAsync()
    {
        if (closing) return;
        if (data.Active != null)
        {
            Reopen();
            if (!await FormWindow.ConfirmAsync(this, "Encerrar a sessão e sair?",
                    "A contagem de uso e os agendamentos serão retomados quando você reabrir o Pulso.", "Encerrar e sair")) return;
            if (!Commit(FocusClock.Stop(data, DateTimeOffset.Now, Zone))) return;
        }
        await ForceShutdownAsync();
        onShutdown();
    }

    internal async Task ForceShutdownAsync()
    {
        if (closing) return;
        CheckpointUsage(true);
        closing = true;
        ticker.Stop();
        BlockNoticeWindow.Dismiss();
        usageSession.Dispose();
        await bridge.CloseAsync();
        Close();
    }

    private void ChooseTheme(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string choice }) return;
        if (choice != data.Theme)
        {
            var next = data with { Theme = choice };
            if (WriteState(next))
            {
                data = next;
                (Application.Current as DesktopApp)?.SetTheme(choice);
                bridge.Update(data);
            }
        }
        MarkThemeChoice();
    }

    private void MarkThemeChoice()
    {
        foreach (var option in new[] { ThemeSystem, ThemeLight, ThemeDark })
        {
            bool selected = Equals(option.Tag, data.Theme);
            option.IsChecked = selected;
            if (selected) ToolTip.SetTip(ThemeButton, "Tema · " + option.Header);
        }
    }

    private void Responsive()
    {
        if (DialFrame == null || Bounds.Width <= 0) return;
        double width = Bounds.Width;
        bool compact = width < 835;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 66 : 196);
        Wordmark.IsVisible = RailState.IsVisible = !compact;
        ThemeLabel.IsVisible = !compact;
        QuitRailLabel.IsVisible = !compact;
        ThemeButton.Padding = new Thickness(compact ? 4 : 8, 8, compact ? 4 : 8, 8);
        foreach (var item in nav.Values)
        {
            item.Label.IsVisible = !compact;
            ((StackPanel)item.Button.Content!).Width = compact ? 24 : 140;
            item.Button.Padding = new Thickness(compact ? 4 : 8, 8, compact ? 4 : 8, 8);
        }
        PageFrame.Margin = compact ? new Thickness(19, 21, 19, 20) : new Thickness(30, 27, 30, 27);
        DialFrame.Width = DialFrame.Height = width < 730 ? 154 : width < 940 ? 176 : 206;
    }

    private bool Commit(FocusData next)
    {
        if (ReferenceEquals(data, next)) return true;
        var now = DateTimeOffset.Now;
        if (next.Routines != data.Routines) next = FocusClock.Tick(next, now, Zone);
        if (next.Apps != data.Apps || next.Sites != data.Sites || next.Routines != data.Routines) next = UsageClock.Reconcile(next);
        next = UsageClock.Advance(next, now, Zone);
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
        Enforce(DateTimeOffset.Now, true);
        RefreshStatus();
    }

    private void SampleUsage(DateTimeOffset now)
    {
        if (!measuringUsage) return;
        string? fg = usageSession.CanMeasure ? windowBlocker.ForegroundProcess(data.Apps) : null;
        var (app, site) = bridge.UsageTarget(fg);
        var next = meter.Sample(data, now, uptime.Elapsed, app, site, Zone);
        if (!ReferenceEquals(next, data)) { data = next; usageDirty = true; }
    }

    private bool Enforce(DateTimeOffset now, bool settingsChanged = false)
    {
        var policy = new UsagePolicy(data, now, Zone);
        if (settingsChanged || siteFocusEnd != policy.Focus?.Until || siteFilters != policy.Filters)
        {
            siteFocusEnd = policy.Focus?.Until;
            siteFilters = policy.Filters;
            bridge.Update(data, policy);
        }
        bool quotaOpen = policy.ActiveQuotas.Length != 0;
        bool watchChanged = settingsChanged || quotaOpen != quotaWindowOpen;
        if (watchChanged)
        {
            quotaWindowOpen = quotaOpen;
            measuringUsage = data.Usage.Enabled || quotaOpen;
            watchedSites = UsageClock.WatchedSites(data, quotaOpen);
            usageSession.Watch(measuringUsage);
            meter.Reset();
        }
        if (!watchChanged && policy.Focus == null && !measuringUsage && blocked.Count == 0 && pausedSites.Count == 0) return false;
        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sites = new Dictionary<string, DateTimeOffset>();
        foreach (var name in data.Apps)
            if (policy.BlockFor(UsageClock.AppKey(name)) != null) apps.Add(name);
        foreach (var domain in data.Sites)
            if (policy.LimitFor(UsageClock.SiteKey(domain)) is { } limit) sites[domain] = limit.Until;
        bool changed = !blocked.SetEquals(apps) || sites.Count != pausedSites.Count
            || sites.Any(pair => !pausedSites.TryGetValue(pair.Key, out var end) || end != pair.Value);
        if (watchChanged || changed) bridge.UpdateUsage(watchedSites, sites);
        blocked = apps;
        pausedSites = sites;

        string? app = minimizeBlockedApps(blocked) ?? BlockNoticeWindow.DisplayedApp;
        if (app != null && blocked.Contains(app) && policy.BlockFor(UsageClock.AppKey(app)) is { } rule)
        {
            string reason = rule.Kind switch
            {
                UsageBlockKind.Focus => $"Sessão de foco em andamento · {rule.Title}.",
                UsageBlockKind.Schedule => $"O saldo por hora do agendamento “{rule.Title}” acabou.",
                _ => rule.Title + " atingido."
            };
            string displayName = data.AppInfo.TryGetValue(app, out var appInfo) ? appInfo.DisplayName : app;
            BlockNoticeWindow.Present(app, displayName, reason, rule.Until);
        }
        else if (BlockNoticeWindow.DisplayedApp != null) BlockNoticeWindow.Dismiss();
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
                    LinuxDesktopServices.SendNotification("Sessão concluída", "Seu tempo de foco terminou.");
                if (page is HistoryId or SchedulesId) RenderPage();
            }
            else nextSaveTry = now.AddSeconds(5);
        }
        CheckpointUsage(Enforce(now));
        if (IsVisible) RefreshStatus();
        if (NoticeBox.IsVisible && now >= noticeUntil) NoticeBox.IsVisible = false;
    }

    private void Notify(string message, bool error = false)
    {
        NoticeText.Text = message;
        Ui.SetResourceBrush(NoticeBox, Border.BackgroundProperty, error ? "ErrorSurface" : "Lilac");
        Ui.SetResourceBrush(NoticeText, TextBlock.ForegroundProperty, error ? "ErrorInk" : "Ink");
        NoticeBox.IsVisible = true;
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
        if (data.EnabledSiteFilters > 0 && bridge.RequiresFilterReload) return "Recarregue a extensão do navegador para aplicar os filtros de sites.";
        if (data.Sites.Length > 0 && bridge.RequiresReload) return "Recarregue a extensão na tela Navegadores para medir os sites.";
        if (data.EnabledSiteFilters > 0 && bridge.Connected.Length == 0) return "Conecte a extensão para aplicar os filtros de sites.";
        if (data.Sites.Length > 0 && bridge.Connected.Length == 0) return "Sites aguardam a conexão do navegador.";
        if (data.Sites.Length + data.Apps.Length + data.EnabledSiteFilters == 0) return "Adicione o que deseja colocar em pausa.";
        if (data.EnabledSiteFilters > 0 && data.Sites.Length + data.Apps.Length == 0)
            return siteFilters.ActiveCount > 0 ? "Bloqueio por categoria em vigor no navegador." : "O bloqueio por categoria aguarda a próxima sessão ou agendamento.";
        if (quotaWindowOpen) return "O limite por hora do agendamento está em vigor.";
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
        const string reloadAction = "clique em Recarregar no cartão da extensão Pulso em chrome://extensions, edge://extensions ou about:debugging.";
        string? reload = bridge.RequiresReload ? "Para medir sites e usar os filtros, " + reloadAction
            : bridge.RequiresFilterReload ? "Para aplicar os filtros de sites, " + reloadAction : null;
        if (browserState != null) browserState.Text = bridge.Error ?? bridge.ExtensionWarning ?? reload ?? connection;
        if (siteState != null) siteState.Text = bridge.Error ?? bridge.ExtensionWarning ?? reload ??
            (browsers.Length == 0 ? "Conecte a extensão para medir e bloquear sites." : "Extensão conectada · " + browsers);
        if (page is RulesId or SchedulesId) RefreshUsageLabels(now);
        if (historyToday != null) historyToday.Text = MinutesText(FocusClock.Today(data, now, Zone));
        bool filtering = siteFilters.ActiveCount > 0 && bridge.HasFilterClient;
        var status = active ? "Em foco" : blocked.Count > 0 || pausedSites.Count > 0 ? "Limite atingido" : quotaWindowOpen ? "Limite agendado"
            : filtering ? (siteFilters.ActiveCount == 1 ? "Filtro de sites ativo" : "Filtros de sites ativos") : "Em espera";
        StatusText.Text = status;
        if (lastStatus != status)
        {
            Ui.SetResourceBrush(StatusDot, Avalonia.Controls.Shapes.Shape.FillProperty, active || filtering ? "StatusActive" : "StatusIdle");
            (Application.Current as DesktopApp)?.SetTrayTooltip("Pulso · " + status);
            lastStatus = status;
        }
        if (page != HomeId) return;
        PageSubtitle.Text = DateLine();
        HeroTitle.Text = active ? s!.Title : "Foco rápido";
        HeroHint.Text = active ? (s!.Scheduled ? "Sessão agendada" : "Sessão manual") + " até " + s.End.ToLocalTime().ToString("HH:mm") + "." : "Escolha a duração da sessão.";
        Presets.IsVisible = !active;
        string action = active ? "Encerrar sessão" : "Iniciar sessão";
        ActionText.Text = action;
        ActionIcon.Data = StreamGeometry.Parse(active ? Icons.Stop : Icons.Play);
        AutomationProperties.SetName(SessionButton, action);
        long seconds = active ? (long)Math.Ceiling((s!.End - now).TotalSeconds) : data.Minutes * 60;
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        TimerText.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        TimerText.FontSize = t.TotalHours >= 1 ? (DialFrame.Width < 160 ? 28 : 35) : (DialFrame.Width < 160 ? 37 : 46);
        TimerLabel.Text = active ? "RESTANTE" : "DURAÇÃO";
        Dial.Set(active ? Math.Clamp((now - s!.Start).TotalSeconds / (s.End - s.Start).TotalSeconds, 0, 1) : Math.Clamp(data.Minutes / 60d, 0, 1));
        foreach (var child in Presets.Children)
        {
            if (child is not Button b) continue;
            bool custom = data.Minutes is not (15 or 25 or 50);
            bool selected = b == PCustom ? custom : (string?)b.Tag == data.Minutes.ToString();
            b.Classes.Set("selected", selected);
            if (b == PCustom) b.Content = custom ? data.Minutes + " min" : "Outro";
        }
        HomeTargets.Text = Count(data.Apps.Length, "app", "apps") + "  ·  " + Count(data.Sites.Length, "site", "sites");
        if (data.EnabledSiteFilters > 0) HomeTargets.Text += "  ·  " + Count(data.EnabledSiteFilters, "filtro", "filtros");
        HomeCoverage.Text = Coverage();
        HomeMinutes.Text = MinutesText(FocusClock.Today(data, now, Zone));
        int sessions = data.History.Count(e => e.End.LocalDateTime.Date == now.LocalDateTime.Date);
        HomeSessions.Text = Count(sessions, "sessão encerrada", "sessões encerradas");
        var next = FocusClock.Next(data, now, Zone);
        HomeNext.Text = next == null ? "Nenhum horário programado." : next.Routine.Name + " · " + next.Start.ToLocalTime().ToString("ddd, dd/MM 'às' HH:mm", Pt);
    }

    private void PresetClick(object? sender, RoutedEventArgs e)
    {
        if (data.Active == null && sender is Button { Tag: string value } && int.TryParse(value, out int minutes))
            Commit(data with { Minutes = minutes });
    }

    private async void CustomMinutes(object? sender, RoutedEventArgs e)
    {
        var dialog = new FormWindow(this, "Duração da sessão", "Usar duração");
        var edit = dialog.Field("Duração em minutos (1 a 720)", data.Minutes.ToString(), 3);
        dialog.Opened += (_, _) => { edit.Focus(); edit.SelectAll(); };
        dialog.Accept.Click += (_, _) =>
        {
            if (!int.TryParse(edit.Text, out var duration) || duration is < 1 or > 720)
            {
                dialog.Invalid("Digite um número entre 1 e 720.", edit);
                return;
            }
            if (data.Active != null) { dialog.Close(false); return; }
            if (Commit(data with { Minutes = duration })) dialog.Close(true);
        };
        await dialog.ShowDialog<bool>(this);
    }

    private async void ToggleSession(object? sender, RoutedEventArgs e)
    {
        if (data.Active == null)
        {
            if (Commit(FocusClock.Start(data, DateTimeOffset.Now, data.Minutes)))
            {
                if (data.EnabledSiteFilters > 0 && bridge.RequiresFilterReload) Notify("Sessão iniciada. Recarregue a extensão do navegador para aplicar os filtros de sites.");
                else if (data.Sites.Length + data.EnabledSiteFilters > 0 && bridge.Connected.Length == 0) Notify("Sessão iniciada. Conecte o navegador para ativar o bloqueio de sites.");
                else if (data.Sites.Length + data.Apps.Length + data.EnabledSiteFilters == 0) Notify("Cronômetro iniciado. Você pode adicionar itens em Lista de bloqueio.");
            }
            return;
        }
        var original = data.Active;
        if (await FormWindow.ConfirmAsync(this, "Encerrar a sessão?",
                "O tempo decorrido ficará no histórico. Os limites de uso configurados continuam valendo.", "Encerrar sessão")
            && ReferenceEquals(original, data.Active))
            Commit(FocusClock.Stop(data, DateTimeOffset.Now, Zone));
    }

    private void GoRules(object? s, RoutedEventArgs e) => Navigate(RulesId);
    private void GoSchedule(object? s, RoutedEventArgs e) => Navigate(SchedulesId);
    private void GoHistory(object? s, RoutedEventArgs e) => Navigate(HistoryId);
}
