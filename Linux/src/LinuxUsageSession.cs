using System.Diagnostics;

namespace Pulso;

// Monitors desktop screen lock asynchronously on Linux without blocking the UI thread.
internal sealed class LinuxUsageSession : IDisposable
{
    private readonly Action interrupt, checkpoint;
    private CancellationTokenSource? watchCts;
    private Task? watchTask;
    private volatile bool screenLocked;
    private bool disposed;

    public LinuxUsageSession(Action interrupt, Action checkpoint)
    {
        this.interrupt = interrupt;
        this.checkpoint = checkpoint;
    }

    public bool CanMeasure => !screenLocked;

    public void Watch(bool enabled)
    {
        if (disposed) return;
        if (enabled && watchCts == null)
        {
            watchCts = new CancellationTokenSource();
            watchTask = Task.Run(() => PollLoopAsync(watchCts.Token));
        }
        else if (!enabled && watchCts != null)
        {
            watchCts.Cancel();
            watchCts.Dispose();
            watchCts = null;
            watchTask = null;
            screenLocked = false;
        }
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool? locked = await QueryScreenLockedAsync(token);
            if (locked is bool value && value != screenLocked)
            {
                screenLocked = value;
                interrupt();
                if (!CanMeasure) checkpoint();
            }
            try { await Task.Delay(TimeSpan.FromMilliseconds(2500), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task<bool?> QueryScreenLockedAsync(CancellationToken token)
    {
        foreach (var (dest, path, iface) in new[]
        {
            ("org.freedesktop.ScreenSaver", "/ScreenSaver", "org.freedesktop.ScreenSaver"),
            ("org.gnome.ScreenSaver", "/org/gnome/ScreenSaver", "org.gnome.ScreenSaver")
        })
        {
            var output = await RunDbusAsync(dest, path, iface + ".GetActive", token);
            if (output == null) continue;
            if (output.Contains("boolean true", StringComparison.OrdinalIgnoreCase)) return true;
            if (output.Contains("boolean false", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return null;
    }

    private static async Task<string?> RunDbusAsync(string dest, string path, string method, CancellationToken token)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("dbus-send")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.StartInfo.ArgumentList.Add("--session");
            proc.StartInfo.ArgumentList.Add($"--dest={dest}");
            proc.StartInfo.ArgumentList.Add("--type=method_call");
            proc.StartInfo.ArgumentList.Add("--print-reply=literal");
            proc.StartInfo.ArgumentList.Add(path);
            proc.StartInfo.ArgumentList.Add(method);
            if (!proc.Start()) return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(500);
            string output = await proc.StandardOutput.ReadToEndAsync(timeout.Token);
            await proc.WaitForExitAsync(timeout.Token);
            return proc.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Watch(false);
    }
}
