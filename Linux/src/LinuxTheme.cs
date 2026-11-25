using System.Diagnostics;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Pulso;

internal static class LinuxTheme
{
    public static bool SystemPrefersDark(Application? app = null)
    {
        if (TryPortalPrefersDark() is { } portal) return portal;
        if (TryGsettingsPrefersDark() is { } gtk) return gtk;
        var current = app ?? Application.Current;
        return current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
    }

    public static void Apply(Application app, string preference)
    {
        bool dark = preference == "dark" || (preference != "light" && SystemPrefersDark(app));
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static bool? TryPortalPrefersDark()
    {
        string? output = RunCommand("dbus-send",
            "--session", "--dest=org.freedesktop.portal.Desktop", "--type=method_call", "--print-reply",
            "/org/freedesktop/portal/desktop", "org.freedesktop.portal.Settings.Read",
            "string:org.freedesktop.appearance", "string:color-scheme");
        if (output == null) return null;
        if (output.Contains("uint32 1", StringComparison.Ordinal)) return true;
        if (output.Contains("uint32 2", StringComparison.Ordinal) || output.Contains("uint32 0", StringComparison.Ordinal)) return false;
        return null;
    }

    private static bool? TryGsettingsPrefersDark()
    {
        string? scheme = RunCommand("gsettings", "get", "org.gnome.desktop.interface", "color-scheme");
        if (scheme != null)
        {
            if (scheme.Contains("prefer-dark", StringComparison.OrdinalIgnoreCase)) return true;
            if (scheme.Contains("prefer-light", StringComparison.OrdinalIgnoreCase) || scheme.Contains("default", StringComparison.OrdinalIgnoreCase)) return false;
        }
        string? theme = RunCommand("gsettings", "get", "org.gnome.desktop.interface", "gtk-theme");
        if (theme != null && theme.Contains("-dark", StringComparison.OrdinalIgnoreCase)) return true;
        return null;
    }

    private static string? RunCommand(string file, params string[] args)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo(file)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            foreach (var arg in args) proc.StartInfo.ArgumentList.Add(arg);
            if (!proc.Start()) return null;
            if (!proc.WaitForExit(500) || proc.ExitCode != 0) return null;
            return proc.StandardOutput.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
