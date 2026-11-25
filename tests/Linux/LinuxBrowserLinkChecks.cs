using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Pulso;

internal static class LinuxBrowserLinkChecks
{
    public static async Task<int> Run()
    {
        var folder = Directory.CreateTempSubdirectory("pulso-linux-browser-");
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();

        var bridge = new BrowserLink(folder.FullName, port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token;

        try
        {
            string ext = bridge.PrepareExtension();
            Check(File.Exists(Path.Combine(ext, "manifest.json"))
                && File.Exists(Path.Combine(ext, "worker.js"))
                && File.Exists(Path.Combine(ext, "pausa.html"))
                && File.Exists(Path.Combine(ext, "status.html"))
                && File.Exists(Path.Combine(ext, "site_porn.txt"))
                && File.Exists(Path.Combine(ext, "site_gambling.txt"))
                && File.ReadAllText(Path.Combine(ext, "conexao.js")).Contains($"const PULSO_PORT = {port};"),
                "PrepareExtension extrai todos os arquivos da extensão e grava porta/protocolo em conexao.js.");

            string ffExt = bridge.PrepareFirefoxExtension();
            var ffManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ffExt, "manifest.json")))!;
            Check((string?)ffManifest["browser_specific_settings"]?["gecko"]?["id"] == "pulso@pulso.local"
                && ffManifest["background"]?["scripts"]?.AsArray().Select(s => (string?)s).SequenceEqual(["conexao.js", "worker.js"]) == true
                && File.Exists(Path.Combine(ffExt, "site_porn.txt"))
                && File.Exists(Path.Combine(ffExt, "site_gambling.txt"))
                && File.Exists(bridge.FirefoxExtensionPackage),
                "PrepareFirefoxExtension gera manifesto Gecko MV3, listas de filtros e pacote .xpi.");

            using (var zip = ZipFile.OpenRead(bridge.FirefoxExtensionPackage))
            {
                var entries = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
                Check(entries.IsSupersetOf(["manifest.json", "worker.js", "conexao.js", "pausa.html", "status.html", "site_porn.txt", "site_gambling.txt"]),
                    "Pacote pulso-firefox.xpi contém todos os arquivos necessários da extensão.");
            }

            string keyPath = Path.Combine(folder.FullName, "conexao.key");
            if (!OperatingSystem.IsWindows())
                Check(File.GetUnixFileMode(keyPath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite),
                    "A chave de conexão usa permissão restrita ao usuário (0600) no Linux.");

            await bridge.StartAsync();
            Check(bridge.Error == null, "Servidor WebSocket local inicia sem erro no Linux.");

            using var http = new HttpClient();
            var forbidden = await http.GetAsync($"http://127.0.0.1:{port}/foco", token);
            Check(forbidden.StatusCode == HttpStatusCode.Forbidden,
                "Requisição HTTP comum sem origem/subprotocolo da extensão recebe 403.");

            using var legacy = await Connect("Chrome");
            var first = await Read(legacy);
            await Send(legacy, $"use:{first["revision"]}:");
            await Until(() => bridge.Connected == "Chrome");
            Check(bridge.RequiresFilterReload && !bridge.RequiresReload && !bridge.HasFilterClient,
                "Cliente sem suporte a filtros sinaliza recarga.");

            var now = DateTimeOffset.Now;
            var end = now.AddMinutes(5);
            var data = new FocusData
            {
                Sites = ["manual.test"],
                Active = new("Foco Linux", now, end, false),
                PornFilter = SiteFilterMode.Always,
                GamblingFilter = SiteFilterMode.Focus
            };
            bridge.UpdateUsage(["manual.test"], new Dictionary<string, DateTimeOffset> { ["manual.test"] = end });
            bridge.Update(data);

            using var modern = await Connect("Edge/1.5");
            var rules = await Read(modern);
            Check((long?)rules["filters"]?["porn"] == -1
                && (long?)rules["filters"]?["gambling"] == end.ToUnixTimeMilliseconds()
                && (string?)rules["sites"]?[0] == "manual.test",
                "Extensão 1.5 no Linux recebe política v2 com filtros de Pornografia e Apostas.");

            await Send(modern, $"use:{rules["revision"]}:manual.test");
            await Until(() => bridge.ForegroundSite("microsoft-edge-stable") == "manual.test");

            const string ffOrigin = "moz-extension://12345678-1234-4123-8123-123456789abc";
            using var firefox = await Connect("Firefox/1.5", ffOrigin);
            var ffRules = await Read(firefox);
            await Send(firefox, $"use:{ffRules["revision"]}:manual.test");
            await Until(() => bridge.ForegroundSite("firefox") == "manual.test" && bridge.ForegroundSite("firefox_firefox") == "manual.test");
            Check(bridge.Connected == "Chrome, Edge e Firefox" && bridge.UsageTarget("firefox").Site == "manual.test",
                "Firefox com origem moz-extension:// conecta ao BrowserLink, mede uso e formata Connected com 3 navegadores.");

            foreach (var badOrigin in new[] { "https://evil.test", "moz-extension://invalido" })
            {
                bool rejected = false;
                using var rogue = new ClientWebSocket();
                rogue.Options.SetRequestHeader("Origin", badOrigin);
                rogue.Options.AddSubProtocol("pulso." + File.ReadAllText(keyPath));
                try { await rogue.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/foco"), token); }
                catch (WebSocketException) { rejected = true; }
                Check(rejected, $"Origem não autorizada ({badOrigin}) é rejeitada pelo WebSocket.");
            }

            Check(bridge.UsageTarget("google-chrome").App == "google-chrome"
                && bridge.UsageTarget("chromium").App == "chromium"
                && bridge.UsageTarget("microsoft-edge").Site == "manual.test"
                && bridge.UsageTarget("microsoft-edge-beta").Site == "manual.test",
                "Nomes de processos de navegadores Linux são reconhecidos pelo BrowserLink.");

            await Send(modern, $"use:{rules["revision"]}:@pause");
            await Until(() => bridge.UsageTarget("microsoft-edge").App == null);
            Check(bridge.HasFilterClient,
                "Aviso @pause de categoria suspende a contagem do navegador conectado no Linux.");

            return 0;
        }
        finally
        {
            await bridge.CloseAsync();
            if (folder.Parent?.FullName.TrimEnd(Path.DirectorySeparatorChar) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                && folder.Name.StartsWith("pulso-linux-browser-", StringComparison.Ordinal))
                folder.Delete(true);
        }

        async Task<ClientWebSocket> Connect(string hello, string origin = BrowserLink.Origin)
        {
            var socket = new ClientWebSocket();
            try
            {
                socket.Options.SetRequestHeader("Origin", origin);
                socket.Options.AddSubProtocol("pulso." + File.ReadAllText(Path.Combine(folder.FullName, "conexao.key")));
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/foco"), token);
                await Send(socket, hello);
                return socket;
            }
            catch { socket.Dispose(); throw; }
        }

        Task Send(ClientWebSocket socket, string message) =>
            socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Text, true, token);

        async Task Until(Func<bool> predicate)
        {
            while (!predicate()) await Task.Delay(15, token);
        }

        async Task<JsonNode> Read(ClientWebSocket socket)
        {
            using var message = new MemoryStream();
            var buffer = new byte[1024];
            WebSocketReceiveResult next;
            do
            {
                next = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (next.MessageType != WebSocketMessageType.Text) throw new IOException("Conexão fechada antes do payload.");
                message.Write(buffer, 0, next.Count);
            } while (!next.EndOfMessage);
            return JsonNode.Parse(message.ToArray())!;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("OK  " + message);
    }
}
