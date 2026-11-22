using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pulso;

public static class WindowBlocker
{
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);

    private static string? ProcessName(int pid)
    {
        if (pid == 0 || pid == Environment.ProcessId) return null;
        try { using var process = Process.GetProcessById(pid); return process.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return null; }
    }

    public static string? ForegroundProcess()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window)) return null;
        GetWindowThreadProcessId(window, out int pid);
        return ProcessName(pid);
    }

    public static string? Apply(IReadOnlySet<string> selected)
    {
        if (selected.Count == 0) return null;
        const int minimize = 6, minimizeWithoutActivation = 7;
        var foreground = GetForegroundWindow();
        string? interrupted = null;
        var names = new Dictionary<int, string?>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            GetWindowThreadProcessId(window, out var pid);
            if (!names.TryGetValue(pid, out var name))
            {
                names[pid] = name = ProcessName(pid);
            }
            if (name != null && selected.Contains(name))
            {
                bool inFront = window == foreground && window == GetForegroundWindow();
                if (ShowWindowAsync(window, inFront ? minimize : minimizeWithoutActivation) && inFront)
                    interrupted = name;
            }
            return true;
        }, IntPtr.Zero);
        return interrupted;
    }

    public static string[] OpenApps()
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var process in Process.GetProcesses())
        using (process)
        {
            try
            {
                if (process.Id != Environment.ProcessId && process.MainWindowHandle != IntPtr.Zero
                    && Targets.App(process.ProcessName) is { } name) names.Add(name);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException) { }
        }
        return names.ToArray();
    }
}
