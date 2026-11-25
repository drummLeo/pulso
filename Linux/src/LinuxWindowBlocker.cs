using System.Runtime.InteropServices;

namespace Pulso;

internal readonly record struct LinuxWindowInfo(
    ulong Handle,
    int Pid,
    bool Visible,
    bool Hidden,
    bool SkipTaskbarOrShell,
    string? ExeName,
    string? CommName,
    string? WmClassInstance,
    string? WmClassClass,
    string? DesktopFileName = null);

internal interface ILinuxWindowBackend : IDisposable
{
    bool Available { get; }
    ulong ActiveWindow();
    IReadOnlyList<LinuxWindowInfo> ListWindows();
    bool Minimize(ulong handle);
}

internal readonly record struct LinuxSessionInfo(bool IsWayland, bool HasX11, string Desktop, string SessionType, bool HasKWinWayland = false)
{
    public bool HasFullWindowControl => (!IsWayland && HasX11) || HasKWinWayland;

    public static LinuxSessionInfo Detect(Func<string, string?>? getEnv = null, bool? hasX11 = null, bool hasKWinWayland = false)
    {
        var env = getEnv ?? Environment.GetEnvironmentVariable;
        string sessionType = env("XDG_SESSION_TYPE") ?? "";
        string waylandDisplay = env("WAYLAND_DISPLAY") ?? "";
        string desktop = env("XDG_CURRENT_DESKTOP") ?? env("DESKTOP_SESSION") ?? "";
        bool isWayland = sessionType.Equals("wayland", StringComparison.OrdinalIgnoreCase) || waylandDisplay.Length > 0;
        bool x11 = hasX11 ?? (!string.IsNullOrWhiteSpace(env("DISPLAY")));
        return new(isWayland, x11, desktop, sessionType, hasKWinWayland);
    }
}

