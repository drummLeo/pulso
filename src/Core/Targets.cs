using System.Globalization;
using System.Text.RegularExpressions;

namespace Pulso;

public static class Targets
{
    public static string? Domain(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 2048 || value.Any(char.IsWhiteSpace) || value.Contains('\\')) return null;
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")
            || url.UserInfo.Length > 0 || url.HostNameType != UriHostNameType.Dns) return null;
        string host;
        try { host = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(url.Host.TrimEnd('.')).ToLowerInvariant(); }
        catch (ArgumentException) { return null; }
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return host.Length <= 253 && host.Contains('.') && host.Split('.').All(s =>
            s.Length is > 0 and <= 63 && Regex.IsMatch(s, "^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")) ? host : null;
    }

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pulso", "explorer", "dwm", "winlogon", "csrss", "taskmgr", "sihost", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "ApplicationFrameHost", "SystemSettings", "TextInputHost",
        "LockApp", "CredentialUIBroker", "consent", "WindowsTerminal", "conhost", "OpenConsole",
        "pulso-linux", "gnome-shell", "plasmashell", "kwin_x11", "kwin_wayland", "xorg", "xwayland", "mutter",
        "xfwm4", "xfce4-panel", "xfdesktop", "cinnamon", "mate-panel", "marco", "sway", "hyprland", "waybar",
        "i3", "openbox", "fluxbox", "systemd", "dbus-daemon", "ksmserver", "kded5", "kded6", "krunner",
        "xdg-desktop-portal", "polkit-gnome-authentication-agent-1", "polkit-kde-authentication-agent-1",
        "lxqt-panel", "budgie-wm", "budgie-panel", "pantheon-shell", "gala", "wingpanel", "cosmic-comp", "cosmic-panel"
    };
    public static string? App(string? name)
    {
        var value = name?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        return value.Length is > 0 and <= 200 && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !value.Any(c => char.IsControl(c) || c is '/' or '\\')
            && value is not ("." or "..") && !Reserved.Contains(value) ? value : null;
    }
}
