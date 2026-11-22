using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulso;

public static class WindowBlocker
{
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int GetClassNameW(IntPtr window, StringBuilder name, int size);
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

    private static string? WindowProcess(IntPtr window, Func<int, string?> processName)
    {
        GetWindowThreadProcessId(window, out int pid);
        return ResolveWindowOwner(window, pid, processName, ChildWindows);
    }

    // ApplicationFrameHost can own several top-level windows, each belonging to a different app.
    internal static string? ResolveWindowOwner(IntPtr window, int pid, Func<int, string?> processName,
        Func<IntPtr, IEnumerable<(int Pid, string ClassName)>> childWindows)
    {
        string? owner = processName(pid);
        if (!string.Equals(owner, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) return owner;
        int candidate = 0;
        foreach (var child in childWindows(window))
        {
            if (child.Pid == pid) continue;
            if (child.ClassName.Length == 0) return null;
            if (child.ClassName != "Windows.UI.Core.CoreWindow") continue;
            if (child.Pid == 0 || (candidate != 0 && child.Pid != candidate)) return null;
            candidate = child.Pid;
        }
        return candidate == 0 ? null : Targets.App(processName(candidate));
    }

    private static IEnumerable<(int Pid, string ClassName)> ChildWindows(IntPtr parent)
    {
        var children = new List<(int, string)>();
        var name = new StringBuilder(256);
        EnumChildWindows(parent, (child, _) =>
        {
            GetWindowThreadProcessId(child, out int pid);
            string className = GetClassNameW(child, name, name.Capacity) == 0 ? "" : name.ToString();
            children.Add((pid, className));
            return true;
        }, IntPtr.Zero);
        return children;
    }

    public static string? ForegroundProcess()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window)) return null;
        return WindowProcess(window, ProcessName);
    }

    public static string? Apply(IReadOnlySet<string> selected)
    {
        if (selected.Count == 0) return null;
        const int minimize = 6, minimizeWithoutActivation = 7;
        var foreground = GetForegroundWindow();
        string? interrupted = null;
        var names = new Dictionary<int, string?>();
        string? CachedName(int pid)
        {
            if (!names.TryGetValue(pid, out var name)) names[pid] = name = ProcessName(pid);
            return name;
        }
        Func<int, string?> lookup = CachedName;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            var name = WindowProcess(window, lookup);
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