internal static class LinuxWindowMatcher
{
    private static readonly HashSet<string> GenericRuntimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sh", "bash", "zsh", "fish", "dash", "env",
        "python", "python2", "python3", "java", "javaw", "node", "electron", "ruby", "perl",
        "wine", "wine64", "wine-preloader", "wine64-preloader", "flatpak", "snap"
    };

    private static readonly string[] Suffixes = ["-stable", "-beta", "-dev", "-unstable", "-bin", "-desktop", "-runtime"];

    private static readonly HashSet<string> ChromeFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "google-chrome", "google-chrome-stable", "google-chrome-beta", "google-chrome-unstable",
        "chromium", "chromium-browser"
    };

    private static readonly HashSet<string> EdgeFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedge", "microsoft-edge", "microsoft-edge-stable", "microsoft-edge-beta", "microsoft-edge-dev"
    };

    private static readonly HashSet<string> FirefoxFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        "firefox", "firefox-esr", "firefox-bin", "firefox_firefox",
        "librewolf", "waterfox", "zen", "zen-browser"
    };

    public static string? PrimaryName(LinuxWindowInfo window)
    {
        string? exe = Targets.App(window.ExeName);
        if (exe != null && !GenericRuntimes.Contains(exe)) return exe;
        if (NormalizeDescriptor(window.WmClassInstance) is { } wmInst) return wmInst;
        if (NormalizeDescriptor(window.DesktopFileName) is { } desktopName) return desktopName;
        string? comm = Targets.App(window.CommName);
        if (comm != null && !GenericRuntimes.Contains(comm)) return comm;
        if (NormalizeDescriptor(window.WmClassClass) is { } wmClass) return wmClass;
        return exe ?? comm;
    }

    public static string? MatchSelected(LinuxWindowInfo window, IReadOnlySet<string> selected)
    {
        if (selected.Count == 0) return null;

        string?[] rawCandidates = [window.ExeName, window.WmClassInstance, window.DesktopFileName, window.CommName, window.WmClassClass];
        foreach (var raw in rawCandidates)
        {
            foreach (var candidate in ExpandCandidates(raw))
            {
                if (FindInSet(candidate, selected) is { } exact) return exact;

                if (ChromeFamily.Contains(candidate))
                {
                    foreach (var alias in ChromeFamily)
                        if (FindInSet(alias, selected) is { } chromeMatch) return chromeMatch;
                }
                if (EdgeFamily.Contains(candidate))
                {
                    foreach (var alias in EdgeFamily)
                        if (FindInSet(alias, selected) is { } edgeMatch) return edgeMatch;
                }
                if (FirefoxFamily.Contains(candidate))
                {
                    foreach (var alias in FirefoxFamily)
                        if (FindInSet(alias, selected) is { } ffMatch) return ffMatch;
                }
            }
        }

        // /proc/<pid>/comm is truncated to 15 characters by the Linux kernel.
        if (Targets.App(window.CommName) is { Length: 15 } comm)
        {
            foreach (var item in selected)
                if (item.Length > 15 && item.StartsWith(comm, StringComparison.OrdinalIgnoreCase)) return item;
        }
        return null;
    }

    public static string? ForegroundProcess(ILinuxWindowBackend backend, IReadOnlyCollection<string> listedApps, int ownPid)
    {
        if (!backend.Available) return null;
        ulong active = backend.ActiveWindow();
        if (active == 0) return null;

        LinuxWindowInfo? activeWin = null;
        foreach (var win in backend.ListWindows())
        {
            if (win.Handle == active) { activeWin = win; break; }
        }
        if (activeWin is not { } window || !window.Visible || window.Hidden || window.SkipTaskbarOrShell || window.Pid == ownPid)
            return null;

        if (listedApps.Count > 0)
        {
            var listed = listedApps as IReadOnlySet<string> ?? listedApps.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (MatchSelected(window, listed) is { } matched) return matched;
        }

        string?[] rawCandidates = [window.ExeName, window.WmClassInstance, window.DesktopFileName, window.CommName, window.WmClassClass];
        foreach (var raw in rawCandidates)
        {
            foreach (var name in ExpandCandidates(raw))
            {
                if (ChromeFamily.Contains(name)) return "chrome";
                if (EdgeFamily.Contains(name)) return "msedge";
                if (FirefoxFamily.Contains(name)) return "firefox";
            }
        }
        return PrimaryName(window);
    }

    public static string? Apply(ILinuxWindowBackend backend, IReadOnlySet<string> selected, int ownPid)
    {
        if (!backend.Available || selected.Count == 0) return null;
        ulong active = backend.ActiveWindow();
        string? interrupted = null;
        foreach (var win in backend.ListWindows())
        {
            if (!win.Visible || win.Hidden || win.SkipTaskbarOrShell || win.Pid == ownPid) continue;
            if (MatchSelected(win, selected) is not { } matched) continue;
            if (backend.Minimize(win.Handle) && win.Handle == active)
                interrupted = matched;
        }
        return interrupted;
    }

    public static string[] OpenApps(ILinuxWindowBackend backend, int ownPid)
    {
        if (!backend.Available) return [];
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var win in backend.ListWindows())
        {
            if ((!win.Visible && !win.Hidden) || win.SkipTaskbarOrShell || win.Pid == ownPid) continue;
            if (PrimaryName(win) is { } name) names.Add(name);
        }
        return names.ToArray();
    }

    private static string? FindInSet(string candidate, IReadOnlySet<string> selected)
    {
        if (selected is HashSet<string> hashSet && hashSet.TryGetValue(candidate, out var actual)) return actual;
        foreach (var item in selected)
            if (item.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return item;
        return null;
    }

    private static string? NormalizeDescriptor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string trimmed = raw.Trim();
        if (trimmed.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^".desktop".Length];
        if (Targets.App(trimmed) is not { } value) return null;
        if (SnapBase(value) is { } snap) return snap;
        if (value.Contains('.'))
        {
            int dot = value.LastIndexOf('.');
            if (dot + 1 < value.Length && Targets.App(value[(dot + 1)..]) is { } tail) return tail;
        }
        return value;
    }

    private static IEnumerable<string> ExpandCandidates(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        string trimmed = raw.Trim();
        if (trimmed.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^".desktop".Length];
        if (Targets.App(trimmed) is not { } candidate) yield break;

        yield return candidate;

        string stripped = StripSuffix(candidate);
        if (!stripped.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            yield return stripped;

        if (SnapBase(candidate) is { } snap)
            yield return snap;

        if (candidate.Contains('.'))
        {
            int dot = candidate.LastIndexOf('.');
            if (dot + 1 < candidate.Length && Targets.App(candidate[(dot + 1)..]) is { } tail)
                yield return tail;
        }
    }

    private static string? SnapBase(string value)
    {
        int underscore = value.IndexOf('_');
        if (underscore > 0 && underscore * 2 + 1 == value.Length)
        {
            var left = value.AsSpan(0, underscore);
            var right = value.AsSpan(underscore + 1);
            if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
                return left.ToString();
        }
        return null;
    }

    private static string StripSuffix(string name)
    {
        foreach (var suffix in Suffixes)
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        return name;
    }
}

