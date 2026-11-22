using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Pulso;

public partial class DesktopApp
{
    private ResourceDictionary? palette;
    private string? paletteName;
    private string themePreference = "system";

    internal void SetTheme(string preference)
    {
        themePreference = preference;
        RefreshTheme();
    }

    private void WindowsPreferencesChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.InvokeAsync(RefreshTheme, DispatcherPriority.Background);

    private void RefreshTheme()
    {
        bool highContrast = SystemParameters.HighContrast;
        bool dark = themePreference == "dark" || themePreference == "system" && WindowsPrefersDark();
        string name = highContrast ? "HighContrast" : dark ? "Dark" : "Light";
        if (name == paletteName) return;
        var next = new ResourceDictionary { Source = new Uri($"/Pulso;component/Themes/{name}.xaml", UriKind.Relative) };
        var dictionaries = Resources.MergedDictionaries;
        if (palette == null) dictionaries.Add(next);
        else dictionaries[dictionaries.IndexOf(palette)] = next;
        palette = next;
        paletteName = name;

        // WPF applies the corresponding native control templates and title bars to all open windows.
#pragma warning disable WPF0001
        ThemeMode = highContrast ? ThemeMode.System : dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
    }

    private static bool WindowsPrefersDark()
    {
        const string personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        var setting = Registry.GetValue(personalize, "AppsUseLightTheme", null)
            ?? Registry.GetValue(personalize, "SystemUsesLightTheme", null);
        return setting is 0;
    }
}

internal sealed class TrayMenuColors : Forms.ProfessionalColorTable
{
    private static Drawing.Color ColorFor(string key)
    {
        var color = ((SolidColorBrush)Application.Current.FindResource(key)).Color;
        return Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public override Drawing.Color ToolStripDropDownBackground => ColorFor("Surface");
    public override Drawing.Color MenuBorder => ColorFor("Line");
    public override Drawing.Color MenuItemBorder => ColorFor("Accent");
    public override Drawing.Color MenuItemSelected => ColorFor("Lilac");

    public static void ApplyTo(Forms.ContextMenuStrip menu)
    {
        var renderer = new Forms.ToolStripProfessionalRenderer(new TrayMenuColors()) { RoundedEdges = false };
        menu.ShowImageMargin = false;
        menu.Opening += (_, _) =>
        {
            if (SystemParameters.HighContrast)
            {
                menu.RenderMode = Forms.ToolStripRenderMode.System;
                menu.ForeColor = Drawing.SystemColors.MenuText;
            }
            else
            {
                menu.Renderer = renderer;
                menu.ForeColor = ColorFor("Ink");
            }
        };
    }
}
