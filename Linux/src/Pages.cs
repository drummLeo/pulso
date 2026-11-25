using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Pulso;

public sealed partial class MainWindow
{
    private TextBlock? siteState, browserState, historyToday;
    private TextBox? siteInput;
    private Button? installedAppsButton;
    private readonly List<(string? Key, TextBlock Label, Button Edit)> usageLabels = [];
    private readonly List<(Routine Routine, TextBlock Label)> scheduleLabels = [];
    private int historyLimit = 40;
    private static readonly (DayOfWeek Day, string Label)[] Week =
    [
        (DayOfWeek.Monday, "Seg"), (DayOfWeek.Tuesday, "Ter"), (DayOfWeek.Wednesday, "Qua"),
        (DayOfWeek.Thursday, "Qui"), (DayOfWeek.Friday, "Sex"), (DayOfWeek.Saturday, "Sáb"), (DayOfWeek.Sunday, "Dom")
    ];

    private bool Change(FocusData next, string message)
    {
        if (!Commit(next)) return false;
        RenderPage();
        RefreshStatus();
        Notify(message);
        return true;
    }

    private void RulesPage()
    {
        var shared = Ui.Section(ContentPage, "Limite compartilhado", "Todos os apps e sites dividem esse saldo por hora ou por dia, em qualquer horário.");
        AddUsageRow(shared, null, null, null);

        var schedules = Ui.Section(ContentPage, "Limites por agendamento", "Durante o horário de cada agendamento, os apps e sites desta lista dividem a cota que você escolher.");
        foreach (var routine in data.Routines.Where(r => r.HourMinutes.HasValue).OrderBy(r => r.StartMinute).ThenBy(r => r.Name))
        {
            var info = new StackPanel();
            var name = Ui.Text(routine.Name);
            name.FontWeight = FontWeight.SemiBold;
            info.Children.Add(name);
            AddScheduleLabel(info, routine);
            Ui.Row(schedules, info, Ui.Button("Editar", () => _ = EditRoutineAsync(routine), null, "Editar limite do agendamento " + routine.Name));
        }
        Ui.Buttons(schedules, Ui.Button("Gerenciar agendamentos", () => Navigate(SchedulesId), "Quiet"));

        var sites = Ui.Section(ContentPage, "Sites", "Escolha quando bloquear cada categoria no Chrome, no Edge ou no Firefox.");
        var categories = new WrapPanel();
        sites.Children.Add(categories);
        AddSiteFilter(categories, "Pornografia", data.PornFilter, mode => data with { PornFilter = mode });
        AddSiteFilter(categories, "Apostas", data.GamblingFilter, mode => data with { GamblingFilter = mode });
        sites.Children.Add(Ui.Text("Durante o foco inclui sessões rápidas e agendamentos, mesmo com saldo por hora. Sempre vale enquanto o Pulso estiver aberto. Só os itens adicionados à lista entram na contagem de uso.", "Hint", 8));
        sites.Children.Add(Ui.Text("Adicione um domínio ou cole um link. Os subdomínios também entram na lista.", "Hint", 16));

        var input = Ui.TextInput("", 2048);
        siteInput = input;
        sites.Children.Add(Ui.Label("Endereço do site", input));
        var inputRow = new DockPanel();
        var addSite = Ui.Button("Adicionar site", () => AddSite(input.Text ?? ""), "Primary");
        DockPanel.SetDock(addSite, Dock.Right);
        addSite.Margin = new Thickness(7, 0, 0, 0);
        inputRow.Children.Add(addSite);
        inputRow.Children.Add(input);
        sites.Children.Add(inputRow);
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddSite(input.Text ?? ""); e.Handled = true; } };

        if (data.Sites.Length == 0)
        {
            var suggestions = new WrapPanel { Margin = new Thickness(0, 7, 0, 1) };
            foreach (var (name, domain) in new[] { ("YouTube", "youtube.com"), ("Instagram", "instagram.com"), ("Reddit", "reddit.com") })
            {
                var suggestion = Ui.Button("+ " + name, () => AddSite(domain), "Quiet", "Adicionar " + name);
                suggestions.Children.Add(suggestion);
            }
            sites.Children.Add(suggestions);
        }
        foreach (var domain in data.Sites)
            AddUsageRow(sites, UsageClock.SiteKey(domain), domain, Ui.Button("Remover", () =>
                Change(data with { Sites = data.Sites.Where(d => d != domain).ToArray() }, "Site removido da lista."), "Quiet", "Remover " + domain));
        siteState = Ui.Text("", "Hint", 8);
        Ui.Row(sites, siteState, Ui.Button("Navegadores", () => Navigate(BrowserId), "Quiet"));

        var apps = Ui.Section(ContentPage, "Aplicativos", "O Pulso minimiza as janelas durante o foco ou quando um limite de uso é atingido.");
        if (!windowBlocker.Session.HasFullWindowControl)
        {
            if (windowBlocker.Session.IsWayland)
            {
                apps.Children.Add(Ui.Text(
                    windowBlocker.Session.HasX11
                        ? "Sessão Wayland detectada: janelas nativas do Wayland neste compositor não expõem o foco global nem permitem minimização automática. Programas em X11/XWayland continuam sendo protegidos; para bloqueio completo de aplicativos, use uma sessão X11 ou KDE Plasma. O bloqueio de sites pela extensão funciona normalmente."
                        : "Sessão Wayland sem servidor X11 detectada: este compositor não permite monitorar ou minimizar janelas globalmente. O bloqueio de sites pela extensão funciona normalmente.",
                    "Hint", 6));
            }
            else
            {
                apps.Children.Add(Ui.Text("Nenhum servidor X11 disponível para monitorar ou minimizar janelas neste ambiente. O bloqueio de sites pela extensão continua funcionando.", "Hint", 6));
            }
        }

        installedAppsButton = Ui.Button("Buscar aplicativos instalados", () => _ = PickInstalledAppsAsync(), "Primary");
        Ui.Buttons(apps, installedAppsButton, Ui.Button("Escolher janela aberta", () => _ = PickOpenAppAsync()), Ui.Button("Escolher executável", () => _ = PickFileAsync(), "Quiet"));
        if (data.Apps.Length == 0) apps.Children.Add(Ui.Text("Nenhum aplicativo na lista.", "Hint", 8));
        foreach (var group in SelectedApps.Groups(SelectedApps.Read(data)))
        {
            apps.Children.Add(SelectedApps.Heading(group.Key));
            foreach (var app in group)
            {
                var name = app.ProcessName;
                var remove = Ui.Button("Remover", () => ChangeApps(SelectedApps.Read(data).Where(a =>
                    !string.Equals(a.ProcessName, name, StringComparison.OrdinalIgnoreCase)), "Aplicativo removido da lista."), "Quiet", "Remover " + name);
                AddUsageRow(apps, UsageClock.AppKey(name), name, remove, app.DisplayName);
            }
        }
    }

    private void AddSiteFilter(Panel parent, string label, SiteFilterMode saved, Func<SiteFilterMode, FocusData> configure)
    {
        var field = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var choice = new ComboBox
        {
            ItemsSource = new[] { "Desativado", "Durante o foco", "Sempre" },
            SelectedIndex = (int)saved,
            MinWidth = 154,
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        field.Children.Add(Ui.Label(label, choice));
        field.Children.Add(choice);
        parent.Children.Add(field);
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedIndex < 0 || choice.SelectedIndex == (int)saved) return;
            var selected = (SiteFilterMode)choice.SelectedIndex;
            var next = configure(selected);
            if (!WriteState(next))
            {
                choice.SelectedIndex = (int)saved;
                return;
            }
            saved = selected;
            data = next;
            Enforce(DateTimeOffset.Now);
            RefreshStatus();
        };
    }

    private void AddUsageRow(Panel section, string? key, string? name, Button? remove, string? friendlyName = null)
    {
        var limit = UsageClock.Get(data, key);
        var info = new StackPanel();
        if (name != null)
        {
            if (string.Equals(friendlyName, name, StringComparison.OrdinalIgnoreCase)) friendlyName = null;
            var title = Ui.Text(friendlyName ?? name);
            title.FontWeight = FontWeight.SemiBold;
            info.Children.Add(title);
            if (friendlyName != null) info.Children.Add(Ui.Text(name, "Hint", 1));
        }
        var label = Ui.Text("", "Hint", name == null ? 0 : 2);
        info.Children.Add(label);
        string description = name == null ? "limite compartilhado" : "limite de " + name;
        var edit = Ui.Button(limit.Enabled ? "Editar limite" : "Configurar limite",
            () => _ = EditUsageLimitAsync(key, friendlyName ?? name), null, (limit.Enabled ? "Editar " : "Configurar ") + description);
        var buttons = new List<Button> { edit };
        usageLabels.Add((key, label, edit));
        if (limit.Enabled)
        {
            var disable = Ui.Button("Desativar", () =>
            {
                if (Change(UsageClock.Configure(data, key, null, null), "Limite desativado.")) FocusUsageRow(key);
            }, "Quiet", "Desativar " + description);
            ToolTip.SetTip(disable, "Desativar e zerar o consumo deste limite.");
            buttons.Add(disable);
        }
        if (remove != null) buttons.Add(remove);
        Ui.Row(section, info, buttons.ToArray());
    }

    private void FocusUsageRow(string? key) => usageLabels.FirstOrDefault(row => row.Key == key).Edit?.Focus();

    private static string UsedTime(long ticks)
    {
        var time = TimeSpan.FromTicks(ticks);
        int minutes = (int)time.TotalMinutes;
        if (minutes == 0) return time.Seconds + " s";
        return time.Seconds == 0 ? minutes + " min" : $"{minutes} min {time.Seconds} s";
    }

    private void RefreshUsageLabels(DateTimeOffset now)
    {
        var policy = new UsagePolicy(data, now, Zone);
        var period = policy.Period;
        foreach (var (key, label, _) in usageLabels)
        {
            var rule = UsageClock.Get(data, key);
            var lines = new List<string>();
            if (rule.HourMinutes is { } hour)
                lines.Add("Nesta hora · " + UsedTime(data.Usage.HourStart == period.HourStart ? rule.HourTicks : 0) + $" de {hour} min");
            if (rule.DayMinutes is { } day)
                lines.Add("Hoje · " + UsedTime(data.Usage.Day == period.Day ? rule.DayTicks : 0) + $" de {day} min");
            if (!rule.Enabled) lines.Add(key == null ? "Desativado" : "Sem limite próprio");
            var block = key == null ? policy.Shared : policy.BlockFor(key);
            if (block != null)
            {
                var local = block.Until.ToLocalTime();
                string date = local.Date == now.LocalDateTime.Date ? "hoje" : local.ToString("dd/MM", Pt);
                lines.Add("Em pausa até " + date + " às " + local.ToString("HH:mm", Pt));
            }
            string text = string.Join(Environment.NewLine, lines);
            if (label.Text != text) label.Text = text;
        }
        foreach (var (routine, label) in scheduleLabels)
        {
            string text = $"{routine.HourMinutes} min por hora para a lista";
            if (!routine.Enabled) text += " · Desativado";
            else if (policy.ActiveQuotas.FirstOrDefault(w => w.Routine.Id == routine.Id) is { } window)
            {
                text = "Nesta hora · " + UsedTime(policy.ScheduleUsed(routine.Id)) + $" de {routine.HourMinutes} min";
                if (policy.ScheduleEnd(window) is { } end) text += " · Em pausa até " + end.ToLocalTime().ToString("HH:mm", Pt);
            }
            if (label.Text != text) label.Text = text;
        }
    }

    private void AddScheduleLabel(Panel parent, Routine routine)
    {
        var label = Ui.Text("", "Hint", 3);
        scheduleLabels.Add((routine, label));
        parent.Children.Add(label);
    }

    private async Task EditUsageLimitAsync(string? key, string? name)
    {
        var limit = UsageClock.Get(data, key);
        var dialog = new FormWindow(this, name == null ? "Limite compartilhado" : "Limite de " + name, "Salvar limite");
        dialog.Fields.Children.Add(Ui.Text("A hora se renova a cada hora cheia e o dia, à meia-noite. Deixe um campo em branco para desativar e zerar aquele período.", "Hint", 8));
        var hour = dialog.Field("Minutos por hora (1 a 60)", limit.HourMinutes?.ToString(Pt) ?? "");
        var day = dialog.Field("Minutos por dia (1 a 1440)", limit.DayMinutes?.ToString(Pt) ?? "");
        dialog.Opened += (_, _) => { hour.Focus(); hour.SelectAll(); };
        dialog.Accept.Click += (_, _) =>
        {
            if (!ReadLimit(hour, 60, "hora", out int? perHour) || !ReadLimit(day, 1440, "dia", out int? perDay)) return;
            var next = UsageClock.Configure(data, key, perHour, perDay);
            bool enabled = perHour.HasValue || perDay.HasValue;
            if (Change(next, enabled ? "Limite salvo." : "Limite desativado.")) dialog.Close(true);
            else dialog.Invalid("O limite não foi salvo. Verifique o espaço e o acesso à pasta de dados.");
        };
        if (await dialog.ShowDialog<bool>(this)) FocusUsageRow(key);

        bool ReadLimit(TextBox field, int maximum, string period, out int? result)
        {
            result = null;
            string value = (field.Text ?? "").Trim();
            if (value.Length == 0) return true;
            if (int.TryParse(value, NumberStyles.None, Pt, out int minutes) && minutes >= 1 && minutes <= maximum)
            { result = minutes; return true; }
            dialog.Invalid($"O limite por {period} deve ser um número inteiro de 1 a {maximum} minutos.", field);
            field.SelectAll();
            return false;
        }
    }

    private void AddSite(string raw)
    {
        var d = Targets.Domain(raw);
        if (d == null || data.Sites.Contains(d))
        {
            Notify(d == null ? "Digite um domínio válido, como exemplo.com." : "Este domínio já está na lista.", true);
            siteInput?.Focus();
            siteInput?.SelectAll();
            return;
        }
        if (Change(data with { Sites = data.Sites.Append(d).Order(StringComparer.OrdinalIgnoreCase).ToArray() }, d + " adicionado à lista."))
            siteInput?.Focus();
    }

    private bool ChangeApps(IEnumerable<DiscoveredApp> selected, string message)
    {
        var next = SelectedApps.Apply(data, selected);
        if (!ReferenceEquals(next, data) && ReferenceEquals(next.Apps, data.Apps))
        {
            if (!WriteState(next)) return false;
            data = next;
        }
        return Change(next, message);
    }

    private async Task PickInstalledAppsAsync()
    {
        var dialog = new AppPickerWindow(this, SelectedApps.Read(data), scanInstalledApps,
            selected => ChangeApps(selected, "Lista de aplicativos salva."));
        await dialog.ShowDialog<bool>(this);
        installedAppsButton?.Focus();
    }

    private bool AddApp(string raw, string? path = null)
    {
        var name = Targets.App(raw);
        if (name == null)
        {
            Notify("Escolha um aplicativo de uso pessoal. Este componente é usado pelo sistema ou pelo Pulso.", true);
            return false;
        }
        if (data.Apps.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            Notify("Este aplicativo já está na lista.", true);
            return false;
        }
        return ChangeApps(SelectedApps.Read(data).Append(SelectedApps.Manual(name, path)), name + " adicionado à lista.");
    }

    private async Task PickFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Escolher executável",
            AllowMultiple = false
        });
        if (files.Count == 0) return;
        string localPath = files[0].Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(localPath))
            AddApp(Path.GetFileName(localPath), localPath);
    }

    private async Task PickOpenAppAsync()
    {
        var dialog = new FormWindow(this, "Escolher janela aberta", "Adicionar aplicativo");
        var openApps = windowBlocker.OpenApps().Except(data.Apps, StringComparer.OrdinalIgnoreCase).ToArray();
        var list = new ListBox { MaxHeight = 250, MinHeight = 65, Padding = new Thickness(4), ItemsSource = openApps };
        dialog.Fields.Children.Add(Ui.Label("Aplicativos com janela aberta", list));
        dialog.Fields.Children.Add(list);
        if (openApps.Length > 0) list.SelectedIndex = 0;
        else dialog.Fields.Children.Add(Ui.Text("Abra o programa desejado ou use a opção de escolher executável.", "Hint", 8));
        dialog.Accept.IsEnabled = openApps.Length > 0;
        void Take()
        {
            if (list.SelectedItem is string name && AddApp(name)) dialog.Close(true);
        }
        dialog.Accept.Click += (_, _) => Take();
        list.DoubleTapped += (_, _) => Take();
        dialog.Opened += (_, _) => list.Focus();
        await dialog.ShowDialog<bool>(this);
    }

    private void SchedulesPage()
    {
        var header = new StackPanel();
        Ui.Card(ContentPage, header);
        Ui.Row(header, Ui.Text("Rotina semanal", "Heading"), Ui.Button("Novo agendamento", () => _ = EditRoutineAsync(null), "Primary"));
        header.Children.Add(Ui.Text("Os horários seguem o relógio do sistema. Mantenha o Pulso aberto ou em segundo plano.", "Hint", 8));
        if (data.Routines.Length == 0)
        {
            Ui.Section(ContentPage, "Nenhum agendamento", "Escolha os dias, o horário de início e a duração da sua primeira sessão recorrente.");
            return;
        }
        foreach (var r in data.Routines.OrderBy(r => r.StartMinute).ThenBy(r => r.Name))
        {
            var box = new StackPanel();
            Ui.Card(ContentPage, box);
            var title = Ui.Text(r.Name, "Heading");
            var toggle = Ui.Button(r.Enabled ? "Desativar" : "Ativar", () => Change(data with
            {
                Routines = data.Routines.Select(item => item.Id == r.Id ? item with { Enabled = !item.Enabled } : item).ToArray()
            }, r.Enabled ? "Agendamento desativado." : "Agendamento ativado."), "Quiet", (r.Enabled ? "Desativar " : "Ativar ") + r.Name);
            Ui.Row(box, title, toggle);
            string days = string.Join(" · ", Week.Where(d => (r.Days & (1 << (int)d.Day)) != 0).Select(d => d.Label));
            string start = TimeOnly.MinValue.AddMinutes(r.StartMinute).ToString("HH:mm");
            box.Children.Add(Ui.Text($"{days}   /   {start}   /   {r.Minutes} min", null, 6));
            if (r.HourMinutes.HasValue) AddScheduleLabel(box, r);
            else box.Children.Add(Ui.Text(r.Enabled ? "Bloqueio desde o início" : "Bloqueio desde o início · Desativado", "Hint", 3));
            Ui.Buttons(box, Ui.Button("Editar", () => _ = EditRoutineAsync(r), null, "Editar " + r.Name),
                Ui.Button("Excluir", () => _ = DeleteRoutineAsync(r), "Quiet", "Excluir " + r.Name));
        }
    }

    private async Task DeleteRoutineAsync(Routine r)
    {
        if (await FormWindow.ConfirmAsync(this, "Excluir agendamento?",
                "O horário e as regras de “" + r.Name + "” serão removidos da agenda.", "Excluir agendamento"))
            Change(data with { Routines = data.Routines.Where(item => item.Id != r.Id).ToArray() }, "Agendamento excluído.");
    }

    private async Task EditRoutineAsync(Routine? existing)
    {
        var initial = existing ?? new Routine { Name = "", Minutes = data.Minutes };
        var dialog = new FormWindow(this, existing == null ? "Novo agendamento" : "Editar agendamento", "Salvar agendamento");
        var name = dialog.Field("Nome da sessão", initial.Name, 60);
        var time = dialog.Field("Horário de início (HH:mm)", TimeOnly.MinValue.AddMinutes(initial.StartMinute).ToString("HH:mm"), 5);
        var minutes = dialog.Field("Duração em minutos (1 a 720)", initial.Minutes.ToString(), 3);
        dialog.Fields.Children.Add(Ui.Text("Repetir nos dias", null, 12));
        var daysRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var checks = new Dictionary<DayOfWeek, CheckBox>();
        foreach (var (day, shortName) in Week)
        {
            var check = new CheckBox
            {
                Content = shortName,
                IsChecked = (initial.Days & (1 << (int)day)) != 0,
                MinWidth = 0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 9, 3)
            };
            AutomationProperties.SetName(check, Pt.DateTimeFormat.GetDayName(day));
            checks[day] = check;
            daysRow.Children.Add(check);
        }
        dialog.Fields.Children.Add(daysRow);

        var quota = new CheckBox
        {
            Content = "Limitar minutos por hora",
            IsChecked = initial.HourMinutes.HasValue,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Ui.SetResourceBrush(quota, CheckBox.ForegroundProperty, "Ink");
        dialog.Fields.Children.Add(quota);
        var hint = Ui.Text("", "Hint", 4);
        dialog.Fields.Children.Add(hint);

        var allowance = new StackPanel();
        var hour = Ui.TextInput(initial.HourMinutes?.ToString(Pt) ?? "", 3);
        allowance.Children.Add(Ui.Label("Minutos por hora (1 a 60)", hour));
        allowance.Children.Add(hour);
        dialog.Fields.Children.Add(allowance);

        void UpdateMode()
        {
            bool enabled = quota.IsChecked == true;
            allowance.IsVisible = enabled;
            hint.Text = enabled ? "Os apps e sites dividem esses minutos durante o agendamento. A cota se renova na hora cheia."
                : "A lista fica bloqueada desde o início até o fim do agendamento.";
            AutomationProperties.SetHelpText(quota, hint.Text);
        }
        quota.IsCheckedChanged += (_, _) => UpdateMode();
        UpdateMode();

        dialog.Opened += (_, _) => name.Focus();
        dialog.Accept.Click += (_, _) =>
        {
            int days = checks.Where(p => p.Value.IsChecked == true).Sum(p => 1 << (int)p.Key);
            if (string.IsNullOrWhiteSpace(name.Text)) { dialog.Invalid("Dê um nome à sessão.", name); return; }
            if (!TimeOnly.TryParseExact((time.Text ?? "").Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            { dialog.Invalid("Digite um horário de 00:00 a 23:59.", time); return; }
            if (!int.TryParse(minutes.Text, out var duration) || duration is < 1 or > 720) { dialog.Invalid("A duração deve ficar entre 1 e 720 minutos.", minutes); return; }
            if (days == 0) { dialog.Invalid("Selecione pelo menos um dia da semana.", checks.Values.First()); return; }
            int? perHour = null;
            if (quota.IsChecked == true)
            {
                if (!int.TryParse((hour.Text ?? "").Trim(), NumberStyles.None, Pt, out int count) || count is < 1 or > 60)
                {
                    dialog.Invalid("Digite um número inteiro de 1 a 60 minutos por hora.", hour);
                    hour.SelectAll();
                    return;
                }
                perHour = count;
            }
            var current = data.Routines.FirstOrDefault(r => r.Id == initial.Id) ?? initial;
            int startMinute = start.Hour * 60 + start.Minute;
            var item = current with
            {
                Name = name.Text.Trim(),
                Days = days,
                StartMinute = startMinute,
                Minutes = duration,
                HourMinutes = perHour,
                LastOccurrence = current.Days == days && current.StartMinute == startMinute && current.HourMinutes.HasValue == perHour.HasValue
                    ? current.LastOccurrence : null
            };
            var routines = existing == null ? data.Routines.Append(item).ToArray() : data.Routines.Select(r => r.Id == item.Id ? item : r).ToArray();
            if (Change(data with { Routines = routines }, "Agendamento salvo.")) dialog.Close(true);
            else dialog.Invalid("O agendamento não foi salvo. Verifique o espaço e o acesso à pasta de dados.");
        };
        await dialog.ShowDialog<bool>(this);
    }

    private void HistoryPage()
    {
        var now = DateTimeOffset.Now;
        var summary = Ui.Section(ContentPage, "Tempo de foco hoje", "");
        historyToday = Ui.Text(MinutesText(FocusClock.Today(data, now, Zone)), "Heading", 8);
        historyToday.FontSize = 30;
        summary.Children.Add(historyToday);
        if (data.Active is { } active) summary.Children.Add(Ui.Text("Em andamento · " + active.Title + " até " + active.End.ToLocalTime().ToString("HH:mm"), "Hint", 4));
        if (data.History.Length == 0)
        {
            Ui.Section(ContentPage, "Nenhuma sessão encerrada", "Ao concluir ou encerrar uma sessão, o tempo decorrido aparece aqui.");
            return;
        }
        var body = Ui.Section(ContentPage, "Sessões anteriores", "");
        foreach (var e in data.History.Take(historyLimit))
        {
            var row = new StackPanel();
            row.Children.Add(Ui.Text(e.Title));
            var start = e.Start.ToLocalTime();
            var end = e.End.ToLocalTime();
            string range = start.ToString("dd/MM/yyyy · HH:mm") + " – " + end.ToString(start.Date == end.Date ? "HH:mm" : "dd/MM HH:mm");
            row.Children.Add(Ui.Text(range + " · " + (e.Completed ? "Concluída" : "Encerrada antes do fim"), "Hint", 2));
            var dock = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var duration = Ui.Text((end - start).TotalMinutes < 1 ? "< 1 min" : MinutesText(end - start));
            duration.FontWeight = FontWeight.SemiBold;
            duration.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(duration, Dock.Right);
            dock.Children.Add(duration);
            dock.Children.Add(row);
            body.Children.Add(dock);
        }
        if (data.History.Length > historyLimit)
            Ui.Buttons(body, Ui.Button("Mostrar mais sessões", () => { historyLimit += 40; RenderPage(); }));
    }

    private void BrowserPage()
    {
        var info = Ui.Section(ContentPage, "Conexão com este computador", "");
        browserState = Ui.Text("", null, 8);
        browserState.FontWeight = FontWeight.SemiBold;
        info.Children.Add(browserState);
        info.Children.Add(Ui.Text(bridge.Error ?? "A extensão filtra as categorias no próprio navegador. Para medir o uso, ela informa ao Pulso o domínio da lista manual quando a aba está em primeiro plano.", "Hint", 4));
        var guide = Ui.Section(ContentPage, "Instalar no Chrome ou no Edge", "Faça isso uma vez em cada perfil do Chrome ou do Edge que quiser usar.");
        void Step(Panel target, string title, string detail)
        {
            var label = Ui.Text(title, null, 16);
            label.FontWeight = FontWeight.SemiBold;
            target.Children.Add(label);
            target.Children.Add(Ui.Text(detail, "Hint", 3));
        }
        Step(guide, "1. Copie o caminho da pasta", "O Pulso prepara a extensão em uma pasta local deste computador e libera a leitura para Chrome/Edge em Flatpak.");
        Ui.Buttons(guide, Ui.Button("Copiar caminho da extensão", () => _ = PrepareBrowserAsync(null)));
        Step(guide, "2. Abra o gerenciador de extensões", "Escolha o navegador em que deseja bloquear sites.");
        Ui.Buttons(guide, Ui.Button("Abrir no Chrome", () => _ = PrepareBrowserAsync("Chrome")), Ui.Button("Abrir no Edge", () => _ = PrepareBrowserAsync("Edge")));
        Step(guide, "3. Carregue a pasta copiada", "Ative o Modo do desenvolvedor e clique em Carregar sem compactação (ou Carregar sem pacote). Cole o caminho da pasta e confirme a seleção. A conexão aparecerá acima.");
        guide.Children.Add(Ui.Text("Se já usa a extensão de uma versão anterior do Pulso, clique em Recarregar no cartão da extensão após abrir o aplicativo atualizado.", "Hint", 10));
        guide.Children.Add(Ui.Text("Para usar em janelas anônimas ou InPrivate, habilite essa permissão nos detalhes da extensão.", "Hint", 8));

        var firefox = Ui.Section(ContentPage, "Instalar no Firefox", "Carregue o pacote da extensão no Firefox ou em navegadores derivados.");
        Step(firefox, "1. Copie o caminho do pacote", "O Pulso gera o pacote pulso-firefox.xpi e os arquivos descompactados em uma pasta local.");
        Ui.Buttons(firefox, Ui.Button("Copiar caminho da extensão (Firefox)", () => _ = PrepareFirefoxAsync(false)));
        Step(firefox, "2. Abra a depuração do Firefox", "Abre a página about:debugging#/runtime/this-firefox no Firefox.");
        Ui.Buttons(firefox, Ui.Button("Abrir no Firefox", () => _ = PrepareFirefoxAsync(true)));
        Step(firefox, "3. Carregue o arquivo copiado", "Clique em Carregar extensão temporária... e selecione o arquivo pulso-firefox.xpi (ou manifest.json na mesma pasta). Depois, clique no ícone do Pulso na barra do Firefox e clique em Permitir acesso aos sites.");
        firefox.Children.Add(Ui.Text("No Firefox comum, extensões locais sem assinatura da Mozilla ficam ativas até fechar o navegador (basta carregar novamente ao reabrir). No Firefox Developer Edition, Nightly, ESR ou LibreWolf com xpinstall.signatures.required=false, o arquivo .xpi pode ser instalado de forma permanente.", "Hint", 10));
    }

    private async Task PrepareBrowserAsync(string? browser)
    {
        try
        {
            string folder = bridge.PrepareExtension();
            await Task.Run(() => LinuxDesktopServices.EnsureFlatpakExtensionAccess(browser, folder));
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(folder);
            if (browser == null) { Notify("Caminho da extensão copiado: " + folder); return; }
            if (!LinuxDesktopServices.OpenBrowserExtensions(browser))
            {
                Notify("Abra " + (browser == "Edge" ? "edge" : "chrome") + "://extensions no navegador. O caminho da pasta já foi copiado.", true);
                return;
            }
            Notify("Gerenciador aberto. O caminho da extensão está copiado.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify("Não foi possível preparar ou abrir a extensão. " + ex.Message, true);
        }
    }

    private async Task PrepareFirefoxAsync(bool openBrowser)
    {
        try
        {
            _ = bridge.PrepareFirefoxExtension();
            string package = bridge.FirefoxExtensionPackage;
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(package);
            if (!openBrowser) { Notify("Caminho da extensão do Firefox copiado: " + package); return; }
            if (!LinuxDesktopServices.OpenBrowserExtensions("Firefox"))
            {
                Notify("Abra about:debugging#/runtime/this-firefox no Firefox. O caminho do pacote já foi copiado.", true);
                return;
            }
            Notify("Página de depuração do Firefox aberta. O caminho do pacote .xpi está copiado.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify("Não foi possível preparar ou abrir a extensão do Firefox. " + ex.Message, true);
        }
    }
}
