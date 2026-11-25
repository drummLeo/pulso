using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Pulso;

internal static class SelectedApps
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer Titles = StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), true);

    public static DiscoveredApp Manual(string name, string? path = null) =>
        new(name, name, AppCategories.ForLinux(name, null, path));

    public static IEnumerable<DiscoveredApp> Read(FocusData data) => data.Apps.Select(name =>
        data.AppInfo.TryGetValue(name, out var info) ? new DiscoveredApp(name, info.DisplayName, info.CategoryId) : Manual(name));

    public static FocusData Apply(FocusData data, IEnumerable<DiscoveredApp> selected)
    {
        var chosen = selected.ToDictionary(app => app.ProcessName, Names);
        var previous = data.Apps.ToHashSet(Names);
        var names = previous.SetEquals(chosen.Keys) ? data.Apps : chosen.Keys.Select(name =>
            previous.TryGetValue(name, out var old) ? old : name).Order(Names).ToArray();
        var details = new Dictionary<string, SavedAppInfo>(Names);
        foreach (var name in names)
        {
            var app = chosen[name];
            if (!Names.Equals(app.DisplayName, name) || app.CategoryId != AppCategories.ForLinux(name))
                details[name] = new(app.DisplayName, app.CategoryId);
        }
        if (ReferenceEquals(names, data.Apps) && details.Count == data.AppInfo.Count
            && details.All(entry => data.AppInfo.GetValueOrDefault(entry.Key) == entry.Value)) return data;
        return data with { Apps = names, AppInfo = details };
    }

    public static IEnumerable<IGrouping<string, DiscoveredApp>> Groups(IEnumerable<DiscoveredApp> apps) =>
        apps.OrderBy(app => AppCategories.Order(app.CategoryId)).ThenBy(app => app.DisplayName, Titles)
            .ThenBy(app => app.ProcessName, Names).GroupBy(app => AppCategories.Label(app.CategoryId));

    public static TextBlock Heading(string category)
    {
        var heading = Ui.Text(category, "Heading", 12);
        heading.FontSize = 17;
        AutomationProperties.SetHeadingLevel(heading, 3);
        return heading;
    }
}

internal sealed class AppPickerWindow : FormWindow
{
    private static readonly CompareInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
    private readonly Func<CancellationToken, AppScan> scan;
    private readonly HashSet<string> original, selected;
    private Dictionary<string, DiscoveredApp> entries;
    private HashSet<string>? installed;
    private CancellationTokenSource? pending;
    private int visibleCount;
    private readonly TextBox search = Ui.TextInput("", 200);
    private readonly TextBlock summary = Ui.Text("", "Hint"), feedback = Ui.Text("", "Hint", 4);
    private readonly Button clear, refresh;

    public AppPickerWindow(Window owner, IEnumerable<DiscoveredApp> saved, Func<CancellationToken, AppScan> scan,
        Func<IEnumerable<DiscoveredApp>, bool> save) : base(owner, "Aplicativos instalados", "Salvar lista")
    {
        this.scan = scan;
        entries = saved.ToDictionary(app => app.ProcessName, StringComparer.OrdinalIgnoreCase);
        original = entries.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        selected = new(original, StringComparer.OrdinalIgnoreCase);
        SizeToContent = SizeToContent.Manual;
        CanResize = true;
        double ownerWidth = owner.Bounds.Width > 0 ? owner.Bounds.Width : 700;
        Width = Math.Min(640, Math.Max(420, ownerWidth - 12));
        MinWidth = Math.Min(365, Width);
        Height = 620;
        MinHeight = 370;

        Header.Children.Add(Ui.Text("Marque os aplicativos que deseja bloquear.", "Hint", 4));
        Header.Children.Add(Ui.Label("Buscar por nome, comando ou categoria", search));
        clear = Ui.Button("Limpar busca", () => { search.Clear(); search.Focus(); }, "Quiet");
        Ui.Row(Header, search, clear);
        refresh = Ui.Button("Atualizar lista", () => _ = RefreshAsync());
        Ui.Row(Header, summary, refresh);
        Header.Children.Add(feedback);
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);

