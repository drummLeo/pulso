using Pulso;

internal static class ScheduledUsageChecks
{
    public static void Run(Action<bool, string> check, string folder)
    {
        var zone = TimeZoneInfo.Utc;
        var start = new DateTimeOffset(2026, 11, 23, 13, 15, 0, TimeSpan.Zero);
        var end = start.AddMinutes(30);
        var study = new Routine { Name = "Estudo", Days = 2, StartMinute = 795, Minutes = 30, HourMinutes = 1 };
        var list = new FocusData { Apps = ["Chrome", "Jogo"], Sites = ["video.test", "canal.video.test"], Routines = [study] };
        string game = UsageClock.AppKey("Jogo"), video = UsageClock.SiteKey("a.canal.video.test");

        check(FocusClock.Tick(list, start, zone).Active == null,
            "Um agendamento com minutos por hora permite usar a lista até gastar o saldo");
        var gameTime = UsageClock.Accrue(list, "Jogo", null, start.AddSeconds(-30), start.AddSeconds(20), zone);
        var both = UsageClock.Accrue(gameTime, "Chrome", "a.canal.video.test", start.AddSeconds(20), start.AddSeconds(59), zone);
        check(UsageClock.BlockedUntil(both, video, start.AddSeconds(59), zone) == null,
            "Só os 59 segundos dentro da janela contam, inclusive com app e subdomínios simultâneos");
        var full = UsageClock.Accrue(both, "Outro navegador", "a.canal.video.test", start.AddSeconds(59), start.AddSeconds(65), zone);
        check(UsageClock.BlockedUntil(full, game, start.AddMinutes(2), zone) == end
            && UsageClock.BlockedUntil(full, video, start.AddMinutes(2), zone) == end,
            "Usar um site esgota o mesmo minuto de todos os apps até o fim do agendamento");
        check(UsageClock.BlockedUntil(full, game, end, zone) == null
            && UsageClock.BlockedUntil(full, UsageClock.AppKey("Editor"), start.AddMinutes(2), zone) == null
            && UsageClock.BlockedUntil(full, UsageClock.SiteKey("outro.test"), start.AddMinutes(2), zone) == null,
            "O saldo da agenda libera a lista no fim da janela e respeita os itens fora da lista");

        var overlap = new Routine { Name = study.Name, Days = 2, StartMinute = 796, Minutes = 5, HourMinutes = 2 };
        var simultaneous = UsageClock.Configure(list with { Routines = [study, overlap] }, null, null, 10);
        simultaneous = UsageClock.Accrue(simultaneous, "Jogo", null, start.AddSeconds(40), start.AddMinutes(4), zone);
        check(simultaneous.Usage.Shared.DayTicks == TimeSpan.TicksPerMinute,
            "A primeira cota esgotada interrompe também o débito dos outros saldos");
        var withoutFirst = UsageClock.Reconcile(simultaneous with { Routines = [overlap] });
        check(UsageClock.BlockedUntil(withoutFirst, game, start.AddMinutes(4), zone) == null,
            "Excluir uma das agendas de mesmo nome libera a parcela que ela bloqueava");
        withoutFirst = UsageClock.Accrue(withoutFirst, "Jogo", null, start.AddMinutes(4), start.AddMinutes(5).AddSeconds(19), zone);
        check(UsageClock.BlockedUntil(withoutFirst, game, start.AddMinutes(5).AddSeconds(19), zone) == null,
            "A segunda agenda mantém os 40 segundos consumidos e permite mais 79 segundos");
        withoutFirst = UsageClock.Accrue(withoutFirst, "Jogo", null, start.AddMinutes(5).AddSeconds(19), start.AddMinutes(5).AddSeconds(22), zone);
        check(UsageClock.BlockedUntil(withoutFirst, game, start.AddMinutes(5).AddSeconds(22), zone) == start.AddMinutes(6),
            "A segunda agenda alcança seu próprio limite e termina no horário escolhido");

        var night = study with { StartMinute = 23 * 60 + 55, Minutes = 20 };
        var midnight = new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
        var overnight = UsageClock.Accrue(list with { Routines = [night] }, "Jogo", null, midnight.AddMinutes(-4), midnight.AddSeconds(-5), zone);
        check(UsageClock.BlockedUntil(overnight, game, midnight.AddSeconds(-5), zone) == midnight,
            "A cota da agenda que cruza a madrugada se renova na primeira hora cheia");
        overnight = UsageClock.Accrue(overnight, "Jogo", null, midnight.AddSeconds(-5), midnight.AddSeconds(59), zone);
        check(UsageClock.BlockedUntil(overnight, game, midnight.AddSeconds(59), zone) == null,
            "A virada da hora descarta o tempo bloqueado e começa outro saldo na mesma ocorrência");
        overnight = UsageClock.Accrue(overnight, "Jogo", null, midnight.AddSeconds(59), midnight.AddMinutes(2), zone);
        check(UsageClock.BlockedUntil(overnight, game, midnight.AddMinutes(2), zone) == midnight.AddMinutes(15),
            "A agenda de segunda também limita o uso no trecho de terça até o fim da janela");
        var brazil = TimeZoneInfo.CreateCustomTimeZone("Agenda -03", TimeSpan.FromHours(-3), "Agenda -03", "Agenda -03");
        var local = UsageClock.Accrue(list, "Jogo", null, start, start.AddMinutes(2), brazil);
        local = UsageClock.Accrue(local, "Jogo", null, start.AddHours(3), start.AddHours(3).AddMinutes(1), brazil);
        check(UsageClock.BlockedUntil(local, game, start, brazil) == null
            && UsageClock.BlockedUntil(local, video, start.AddHours(3).AddMinutes(1), brazil) == end.AddHours(3),
            "O horário e a liberação da agenda seguem o relógio local do dispositivo");

        var limitedItem = UsageClock.Configure(list, game, 1, null);
        limitedItem = UsageClock.Accrue(limitedItem, "Jogo", null, start.AddMinutes(-2), start.AddMinutes(-1), zone);
        limitedItem = UsageClock.Accrue(limitedItem, "Jogo", null, start, start.AddMinutes(2), zone);
        check(UsageClock.BlockedUntil(limitedItem, video, start.AddMinutes(2), zone) == null,
            "Um limite individual atingido antes do início não gasta o saldo da agenda");
        var manual = FocusClock.Start(list, start.AddSeconds(5), 1);
        manual = UsageClock.Accrue(manual, "Jogo", null, start, start.AddMinutes(1).AddSeconds(59), zone);
        check(UsageClock.BlockedUntil(manual, game, start.AddMinutes(1).AddSeconds(59), zone) == null,
            "Os segundos da sessão manual sobreposta ficam fora da conta da agenda");
        check(UsageClock.WatchedSites(list, start.AddSeconds(-1), zone).Length == 0
            && UsageClock.WatchedSites(list, start, zone).ToHashSet().SetEquals(list.Sites)
            && UsageClock.WatchedSites(list, end, zone).Length == 0,
            "A extensão só precisa observar a lista inteira durante a janela da cota");
        check(UsageClock.BlockFor(full, game, start.AddMinutes(2), zone) is { Kind: UsageBlockKind.Schedule, Title: "Estudo" } schedule
            && schedule.Until == end && UsageClock.BlockedUntil(full, null, start.AddMinutes(2), zone) == null,
            "O motivo informa a agenda responsável sem atribuir a pausa ao limite global");
        var daily = UsageClock.Accrue(UsageClock.Configure(list, game, null, 1), "Jogo", null, start, start.AddMinutes(1), zone);
        check(UsageClock.BlockFor(daily, game, start.AddMinutes(1), zone) is { Kind: UsageBlockKind.Item } individual
            && individual.Until == midnight && UsageClock.BlockedUntil(daily, video, start.AddMinutes(1), zone) == end,
            "O prazo individual mais longo prevalece sobre a cota da agenda para aquele app");
        var sharedDay = UsageClock.Accrue(UsageClock.Configure(list, null, null, 1), "Jogo", null, start, start.AddMinutes(1), zone);
        check(UsageClock.BlockFor(sharedDay, video, start.AddMinutes(1), zone) is { Kind: UsageBlockKind.Shared } shared
            && shared.Until == midnight, "O limite global diário continua aplicável depois do fim da agenda");
        var fullFocus = FocusClock.Start(full, start.AddMinutes(2), 50);
        check(UsageClock.BlockFor(fullFocus, game, start.AddMinutes(3), zone) is { Kind: UsageBlockKind.Focus } focus
            && focus.Until == start.AddMinutes(52), "A sessão de foco pode durar mais que a pausa da agenda");

        var immediate = study with { HourMinutes = null };
        var running = FocusClock.Tick(list with { Routines = [immediate] }, start, zone);
        check(running.Active?.Title == study.Name, "A agenda tradicional bloqueia desde o início");
        var shortFocus = FocusClock.Start(list with { Routines = [immediate] }, start.AddMinutes(-1), 5);
        check(UsageClock.BlockFor(shortFocus, game, start.AddMinutes(1), zone) is { Title: "Estudo" } longerFocus
            && longerFocus.Until == end, "O prazo inclui o foco agendado que continua depois da sessão manual");
        var endedEarly = FocusClock.Stop(shortFocus, start.AddMinutes(2), zone);
        check(UsageClock.BlockFor(endedEarly, game, start.AddMinutes(2), zone) == null,
            "Encerrar o foco antecipadamente continua liberando ocorrências tradicionais já em andamento");
        var converted = FocusClock.Tick(running with { Routines = [study] }, start.AddMinutes(1), zone);
        check(converted.Active == null, "Trocar uma agenda em andamento para cota encerra o bloqueio imediato");

        var disabled = UsageClock.Reconcile(full with { Routines = [study with { Enabled = false }] });
        check(UsageClock.BlockedUntil(disabled, game, start.AddMinutes(2), zone) == null,
            "Desativar o agendamento libera os itens mesmo com o saldo esgotado");
        var reenabled = UsageClock.Reconcile(disabled with { Routines = [study] });
        check(UsageClock.BlockedUntil(reenabled, game, start.AddMinutes(3), zone) == end,
            "Reativar na mesma hora conserva o consumo da agenda desativada");
        var renamed = study with { Name = "Outro título", HourMinutes = 2 };
        var updated = UsageClock.Reconcile(full with { Routines = [renamed] });
        check(UsageClock.BlockedUntil(updated, game, start.AddMinutes(2), zone) == null,
            "Aumentar a cota na mesma agenda libera os itens");
        updated = UsageClock.Accrue(updated, "Jogo", null, start.AddMinutes(3), start.AddMinutes(4), zone);
        check(UsageClock.BlockFor(updated, video, start.AddMinutes(4), zone) is { Title: "Outro título" } updatedBlock
            && updatedBlock.Until == end, "Renomear e editar a cota conserva os segundos usados no mesmo ID");
        var outside = UsageClock.Accrue(UsageClock.Configure(full, null, null, 5), "Jogo", null,
            end.AddSeconds(-20), end.AddSeconds(22), zone);
        check(outside.Usage.Shared.DayTicks == TimeSpan.FromSeconds(22).Ticks,
            "O limite global volta a medir no instante em que a janela esgotada termina");
        var inImmediateMode = UsageClock.Reconcile(full with { Routines = [immediate] });
        check(UsageClock.BlockedUntil(inImmediateMode with { Routines = [study] }, game, start.AddMinutes(4), zone) == null,
            "Trocar para bloqueio imediato remove o contador da regra por hora");

        var store = new FocusStore(folder);
        store.Save(gameTime);
        var restored = new FocusStore(folder).Load();
        var resumed = new UsageMeter().Sample(restored, start.AddMinutes(5), TimeSpan.Zero, "Jogo", null, zone);
        check(resumed.Routines.Single().Id == study.Id && resumed.Usage.ScheduleTicks[study.Id] == TimeSpan.FromSeconds(20).Ticks,
            "A agenda e os segundos observados sobrevivem ao reinício sem cobrar o tempo fechado");
        resumed = UsageClock.Accrue(resumed, "Jogo", null, start.AddMinutes(5), start.AddMinutes(5).AddSeconds(40), zone);
        check(UsageClock.BlockedUntil(resumed, game, start.AddMinutes(6), zone) == end,
            "Reabrir na mesma hora retoma a cota pelo mesmo ID");
        store.Save(disabled);
        var restoredDisabled = new FocusStore(folder).Load();
        check(UsageClock.BlockedUntil(restoredDisabled with { Routines = [study] }, game, start.AddMinutes(6), zone) == end,
            "O contador da agenda desativada também é preservado ao fechar e reabrir");
        store.Save(list with { Routines = [study with { HourMinutes = 60 }] });
        store.Save(list with { Routines = [study with { HourMinutes = 61 }] });
        check(new FocusStore(folder).Load().Routines.Single().HourMinutes == 60,
            "O perfil aceita o máximo de 60 e recupera a cópia válida se a cota gravada excede a faixa");
        File.WriteAllText(Path.Combine(folder, "foco.json"), """
            {"Apps":["Jogo"],"Sites":[],"Routines":[{"Name":"Legado","Days":2,"StartMinute":795,"Minutes":30}],"History":[]}
            """);
        var legacy = new FocusStore(folder).Load();
        check(legacy.Routines[0].HourMinutes == null && FocusClock.Tick(legacy, start, zone).Active?.Title == "Legado",
            "Um perfil antigo sem o campo de minutos por hora mantém o bloqueio imediato");
    }
}
