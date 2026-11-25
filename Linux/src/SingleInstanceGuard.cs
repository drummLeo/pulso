using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Pulso;

internal sealed class SingleInstanceGuard : IDisposable
{
    [DllImport("libc")] private static extern uint geteuid();
    private readonly FileStream lockFile;
    private readonly string socketPath;
    private readonly Socket listener;
    private readonly CancellationTokenSource cts = new();
    private readonly Action onReopen;
    private bool disposed;

    private SingleInstanceGuard(FileStream lockFile, string socketPath, Socket listener, Action onReopen)
    {
        this.lockFile = lockFile;
        this.socketPath = socketPath;
        this.listener = listener;
        this.onReopen = onReopen;
        _ = Task.Run(AcceptLoopAsync);
    }

    public static SingleInstanceGuard? TryAcquire(string dataFolder, Action onReopen)
    {
        Directory.CreateDirectory(dataFolder);
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(dataFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        string fullData = Path.GetFullPath(dataFolder);
        string lockPath = Path.Combine(fullData, "instancia.lock");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullData)))[..16];
        string uid = OperatingSystem.IsLinux() ? geteuid().ToString() : Environment.UserName;
        string sockPath = Path.Combine(LinuxPaths.RuntimeDirectory(), $"pulso-{uid}-{hash}.sock");

        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            NotifyExisting(sockPath);
            return null;
        }

        try
        {
            if (File.Exists(sockPath)) File.Delete(sockPath);
            var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                server.Bind(new UnixDomainSocketEndPoint(sockPath));
                if (!OperatingSystem.IsWindows())
                {
                    try { File.SetUnixFileMode(sockPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                server.Listen(8);
                return new SingleInstanceGuard(lockStream, sockPath, server, onReopen);
            }
            catch
            {
                server.Dispose();
                throw;
            }
        }
        catch
        {
            lockStream.Dispose();
            throw;
        }
    }

    private static void NotifyExisting(string sockPath)
    {
        if (!File.Exists(sockPath)) return;
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.SendTimeout = 1500;
            client.ReceiveTimeout = 1500;
            client.Connect(new UnixDomainSocketEndPoint(sockPath));
            client.Send("abrir\n"u8);
            var buf = new byte[16];
            _ = client.Receive(buf);
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException) { }
    }

    private async Task AcceptLoopAsync()
    {
        var buffer = new byte[64];
        while (!cts.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptAsync(cts.Token);
                int read = await client.ReceiveAsync(buffer, SocketFlags.None, cts.Token);
                string message = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                if (message == "abrir")
                {
                    await client.SendAsync("ok\n"u8.ToArray(), SocketFlags.None, cts.Token);
                    onReopen();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or IOException)
            {
                if (cts.IsCancellationRequested) break;
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cts.Cancel();
        try { listener.Dispose(); } catch { }
        try { if (File.Exists(socketPath)) File.Delete(socketPath); } catch { }
        try { lockFile.Dispose(); } catch { }
        cts.Dispose();
    }
}
