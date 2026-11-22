using System.Text.Json.Nodes;
using Pulso;

internal static class SiteFilterChecks
{
    public static void Run(Action<bool, string> check, string folder)
    {
        var path = Path.Combine(folder, "foco.json");
        var profile = JsonNode.Parse("""
            {
              "Minutes": 35, "Theme": "dark", "Apps": ["Leitor"], "Sites": ["exemplo.test"],
              "Routines": [{"Id":"11111111-1111-1111-1111-111111111111","Name":"Leitura","Days":2,
                            "StartMinute":780,"Minutes":30,"HourMinutes":4}],
              "History": [{"Title":"Trabalho","Start":"2026-11-22T09:00:00Z","End":"2026-11-22T09:10:00Z","Completed":true}],
              "Usage": {"Shared":{"HourMinutes":10,"DayMinutes":30,"HourTicks":200000000,"DayTicks":300000000},
                        "Items":{"site:exemplo.test":{"HourMinutes":5,"HourTicks":100000000}},
                        "ScheduleTicks":{"11111111-1111-1111-1111-111111111111":100000000},
                        "Day":"2026-11-23","HourStart":"2026-11-23T13:00:00Z"}
            }
            """)!.AsObject();
        File.WriteAllText(path, profile.ToJsonString());
        var store = new FocusStore(folder);
        var legacy = store.Load();
        check(legacy.EnabledSiteFilters == 0 && new FocusData().EnabledSiteFilters == 0,
            "Tanto o primeiro uso quanto a atualização do perfil 1.4 deixam as duas categorias desativadas");
        check(legacy.Theme == "dark" && legacy.Minutes == 35 && legacy.Sites.SequenceEqual(["exemplo.test"])
            && legacy.Apps.SequenceEqual(["Leitor"]) && legacy.History.Single().Title == "Trabalho"
            && legacy.Routines.Single().HourMinutes == 4 && legacy.Usage.ScheduleTicks.Single().Value == 100000000
            && legacy.Usage.Shared.DayTicks == 300000000 && legacy.Usage.Items["site:exemplo.test"].HourTicks == 100000000,
            "Um perfil 1.4 mantém as listas, o tema, o histórico, a agenda e os saldos já consumidos");

        profile["PornFilter"] = "always";
        profile["GamblingFilter"] = "focus";
        File.WriteAllText(path, profile.ToJsonString());
        store.Save(store.Load());
        var saved = JsonNode.Parse(File.ReadAllText(path))!;
        check((string?)saved["PornFilter"] == "always" && (string?)saved["GamblingFilter"] == "focus",
            "Cada categoria conserva seu modo de bloqueio ao salvar e reabrir");

        profile["PornFilter"] = new JsonObject { ["formato"] = 9 };
        profile["OutrosDados"] = new string('x', 64000);
        File.WriteAllText(path, profile.ToJsonString());
        var partial = new FocusStore(folder).Load();
        check(partial.PornFilter == SiteFilterMode.Off && partial.GamblingFilter == SiteFilterMode.Focus
            && partial.Usage.Shared.HourTicks == 200000000,
            "Um modo inválido desativa somente a categoria afetada e conserva o perfil principal");

        var zone = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2026, 11, 23, 13, 0, 0, TimeSpan.Zero);
        var end = now.AddMinutes(30);
        SiteFilterPolicy Filters(FocusData data, DateTimeOffset at) => new UsagePolicy(data, at, zone).Filters;
        var mixed = legacy with { PornFilter = SiteFilterMode.Focus, GamblingFilter = SiteFilterMode.Always };
        check(Filters(mixed, now.AddTicks(-1)) == new SiteFilterPolicy(0, -1)
            && Filters(mixed, now) == new SiteFilterPolicy(end.ToUnixTimeMilliseconds(), -1)
            && Filters(mixed, end) == new SiteFilterPolicy(0, -1),
            "Durante o foco acompanha a janela da agenda com saldo disponível, e Sempre continua após o fim");
        var exhausted = UsageClock.Accrue(mixed, "Leitor", null, now, now.AddMinutes(6), zone);
        check(Filters(exhausted, now.AddMinutes(7)).Porn == end.ToUnixTimeMilliseconds(),
            "O esgotamento da cota não encurta a duração do filtro");

        var routine = mixed.Routines.Single();
        var scheduled = routine with { Id = Guid.NewGuid(), HourMinutes = null, StartMinute = 775, Minutes = 15 };
        var all = FocusClock.Start(mixed with { Routines = [scheduled, routine] }, now.AddMinutes(-10), 20);
        check(Filters(all, now).Porn == end.ToUnixTimeMilliseconds(),
            "Sessão rápida, agendamento imediato e agenda com saldo aplicam o prazo mais longo em vigor");
        var stopped = FocusClock.Stop(all, now.AddSeconds(1), zone);
        check(Filters(stopped, now.AddSeconds(1)).Porn == end.ToUnixTimeMilliseconds(),
            "Encerrar a sessão sobreposta conserva a proteção da agenda com saldo");
        var disabled = stopped with { Routines = stopped.Routines.Select(r => r.Id == routine.Id ? r with { Enabled = false } : r).ToArray() };
        check(Filters(disabled, now.AddSeconds(2)).Porn == 0,
            "Desativar a última agenda libera a categoria sem reabrir a sessão encerrada");
        var manual = FocusClock.Start(mixed with { Routines = [] }, now, 5);
        check(Filters(manual, now).Porn == now.AddMinutes(5).ToUnixTimeMilliseconds()
            && Filters(manual, now.AddMinutes(5)).Porn == 0
            && Filters(FocusClock.Stop(manual, now.AddSeconds(1), zone), now.AddSeconds(1)).Porn == 0,
            "A sessão rápida libera o filtro exatamente no vencimento ou ao encerrá-la antes do fim");
        var immediate = FocusClock.Tick(mixed with { Routines = [scheduled] }, now, zone);
        check(Filters(immediate, now).Porn == now.AddMinutes(10).ToUnixTimeMilliseconds()
            && Filters(FocusClock.Stop(immediate, now.AddSeconds(1), zone), now.AddSeconds(1)).Porn == 0,
            "Um agendamento imediato aplica a categoria e respeita o encerramento antecipado");
        var changed = mixed with { PornFilter = SiteFilterMode.Off, GamblingFilter = SiteFilterMode.Focus };
        check(Filters(changed, now) == new SiteFilterPolicy(0, end.ToUnixTimeMilliseconds())
            && Filters(changed, end) == new SiteFilterPolicy(0, 0),
            "Trocar os modos durante uma janela passa os prazos corretos para cada categoria");
        var unlisted = UsageClock.Accrue(mixed, null, "fora-da-lista.test", now, now.AddSeconds(30), zone);
        check(UsageClock.WatchedSites(mixed, now, zone).SequenceEqual(["exemplo.test"])
            && unlisted.Usage.Shared == mixed.Usage.Shared && unlisted.Usage.ScheduleTicks.SequenceEqual(mixed.Usage.ScheduleTicks)
            && !UsageClock.HasLimits(new FocusData { GamblingFilter = SiteFilterMode.Always }),
            "Filtros não acrescentam domínios monitorados, débitos de uso ou limites à lista");
    }
}
