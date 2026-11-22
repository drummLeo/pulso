using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Pulso;

internal static class Ui
{
    public static Style Style(string key) => (Style)Application.Current.FindResource(key);
    public static TextBox TextInput(string value, int length) => new()
    {
        Text = value, MaxLength = length, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center,
        MinHeight = 35, Padding = new Thickness(8, 6, 8, 6)
    };
    public static TextBlock Text(string value, string? style = null, double top = 0)
    {
        var text = new TextBlock { Text = value, Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap };
        if (style != null) text.Style = Style(style);
        return text;
    }
    public static Button Button(string label, Action action, string? style = null, string? accessibleName = null)
    {
        var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (style != null) b.Style = Style(style);
        b.Click += (_, _) => action();
        if (accessibleName != null) AutomationProperties.SetName(b, accessibleName);
        return b;
    }
    public static Border Card(Panel parent, UIElement content)
    {
        var card = new Border { Style = Style("Card"), Margin = new Thickness(0, 0, 0, 12), Child = content };
        parent.Children.Add(card);
        return card;
    }
    public static StackPanel Section(Panel parent, string heading, string hint)
    {
        var body = new StackPanel();
        Card(parent, body);
        body.Children.Add(Text(heading, "Heading"));
        if (hint.Length > 0) body.Children.Add(Text(hint, "Hint", 4));
        return body;
    }
    public static Label Label(string title, Control field)
    {
        AutomationProperties.SetName(field, title);
        return new Label { Content = title, Target = field, Padding = new Thickness(0), Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.SemiBold };
    }
    public static void Buttons(Panel parent, params Button[] buttons)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var button in buttons) { button.Margin = new Thickness(0, 2, 7, 2); row.Children.Add(button); }
        parent.Children.Add(row);
    }
    public static DockPanel Row(Panel parent, FrameworkElement info, params Button[] buttons)
    {
        var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = buttons.Length - 1; i >= 0; i--)
        {
            buttons[i].Margin = new Thickness(5, 0, 0, 0);
            DockPanel.SetDock(buttons[i], Dock.Right);
            line.Children.Add(buttons[i]);
        }
        info.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(info);
        parent.Children.Add(line);
        return line;
    }
}

internal sealed class FormWindow : Window
{
    public StackPanel Fields { get; } = new();
    public Button Accept { get; }
    public Button CancelButton { get; }
    private readonly TextBlock error;
    public FormWindow(Window owner, string title, string action)
    {
        Owner = owner;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = title;
        Icon = owner.Icon;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = Math.Min(485, owner.ActualWidth - 12);
        MaxHeight = Math.Max(260, SystemParameters.WorkArea.Height - 30);
        var layout = new DockPanel { Margin = new Thickness(23, 20, 23, 16) };
        var heading = Ui.Text(title, "Heading");
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        error = Ui.Text("", null, 8);
        error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorInk");
        error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        footer.Children.Add(error);
        Accept = new Button { Content = action, Style = Ui.Style("Primary"), IsDefault = true };
        CancelButton = new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(CancelButton);
        actions.Children.Add(Accept);
        footer.Children.Add(actions);
        layout.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
    }
    public TextBox Field(string title, string value, int length = 80)
    {
        var edit = Ui.TextInput(value, length);
        Fields.Children.Add(Ui.Label(title, edit));
        Fields.Children.Add(edit);
        return edit;
    }
    public void Invalid(string message, Control? field = null)
    {
        error.Text = message; error.Visibility = Visibility.Visible; field?.Focus();
    }
    public static bool Confirm(Window owner, string title, string detail, string action)
    {
        var dialog = new FormWindow(owner, title, action);
        dialog.Fields.Children.Add(Ui.Text(detail, "Hint", 11));
        dialog.Accept.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => dialog.CancelButton.Focus();
        return dialog.ShowDialog() == true;
    }
}