internal sealed class X11WindowBackend : ILinuxWindowBackend
{
    private delegate int XErrorHandler(nint display, nint errorEvent);
    private static readonly XErrorHandler IgnoreErrors = (_, _) => 0;

    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint displayName);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern nint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern int XDefaultScreen(nint display);
    [DllImport("libX11.so.6")] private static extern nint XInternAtom(nint display, string atomName, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XGetWindowProperty(
        nint display, nint window, nint property, nint longOffset, nint longLength, bool delete, nint reqType,
        out nint actualTypeReturn, out int actualFormatReturn, out nuint nitemsReturn, out nuint bytesAfterReturn, out nint propReturn);
    [DllImport("libX11.so.6")] private static extern int XFree(nint data);
    [DllImport("libX11.so.6")] private static extern int XGetWindowAttributes(nint display, nint window, nint attributesBuffer);
    [DllImport("libX11.so.6")] private static extern int XGetClassHint(nint display, nint window, out XClassHint classHints);
    [DllImport("libX11.so.6")] private static extern int XIconifyWindow(nint display, nint window, int screenNumber);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libX11.so.6")] private static extern nint XSetErrorHandler(XErrorHandler handler);

    [StructLayout(LayoutKind.Sequential)]
    private struct XClassHint
    {
        public nint ResName;
        public nint ResClass;
    }

    private readonly object gate = new();
    private nint display;
    private nint root;
    private int screen;
    private readonly nint atomActiveWindow, atomClientList, atomWmPid, atomWmState, atomWmStateHidden;
    private readonly nint atomWmWindowType, atomWmWindowTypeDock, atomWmWindowTypeDesktop;
    private readonly nint atomCardinal = 6, atomWindow = 33, atomAtom = 4;

    public bool Available => display != 0;

    public X11WindowBackend()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))) return;
        try
        {
            XSetErrorHandler(IgnoreErrors);
            display = XOpenDisplay(0);
            if (display == 0) return;
            root = XDefaultRootWindow(display);
            screen = XDefaultScreen(display);
            atomActiveWindow = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
            atomClientList = XInternAtom(display, "_NET_CLIENT_LIST", false);
            atomWmPid = XInternAtom(display, "_NET_WM_PID", false);
            atomWmState = XInternAtom(display, "_NET_WM_STATE", false);
            atomWmStateHidden = XInternAtom(display, "_NET_WM_STATE_HIDDEN", false);
            atomWmWindowType = XInternAtom(display, "_NET_WM_WINDOW_TYPE", false);
            atomWmWindowTypeDock = XInternAtom(display, "_NET_WM_WINDOW_TYPE_DOCK", false);
            atomWmWindowTypeDesktop = XInternAtom(display, "_NET_WM_WINDOW_TYPE_DESKTOP", false);
        }
        catch (DllNotFoundException)
        {
            display = 0;
        }
    }

    public ulong ActiveWindow()
    {
        lock (gate)
        {
            if (display == 0) return 0;
            var values = ReadLongProperty(root, atomActiveWindow, atomWindow, 1);
            return values.Length > 0 ? (ulong)values[0] : 0;
        }
    }

    public IReadOnlyList<LinuxWindowInfo> ListWindows()
    {
        lock (gate)
        {
            if (display == 0) return [];
            var handles = ReadLongProperty(root, atomClientList, atomWindow, 1024);
            if (handles.Length == 0) return [];

            var result = new List<LinuxWindowInfo>(handles.Length);
            var procCache = new Dictionary<int, (string? Exe, string? Comm)>();
            nint attrsBuffer = Marshal.AllocHGlobal(256);
            try
            {
                int mapStateOffset = nint.Size == 8 ? 92 : 60;
                foreach (var handle in handles)
                {
                    if (handle == 0) continue;
                    bool visible = XGetWindowAttributes(display, handle, attrsBuffer) != 0
                        && Marshal.ReadInt32(attrsBuffer, mapStateOffset) == 2; // IsViewable == 2
                    var states = ReadLongProperty(handle, atomWmState, atomAtom, 32);
                    bool hidden = states.Contains(atomWmStateHidden);
                    var types = ReadLongProperty(handle, atomWmWindowType, atomAtom, 16);
                    bool shell = types.Contains(atomWmWindowTypeDock) || types.Contains(atomWmWindowTypeDesktop);
                    var pids = ReadLongProperty(handle, atomWmPid, atomCardinal, 1);
                    int pid = pids.Length > 0 ? (int)pids[0] : 0;

                    string? exe = null, comm = null;
                    if (pid > 0)
                    {
                        if (!procCache.TryGetValue(pid, out var cached))
                            procCache[pid] = cached = ReadProcNames(pid);
                        (exe, comm) = cached;
                    }

                    string? wmInst = null, wmClass = null;
                    if (XGetClassHint(display, handle, out var hint) != 0)
                    {
                        try
                        {
                            wmInst = hint.ResName != 0 ? Marshal.PtrToStringUTF8(hint.ResName) : null;
                            wmClass = hint.ResClass != 0 ? Marshal.PtrToStringUTF8(hint.ResClass) : null;
                        }
                        finally
                        {
                            if (hint.ResName != 0) XFree(hint.ResName);
                            if (hint.ResClass != 0) XFree(hint.ResClass);
                        }
                    }

                    result.Add(new((ulong)handle, pid, visible, hidden, shell, exe, comm, wmInst, wmClass));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(attrsBuffer);
            }
            return result;
        }
    }

    public bool Minimize(ulong handle)
    {
        lock (gate)
        {
            if (display == 0 || handle == 0) return false;
            int status = XIconifyWindow(display, (nint)handle, screen);
            XFlush(display);
            return status != 0;
        }
    }

    private nint[] ReadLongProperty(nint window, nint property, nint reqType, int maxItems)
    {
        if (property == 0) return [];
        int status = XGetWindowProperty(display, window, property, 0, maxItems, false, reqType,
            out _, out int format, out nuint count, out _, out nint prop);
        if (status != 0 || prop == 0) return [];
        try
        {
            if (format != 32 || count == 0) return [];
            int n = (int)Math.Min(count, (nuint)maxItems);
            var values = new nint[n];
            for (int i = 0; i < n; i++)
                values[i] = Marshal.ReadIntPtr(prop, i * nint.Size);
            return values;
        }
        finally
        {
            XFree(prop);
        }
    }

    internal static (string? Exe, string? Comm) ReadProcNames(int pid)
    {
        string? exe = null, comm = null;
        try
        {
            var target = new FileInfo($"/proc/{pid}/exe").ResolveLinkTarget(false)?.FullName;
            if (!string.IsNullOrWhiteSpace(target))
            {
                if (target.EndsWith(" (deleted)", StringComparison.Ordinal))
                    target = target[..^" (deleted)".Length];
                exe = Path.GetFileName(target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
        try
        {
            comm = File.ReadAllText($"/proc/{pid}/comm").TrimEnd('\r', '\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (exe, comm);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (display != 0)
            {
                XCloseDisplay(display);
                display = 0;
            }
        }
    }
}

internal sealed class LinuxWindowBlocker : IDisposable
{
    private readonly ILinuxWindowBackend backend;
    private readonly int ownPid;

    public LinuxSessionInfo Session { get; }

    public LinuxWindowBlocker() : this(CreateDefaultBackend(), Environment.ProcessId) { }

    internal LinuxWindowBlocker(ILinuxWindowBackend backend, int ownPid)
    {
        this.backend = backend;
        this.ownPid = ownPid;
        Session = LinuxSessionInfo.Detect(
            hasX11: backend is X11WindowBackend ? backend.Available : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")),
            hasKWinWayland: backend is KWinWaylandWindowBackend && backend.Available);
    }

    private static ILinuxWindowBackend CreateDefaultBackend()
    {
        var session = LinuxSessionInfo.Detect();
        if (session.IsWayland)
        {
            var kwin = KWinWaylandWindowBackend.TryCreate();
            if (kwin != null) return kwin;
        }
        return new X11WindowBackend();
    }

    public string? ForegroundProcess(IReadOnlyCollection<string> listedApps) =>
        LinuxWindowMatcher.ForegroundProcess(backend, listedApps, ownPid);

    public string? Apply(IReadOnlySet<string> selected) =>
        LinuxWindowMatcher.Apply(backend, selected, ownPid);

    public string[] OpenApps() =>
        LinuxWindowMatcher.OpenApps(backend, ownPid);

    public void Dispose() => backend.Dispose();
}
