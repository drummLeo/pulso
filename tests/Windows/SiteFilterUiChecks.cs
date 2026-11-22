using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Pulso;

internal static class SiteFilterUiChecks
{
    // Called on the verifier's private desktop, with a separate test profile.
    public static async Task Run(string folder)
    {
        var store = new FocusStore(folder);
        var window = new MainWindow(store, new BrowserLink(folder, 0), _ => null);
        window.Show();
        var nav = (StackPanel)window.FindName("NavItems");
        var page = (StackPanel)window.FindName("ContentPage");
        void Navigate(string name) => Press(nav.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == name));
        ComboBox Choice(string name) => Descendants(page).OfType<ComboBox>().FirstOrDefault(c => AutomationProperties.GetName(c) == name)
            ?? throw new InvalidOperationException("Seleção de modo ausente em Sites: " + name);
        Navigate("Lista de bloqueio");
        var porn = Choice("Pornografia");
        var gambling = Choice("Apostas");
        var input = Descendants(page).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Endereço do site");
        input.Text = "rascunho.test";
        Check(Equals(porn.SelectedItem, "Desativado") && Equals(gambling.SelectedItem, "Desativado"),
            "As duas categorias começam desativadas na interface.");
        await Choose(porn, "Sempre");
        await Choose(gambling, "Durante o foco");
        var saved = store.Load();
        Check(saved.PornFilter == SiteFilterMode.Always && saved.GamblingFilter == SiteFilterMode.Focus
            && input.Text == "rascunho.test" && saved.Sites.Length == 0 && !saved.Usage.Enabled,
            "Os seletores salvam cada modo e preservam o domínio em edição.");
        using (var locked = new FileStream(Path.Combine(folder, "foco.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Choose(porn, "Desativado");
            Check(Equals(porn.SelectedItem, "Sempre"), "O seletor volta ao modo salvo se a gravação falhar.");
        }
        Navigate("Visão geral");
        Check(((TextBlock)window.FindName("HomeTargets")).Text.Contains("2 filtros")
            && ((TextBlock)window.FindName("HomeCoverage")).Text.Contains("extensão", StringComparison.OrdinalIgnoreCase),
            "O resumo conta filtros mesmo sem itens manuais e informa que o navegador precisa conectar.");
        Press((Button)window.FindName("SessionButton"));
        Check(store.Load().Active != null && ((TextBlock)window.FindName("NoticeText")).Text.Contains("navegador"),
            "Iniciar foco somente com filtros orienta a conexão do navegador.");
        Navigate("Lista de bloqueio");
        Check(Equals(Choice("Pornografia").SelectedItem, "Sempre") && Equals(Choice("Apostas").SelectedItem, "Durante o foco"),
            "Voltar à lista restaura as escolhas de cada categoria.");
        await Choose(Choice("Pornografia"), "Desativado");
        Check(store.Load() is { PornFilter: SiteFilterMode.Off, GamblingFilter: SiteFilterMode.Focus, Active: not null },
            "Desativar uma categoria durante o foco mantém a outra e a sessão.");
    }

    private static async Task Choose(ComboBox field, string option)
    {
        var window = Window.GetWindow(field);
        nint handle = new WindowInteropHelper(window).Handle;
        var name = AutomationProperties.GetName(field);
        field.BringIntoView();
        window.UpdateLayout();
        await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(handle);
            var combo = root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
                new PropertyCondition(AutomationElement.NameProperty, name)));
            var menu = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            menu.Expand();
            var items = (ItemContainerPattern)combo.GetCurrentPattern(ItemContainerPattern.Pattern);
            var item = items.FindItemByProperty(null, AutomationElement.NameProperty, option);
            ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            if (menu.Current.ExpandCollapseState == ExpandCollapseState.Expanded) menu.Collapse();
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var node in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return node;
            foreach (var child in Descendants(node)) yield return child;
        }
    }

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("OK  " + message);
    }
}
