using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pulso;

internal static class ThemeChecks
{
    public static int Run(string? imageFolder = null)
    {
        var folder = Directory.CreateTempSubdirectory("pulso-tema-teste-");
        System.Reflection.Assembly.SetEntryAssembly(typeof(MainWindow).Assembly);
        var app = new DesktopApp();
        app.InitializeComponent();
        try
        {
            var store = new FocusStore(folder.FullName);
            var state = UsageClock.Configure(new FocusData { Sites = ["pulso.test"], Minutes = 50 }, null, 15, null);
            state = FocusClock.Start(state, DateTimeOffset.Now, state.Minutes);
            var original = JsonSerializer.SerializeToNode(state)!.AsObject();
            original.Remove("Theme");
            var path = Path.Combine(folder.FullName, "foco.json");
            File.WriteAllText(path, original.ToJsonString());

            MainWindow Open() => new(store, new BrowserLink(folder.FullName, 0));
            var window = Open();
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var menu = ThemeMenu(window);
            Check(Option(menu, "Sistema").IsChecked, "O perfil anterior deve começar em Sistema.");

            var navigation = (StackPanel)window.FindName("NavItems");
            var list = navigation.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Lista de bloqueio");
            list.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, list));
            var input = FindTextBox((StackPanel)window.FindName("ContentPage"))!;
            input.Text = "texto em edição";
            Select(menu, "Escuro");
            Check(Option(menu, "Escuro").IsChecked && !Option(menu, "Sistema").IsChecked, "A marca deve acompanhar a escolha.");
            var dark = Canvas(window);
            if (!SystemParameters.HighContrast)
            {
                Check(dark.R < 100 && dark.G < 100 && dark.B < 120, "O fundo da janela deve ficar escuro imediatamente.");
                Check(((SolidColorBrush)input.Foreground).Color.R > 150, "O texto do campo deve ficar claro no modo escuro.");
                CheckTitleBar(handle, true);
            }
            Check(input.Text == "texto em edição" && ReferenceEquals(input, FindTextBox((StackPanel)window.FindName("ContentPage"))),
                "A troca deve preservar o campo em edição.");
            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Check((string?)persisted["Theme"] == "dark", "A escolha deve ser salva no perfil.");
            persisted.Remove("Theme");
            Check(JsonNode.DeepEquals(original, persisted), "A aparência alterou a sessão, as listas ou as cotas.");
            if (imageFolder != null)
            {
                Snapshot(window, Path.Combine(imageFolder, "lista-escura.png"));
                var home = navigation.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Visão geral");
                home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, home));
                Snapshot(window, Path.Combine(imageFolder, "foco-escuro.png"));
            }

            var reopened = Open();
            Check(Option(ThemeMenu(reopened), "Escuro").IsChecked && Canvas(reopened) == dark, "O tema deve voltar na próxima abertura.");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Select(ThemeMenu(reopened), "Claro");
                Check(Option(ThemeMenu(reopened), "Escuro").IsChecked && Canvas(reopened) == dark,
                    "Uma falha ao salvar deve preservar a opção e o tema anterior.");
            }
            Select(ThemeMenu(reopened), "Claro");
            if (!SystemParameters.HighContrast)
            {
                Check(Canvas(reopened).R > 210, "O tema claro deve atualizar a mesma janela.");
                CheckTitleBar(handle, false);
            }
            if (imageFolder != null)
            {
                var home = ((StackPanel)reopened.FindName("NavItems")).Children.OfType<Button>()
                    .Single(b => AutomationProperties.GetName(b) == "Visão geral");
                home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, home));
                Snapshot(reopened, Path.Combine(imageFolder, "foco-claro.png"));
            }
            Select(ThemeMenu(reopened), "Sistema");
            Check((string?)JsonNode.Parse(File.ReadAllText(path))!["Theme"] == "system", "Sistema deve poder ser escolhido novamente.");
            Console.WriteLine("OK  Tema funciona ao vivo, persiste no perfil e preserva a sessão, as cotas e a edição em andamento.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            app.Shutdown();
            if (folder.Parent?.FullName.TrimEnd('\\') == Path.GetTempPath().TrimEnd('\\') && folder.Name.StartsWith("pulso-tema-teste-"))
                folder.Delete(true);
        }
    }

    private static ContextMenu ThemeMenu(Window window) =>
        (window.FindName("ThemeButton") as Button)?.ContextMenu ?? throw new InvalidOperationException("O botão Tema deve abrir o seletor.");

    private static MenuItem Option(ContextMenu menu, string label) => menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, label));

    private static void Select(ContextMenu menu, string label)
    {
        var item = Option(menu, label);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    private static Color Canvas(Window window) => ((SolidColorBrush)window.Background).Color;

    private static void Snapshot(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var size = new Size(998, 692);
        root.Width = size.Width;
        root.Height = size.Height;
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var scene = new DrawingVisual();
        using (var drawing = scene.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(size));
            drawing.DrawRectangle(new VisualBrush(root) { AutoLayoutContent = false }, null, new Rect(size));
        }
        var pixels = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        pixels.Render(scene);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(pixels));
        using var stream = File.Create(path);
        png.Save(stream);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    private static void CheckTitleBar(nint window, bool dark)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && DwmGetWindowAttribute(window, 20, out int applied, sizeof(int)) == 0)
            Check((applied != 0) == dark, "A barra de título deve acompanhar o tema na mesma janela.");
    }

    private static TextBox? FindTextBox(DependencyObject parent)
    {
        if (parent is TextBox text) return text;
        return LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>().Select(FindTextBox).FirstOrDefault(field => field != null);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
