using System.Text.Json;
using Tmds.DBus.Protocol;

namespace Pulso;

internal sealed record KWinSnapshot(ulong ActiveHandle, IReadOnlyList<LinuxWindowInfo> Windows, Dictionary<ulong, string> HandleToId);

internal static class KWinSnapshotParser
{
    public static KWinSnapshot Parse(string json, Func<int, (string? Exe, string? Comm)>? procReader = null, Func<string, ulong>? handleForId = null)
    {
        var resolveProc = procReader ?? (_ => (null, null));
        ulong nextLocalHandle = 1;
        var localHandles = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var resolveHandle = handleForId ?? (id =>
        {
            if (!localHandles.TryGetValue(id, out var h)) localHandles[id] = h = nextLocalHandle++;
            return h;
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? activeId = root.TryGetProperty("active", out var actEl) && actEl.ValueKind == JsonValueKind.String
            ? actEl.GetString()
            : null;

        ulong activeHandle = 0;
        var windows = new List<LinuxWindowInfo>();
        var handleToId = new Dictionary<ulong, string>();
        var procCache = new Dictionary<int, (string? Exe, string? Comm)>();

        if (root.TryGetProperty("windows", out var arrEl) && arrEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arrEl.EnumerateArray())
            {
                string? id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;

                ulong handle = resolveHandle(id);
                handleToId[handle] = id;
                if (activeId == id) activeHandle = handle;

                int pid = item.TryGetProperty("pid", out var pidEl) && pidEl.TryGetInt32(out int p) ? p : 0;
                bool minimized = item.TryGetProperty("minimized", out var minEl) && minEl.ValueKind == JsonValueKind.True;
                bool skip = item.TryGetProperty("skip", out var skipEl) && skipEl.ValueKind == JsonValueKind.True;
                string? resName = ReadNonEmpty(item, "name");
                string? resClass = ReadNonEmpty(item, "cls");
                string? desktop = ReadNonEmpty(item, "desktop");

                string? exe = null, comm = null;
                if (pid > 0)
                {
                    if (!procCache.TryGetValue(pid, out var proc))
                        procCache[pid] = proc = resolveProc(pid);
                    (exe, comm) = proc;
                }

                windows.Add(new(handle, pid, Visible: !minimized, Hidden: minimized, SkipTaskbarOrShell: skip,
                    ExeName: exe, CommName: comm, WmClassInstance: resName, WmClassClass: resClass, DesktopFileName: desktop));
            }
        }

        return new(activeHandle, windows, handleToId);
    }

    private static string? ReadNonEmpty(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String) return null;
        string? s = prop.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}

internal sealed class KWinWaylandWindowBackend : ILinuxWindowBackend
{
    private const string BridgePath = "/PulsoKWin";
    private const string BridgeInterface = "br.local.pulso.KWinBridge";
    private static readonly ReadOnlyMemory<byte>[] IntrospectXml =
    [
        """
        <interface name="br.local.pulso.KWinBridge">
          <method name="Report">
            <arg type="s" name="json" direction="in"/>
          </method>
        </interface>
        """u8.ToArray()
    ];

