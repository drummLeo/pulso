using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Pulso;

public partial class MainWindow
{
    private TextBlock? siteState, browserState, historyToday;
    private TextBox? siteInput;
    private readonly List<(string? Key, TextBlock Label, Button Edit)> usageLabels = [];
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
        var shared = Ui.Section(ContentPage, "Limite compartilhado", "Todos os itens usam o mesmo saldo. Os limites valem também fora das sessões de foco.");
        AddUsageRow(shared, null, null, null);
        var sites = Ui.Section(ContentPage, "Sites", "Adicione um domínio ou cole um link. Os subdomínios também entram na lista.");
        var input = Ui.TextInput("", 2048);
        siteInput = input;
        sites.Children.Add(Ui.Label("Endereço do site", input));
        var inputRow = new DockPanel();
        var addSite = Ui.Button("Adicionar site", () => AddSite(input.Text), "Primary");
        DockPanel.SetDock(addSite, Dock.Right);
        addSite.Margin = new Thickness(7, 0, 0, 0);
        inputRow.Children.Add(addSite);
        inputRow.Children.Add(input);
        sites.Children.Add(inputRow);
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddSite(input.Text); e.Handled = true; } };
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
        Ui.Buttons(apps, Ui.Button("Escolher janela aberta", PickOpenApp), Ui.Button("Escolher arquivo .exe", PickFile, "Quiet"));
        if (data.Apps.Length == 0) apps.Children.Add(Ui.Text("Nenhum aplicativo na lista.", "Hint", 8));
        foreach (var name in data.Apps)
            AddUsageRow(apps, UsageClock.AppKey(name), name, Ui.Button("Remover", () =>
                Change(data with { Apps = data.Apps.Where(a => a != name).ToArray() }, "Aplicativo removido da lista."), "Quiet", "Remover " + name));
    }

    private void AddUsageRow(Panel section, string? key, string? name, Button? remove)
    {
        var limit = UsageClock.Get(data, key);
        var info = new StackPanel();
        if (name != null)
        {
            var title = Ui.Text(key == UsageClock.AppKey(name) ? name + ".exe" : name);
            title.FontWeight = FontWeights.SemiBold;
            info.Children.Add(title);
        }
        var label = Ui.Text("", "Hint", name == null ? 0 : 2);
        info.Children.Add(label);
        string description = name == null ? "limite compartilhado" : "limite de " + name;
        var edit = Ui.Button(limit.Enabled ? "Editar limite" : "Configurar limite", () => EditUsageLimit(key, name), null,
            (limit.Enabled ? "Editar " : "Configurar ") + description);
        var buttons = new List<Button> { edit };
        usageLabels.Add((key, label, edit));
        if (limit.Enabled)
        {
            var disable = Ui.Button("Desativar", () =>
            {
                if (Change(UsageClock.Configure(data, key, null, null), "Limite desativado.")) FocusUsageRow(key);
            }, "Quiet", "Desativar " + description);
            disable.ToolTip = "Desativar e zerar o consumo deste limite.";
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
        var period = UsagePeriod.At(now, Zone);
        foreach (var (key, label, _) in usageLabels)
        {
            var rule = UsageClock.Get(data, key);
            var lines = new List<string>();
            if (rule.HourMinutes is { } hour)
                lines.Add("Nesta hora · " + UsedTime(data.Usage.HourStart == period.HourStart ? rule.HourTicks : 0) + $" de {hour} min");
            if (rule.DayMinutes is { } day)
                lines.Add("Hoje · " + UsedTime(data.Usage.Day == period.Day ? rule.DayTicks : 0) + $" de {day} min");
            if (!rule.Enabled) lines.Add(key == null ? "Desativado" : "Sem limite próprio");
            var end = UsageClock.BlockedUntil(data, key, period);
            if (key != null && data.Active is { } focus && focus.Start <= now && focus.End > now)
                end = UsageClock.Later(end, focus.End);
            if (end.HasValue)
            {
                var local = end.Value.ToLocalTime();
                string date = local.Date == now.LocalDateTime.Date ? "hoje" : local.ToString("dd/MM", Pt);
                lines.Add("Em pausa até " + date + " às " + local.ToString("HH:mm", Pt));
            }
            string text = string.Join(Environment.NewLine, lines);
            if (label.Text != text) label.Text = text;
        }
    }

    private void EditUsageLimit(string? key, string? name)
    {
        var limit = UsageClock.Get(data, key);
        var dialog = new FormWindow(this, name == null ? "Limite compartilhado" : "Limite de " + name, "Salvar limite");
        dialog.Fields.Children.Add(Ui.Text("A hora se renova a cada hora cheia e o dia, à meia-noite. Deixe um campo em branco para desativar e zerar aquele período.", "Hint", 8));
        var hour = dialog.Field("Minutos por hora (1 a 60)", limit.HourMinutes?.ToString(Pt) ?? "");
        var day = dialog.Field("Minutos por dia (1 a 1440)", limit.DayMinutes?.ToString(Pt) ?? "");
        dialog.Loaded += (_, _) => { hour.Focus(); hour.SelectAll(); };
        dialog.Accept.Click += (_, _) =>
        {
            if (!ReadLimit(hour, 60, "hora", out int? perHour) || !ReadLimit(day, 1440, "dia", out int? perDay)) return;
            var next = UsageClock.Configure(data, key, perHour, perDay);
            bool enabled = perHour.HasValue || perDay.HasValue;
            if (Change(next, enabled ? "Limite salvo." : "Limite desativado.")) dialog.DialogResult = true;
            else dialog.Invalid("O limite não foi salvo. Verifique o espaço e o acesso à pasta de dados.");
        };
        if (dialog.ShowDialog() == true) FocusUsageRow(key);

        bool ReadLimit(TextBox field, int maximum, string period, out int? result)
        {
            result = null;
            string value = field.Text.Trim();
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
            siteInput?.Focus(); siteInput?.SelectAll();
            return;
        }
        if (Change(data with { Sites = data.Sites.Append(d).Order(StringComparer.OrdinalIgnoreCase).ToArray() }, d + " adicionado à lista.")) siteInput?.Focus();
    }
    private bool AddApp(string raw)
    {
        var name = Targets.App(raw);
        if (name == null) { Notify("Escolha um aplicativo de uso pessoal. Este componente é usado pelo Windows ou pelo Pulso.", true); return false; }
        if (data.Apps.Contains(name, StringComparer.OrdinalIgnoreCase)) { Notify("Este aplicativo já está na lista.", true); return false; }
        return Change(data with { Apps = data.Apps.Append(name).Order(StringComparer.CurrentCultureIgnoreCase).ToArray() }, name + " adicionado à lista.");
    }
    private void PickFile()
    {
        var dialog = new OpenFileDialog { Title = "Escolher aplicativo", Filter = "Aplicativo do Windows (*.exe)|*.exe", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) AddApp(Path.GetFileNameWithoutExtension(dialog.FileName));
    }
    private void PickOpenApp()
    {
        var dialog = new FormWindow(this, "Escolher janela aberta", "Adicionar aplicativo");
        var list = new ListBox { MaxHeight = 250, MinHeight = 65, Padding = new Thickness(4), ItemsSource = WindowBlocker.OpenApps().Except(data.Apps, StringComparer.OrdinalIgnoreCase) };
        dialog.Fields.Children.Add(Ui.Label("Aplicativos com janela aberta", list));
        dialog.Fields.Children.Add(list);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        else dialog.Fields.Children.Add(Ui.Text("Abra o programa desejado ou use a opção de escolher um arquivo .exe.", "Hint", 8));
        dialog.Accept.IsEnabled = list.Items.Count > 0;
        void Take()
        {
            if (list.SelectedItem is string name && AddApp(name)) dialog.DialogResult = true;
        }
        dialog.Accept.Click += (_, _) => Take();
        list.MouseDoubleClick += (_, _) => Take();
        dialog.Loaded += (_, _) => list.Focus();
        dialog.ShowDialog();
    }

    private void SchedulesPage()
    {
        var header = new StackPanel();
        Ui.Card(ContentPage, header);
        Ui.Row(header, Ui.Text("Rotina semanal", "Heading"), Ui.Button("Novo agendamento", () => EditRoutine(null), "Primary"));
        header.Children.Add(Ui.Text("Os horários seguem o relógio do Windows. Mantenha o Pulso aberto ou na área de notificação.", "Hint", 8));
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
            box.Children.Add(Ui.Text(r.Enabled ? "Agendamento ativo" : "Agendamento desativado", "Hint", 3));
            Ui.Buttons(box, Ui.Button("Editar", () => EditRoutine(r), null, "Editar " + r.Name),
                Ui.Button("Excluir", () =>
                {
                    if (FormWindow.Confirm(this, "Excluir agendamento?", "As próximas sessões de “" + r.Name + "” serão removidas da agenda.", "Excluir agendamento"))
                        Change(data with { Routines = data.Routines.Where(item => item.Id != r.Id).ToArray() }, "Agendamento excluído.");
                }, "Quiet", "Excluir " + r.Name));
        }
    }
    private void EditRoutine(Routine? existing)
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
                Content = shortName, IsChecked = (initial.Days & (1 << (int)day)) != 0, MinWidth = 0,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 9, 3)
            };
            AutomationProperties.SetName(check, Pt.DateTimeFormat.GetDayName(day));
            checks[day] = check; daysRow.Children.Add(check);
        }
        dialog.Fields.Children.Add(daysRow);
        dialog.Loaded += (_, _) => name.Focus();
        dialog.Accept.Click += (_, _) =>
        {
            int days = checks.Where(p => p.Value.IsChecked == true).Sum(p => 1 << (int)p.Key);
            if (string.IsNullOrWhiteSpace(name.Text)) { dialog.Invalid("Dê um nome à sessão.", name); return; }
            if (!TimeOnly.TryParseExact(time.Text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            { dialog.Invalid("Digite um horário de 00:00 a 23:59.", time); return; }
            if (!int.TryParse(minutes.Text, out var duration) || duration is < 1 or > 720) { dialog.Invalid("A duração deve ficar entre 1 e 720 minutos.", minutes); return; }
            if (days == 0) { dialog.Invalid("Selecione pelo menos um dia da semana.", checks.Values.First()); return; }
            var current = data.Routines.FirstOrDefault(r => r.Id == initial.Id) ?? initial;
            int startMinute = start.Hour * 60 + start.Minute;
            var item = current with { Name = name.Text.Trim(), Days = days, StartMinute = startMinute, Minutes = duration,
                LastOccurrence = current.Days == days && current.StartMinute == startMinute ? current.LastOccurrence : null };
            var routines = existing == null ? data.Routines.Append(item).ToArray() : data.Routines.Select(r => r.Id == item.Id ? item : r).ToArray();
            if (Change(data with { Routines = routines }, "Agendamento salvo.")) dialog.DialogResult = true;
        };
        dialog.ShowDialog();
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
            duration.FontWeight = FontWeights.SemiBold; duration.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(duration, Dock.Right);
            dock.Children.Add(duration); dock.Children.Add(row);
            body.Children.Add(dock);
        }
        if (data.History.Length > historyLimit)
            Ui.Buttons(body, Ui.Button("Mostrar mais sessões", () => { historyLimit += 40; RenderPage(); }));
    }

    private void BrowserPage()
    {
        var info = Ui.Section(ContentPage, "Conexão com este PC", "");
        browserState = Ui.Text("", null, 8); browserState.FontWeight = FontWeights.SemiBold;
        info.Children.Add(browserState);
        info.Children.Add(Ui.Text(bridge.Error ?? "A extensão identifica o domínio da lista quando a aba está em primeiro plano. O histórico de navegação fica no navegador.", "Hint", 4));
        var guide = Ui.Section(ContentPage, "Instalar a extensão", "Faça isso uma vez em cada perfil do Chrome ou do Edge que quiser usar.");
        void Step(string title, string detail)
        {
            var label = Ui.Text(title, null, 16); label.FontWeight = FontWeights.SemiBold; guide.Children.Add(label);
            guide.Children.Add(Ui.Text(detail, "Hint", 3));
        }
        Step("1. Copie o caminho da pasta", "O Pulso prepara a extensão em uma pasta local deste computador.");
        Ui.Buttons(guide, Ui.Button("Copiar caminho da extensão", () => PrepareBrowser(null)));
        Step("2. Abra o gerenciador de extensões", "Escolha o navegador em que deseja bloquear sites.");
        Ui.Buttons(guide, Ui.Button("Abrir no Chrome", () => PrepareBrowser("Chrome")), Ui.Button("Abrir no Edge", () => PrepareBrowser("Edge")));
        Step("3. Carregue a pasta copiada", "Ative o Modo do desenvolvedor e clique em Carregar sem compactação (ou Carregar sem pacote). Cole o caminho da pasta e confirme a seleção. A conexão aparecerá acima.");
        guide.Children.Add(Ui.Text("Se já usa a extensão de uma versão anterior do Pulso, clique em Recarregar no cartão da extensão após abrir o aplicativo atualizado.", "Hint", 10));
        guide.Children.Add(Ui.Text("Para usar em janelas anônimas ou InPrivate, habilite essa permissão nos detalhes da extensão.", "Hint", 12));
    }
    private void PrepareBrowser(string? browser)
    {
        try
        {
            string folder = bridge.PrepareExtension();
            Clipboard.SetDataObject(folder, true);
            if (browser == null) { Notify("Caminho da extensão copiado."); return; }
            string exe = browser == "Edge" ? @"Microsoft\Edge\Application\msedge.exe" : @"Google\Chrome\Application\chrome.exe";
            var path = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData }
                .Select(Environment.GetFolderPath).Select(p => Path.Combine(p, exe)).FirstOrDefault(File.Exists);
            if (path == null) { Notify("Abra " + (browser == "Edge" ? "edge" : "chrome") + "://extensions no navegador. O caminho da pasta já foi copiado.", true); return; }
            var start = new ProcessStartInfo(path) { UseShellExecute = false };
            start.ArgumentList.Add(browser == "Edge" ? "edge://extensions/" : "chrome://extensions/");
            using var process = Process.Start(start);
            Notify("Gerenciador aberto. O caminho da extensão está copiado.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or System.Runtime.InteropServices.COMException)
        {
            Notify("Não foi possível preparar ou abrir a extensão. " + ex.Message, true);
        }
    }
}
