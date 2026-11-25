using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pulso;

internal static class Ui
{
    public static TextBox TextInput(string value, int length) => new()
    {
        Text = value,
        MaxLength = length,
        FontSize = 15,
        VerticalContentAlignment = VerticalAlignment.Center,
        MinHeight = 35,
        Padding = new Thickness(8, 6, 8, 6)
    };

    public static TextBlock Text(string value, string? styleClass = null, double top = 0)
    {
        var text = new TextBlock { Text = value, Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap };
        if (styleClass != null) text.Classes.Add(styleClass);
        return text;
    }

    public static Button Button(string label, Action action, string? styleClass = null, string? accessibleName = null)
    {
        var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (styleClass != null) b.Classes.Add(styleClass);
        b.Click += (_, _) => action();
        if (accessibleName != null) AutomationProperties.SetName(b, accessibleName);
        return b;
    }

    public static Border Card(Panel parent, Control content)
    {
        var card = new Border { Margin = new Thickness(0, 0, 0, 12), Child = content };
        card.Classes.Add("Card");
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
        return new Label
        {
            Content = title,
            Target = field,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 12, 0, 4),
            FontWeight = FontWeight.SemiBold
        };
    }

    public static void Buttons(Panel parent, params Button[] buttons)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var button in buttons)
        {
            button.Margin = new Thickness(0, 2, 7, 2);
            row.Children.Add(button);
        }
        parent.Children.Add(row);
    }

    public static DockPanel Row(Panel parent, Control info, params Button[] buttons)
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

    public static void SetResourceBrush(Control control, AvaloniaProperty property, string resourceKey)
    {
        control[!property] = control.GetResourceObservable(resourceKey).ToBinding();
    }
}

internal class FormWindow : Window
{
    public StackPanel Fields { get; } = new();
    protected StackPanel Header { get; } = new();
    protected ScrollViewer BodyScroll { get; }
    public Button Accept { get; }
    public Button CancelButton { get; }
    private readonly TextBlock error;

    public FormWindow(Window owner, string title, string action)
    {
        Title = title;
        Icon = owner.Icon;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        double ownerWidth = owner.Bounds.Width > 0 ? owner.Bounds.Width : 700;
        Width = Math.Min(485, Math.Max(340, ownerWidth - 12));
        double screenHeight = owner.Screens.ScreenFromWindow(owner)?.WorkingArea.Height / (owner.RenderScaling > 0 ? owner.RenderScaling : 1) ?? 720;
        MaxHeight = Math.Max(260, screenHeight - 30);

        var layout = new Grid { Margin = new Thickness(23, 20, 23, 16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Header.Children.Add(Ui.Text(title, "Heading"));
        layout.Children.Add(Header);

        BodyScroll = new ScrollViewer
        {
            Content = Fields,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetRow(BodyScroll, 1);
        layout.Children.Add(BodyScroll);

        var footer = new StackPanel();
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        error = Ui.Text("", null, 8);
        Ui.SetResourceBrush(error, TextBlock.ForegroundProperty, "ErrorInk");
        error.IsVisible = false;
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        footer.Children.Add(error);

        Accept = new Button { Content = action, IsDefault = true };
        Accept.Classes.Add("Primary");
        CancelButton = new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        CancelButton.Click += (_, _) => Close(false);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        actions.Children.Add(CancelButton);
        actions.Children.Add(Accept);
        footer.Children.Add(actions);

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
        error.Text = message;
        error.IsVisible = true;
        field?.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
        }
    }

    public static async Task<bool> ConfirmAsync(Window owner, string title, string detail, string action)
    {
        var dialog = new FormWindow(owner, title, action);
        dialog.Fields.Children.Add(Ui.Text(detail, "Hint", 11));
        dialog.Accept.Click += (_, _) => dialog.Close(true);
        dialog.Opened += (_, _) => dialog.CancelButton.Focus();
        return await dialog.ShowDialog<bool>(owner);
    }
}
