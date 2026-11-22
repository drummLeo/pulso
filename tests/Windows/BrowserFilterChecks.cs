using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Pulso;

internal static class BrowserFilterChecks
{
    public static async Task<int> Run()
    {
        var folder = Directory.CreateTempSubdirectory("pulso-filtros-conexao-");
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var bridge = new BrowserLink(folder.FullName, port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        try
        {
            await bridge.StartAsync();
            if (bridge.Error != null) throw new InvalidOperationException(bridge.Error);
            using var legacy = await Connect("Chrome");
            var first = await Read(legacy);
            Check((int?)first["version"] == 2 && (long?)first["filters"]?["porn"] == 0
                && (long?)first["filters"]?["gambling"] == 0,
                "O cliente antigo recebe a política v2 com os filtros inicialmente desligados.");
            await Send(legacy, $"use:{first["revision"]}:");
            await Until(() => bridge.Connected == "Chrome");
            Check(bridge.RequiresFilterReload && !bridge.RequiresReload && !bridge.HasFilterClient,
                "O Pulso reconhece que a extensão anterior mede uso e precisa recarregar para aplicar filtros.");

            var now = DateTimeOffset.Now;
            var end = now.AddMinutes(3);
            var data = new FocusData { Sites = ["manual.test"], Active = new("Teste local", now, end, false),
                PornFilter = SiteFilterMode.Always, GamblingFilter = SiteFilterMode.Focus };
            bridge.UpdateUsage(["manual.test"], new Dictionary<string, DateTimeOffset> { ["manual.test"] = end.AddMinutes(2) });
            bridge.Update(data);
            using var recent = await Connect("Edge/1.5");
            var rules = await Read(recent);
            Check((long?)rules["filters"]?["porn"] == -1 && (long?)rules["filters"]?["gambling"] == end.ToUnixTimeMilliseconds()
                && (long?)rules["until"] == end.ToUnixTimeMilliseconds() && (string?)rules["sites"]?[0] == "manual.test"
                && rules["watch"]?.AsArray().Count == 1 && (string?)rules["watch"]?[0] == "manual.test"
                && (long?)rules["limits"]?[0]?["until"] == end.AddMinutes(2).ToUnixTimeMilliseconds(),
                "A conexão 1.5 recebe as duas categorias e as regras manuais de foco, medição e limite.");
            await Send(recent, $"use:{rules["revision"]}:manual.test");
            var oldRules = await Read(legacy);
            while ((long?)oldRules["revision"] != (long?)rules["revision"]) oldRules = await Read(legacy);
            await Send(legacy, $"use:{rules["revision"]}:manual.test");
            await Until(() => bridge.ForegroundSite("msedge") == "manual.test" && bridge.ForegroundSite("chrome") == "manual.test");
            Check(bridge.Connected == "Chrome e Edge" && bridge.HasFilterClient && bridge.RequiresFilterReload,
                "Versões diferentes continuam medindo o site manual nos dois navegadores.");

            bridge.Update(data with { PornFilter = SiteFilterMode.Off });
            var updated = await After(recent, rules);
            Check((long?)updated["revision"] > (long?)rules["revision"] && (long?)updated["filters"]?["porn"] == 0
                && (long?)updated["filters"]?["gambling"] == end.ToUnixTimeMilliseconds()
                && (string?)updated["watch"]?[0] == "manual.test",
                "Alterar só um modo publica uma nova política na conexão aberta.");
            await Send(legacy, $"use:{updated["revision"]}:@pause");
            await Until(() => !bridge.RequiresFilterReload);
            Check(bridge.Connected == "Edge" && bridge.HasFilterClient,
                "O cliente antigo que tenta informar pausa de categoria é desconectado e sai do aviso de recarga.");
            await Send(recent, $"use:{updated["revision"]}:manual.test");
            await Until(() => bridge.ForegroundSite("msedge") == "manual.test");
            await Send(recent, $"use:{updated["revision"]}:@pause");
            await Until(() => bridge.ForegroundSite("msedge") == null);
            Check(bridge.HasFilterClient,
                "Um aviso de categoria sem domínio é aceito pela conexão 1.5 enquanto há filtro em vigor.");

            var clock = new DateTimeOffset(2026, 11, 23, 13, 10, 0, TimeSpan.Zero);
            var quota = new Routine { Days = 2, StartMinute = 780, Minutes = 30, HourMinutes = 4 };
            var measured = new FocusData { Apps = ["msedge"], Sites = ["manual.test"], Routines = [quota], PornFilter = SiteFilterMode.Always };
            measured = UsageClock.Configure(measured, null, 5, 5);
            measured = UsageClock.Configure(measured, "app:msedge", 5, null);
            measured = UsageClock.Configure(measured, "site:manual.test", 5, null);
            var meter = new UsageMeter();
            void Sample(int second)
            {
                var (app, site) = bridge.UsageTarget("msedge");
                measured = meter.Sample(measured, clock.AddSeconds(second), TimeSpan.FromSeconds(second), app, site, TimeZoneInfo.Utc);
            }
            bridge.Update(measured);
            var always = await After(recent, updated);
            await Send(recent, $"use:{always["revision"]}:manual.test");
            await Until(() => bridge.ForegroundSite("msedge") == "manual.test");
            Sample(0); Sample(1);
            await Send(recent, $"use:{always["revision"]}:@pause");
            await Until(() => bridge.UsageTarget("msedge").App == null);
            using var otherProfile = await Connect("Edge/1.5");
            var samePolicy = await Read(otherProfile);
            await Send(otherProfile, $"use:{samePolicy["revision"]}:manual.test");
            await Until(() => bridge.ForegroundSite("msedge") == "manual.test");
            await Send(otherProfile, $"use:{samePolicy["revision"]}:");
            await Until(() => bridge.UsageTarget("msedge").App == null);
            Sample(2); Sample(3);
            Check(bridge.UsageTarget("msedge") == (null, null) && bridge.UsageTarget("chrome") == ("chrome", null)
                && measured.Usage.Items.Values.All(i => i.HourTicks == TimeSpan.TicksPerSecond)
                && measured.Usage.Shared.HourTicks == TimeSpan.TicksPerSecond
                && measured.Usage.ScheduleTicks[quota.Id] == TimeSpan.TicksPerSecond,
                "A pausa em primeiro plano exclui o navegador, o domínio listado e as cotas, mesmo com outro perfil conectado.");
            await Send(recent, $"use:{always["revision"]}:manual.test");
            await Until(() => bridge.ForegroundSite("msedge") == "manual.test");
            Sample(4); Sample(5);
            Check(measured.Usage.Items.Values.All(i => i.HourTicks == 2 * TimeSpan.TicksPerSecond)
                && measured.Usage.Shared.HourTicks == 2 * TimeSpan.TicksPerSecond
                && measured.Usage.ScheduleTicks[quota.Id] == 2 * TimeSpan.TicksPerSecond,
                "Trocar da pausa para a aba manual retoma a medição sem cobrar o intervalo.");
            otherProfile.Abort();

            bridge.UpdateUsage([], new Dictionary<string, DateTimeOffset>());
            var noWatch = await After(recent, always);
            await Send(recent, $"use:{noWatch["revision"]}:@pause");
            await Until(() => bridge.UsageTarget("msedge").App == null);
            var heartbeat = await Read(recent).WaitAsync(TimeSpan.FromSeconds(7), token);
            Check((long?)heartbeat["revision"] == (long?)noWatch["revision"] && heartbeat["watch"]?.AsArray().Count == 0,
                "Mesmo com a lista de medição vazia, a política solicita o aviso antes do limite de oito segundos.");
            var shortFocus = DateTimeOffset.Now;
            bridge.Update(new FocusData { GamblingFilter = SiteFilterMode.Focus,
                Active = new Session("Curta", shortFocus, shortFocus.AddSeconds(2), false) });
            var expiring = await After(recent, noWatch);
            Check(bridge.UsageTarget("msedge").App == "msedge", "Uma nova revisão descarta o aviso da política anterior.");
            await Send(recent, $"use:{expiring["revision"]}:@pause");
            await Until(() => bridge.UsageTarget("msedge").App == null);
            await Until(() => bridge.UsageTarget("msedge").App != null);
            Check(bridge.HasFilterClient, "O aviso expira no fim do foco sem depender do próximo relatório.");

            await Send(recent, $"use:{expiring["revision"]}:fora-da-lista.test");
            await Until(() => !bridge.HasFilterClient);
            Check(bridge.ForegroundSite("msedge") == null,
                "O PC rejeita relatórios de domínio fora da lista manual de medição.");
            return 0;
        }
        finally
        {
            await bridge.CloseAsync();
            if (folder.Parent?.FullName.TrimEnd('\\') == Path.GetTempPath().TrimEnd('\\')
                && folder.Name.StartsWith("pulso-filtros-conexao-", StringComparison.Ordinal)) folder.Delete(true);
        }

        async Task<ClientWebSocket> Connect(string name)
        {
            var socket = new ClientWebSocket();
            try
            {
                socket.Options.SetRequestHeader("Origin", BrowserLink.Origin);
                socket.Options.AddSubProtocol("pulso." + File.ReadAllText(Path.Combine(folder.FullName, "conexao.key")));
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/foco"), token);
                await Send(socket, name);
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

        async Task<JsonNode> After(ClientWebSocket socket, JsonNode previous)
        {
            JsonNode next;
            do { next = await Read(socket); } while ((long?)next["revision"] <= (long?)previous["revision"]);
            return next;
        }

        async Task<JsonNode> Read(ClientWebSocket socket)
        {
            using var message = new MemoryStream();
            var buffer = new byte[1024];
            WebSocketReceiveResult next;
            do
            {
                next = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (next.MessageType != WebSocketMessageType.Text) throw new IOException("A conexão terminou antes de enviar a política.");
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