    private sealed class BridgeHandler(Action<string> onReport) : IMethodHandler
    {
        public string Path => BridgePath;
        public bool RunMethodHandlerSynchronously(Message message) => true;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml(IntrospectXml);
                return ValueTask.CompletedTask;
            }
            if (context.Request.MemberAsString == "Report" && context.Request.InterfaceAsString == BridgeInterface)
            {
                string json = context.Request.GetBodyReader().ReadString();
                onReport(json);
                if (!context.NoReplyExpected)
                {
                    using var reply = context.CreateReplyWriter("");
                    context.Reply(reply.CreateMessage());
                }
                return ValueTask.CompletedTask;
            }
            context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", "Unknown method");
            return ValueTask.CompletedTask;
        }
    }

    private readonly Connection conn;
    private readonly string observerPlugin;
    private readonly string observerPath;
    private readonly string minimizePlugin;
    private readonly string minimizePath;
    private readonly object snapshotGate = new();
    private readonly object scriptGate = new();
    private readonly Dictionary<string, ulong> idHandles = new(StringComparer.Ordinal);
    private readonly ManualResetEventSlim initialSnapshot = new(false);
    private ulong nextHandle = 1;
    private ulong activeHandle;
    private IReadOnlyList<LinuxWindowInfo> windows = [];
    private Dictionary<ulong, string> handleToId = new();
    private bool disposed;

    public bool Available { get; private set; }

    private KWinWaylandWindowBackend(Connection conn, int pid)
    {
        this.conn = conn;
        observerPlugin = $"pulso_kwin_obs_{pid}";
        minimizePlugin = $"pulso_kwin_min_{pid}";
        string runtimeDir = LinuxPaths.RuntimeDirectory();
        observerPath = Path.Combine(runtimeDir, $"pulso-kwin-obs-{pid}.js");
        minimizePath = Path.Combine(runtimeDir, $"pulso-kwin-min-{pid}.js");
    }

    public static KWinWaylandWindowBackend? TryCreate()
    {
        try
        {
            string? addr = Address.Session;
            if (string.IsNullOrEmpty(addr)) return null;
            var conn = new Connection(addr);
            conn.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(800)).GetAwaiter().GetResult();

            var services = conn.ListServicesAsync().WaitAsync(TimeSpan.FromMilliseconds(800)).GetAwaiter().GetResult();
            if (!services.Contains("org.kde.KWin", StringComparer.Ordinal))
            {
                conn.Dispose();
                return null;
            }

            var backend = new KWinWaylandWindowBackend(conn, Environment.ProcessId);
            if (!backend.Initialize())
            {
                backend.Dispose();
                return null;
            }
            return backend;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool Initialize()
    {
        string? busName = conn.UniqueName;
        if (string.IsNullOrEmpty(busName)) return false;

        conn.AddMethodHandler(new BridgeHandler(OnReport));

        string script = BuildObserverScript(busName);
        WritePrivateFile(observerPath, script);

        UnloadScript(observerPlugin);
        if (!LoadAndStartScript(observerPath, observerPlugin)) return false;

        Available = true;
        initialSnapshot.Wait(TimeSpan.FromMilliseconds(600));
        return true;
    }

    private void OnReport(string json)
    {
        try
        {
            var parsed = KWinSnapshotParser.Parse(json, X11WindowBackend.ReadProcNames, GetOrCreateHandle);
            lock (snapshotGate)
            {
                activeHandle = parsed.ActiveHandle;
                windows = parsed.Windows;
                handleToId = parsed.HandleToId;
            }
            initialSnapshot.Set();
        }
        catch (JsonException) { }
    }

    private ulong GetOrCreateHandle(string internalId)
    {
        lock (snapshotGate)
        {
            if (!idHandles.TryGetValue(internalId, out var h)) idHandles[internalId] = h = nextHandle++;
            return h;
        }
    }

    public ulong ActiveWindow()
    {
        lock (snapshotGate) return activeHandle;
    }

    public IReadOnlyList<LinuxWindowInfo> ListWindows()
    {
        lock (snapshotGate) return windows;
    }

    public bool Minimize(ulong handle)
    {
        if (!Available || disposed || handle == 0) return false;
        string? internalId;
        lock (snapshotGate)
        {
            if (!handleToId.TryGetValue(handle, out internalId)) return false;
        }

        lock (scriptGate)
        {
            if (disposed) return false;
            try
            {
                string idJson = JsonSerializer.Serialize(internalId);
                string script =
                    $"for (const w of workspace.windowList()) {{\n" +
                    $"  if (String(w.internalId) === {idJson}) {{\n" +
                    $"    w.minimized = true;\n" +
                    $"    break;\n" +
                    $"  }}\n" +
                    $"}}\n";
                WritePrivateFile(minimizePath, script);
                UnloadScript(minimizePlugin);
                bool started = LoadAndStartScript(minimizePath, minimizePlugin);
                UnloadScript(minimizePlugin);
                if (!started) return false;

                lock (snapshotGate)
                {
                    var updated = new List<LinuxWindowInfo>(windows.Count);
                    foreach (var w in windows)
                        updated.Add(w.Handle == handle ? w with { Visible = false, Hidden = true } : w);
                    windows = updated;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private bool LoadAndStartScript(string filePath, string pluginName)
    {
        try
        {
            int scriptId;
            using (var w = conn.GetMessageWriter())
            {
                w.WriteMethodCallHeader("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "loadScript", "ss", MessageFlags.None);
                w.WriteString(filePath);
                w.WriteString(pluginName);
                scriptId = conn.CallMethodAsync(w.CreateMessage(), (msg, _) => msg.GetBodyReader().ReadInt32(), null!)
                    .WaitAsync(TimeSpan.FromMilliseconds(1000)).GetAwaiter().GetResult();
            }
            if (scriptId < 0) return false;

            using (var w = conn.GetMessageWriter())
            {
                w.WriteMethodCallHeader("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "start", "", MessageFlags.None);
                conn.CallMethodAsync(w.CreateMessage()).WaitAsync(TimeSpan.FromMilliseconds(1000)).GetAwaiter().GetResult();
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void UnloadScript(string pluginName)
    {
        try
        {
            using var w = conn.GetMessageWriter();
            w.WriteMethodCallHeader("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "unloadScript", "s", MessageFlags.None);
            w.WriteString(pluginName);
            conn.CallMethodAsync(w.CreateMessage()).WaitAsync(TimeSpan.FromMilliseconds(600)).GetAwaiter().GetResult();
        }
        catch (Exception) { }
    }

    private static string BuildObserverScript(string busName)
    {
        string busJson = JsonSerializer.Serialize(busName);
        return $$"""
            const BUS = {{busJson}};
            const observed = new Set();

            function report() {
                const active = workspace.activeWindow;
                const activeId = active ? String(active.internalId) : "";
                const list = workspace.windowList();
                const windows = [];
                for (let i = 0; i < list.length; i++) {
                    const w = list[i];
                    const skip = !!(w.dock || w.desktopWindow || w.specialWindow || w.notification || w.onScreenDisplay || w.utility || w.splash || w.popupWindow);
                    windows.push({
                        id: String(w.internalId),
                        pid: w.pid || 0,
                        minimized: !!w.minimized,
                        skip: skip,
                        name: String(w.resourceName || ""),
                        cls: String(w.resourceClass || ""),
                        desktop: String(w.desktopFileName || "")
                    });
                }
                callDBus(BUS, "{{BridgePath}}", "{{BridgeInterface}}", "Report", JSON.stringify({ active: activeId, windows: windows }));
            }

            function watch(w) {
                if (!w) return;
                const key = String(w.internalId);
                if (observed.has(key)) return;
                observed.add(key);
                if (w.minimizedChanged) w.minimizedChanged.connect(report);
                if (w.desktopFileNameChanged) w.desktopFileNameChanged.connect(report);
                if (w.closed) {
                    w.closed.connect(() => {
                        observed.delete(key);
                        report();
                    });
                }
            }

            workspace.windowList().forEach(watch);
            workspace.windowAdded.connect((w) => {
                watch(w);
                report();
            });
            workspace.windowRemoved.connect((w) => {
                if (w) observed.delete(String(w.internalId));
                report();
            });
            workspace.windowActivated.connect(() => report());
            report();
            """;
    }

    private static void WritePrivateFile(string path, string content)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, true);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Available = false;
        lock (scriptGate)
        {
            UnloadScript(observerPlugin);
            UnloadScript(minimizePlugin);
            try { if (File.Exists(observerPath)) File.Delete(observerPath); } catch { }
            try { if (File.Exists(minimizePath)) File.Delete(minimizePath); } catch { }
        }
        try { conn.RemoveMethodHandler(BridgePath); } catch { }
        try { conn.Dispose(); } catch { }
        initialSnapshot.Dispose();
    }
}
