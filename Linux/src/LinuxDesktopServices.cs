using System.Diagnostics;

namespace Pulso;

internal static class LinuxDesktopServices
{
    public static void SendNotification(string title, string body)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var start = new ProcessStartInfo("notify-send")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add("-a");
                start.ArgumentList.Add("Pulso");
                start.ArgumentList.Add(title);
                start.ArgumentList.Add(body);
                using var proc = Process.Start(start);
                proc?.WaitForExit(1500);
            }
            catch (Exception) { }
        });
    }

    public static bool HasStatusNotifierWatcher()
    {
        try
        {
            var start = new ProcessStartInfo("dbus-send")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--session");
            start.ArgumentList.Add("--dest=org.freedesktop.DBus");
            start.ArgumentList.Add("--type=method_call");
            start.ArgumentList.Add("--print-reply=literal");
            start.ArgumentList.Add("/org/freedesktop/DBus");
            start.ArgumentList.Add("org.freedesktop.DBus.NameHasOwner");
            start.ArgumentList.Add("string:org.kde.StatusNotifierWatcher");
            using var proc = Process.Start(start);
            if (proc == null || !proc.WaitForExit(500) || proc.ExitCode != 0) return false;
            return proc.StandardOutput.ReadToEnd().Contains("boolean true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool EnsureFlatpakExtensionAccess(string? browser, string extensionFolder)
    {
        if (FindInPath("flatpak") is not { } flatpak) return false;
        string[] candidates = browser switch
        {
            "Chrome" => ["com.google.Chrome", "org.chromium.Chromium"],
            "Edge" => ["com.microsoft.Edge"],
            "Firefox" => ["org.mozilla.firefox"],
            _ => ["com.google.Chrome", "org.chromium.Chromium", "com.microsoft.Edge"]
        };
        bool configured = false;
        foreach (var id in candidates)
        {
            if (!IsFlatpakInstalled(flatpak, id)) continue;
            try
            {
                var start = new ProcessStartInfo(flatpak)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add("override");
                start.ArgumentList.Add("--user");
                start.ArgumentList.Add($"--filesystem={Path.GetFullPath(extensionFolder)}:ro");
                start.ArgumentList.Add(id);
                using var proc = Process.Start(start);
                if (proc != null && proc.WaitForExit(1500) && proc.ExitCode == 0)
                    configured = true;
            }
            catch (Exception) { }
        }
        return configured;
    }

    public static bool OpenBrowserExtensions(string browser)
    {
        string url = browser switch
        {
            "Edge" => "edge://extensions/",
            "Firefox" => "about:debugging#/runtime/this-firefox",
            _ => "chrome://extensions/"
        };
        string[] commands = browser switch
        {
            "Edge" => ["microsoft-edge", "microsoft-edge-stable", "microsoft-edge-beta", "microsoft-edge-dev"],
            "Firefox" => ["firefox", "firefox-esr", "librewolf", "waterfox"],
            _ => ["google-chrome", "google-chrome-stable", "google-chrome-beta", "google-chrome-unstable", "chromium", "chromium-browser"]
        };

        string[] flatpakIds = browser switch
        {
            "Edge" => ["com.microsoft.Edge"],
            "Firefox" => ["org.mozilla.firefox", "io.gitlab.librewolf-community"],
            _ => ["com.google.Chrome", "org.chromium.Chromium"]
        };
        if (FindInPath("flatpak") is { } flatpak)
        {
            foreach (var id in flatpakIds)
            {
                if (IsFlatpakInstalled(flatpak, id))
                    return StartDetached(flatpak, "run", id, url);
            }
        }

        foreach (var cmd in commands)
        {
            if (FindInPath(cmd) is { } path)
                return StartDetached(path, url);
        }

        return false;
    }

    private static string? FindInPath(string command)
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin:/snap/bin";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir, command);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsFlatpakInstalled(string flatpakPath, string appId)
    {
        try
        {
            var start = new ProcessStartInfo(flatpakPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("info");
            start.ArgumentList.Add(appId);
            using var proc = Process.Start(start);
            return proc != null && proc.WaitForExit(800) && proc.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool StartDetached(string file, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var proc = Process.Start(start);
            return proc != null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
