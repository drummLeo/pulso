package br.local.pulso;

import java.time.Instant;
import java.time.ZoneId;
import java.util.List;

public final class FocusTest {
    static int count;
    static void check(boolean result,String message) {
        if(!result)throw new AssertionError(message);
        count++;System.out.println("OK  "+message);
    }
    static long instant(String iso) { return Instant.parse(iso).toEpochMilli(); }
    static void limits(ZoneId zone) {
        long start=instant("2026-11-23T13:20:00Z");
        Limits.Window window=Limits.Window.at(start,zone);
        Focus f=new Focus();
        f.apps.add("leitor"); f.apps.add("navegador"); f.sites.add("site.test"); f.sites.add("sub.site.test");
        String app=Limits.app("leitor"), site=Limits.site("site.test"), sub=Limits.site("sub.site.test");
        f.limits.set(app,1,0); f.limits.set(site,0,4); f.limits.set(sub,2,0); f.limits.set(Limits.SHARED,2,3);
        List<String> reader=f.targets("leitor",null), web=f.targets("navegador","sub.site.test");
        Limits.Quota common=f.limits.get(Limits.SHARED), individual=f.limits.get(app);
        Limits.consume(f.limits.forTargets(reader),start,start+30000,zone);
        Limits.consume(f.limits.forTargets(web),start+30000,start+45000,zone);
        Limits.consume(f.limits.forTargets(reader),start+45000,start+60000,zone);
        check(common.hourUsed(window)==60000 && common.dayUsed(window)==60000,
            "Duas trocas de alvo e app mais site descontam uma vez do saldo compartilhado");
        check(individual.hourUsed(window)==45000 && f.limits.get(site).dayUsed(window)==15000 && f.limits.get(sub).hourUsed(window)==15000,
            "Cada app e domínio, inclusive um subdomínio listado, tem saldo independente");
        check(f.targets("outro","falso-site.test").isEmpty() && f.limits.forTargets(f.targets("outro",null)).isEmpty(),
            "App sem seleção e domínio parecido ficam fora do rateio");
        Limits.consume(f.limits.forTargets(reader),start+60000,start+130000,zone);
        check(individual.hourUsed(window)==60000 && common.hourUsed(window)==75000
            && f.restriction(reader,window).end==instant("2026-11-23T14:00:00Z"),
            "Esgotar um limite individual interrompe também o consumo compartilhado");
        Limits.consume(f.limits.forTargets(web),start+130000,start+200000,zone);
        check(common.hourUsed(window)==120000 && f.limits.get(site).dayUsed(window)==60000
            && f.restriction(f.targets("navegador",null),window).end==window.hourEnd,
            "O grupo esgotado pausa inclusive itens sem um limite próprio");
        f.limits.set(app,2,0); f.limits.set(Limits.SHARED,3,0);
        check(individual.hourUsed(window)==60000 && common.hourUsed(window)==120000 && common.dayUsed(window)==0
            && f.restriction(reader,window)==null,"Editar preserva uso ativo e desligar limpa apenas o período escolhido");
        f.limits.set(Limits.SHARED,3,1); f.limits.set(app,0,0);
        check(f.limits.get(app)==null && common.dayUsed(window)==0,"Desativar e reativar inicia o período em zero");
        Limits.consume(f.limits.forTargets(reader),start,start+60000,zone);
        check(f.restriction(reader,window).end==instant("2026-11-24T03:00:00Z"),
            "Quando os limites horário e diário acabam juntos, prevalece a meia-noite local");
        f.start(start,25);
        check(f.restriction(reader,window).end==window.dayEnd,
            "O fim da sessão não antecipa a liberação de uma cota diária esgotada");
        f.limits.set(Limits.SHARED,0,0); f.limits.set(app,2,0);
        check(f.restriction(reader,window).end==start+1500000 && f.restriction(f.targets("livre",null),window)==null,
            "A sessão continua protegendo somente os itens da lista sem cota");
        Limits.Tracker tracker=new Limits.Tracker();
        tracker.watch(f,reader,start,1000,zone,Long.MAX_VALUE);
        check(!tracker.finish(start+5000,6000,zone) && f.limits.get(app).hourUsed(window)==0,"Sessão em foco não debita uso");
        f.stop(start+5000,zone);
        tracker.watch(f,reader,start+5000,6000,zone,start+7000);
        tracker.finish(start+10000,11000,zone);
        check(f.limits.get(app).hourUsed(window)==2000,"Uma sessão agendada no meio da observação interrompe o débito ali");
        tracker.watch(f,reader,start+10000,11000,zone,Long.MAX_VALUE);
        tracker.finish(start+13000,14000,zone);
        tracker.finish(start+3600000,17000,zone);
        tracker.watch(f,reader,start+14000,18000,zone,Long.MAX_VALUE);
        tracker.finish(start+1800000,19000,zone);
        tracker.watch(f,reader,start+1800000,20000,zone,Long.MAX_VALUE);
        tracker.finish(start+15000,21000,zone);
        check(f.limits.get(app).hourUsed(window)==5000,"Tela apagada, serviço parado e saltos no relógio não inventam uso");
        long edge=instant("2026-11-24T02:59:57Z");
        f.limits.set(app,1,2);
        Limits.consume(f.limits.forTargets(reader),edge,edge+7000,zone);
        check(f.limits.get(app).hourUsed(Limits.Window.at(edge+7000,zone))==4000
            && f.limits.get(app).dayUsed(Limits.Window.at(edge+7000,zone))==4000,
            "Uso contínuo é dividido exatamente na virada de hora e de dia");
        long hour=instant("2026-11-24T03:59:57Z");
        Limits.consume(f.limits.forTargets(reader),hour,hour+7000,zone);
        check(f.limits.get(app).hourUsed(Limits.Window.at(hour+7000,zone))==4000
            && f.limits.get(app).dayUsed(Limits.Window.at(hour+7000,zone))==11000,"Virar a hora preserva o uso diário");
        ZoneId ny=ZoneId.of("America/New_York");
        Limits.Window first=Limits.Window.at(instant("2026-11-01T05:59:00Z"),ny);
        Limits.Window repeat=Limits.Window.at(instant("2026-11-01T06:00:00Z"),ny);
        check(first.hourEnd==instant("2026-11-01T06:00:00Z") && repeat.hourEnd==instant("2026-11-01T07:00:00Z")
            && first.dayEnd==repeat.dayEnd,"A hora repetida no horário de verão ganha outro saldo com o mesmo dia");
        check(!Limits.valid(61,0) && !Limits.valid(-1,0) && !Limits.valid(0,1441) && Limits.valid(60,1440)
            && Limits.valid(0,0),"Faixas inclusivas de hora e dia e desativação");
    }
    public static void main(String[] args) {
        ZoneId zone=ZoneId.of("America/Sao_Paulo");
        long mon=Instant.parse("2026-11-24T02:50:00Z").toEpochMilli();
        Focus f=new Focus();
        check(f.start(mon,25)&&!f.start(mon,50),"Uma sessão por vez");
        check(f.tick(mon+100000000,zone)&&f.active==null&&f.history.get(0).end==mon+1500000,"Vencimento depois de dormir");
        check(!f.tick(mon+100000000,zone)&&f.history.size()==1,"Conclusão registrada só uma vez");
        check(f.todayMillis(mon+7200000,zone)==900000,"Resumo local dividido na meia-noite");
        f.routines.add(new Focus.Routine("Leitura",1,23*60+45,45));
        long tue=mon+1200000;
        check(f.tick(tue,zone)&&f.active.end==mon+2400000,"Agendamento de segunda continua na terça");
        check(f.stop(tue+60000,zone)&&!f.tick(tue+61000,zone),"Encerrar libera o restante da ocorrência");
        check(f.tick(tue+7*86400000L,zone)&&f.active.scheduled,"Agendamento volta na semana seguinte");
        check("youtube.com".equals(Focus.rule(" https://www.YouTube.com/watch?v=1 ")),"Link normalizado");
        check("xn--caf-dma.example".equals(Focus.rule("café.example")),"Domínio internacional");
        check(Focus.rule("chrome://settings")==null&&Focus.rule("nome@site.test")==null&&Focus.rule("busca palavras")==null,"Busca e endereços internos ficam livres");
        f.sites.add("pulso.test");
        check(f.blocksHost(Focus.host("sub.pulso.test/a"))&&!f.blocksHost("fimpulso.test")&&!f.blocksHost("pulso.test.exemplo.org"),"Correspondência em limites de domínio");
        limits(zone);
        System.out.println(count+" verificações.");
    }
}
