using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

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
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "Aplicativo em pausa · Pulso";
        Icon = Application.Current.MainWindow?.Icon;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = Math.Min(420, SystemParameters.WorkArea.Width);
        MaxHeight = SystemParameters.WorkArea.Height;

        var layout = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        acknowledge = Ui.Button("Entendi", Close, "Primary");
        acknowledge.IsDefault = true;
        AutomationProperties.SetAcceleratorKey(acknowledge, "Esc");
        acknowledge.HorizontalAlignment = HorizontalAlignment.Right;
        acknowledge.Margin = new Thickness(0, 18, 0, 0);
        DockPanel.SetDock(acknowledge, Dock.Bottom);
        layout.Children.Add(acknowledge);

        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        AutomationProperties.SetAutomationId(explanation, "BlockReason");
        AutomationProperties.SetAutomationId(release, "BlockRelease");
        var details = new StackPanel();
        details.Children.Add(heading);
        details.Children.Add(explanation);
        release.FontWeight = FontWeights.SemiBold;
        release.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        var time = new Border
        {
            Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(7), Child = release
        };
        time.SetResourceReference(Border.BackgroundProperty, "Lilac");
        details.Children.Add(time);
        layout.Children.Add(new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
    }

    internal static void Present(string app, string reason, DateTimeOffset? until)
    {
        if (current == null)
        {
            current = new BlockNoticeWindow();
            current.UpdateContent(app, reason, until);
            current.Show();
        }
        else current.UpdateContent(app, reason, until);
    }

    internal static void Dismiss() => current?.Close();

    private void UpdateContent(string app, string reason, DateTimeOffset? until)
    {
        string when = ReleaseText(until);
        if (app == application && reason == explanation.Text && when == release.Text) return;
        application = app;
        heading.Text = app + " está em pausa";
        explanation.Text = reason;
        release.Text = when;
        AutomationProperties.SetHelpText(acknowledge, $"{heading.Text}. {reason} {when}");
        if (IsVisible) Announce();
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

    private void Announce()
    {
        if (AutomationPeer.ListenerExists(AutomationEvents.Notification))
            UIElementAutomationPeer.CreatePeerForElement(this)?.RaiseNotificationEvent(AutomationNotificationKind.Other,
                AutomationNotificationProcessing.ImportantMostRecent, AutomationProperties.GetHelpText(acknowledge), "Pulso.BlockNotice");
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        acknowledge.Focus();
        Announce();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
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
