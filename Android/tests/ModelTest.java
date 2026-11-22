package br.local.pulso;

import org.json.JSONArray;
import org.json.JSONObject;
import java.time.Instant;
import java.time.ZoneId;
import java.util.Arrays;

/** JSON round trips use only synthetic package names and domains. */
public final class ModelTest {
    private static void check(boolean ok,String message) {
        if (!ok) throw new AssertionError(message);
        System.out.println("OK  "+message);
    }
    public static void main(String[] args) throws Exception {
        ZoneId zone=ZoneId.of("America/Sao_Paulo");
        long now=Instant.parse("2026-11-23T15:30:00Z").toEpochMilli();
        Limits.Window window=Limits.Window.at(now,zone);
        JSONObject legacy=new JSONObject().put("versao",2).put("minutos",20)
            .put("apps",new JSONArray().put("exemplo.leitor")).put("sites",new JSONArray())
            .put("limites",new JSONObject().put("lista",new JSONArray().put(3).put(0)))
            .put("agenda",new JSONArray().put(new JSONArray().put("Leitura").put(1).put(12*60).put(60).put(true).put(Long.MIN_VALUE)))
            .put("historico",new JSONArray());
        Focus f=Model.decode(legacy.toString());
        check(f!=null && f.minutes==20 && f.limits.get("lista").hourMinutes==3
            && f.tick(now,zone) && f.active.scheduled,"Agenda gravada na versão anterior mantém o bloqueio imediato e as cotas globais");
        f.stop(now,zone);
        Focus.Routine first=f.routines.get(0);
        first.setHourLimit(1);
        Focus.Routine second=new Focus.Routine("Leitura",1,12*60,60);
        second.setHourLimit(2); f.routines.add(second);
        Limits.consume(Arrays.asList(first.quota,second.quota),now,now+60000,zone);
        Limits.consume(Arrays.asList(second.quota),now,now+8000,zone);
        second.name="Outro nome";
        f.routines.remove(first); f.routines.add(first);
        Focus restored=Model.decode(Model.encode(f).toString());
        check(restored!=null && Model.restoreUsage(restored,Model.usage(f).toString()),"Configuração e saldos de uso reabrem após gravar");
        Focus.Routine a=restored.routines.get(0), b=restored.routines.get(1);
        check(!a.id.equals(b.id) && a.id.equals(second.id) && b.id.equals(first.id)
            && a.quota.hourMinutes==2 && a.quota.hourUsed(window)==68000
            && b.quota.hourMinutes==1 && b.quota.hourUsed(window)==60000
            && restored.restriction(restored.targets("exemplo.leitor",null),window).reason.contains("Leitura"),
            "Agendas com nomes iguais mantêm saldos e bloqueio próprios após renomear, reordenar e reabrir");
        check(!Model.restoreUsage(restored,"{") && Model.decode("{")==null,"Dados corrompidos permitem recorrer à cópia anterior");
        JSONObject profile14=new JSONObject().put("versao",3).put("minutos",18)
            .put("apps",new JSONArray().put("exemplo.leitor")).put("sites",new JSONArray().put("manual.test"))
            .put("limites",new JSONObject().put("site:manual.test",new JSONArray().put(7).put(15)))
            .put("agenda",new JSONArray().put(new JSONArray().put("Leitura").put(1).put(720).put(60).put(true).put(-1).put("abc").put(3)))
            .put("historico",new JSONArray().put(new JSONArray().put("Anterior").put(now-3000).put(now).put(true)))
            .put("sessao",new JSONArray().put("Foco rápido").put(now).put(now+60000).put(false));
        Focus migrated=Model.decode(profile14.toString());
        check(migrated!=null && migrated.siteModes.get(SiteCatalog.Category.PORN)==SiteCatalog.Mode.OFF
            && migrated.siteModes.get(SiteCatalog.Category.GAMBLING)==SiteCatalog.Mode.OFF,"Perfil 1.4 começa com os dois bloqueios desligados");
        migrated.siteModes.set(SiteCatalog.Category.PORN,SiteCatalog.Mode.IN_FOCUS);
        migrated.siteModes.set(SiteCatalog.Category.GAMBLING,SiteCatalog.Mode.ALWAYS);
        JSONObject profile15=Model.encode(migrated);
        Focus reopened=Model.decode(profile15.toString());
        check(reopened.siteModes.get(SiteCatalog.Category.PORN)==SiteCatalog.Mode.IN_FOCUS
            && reopened.siteModes.get(SiteCatalog.Category.GAMBLING)==SiteCatalog.Mode.ALWAYS
            && reopened.minutes==18 && reopened.apps.contains("exemplo.leitor") && reopened.sites.contains("manual.test")
            && reopened.limits.get("site:manual.test").dayMinutes==15 && reopened.routines.get(0).quota.hourMinutes==3
            && reopened.active.end==now+60000 && reopened.history.get(0).title.equals("Anterior"),
            "Gravar os modos independentes conserva a lista, os limites, a duração, o horário, a sessão e o histórico de 1.4");
        profile15.getJSONObject("categoriasSites").put("apostas","invalido");
        reopened=Model.decode(profile15.toString());
        check(reopened.sites.contains("manual.test") && reopened.siteModes.get(SiteCatalog.Category.GAMBLING)==SiteCatalog.Mode.OFF
            && reopened.siteModes.get(SiteCatalog.Category.PORN)==SiteCatalog.Mode.IN_FOCUS,
            "Um modo inválido desativa só a categoria afetada");
    }
}
