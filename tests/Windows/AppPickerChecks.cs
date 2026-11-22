using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Pulso;

internal static class AppPickerChecks
{
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder value, int size, out int needed);

    public static int Run()
    {
        const string prefix = "PulsoCatalogo";
        var name = new StringBuilder(256);
        if (!GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), 2, name, name.Capacity * 2, out _)
            || !name.ToString().StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name.ToString()[prefix.Length..], "N", out _))
            throw new InvalidOperationException("Execute catalogo-interface em uma área de trabalho privada chamada PulsoCatalogo seguida de um GUID sem traços.");

        var profile = Directory.CreateTempSubdirectory("pulso-catalogo-teste-");
        var previous = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", profile.FullName);
        System.Reflection.Assembly.SetEntryAssembly(typeof(MainWindow).Assembly);
        var app = new DesktopApp();
        try
        {
            app.InitializeComponent();
            var checks = app.Dispatcher.InvokeAsync(async () =>
            {
                await Until(() => app.MainWindow?.IsLoaded == true);
                app.MainWindow.Hide();
                await Exercise(app, profile.FullName);
            }, DispatcherPriority.ApplicationIdle).Task.Unwrap();
            var frame = new DispatcherFrame();
            _ = checks.ContinueWith(_ => app.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            checks.GetAwaiter().GetResult();
            Console.WriteLine("OK  Catálogo Windows agrupa, busca, atualiza e salva somente a seleção, preservando a lista e os limites.");
            return 0;
        }
        finally
        {
            app.Shutdown();
            Environment.SetEnvironmentVariable("LOCALAPPDATA", previous);
            if (profile.Parent?.FullName.TrimEnd('\\') == Path.GetTempPath().TrimEnd('\\') && profile.Name.StartsWith("pulso-catalogo-teste-"))
                profile.Delete(true);
        }
    }

    private static async Task Exercise(DesktopApp app, string folder)
    {
        var store = new FocusStore(folder);
        var initial = new FocusData { Theme = "dark", Apps = ["ManualAntigo", "NavegadorTeste"] };
        initial = UsageClock.Configure(UsageClock.Configure(initial, "app:navegadorteste", 12, null), null, null, 45);
        initial = UsageClock.Advance(initial, DateTimeOffset.Now, TimeZoneInfo.Local);
        initial.Usage.Items["app:navegadorteste"] = initial.Usage.Items["app:navegadorteste"] with { HourTicks = 10_000_000 };
        initial = initial with { Usage = initial.Usage with { Shared = initial.Usage.Shared with { DayTicks = 20_000_000 } } };
        store.Save(initial);
        var path = Path.Combine(folder, "foco.json");
        var legacy = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        legacy.Remove("AppInfo");
        File.WriteAllText(path, legacy.ToJsonString());
        var full = new AppScan([
            new DiscoveredApp("ilha", "Jogo da Ilha", "games"),
            new DiscoveredApp("vale", "Jogo do Vale", "games"),
            new DiscoveredApp("navegadorteste", "Navegador Azul", "browsers"),
            new DiscoveredApp("mensagens", "Mensagens do Café", "communication")
        ], true);
        using var release = new ManualResetEventSlim();
        Func<CancellationToken, AppScan> scan = token => { release.Wait(token); return full; };
        var uiThread = Environment.CurrentManagedThreadId;
        var main = new MainWindow(store, new BrowserLink(folder, 0), _ => null, token =>
        {
            Check(Environment.CurrentManagedThreadId != uiThread, "A descoberta precisa ocorrer fora da thread de interface.");
            return scan(token);
        });
        main.Show();
        var page = (StackPanel)main.FindName("ContentPage");
        Press(ButtonNamed((StackPanel)main.FindName("NavItems"), "Lista de bloqueio"));
        await Until(() => Buttons(page).Any(b => Name(b) == "Buscar aplicativos instalados"));
        ButtonNamed(page, "Escolher janela aberta");
        ButtonNamed(page, "Escolher arquivo .exe");

        async Task Open(Func<Window, Task> inspect)
        {
            Press(ButtonNamed(page, "Buscar aplicativos instalados"));
            Window? picker = null;
            await Until(() => (picker = app.Windows.OfType<Window>().SingleOrDefault(w => w.Title == "Aplicativos instalados" && w.IsVisible)) != null);
            try { await inspect(picker!); }
            finally { if (picker!.IsVisible) picker.Close(); }
        }
        async Task WaitForScan(Window picker) => await Until(() => ButtonNamed(picker, "Atualizar lista").IsEnabled);

        await Open(async picker =>
        {
            Check(!ButtonNamed(picker, "Atualizar lista").IsEnabled, "A busca automática deve sinalizar que está em andamento.");
            release.Set();
            await WaitForScan(picker);
            Check(Texts(picker).Any(t => t.Contains("Alguns aplicativos", StringComparison.Ordinal)), "A descoberta parcial precisa ficar visível.");
            Search(picker).Text = "comunicacao";
            Check(Checks(picker).Count() == 1 && !IsChecked(picker, "mensagens"), "A busca por categoria deve aceitar texto sem acento.");
            Search(picker).Text = "ilha";
            Press(ButtonNamed(picker, "Selecionar resultados em Jogos"));
            await Until(() => IsChecked(picker, "ilha"));
            Press(ButtonNamed(picker, "Limpar busca"));
            await Until(() => Search(picker).Text.Length == 0);
            Check(!IsChecked(picker, "vale") && IsChecked(picker, "ManualAntigo"), "Selecionar os resultados alterou itens ocultos.");
            scan = _ => throw new IOException("Falha fictícia");
            Press(ButtonNamed(picker, "Atualizar lista"));
            await Until(() => Texts(picker).Any(t => t.Contains("Não foi possível buscar", StringComparison.Ordinal)));
            Check(IsChecked(picker, "ilha"), "Uma falha na atualização descartou o rascunho.");
            Press(ButtonNamed(picker, "Cancelar"));
            await Until(() => !picker.IsVisible);
        });
        Check(store.Load().Apps.SequenceEqual(initial.Apps), "Cancelar alterou a lista salva.");

        scan = _ => full;
        await Open(async picker =>
        {
            await WaitForScan(picker);
            Check(!IsChecked(picker, "ilha"), "O rascunho cancelado reapareceu.");
            Search(picker).Text = "jogo da ilha";
            Check(Checks(picker).Count() == 1, "Buscar pelo nome amigável deve filtrar os aplicativos.");
            Search(picker).Text = "ILHA.EXE";
            Press(ButtonNamed(picker, "Selecionar resultados em Jogos"));
            await Until(() => IsChecked(picker, "ilha"));
            Search(picker).Text = "jogos";
            Check(Checks(picker).Count() == 2 && !IsChecked(picker, "vale"), "A busca por categoria deve mostrar só os dois jogos fictícios.");
            Press(ButtonNamed(picker, "Selecionar resultados em Jogos"));
            await Until(() => IsChecked(picker, "vale"));
            Search(picker).Text = "vale";
            Press(ButtonNamed(picker, "Desmarcar resultados em Jogos"));
            await Until(() => !IsChecked(picker, "vale"));
            Search(picker).Text = "sem-correspondencia";
            Check(Checks(picker).Count() == 0 && Texts(picker).Any(t => t.Contains("Nenhum resultado")), "A busca vazia precisa explicar o resultado.");
            Search(picker).Clear();
            scan = _ => new AppScan([], false);
            Press(ButtonNamed(picker, "Atualizar lista"));
            await Until(() => Texts(picker).Any(t => t.Contains("Nenhum aplicativo instalado")));
            Check(IsChecked(picker, "ilha") && IsChecked(picker, "ManualAntigo"), "Atualizar perdeu uma seleção fora da varredura.");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Press(ButtonNamed(picker, "Salvar lista"));
                await Until(() => Texts(picker).Any(t => t.Contains("Não foi possível salvar a lista")));
                Check(IsChecked(picker, "ilha"), "Uma falha ao salvar perdeu o rascunho.");
            }
            Press(ButtonNamed(picker, "Salvar lista"));
            await Until(() => !picker.IsVisible);
        });
        var saved = store.Load();
        Check(saved.Apps.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["ManualAntigo", "NavegadorTeste", "ilha"]),
            "Salvar incluiu um jogo oculto ou perdeu um aplicativo antigo.");
        Check(!File.ReadAllText(path).Contains("Jogo do Vale"), "A configuração gravou parte do inventário sem seleção.");
        Check(Texts(page).Contains("Jogos") && Texts(page).Contains("Navegadores") && Texts(page).Contains("Jogo da Ilha"),
            "A lista salva precisa usar as categorias e os nomes exibidos no seletor.");
        var rule = UsageClock.Get(saved, "app:navegadorteste");
        Check(rule.HourMinutes == 12 && saved.Usage.Shared.DayMinutes == 45
            && (saved.Usage.HourStart != initial.Usage.HourStart || rule.HourTicks == 10_000_000)
            && (saved.Usage.Day != initial.Usage.Day || saved.Usage.Shared.DayTicks == 20_000_000),
            "A seleção alterou a configuração ou o consumo de limites existentes.");
        ButtonNamed(page, "Editar limite de NavegadorTeste");
        var reopened = SelectedApps.Read(new FocusStore(folder).Load()).Single(a => a.ProcessName == "ilha");
        Check(reopened.DisplayName == "Jogo da Ilha" && reopened.CategoryId == "games", "Reabrir o perfil perdeu os dados do aplicativo selecionado.");
        var lake = new DiscoveredApp("lago", "Jogo do Lago", "games");
        scan = _ => new AppScan([lake], false);
        await Open(async picker =>
        {
            await WaitForScan(picker);
            Check(IsChecked(picker, "ilha") && !IsChecked(picker, "lago"), "Um jogo novo não deve entrar sozinho na lista.");
            Press(ButtonNamed(picker, "Selecionar todos em Jogos"));
            await Until(() => IsChecked(picker, "lago"));
            Press(ButtonNamed(picker, "Salvar lista"));
            await Until(() => !picker.IsVisible);
        });
        scan = _ => new AppScan([lake, new DiscoveredApp("serra", "Jogo da Serra", "games")], false);
        await Open(async picker =>
        {
            await WaitForScan(picker);
            Check(!IsChecked(picker, "serra") && IsChecked(picker, "ilha") && IsChecked(picker, "lago"),
                "Selecionar uma categoria inteira não deve incluir instalações futuras.");
            var individual = Item(picker, "ilha");
            ((IToggleProvider)new CheckBoxAutomationPeer(individual).GetPattern(PatternInterface.Toggle)).Toggle();
            await Until(() => !IsChecked(picker, "ilha"));
            Check(IsChecked(picker, "lago") && !IsChecked(picker, "serra"), "Uma marcação individual modificou outro aplicativo.");
            picker.Close();
        });
        Check(store.Load().Apps.Contains("ilha"), "Fechar sem salvar alterou os bloqueios.");
        Press(ButtonNamed(page, "Remover ilha"));
        await Until(() => !store.Load().Apps.Contains("ilha"));
        Check(!File.ReadAllText(path).Contains("Jogo da Ilha"), "Remover um app deixou seu rótulo no perfil.");
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        foreach (var node in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return node;
            foreach (var child in Children(node)) yield return child;
        }
    }
    private static string Name(Button button) => AutomationProperties.GetName(button) is { Length: > 0 } label ? label : button.Content?.ToString() ?? "";
    private static IEnumerable<Button> Buttons(DependencyObject root) => Children(root).OfType<Button>();
    private static Button ButtonNamed(DependencyObject root, string text) => Buttons(root).Single(b => Name(b) == text);
    private static IEnumerable<string> Texts(DependencyObject root) => Children(root).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text);
    private static IEnumerable<CheckBox> Checks(DependencyObject root) => Children(root).OfType<CheckBox>();
    private static CheckBox Item(Window window, string name) => Checks(window).Single(c =>
        AutomationProperties.GetName(c).EndsWith(name + ".exe", StringComparison.OrdinalIgnoreCase));
    private static bool IsChecked(Window window, string name) => Item(window, name).IsChecked == true;
    private static TextBox Search(Window window) => Children(window).OfType<TextBox>().Single();
    private static void Press(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> test)
    {
        var watch = Stopwatch.StartNew();
        while (!test())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("A interface não atingiu o estado esperado.");
            await Task.Delay(20);
        }
    }
}
