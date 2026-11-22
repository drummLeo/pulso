using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Pulso;

public sealed class BrowserLink
{
    public const int Port = 38473;
    public const string Origin = "chrome-extension://ebadenennfkkhaocjohijlpknihkjmbd";
    private const string Assets = "Pulso.Extension.";
    private readonly string folder;
    private readonly int port;
    private readonly string protocol;
    private sealed record Client(string Browser, bool MeasuresUsage, bool SupportsFilters, string? Site, bool CategoryPaused, long Revision, long Seen);
    private sealed record SiteLimit(string Domain, long Until);
    private sealed record Policy(long Revision, long Until, string[] Sites, string[] Watch, SiteLimit[] Limits, string Theme, SiteFilterPolicy Filters);
    private readonly ConcurrentDictionary<Guid, Client> clients = new();
    private readonly object policyLock = new();
    private volatile Policy policy = new(0, 0, [], [], [], "system", default);
    private WebApplication? server;
    public string? Error { get; private set; }
    public string? ExtensionWarning { get; private set; }
    public string Connected => string.Join(" e ", clients.Values.Select(c => c.Browser).Distinct().Order());
    public bool RequiresReload => clients.Values.Any(c => !c.MeasuresUsage);
    public bool RequiresFilterReload => clients.Values.Any(c => !c.SupportsFilters);
    public bool HasFilterClient => clients.Values.Any(c => c.SupportsFilters);
    public string ExtensionFolder => Path.Combine(folder, "Extensao");

