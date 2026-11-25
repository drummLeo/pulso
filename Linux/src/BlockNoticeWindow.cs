using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pulso;

internal sealed class BlockNoticeWindow : Window
{
    private static BlockNoticeWindow? current;
    private readonly TextBlock heading = Ui.Text("", "Heading");
    private readonly TextBlock explanation = Ui.Text("", "Hint", 6);
    private readonly TextBlock release = Ui.Text("");
    private readonly Button acknowledge;
    private string application = "";

    internal static string? DisplayedApp => current?.application;

    private BlockNoticeWindow()
    {
        Title = "Aplicativo em pausa · Pulso";
        if (Application.Current is DesktopApp app) Icon = app.AppIcon;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        Width = 420;
        MaxHeight = 520;

        var layout = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        acknowledge = Ui.Button("Entendi", Close, "Primary");
        acknowledge.IsDefault = true;
        AutomationProperties.SetAcceleratorKey(acknowledge, "Esc");
        acknowledge.HorizontalAlignment = HorizontalAlignment.Right;
        acknowledge.Margin = new Thickness(0, 18, 0, 0);
        DockPanel.SetDock(acknowledge, Dock.Bottom);
        layout.Children.Add(acknowledge);

        AutomationProperties.SetHeadingLevel(heading, 1);
        AutomationProperties.SetAutomationId(explanation, "BlockReason");
        AutomationProperties.SetAutomationId(release, "BlockRelease");
        var details = new StackPanel();
        details.Children.Add(heading);
        details.Children.Add(explanation);
        release.FontWeight = FontWeight.SemiBold;
        Ui.SetResourceBrush(release, TextBlock.ForegroundProperty, "Accent");
        var time = new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(7),
            Child = release
        };
        Ui.SetResourceBrush(time, Border.BackgroundProperty, "Lilac");
        details.Children.Add(time);
        layout.Children.Add(new ScrollViewer
        {
            Content = details,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
        Content = layout;
    }

    internal static void Present(string appKey, string displayName, string reason, DateTimeOffset? until)
    {
        if (current == null)
        {
            current = new BlockNoticeWindow();
            current.UpdateContent(appKey, displayName, reason, until);
            current.Show();
        }
        else current.UpdateContent(appKey, displayName, reason, until);
    }

    internal static void Dismiss() => current?.Close();

    private void UpdateContent(string appKey, string displayName, string reason, DateTimeOffset? until)
    {
        string when = ReleaseText(until);
        string titleText = displayName + " está em pausa";
        if (appKey == application && heading.Text == titleText && explanation.Text == reason && release.Text == when) return;
        application = appKey;
        heading.Text = titleText;
        explanation.Text = reason;
        release.Text = when;
        AutomationProperties.SetHelpText(acknowledge, $"{heading.Text}. {reason} {when}");
    }

    private static string ReleaseText(DateTimeOffset? until)
    {
        if (until == null) return "Sem horário de liberação previsto.";
        var local = until.Value.LocalDateTime;
        var today = DateTime.Today;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        string day = local.Date == today ? "hoje" : local.Date == today.AddDays(1) ? "amanhã" : "em " + local.ToString("dd/MM/yyyy", pt);
        string time = local.ToString(local.Second == 0 ? "HH:mm" : "HH:mm:ss", pt);
        return $"Você pode voltar {day} às {time}.";
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        acknowledge.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ReferenceEquals(this, current)) current = null;
        base.OnClosed(e);
    }
}
