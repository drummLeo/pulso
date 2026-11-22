using Pulso;

return TestProcess.Run(RunChecks);

static int RunChecks()
{
    var passed = 0;
    void Check(bool result, string name)
    {
        if (!result) throw new InvalidOperationException(name);
        Console.WriteLine("OK  " + name);
        passed++;
    }
    var mon = new DateTimeOffset(2026, 11, 23, 23, 50, 0, TimeSpan.Zero);
    var tz = TimeZoneInfo.Utc;
    var fresh = new FocusData();
    var started = FocusClock.Start(fresh, mon, 25);
    Check(started.Active?.End == mon.AddMinutes(25), "Sessão manual tem prazo absoluto");
    var done = FocusClock.Tick(started, mon.AddDays(1), tz);
    Check(done.Active == null && done.History.Single().End == mon.AddMinutes(25), "Retomada depois do prazo registra somente a duração programada");
    Check(ReferenceEquals(done, FocusClock.Tick(done, mon.AddDays(1), tz)), "Conclusão é idempotente");
    var night = new Routine { Name = "Leitura", Days = 1 << (int)DayOfWeek.Monday, StartMinute = 23 * 60 + 45, Minutes = 45 };
    var withRoutine = fresh with { Routines = [night] };
    var tue = mon.AddMinutes(20);
    var overnight = FocusClock.Tick(withRoutine, tue, tz);
    Check(overnight.Active?.Title == "Leitura" && overnight.Active.Start == tue && overnight.Active.End == mon.AddMinutes(40), "Janela de segunda-feira continua na madrugada de terça");
    var stopped = FocusClock.Stop(overnight, tue.AddMinutes(2), tz);
    Check(FocusClock.Tick(stopped, tue.AddMinutes(3), tz).Active == null, "Encerrar uma ocorrência mantém o restante do intervalo livre");
    Check(FocusClock.Tick(stopped, tue.AddDays(7), tz).Active?.Title == "Leitura", "Agendamento volta na semana seguinte");
    var overlap = FocusClock.Start(withRoutine, mon, 10);
    var transition = FocusClock.Tick(overlap, mon.AddMinutes(10), tz);
    Check(transition.Active?.Title == "Leitura" && transition.History.Length == 1, "Intervalo agendado continua depois de uma sessão manual sobreposta");
    var dayFocus = FocusClock.Today(done, mon.AddHours(2), tz);
    Check(dayFocus.TotalMinutes == 15, "Resumo diário divide uma sessão na virada do dia");
    Check(Targets.Domain(" https://www.YOUTUBE.com/watch?v=1 ") == "youtube.com", "URL colada inclui domínio e subdomínios");
    Check(Targets.Domain("café.example") == "xn--caf-dma.example", "Domínio internacional é convertido para o navegador");
    Check(Targets.Domain("file:///c:/nota.txt") == null && Targets.Domain("dois sites") == null && Targets.Domain("https://nome@exemplo.com") == null, "Entradas inválidas não viram regras");
    Check(Targets.App("Discord.exe") == "Discord" && Targets.App("explorer.exe") == null && Targets.App("Pulso.exe") == null, "Programas escolhidos e componentes do Windows são diferenciados");
    var clock = new DateTimeOffset(2026, 11, 23, 13, 15, 0, TimeSpan.Zero);
    var listed = fresh with { Apps = ["Chrome", "Jogo"], Sites = ["video.test", "canal.video.test", "outro.test"] };
    var chrome = UsageClock.AppKey("chrome");
    var game = UsageClock.AppKey("Jogo");
    var video = UsageClock.SiteKey("video.test");
    var channel = UsageClock.SiteKey("canal.video.test");
    long Seconds(int value) => TimeSpan.FromSeconds(value).Ticks;
    var budgets = UsageClock.Configure(listed, null, 2, 10);
    budgets = UsageClock.Configure(budgets, chrome, null, 8);
    budgets = UsageClock.Configure(budgets, video, 1, 5);
    budgets = UsageClock.Configure(budgets, channel, null, 7);
    var watched = UsageClock.WatchedSites(budgets);
    Check(watched.ToHashSet().SetEquals(listed.Sites), "O saldo compartilhado monitora toda a lista, inclusive sites sem limite próprio");
    var charged = UsageClock.Accrue(budgets, "CHROME", "a.canal.video.test", clock, clock.AddSeconds(75), tz);
    Check(charged.Usage.Shared.HourTicks == Seconds(60) && charged.Usage.Shared.DayTicks == Seconds(60)
        && charged.Usage.Items[chrome].DayTicks == Seconds(60) && charged.Usage.Items[video].HourTicks == Seconds(60)
        && charged.Usage.Items[channel].DayTicks == Seconds(60), "Navegador e domínios sobrepostos usam um só saldo compartilhado e param juntos no primeiro limite");
    var firstHourEnd = new DateTimeOffset(2026, 11, 23, 14, 0, 0, TimeSpan.Zero);
    Check(UsageClock.BlockedUntil(charged, channel, clock.AddMinutes(2), tz) == firstHourEnd,
        "Subdomínio herda o horário de liberação do domínio principal");
    var other = UsageClock.Accrue(charged, "Jogo", null, clock.AddMinutes(2), clock.AddMinutes(4), tz);
    Check(other.Usage.Shared.HourTicks == Seconds(120)
        && UsageClock.BlockedUntil(other, game, clock.AddMinutes(4), tz) == firstHourEnd
        && UsageClock.BlockedUntil(other, UsageClock.SiteKey("outro.test"), clock.AddMinutes(4), tz) == firstHourEnd,
        "Um segundo aplicativo pode esgotar o saldo que pausa todos os itens");
    var afterHour = UsageClock.Accrue(other, "Jogo", null, firstHourEnd.AddSeconds(-15), firstHourEnd.AddSeconds(15), tz);
    Check(afterHour.Usage.Shared.HourTicks == Seconds(15) && afterHour.Usage.Shared.DayTicks == Seconds(135),
        "A virada da hora libera o uso a partir do minuto zero e conserva o uso do dia");
    var midnight = new DateTimeOffset(2026, 11, 24, 3, 0, 0, TimeSpan.Zero);
    var sao = TimeZoneInfo.CreateCustomTimeZone("Teste -03", TimeSpan.FromHours(-3), "Teste -03", "Teste -03");
    var dayBudget = UsageClock.Configure(listed, null, 1, 1);
    var beforeMidnight = UsageClock.Accrue(dayBudget, "Jogo", null, midnight.AddHours(-2), midnight.AddHours(-2).AddMinutes(1), sao);
    Check(UsageClock.BlockedUntil(beforeMidnight, game, midnight.AddHours(-1.5), sao) == midnight,
        "Se hora e dia esgotam, a pausa dura até a meia-noite local");
    var afterMidnight = UsageClock.Accrue(beforeMidnight, "Jogo", null, midnight.AddSeconds(-20), midnight.AddSeconds(20), sao);
    Check(afterMidnight.Usage.Shared.HourTicks == Seconds(20) && afterMidnight.Usage.Shared.DayTicks == Seconds(20),
        "A virada do dia separa o tempo permitido dos dois lados da meia-noite");
    var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    var fall = new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
    var repeatedHour = UsageClock.Accrue(UsageClock.Configure(listed, game, 1, 10), "Jogo", null, fall.AddMinutes(-2), fall.AddSeconds(-1), eastern);
    Check(UsageClock.BlockedUntil(repeatedHour, game, fall.AddSeconds(-1), eastern) == fall,
        "A primeira ocorrência da hora repetida termina na mudança de fuso");
    repeatedHour = UsageClock.Accrue(repeatedHour, "Jogo", null, fall.AddSeconds(-1), fall.AddSeconds(12), eastern);
    Check(repeatedHour.Usage.Items[game] is { } repeat && repeat.HourTicks == Seconds(12) && repeat.DayTicks == Seconds(72),
        "A hora repetida no fim do horário de verão abre outro saldo e preserva o dia");
    var spring = new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero);
    var springDay = UsageClock.Accrue(UsageClock.Configure(listed, game, null, 1), "Jogo", null, spring, spring.AddMinutes(1), eastern);
    Check(UsageClock.BlockedUntil(springDay, game, spring.AddMinutes(1), eastern) == new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero),
        "A liberação diária segue a meia-noite local mesmo quando o dia tem 23 horas");
    var edited = UsageClock.Configure(charged, video, 3, null);
    Check(edited.Usage.Items[video].HourTicks == Seconds(60) && edited.Usage.Items[video].DayTicks == 0
        && UsageClock.BlockedUntil(edited, channel, clock.AddMinutes(2), tz) == null,
        "Aumentar o limite libera o item, mantém o uso da hora e zera apenas o período desativado");
    var restartedDay = UsageClock.Configure(edited, video, 3, 1);
    Check(restartedDay.Usage.Items[video].DayTicks == 0 && restartedDay.Usage.Items[video].HourTicks == Seconds(60),
        "Reativar o limite diário começa do zero e não reembolsa o saldo horário");
    var lowered = UsageClock.Configure(UsageClock.Accrue(edited, "chrome", "video.test", clock.AddMinutes(2), clock.AddMinutes(2.5), tz), video, 1, null);
    Check(lowered.Usage.Items[video].HourTicks == Seconds(90) && UsageClock.BlockedUntil(lowered, video, clock.AddMinutes(3), tz) == firstHourEnd,
        "Reduzir o limite preserva o consumo já realizado e pausa de imediato");
    var individual = UsageClock.Configure(budgets, null, null, null);
    Check(UsageClock.WatchedSites(individual).ToHashSet().SetEquals(["video.test", "canal.video.test"])
        && UsageClock.BlockedUntil(charged, game, clock.AddMinutes(2), tz) == null,
        "Limites próprios não pausam aplicativos ou sites alheios");
    var duringFocus = budgets with { Active = new("Pausa", clock.AddSeconds(5), clock.AddSeconds(15), false) };
    var focusOverlap = UsageClock.Accrue(duringFocus, "Jogo", null, clock, clock.AddSeconds(20), tz);
    Check(focusOverlap.Usage.Shared.DayTicks == Seconds(10), "A sessão de foco suspende o débito exatamente durante sua vigência");
    var unknown = UsageClock.Accrue(budgets, "Editor", null, clock, clock.AddSeconds(20), tz);
    Check(unknown.Usage.Shared.DayTicks == 0, "Usar um programa fora da lista não consome o saldo");
    Check(UsageClock.BlockedUntil(other, UsageClock.AppKey("Editor"), clock.AddMinutes(4), tz) == null
        && UsageClock.BlockedUntil(other, UsageClock.SiteKey("livre.test"), clock.AddMinutes(4), tz) == null,
        "O saldo compartilhado esgotado só restringe os itens da lista");
    var meter = new UsageMeter();
    FocusData Sample(FocusData state, int second, string? process) =>
        meter.Sample(state, clock.AddSeconds(second), TimeSpan.FromSeconds(second), process, null, tz);
    var sampled = Sample(budgets, 0, "Jogo");
    sampled = Sample(sampled, 1, "Jogo");
    sampled = Sample(sampled, 2, null);
    sampled = Sample(sampled, 3, "Jogo");
    sampled = Sample(sampled, 80, "Jogo");
    sampled = Sample(sampled, 81, "Jogo");
    meter.Reset();
    sampled = Sample(sampled, 82, "Jogo");
    sampled = Sample(sampled, 83, "Jogo");
    Check(sampled.Usage.Shared.DayTicks == Seconds(3), "Foco perdido, tela desligada, retomada e lacunas do monitor não viram uso");
    sampled = meter.Sample(sampled, clock.AddMinutes(10), TimeSpan.FromSeconds(84), "Jogo", null, tz);
    Check(sampled.Usage.Shared.DayTicks == Seconds(3), "Um ajuste no relógio civil não vira tempo de uso");
    var validation = new (int? Hour, int? Day)[] { (0, null), (61, 1), (1, 0), (null, 1441) };
    foreach (var (hour, day) in validation)
    {
        bool rejected = false;
        try { UsageClock.Configure(listed, null, hour, day); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, $"Faixa inválida é rejeitada ({hour}, {day})");
    }
    var maximum = UsageClock.Configure(listed, null, 60, 1440);
    Check(maximum.Usage.Shared.HourMinutes == 60 && maximum.Usage.Shared.DayMinutes == 1440, "Os máximos de hora e dia são aceitos");
    var folder = Directory.CreateTempSubdirectory("pulso-core-").FullName;
    try
    {
        var store = new FocusStore(folder);
        store.Save(started);
        store.Save(done);
        Check(new FocusStore(folder).Load().History.Single().Title == "Foco rápido", "Histórico sobrevive ao reinício");
        File.WriteAllText(Path.Combine(folder, "foco.json"), "{");
        var recovered = new FocusStore(folder);
        Check(recovered.Load().Active == started.Active, "Cópia anterior recupera um arquivo incompleto");
        recovered.Save(done);
        Check(new FocusStore(folder).Load().History.Length == 1, "Estado recuperado pode ser salvo novamente");
        store.Save(charged);
        var reloaded = new FocusStore(folder).Load();
        var resumed = new UsageMeter().Sample(reloaded, clock.AddMinutes(5), TimeSpan.Zero, "Jogo", null, tz);
        Check(resumed.Usage.Shared.HourTicks == Seconds(60) && resumed.Usage.Items[video] == charged.Usage.Items[video],
            "Reabrir mantém contadores e limites sem lançar o intervalo fechado");
        store.Save(charged with { Sites = ["outro.test"] });
        Check(!new FocusStore(folder).Load().Usage.Items.ContainsKey(video), "A remoção de um item elimina seu limite próprio");
        File.WriteAllText(Path.Combine(folder, "foco.json"), """{"Minutes":35,"Apps":["Jogo"],"Sites":["www.video.test"],"Routines":[],"History":[]}""");
        var legacy = new FocusStore(folder).Load();
        Check(legacy.Minutes == 35 && legacy.Sites.SequenceEqual(["video.test"]) && legacy.Apps.SequenceEqual(["Jogo"])
            && !legacy.Usage.Shared.Enabled && legacy.Usage.Items.Count == 0, "Perfil da versão 1 preserva a lista e começa com os limites desativados");
        ScheduledUsageChecks.Run(Check, folder);
        SiteFilterChecks.Run(Check, folder);
    }
    finally
    {
        var temp = new DirectoryInfo(folder);
        if (string.Equals(temp.Parent?.FullName.TrimEnd('\\'), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            && temp.Name.StartsWith("pulso-core-", StringComparison.Ordinal)) temp.Delete(true);
    }
    Console.WriteLine($"{passed} verificações concluídas.");
    return 0;
}