        search.TextChanged += (_, _) => { Render(); BodyScroll.Offset = Vector.Zero; };
        Accept.IsDefault = false;
        Accept.Click += (_, _) =>
        {
            if (save(selected.Select(key => entries[key]))) Close(true);
            else Invalid("Não foi possível salvar a lista. Verifique o espaço e o acesso à pasta de dados.");
        };
        Opened += async (_, _) => { search.Focus(); await RefreshAsync(); };
        Closed += (_, _) => pending?.Cancel();
        Render();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            search.Focus();
            search.SelectAll();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private async Task RefreshAsync()
    {
        if (pending != null) return;
        using var source = new CancellationTokenSource();
        pending = source;
        refresh.IsEnabled = false;
        Announce(feedback, "Buscando aplicativos instalados…");
        AppScan result;
        try { result = await Task.Run(() => scan(source.Token), source.Token); }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { return; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!source.IsCancellationRequested)
                Announce(feedback, "Não foi possível buscar os aplicativos. Use Atualizar lista para tentar novamente.");
            return;
        }
        finally
        {
            pending = null;
            if (!source.IsCancellationRequested) refresh.IsEnabled = true;
        }
        if (source.IsCancellationRequested) return;
        var next = entries.Values.Where(app => original.Contains(app.ProcessName) || selected.Contains(app.ProcessName))
            .ToDictionary(app => app.ProcessName, StringComparer.OrdinalIgnoreCase);
        foreach (var app in result.Apps) next[app.ProcessName] = app;
        entries = next;
        installed = result.Apps.Select(app => app.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();
        if (installed.Count == 0) messages.Add("Nenhum aplicativo instalado foi encontrado.");
        if (result.Partial) messages.Add("Alguns aplicativos podem não ter aparecido. Use Atualizar lista para tentar novamente.");
        else if (installed.Count == 0) messages.Add("Você também pode escolher uma janela aberta ou um executável na lista de bloqueio.");
        Announce(feedback, string.Join(" ", messages));
        Render();
    }

    private static bool Matches(string text, string query) =>
        Portuguese.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private void Render()
    {
        string query = (search.Text ?? "").Trim();
        clear.IsEnabled = !string.IsNullOrEmpty(search.Text);
        Fields.Children.Clear();
        var shown = entries.Values.Where(app => query.Length == 0 || Matches(app.DisplayName, query)
            || Matches(app.ProcessName, query) || Matches(AppCategories.Label(app.CategoryId), query)).ToArray();
        visibleCount = shown.Length;
        foreach (var group in SelectedApps.Groups(shown)) AddGroup(group, query.Length != 0);
        if (query.Length > 0 && shown.Length == 0)
            Fields.Children.Add(Ui.Text("Nenhum resultado para esta busca. Tente outro nome ou limpe a busca.", "Hint", 14));
        UpdateSummary();
    }

    private void AddGroup(IGrouping<string, DiscoveredApp> group, bool filtered)
    {
        var rows = new List<CheckBox>();
        bool changingGroup = false;
        var toggle = new Button { VerticalAlignment = VerticalAlignment.Center };
        toggle.Classes.Add("Quiet");
        toggle.Click += (_, _) => ToggleGroup();
        Ui.Row(Fields, SelectedApps.Heading(group.Key), toggle);

        foreach (var app in group)
        {
            string file = app.ProcessName;
            bool showFile = !string.Equals(file, app.DisplayName, StringComparison.OrdinalIgnoreCase);
            bool missing = installed != null && !installed.Contains(app.ProcessName);
            var info = new StackPanel();
            info.Children.Add(Ui.Text(app.DisplayName));
            string hint = showFile ? file : "";
            if (missing) hint = showFile ? file + " · Ausente na última atualização" : "Ausente na última atualização";
            if (hint.Length > 0) info.Children.Add(Ui.Text(hint, "Hint"));

            var item = new CheckBox
            {
                Content = info,
                IsChecked = selected.Contains(app.ProcessName),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(5, 3, 0, 3),
                Margin = new Thickness(0, 3, 4, 1)
            };
            Ui.SetResourceBrush(item, CheckBox.ForegroundProperty, "Ink");
            AutomationProperties.SetName(item, showFile ? app.DisplayName + ", " + file : file);
            AutomationProperties.SetHelpText(item, missing ? group.Key + ". Ausente na última atualização." : group.Key);
            item.IsCheckedChanged += (_, _) => SetSelected(item.IsChecked == true);
            rows.Add(item);
            Fields.Children.Add(item);

            void SetSelected(bool value)
            {
                if (value) selected.Add(app.ProcessName);
                else selected.Remove(app.ProcessName);
                if (!changingGroup) { UpdateAction(); UpdateSummary(); }
            }
        }
        UpdateAction();

        void ToggleGroup()
        {
            bool value = rows.Any(item => item.IsChecked != true);
            changingGroup = true;
            foreach (var item in rows) item.IsChecked = value;
            changingGroup = false;
            UpdateAction();
            UpdateSummary();
        }

        void UpdateAction()
        {
            bool all = rows.All(item => item.IsChecked == true);
            string label = (all ? "Desmarcar " : "Selecionar ") + (filtered ? "resultados" : "todos");
            toggle.Content = label;
            AutomationProperties.SetName(toggle, label + " em " + group.Key);
        }
    }

    private void UpdateSummary()
    {
        string quantity = selected.Count == 1 ? "1 aplicativo selecionado" : selected.Count + " aplicativos selecionados";
        if (!string.IsNullOrWhiteSpace(search.Text))
            quantity = (visibleCount == 1 ? "1 resultado" : visibleCount + " resultados") + " · " + quantity;
        Announce(summary, quantity);
    }

    private static void Announce(TextBlock label, string text)
    {
        label.IsVisible = text.Length > 0;
        if (label.Text != text) label.Text = text;
    }
}