    public BrowserLink(string folder, int port = Port)
    {
        this.folder = folder;
        this.port = port;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "conexao.key");
        if (!File.Exists(path))
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var key = File.ReadAllText(path).Trim();
        if (key.Length != 48 || !key.All(Uri.IsHexDigit)) throw new InvalidDataException("A chave de conexão do navegador está danificada.");
        protocol = "pulso." + key;
    }

    public string PrepareExtension()
    {
        var assembly = typeof(BrowserLink).Assembly;
        Directory.CreateDirectory(ExtensionFolder);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Assets, StringComparison.Ordinal)))
        {
            using var resource = assembly.GetManifestResourceStream(name)!;
            var fileName = name[Assets.Length..];
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();
            if (fileName == "manifest.json" && port != Port)
                bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("127.0.0.1:" + Port, "127.0.0.1:" + port));
            WriteIfChanged(Path.Combine(ExtensionFolder, fileName), bytes);
        }
        WriteIfChanged(Path.Combine(ExtensionFolder, "conexao.js"), Encoding.UTF8.GetBytes(
            "const PULSO_PROTOCOL = " + JsonSerializer.Serialize(protocol) + ";\nconst PULSO_PORT = " + port + ";"));
        ExtensionWarning = null;
        return ExtensionFolder;
    }

    private static void WriteIfChanged(string path, byte[] bytes)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Update(FocusData data) => Update(data, new UsagePolicy(data, DateTimeOffset.Now, TimeZoneInfo.Local));

    internal void Update(FocusData data, UsagePolicy rules)
    {
        DateTimeOffset? focusEnd = rules.Focus?.Until;
        long until = focusEnd?.ToUnixTimeMilliseconds() ?? 0;
        string[] sites = focusEnd.HasValue ? data.Sites : [];
        string theme = data.Theme is "light" or "dark" ? data.Theme : "system";
        lock (policyLock)
        {
            var previous = policy;
            if (previous.Until != until || previous.Filters != rules.Filters || previous.Theme != theme || !previous.Sites.SequenceEqual(sites))
                policy = previous with { Revision = previous.Revision + 1, Until = until, Sites = sites, Theme = theme, Filters = rules.Filters };
        }
    }

    public void UpdateUsage(IEnumerable<string> watchedSites, IReadOnlyDictionary<string, DateTimeOffset> blockedSites)
    {
        lock (policyLock)
        {
            var previous = policy;
            if (previous.Watch.Length == 0 && previous.Limits.Length == 0 && !watchedSites.Any() && blockedSites.Count == 0) return;
            string[] watch = watchedSites.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var limits = blockedSites.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new SiteLimit(kv.Key, kv.Value.ToUnixTimeMilliseconds())).ToArray();
            if (!previous.Watch.SequenceEqual(watch) || !previous.Limits.SequenceEqual(limits))
                policy = previous with { Revision = previous.Revision + 1, Watch = watch, Limits = limits };
        }
    }

    public string? ForegroundSite(string? processName) => UsageTarget(processName).Site;

    internal (string? App, string? Site) UsageTarget(string? processName)
    {
        string? browser = processName?.ToLowerInvariant() switch { "chrome" => "Chrome", "msedge" => "Edge", _ => null };
        if (browser == null) return (processName, null);
        var current = policy;
        bool filtersActive = current.Filters.IsActiveAt(DateTimeOffset.UtcNow);
        if (current.Watch.Length == 0 && !filtersActive) return (processName, null);
        long cutoff = Stopwatch.GetTimestamp() - 8 * Stopwatch.Frequency;
        var tab = clients.Values.Where(c => c.Browser == browser && c.Revision == current.Revision && c.Seen >= cutoff
                && (c.Site != null || c.CategoryPaused && filtersActive))
            .MaxBy(c => c.Seen);
        return tab?.CategoryPaused == true ? (null, null) : (processName, tab?.Site);
    }

    public async Task StartAsync()
    {
        if (Directory.Exists(ExtensionFolder))
        {
            try { PrepareExtension(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ExtensionWarning = "Abra Navegadores para atualizar a pasta da extensão.";
            }
        }
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxConcurrentUpgradedConnections = 24;
                options.Listen(IPAddress.Loopback, port);
            });
            server = builder.Build();
            server.UseWebSockets();
            server.Run(Handle);
            await server.StartAsync();
            Error = null;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Error = "Conexão local indisponível na porta " + port + ". Feche outra instância do Pulso e reabra o aplicativo.";
            if (server != null) await server.DisposeAsync();
            server = null;
        }
    }

    private async Task Handle(HttpContext context)
    {
        if (context.Request.Path != "/foco" || context.Request.Host.Host != "127.0.0.1"
            || context.Request.Headers.Origin != Origin || !context.WebSockets.IsWebSocketRequest
            || !context.WebSockets.WebSocketRequestedProtocols.Contains(protocol))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync(protocol);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, server!.Lifetime.ApplicationStopping);
        var token = lifetime.Token;
        var id = Guid.NewGuid();
        var buffer = new byte[384];
        async Task<string?> Read()
        {
            int length = 0;
            while (length < buffer.Length)
            {
                var part = new ArraySegment<byte>(buffer, length, buffer.Length - length);
                var packet = await socket.ReceiveAsync(part, token).WaitAsync(TimeSpan.FromSeconds(18), token);
                if (packet.MessageType != WebSocketMessageType.Text) return null;
                length += packet.Count;
                if (packet.EndOfMessage) return Encoding.UTF8.GetString(buffer, 0, length);
            }
            return null;
        }
        async Task ReceiveReports(string browser, bool supportsFilters)
        {
            while (await Read() is { } report)
            {
                var current = policy;
                string[] fields = report.Split(':', 3);
                bool modern = fields.Length == 3 && fields[0] == "use";
                if (!modern && !(fields.Length == 2 && fields[0] == "ok")) return;
                if (!long.TryParse(fields[1], out var revision)) return;
                if (revision != current.Revision) continue;
                string? site = modern && fields[2].Length > 0 ? fields[2] : null;
                bool categoryPaused = false;
                if (site == "@pause")
                {
                    if (!supportsFilters) return;
                    categoryPaused = current.Filters.IsActiveAt(DateTimeOffset.UtcNow);
                    site = null;
                }
                else if (site != null && Array.BinarySearch(current.Watch, site, StringComparer.Ordinal) < 0) return;
                clients[id] = new(browser, modern, supportsFilters, site, categoryPaused, revision, Stopwatch.GetTimestamp());
            }
        }
        async Task SendPolicies()
        {
            while (!token.IsCancellationRequested)
            {
                var current = policy;
                var payload = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 2, revision = current.Revision, until = current.Until, sites = current.Sites, theme = current.Theme,
                    watch = current.Watch, limits = current.Limits.Select(s => new { domain = s.Domain, until = s.Until }),
                    filters = new { porn = current.Filters.Porn, gambling = current.Filters.Gambling }
                });
                await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, token);
                int ticks = current.Watch.Length > 0 || current.Filters.IsActiveAt(DateTimeOffset.UtcNow) ? 12 : 32;
                for (int tick = 0; tick < ticks && ReferenceEquals(current, policy); tick++)
                    await Task.Delay(250, token);
            }
        }
        try
        {
            var hello = await Read();
            if (hello is not ("Edge" or "Chrome" or "Edge/1.5" or "Chrome/1.5")) return;
            bool supportsFilters = hello.EndsWith("/1.5", StringComparison.Ordinal);
            string browser = supportsFilters ? hello[..^4] : hello;
            Task sending = SendPolicies(), receiving = ReceiveReports(browser, supportsFilters);
            await Task.WhenAny(sending, receiving);
            lifetime.Cancel();
            socket.Abort();
            await Task.WhenAll(sending, receiving);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or TimeoutException) { }
        finally
        {
            clients.TryRemove(id, out _);
            socket.Abort();
        }
    }

    public async Task CloseAsync()
    {
        if (server == null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await server.StopAsync(timeout.Token); }
        catch (OperationCanceledException) { }
        await server.DisposeAsync();
    }
}
