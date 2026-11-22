package br.local.pulso;

import java.io.ByteArrayInputStream;
import java.io.BufferedReader;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.time.Instant;
import java.time.ZoneId;
import java.util.Collections;
import static br.local.pulso.SiteCatalog.Category.*;
import static br.local.pulso.SiteCatalog.Mode.*;

public final class SiteCatalogTest {
    private static void check(boolean ok, String message) {
        if (!ok) throw new AssertionError(message);
        System.out.println("OK  " + message);
    }
    private static ByteArrayInputStream ascii(String value) {
        return new ByteArrayInputStream(value.getBytes(StandardCharsets.US_ASCII));
    }
    private static void bundled(Path directory) throws Exception {
        Path porn = directory.resolve("site_porn.txt"), gambling = directory.resolve("site_gambling.txt");
        SiteCatalog catalog;
        try (InputStream a=Files.newInputStream(porn); InputStream b=Files.newInputStream(gambling)) {
            catalog=new SiteCatalog(a,b);
        }
        for (SiteCatalog.Category category : SiteCatalog.Category.values()) {
            SiteCatalog.Settings settings=new SiteCatalog.Settings();
            settings.set(category,ALWAYS);
            Path table=category==PORN ? porn : gambling;
            int count=0;
            try (BufferedReader reader=Files.newBufferedReader(table,StandardCharsets.US_ASCII)) {
                for (String name; (name=reader.readLine())!=null;) {
                    count++;
                    if (catalog.block(name,settings,0)==null || catalog.block("sub."+name,settings,0)==null)
                        throw new AssertionError("Falha na linha "+count+" da tabela "+category.label);
                }
            }
            check(count>0, category.label+" reconhece todos os "+count+" domínios distribuídos e seus subdomínios");
        }
    }
    public static void main(String[] args) throws Exception {
        SiteCatalog catalog = new SiteCatalog(ascii("adulto.test\nambos.test\nxn--caf-dma.test"),
            ascii("ambos.test\r\naposta.test\r\n"));
        Focus f = new Focus();
        long at = Instant.parse("2026-11-23T15:30:00Z").toEpochMilli();
        ZoneId zone = ZoneId.of("America/Sao_Paulo");
        check(!f.siteModes.active(0) && catalog.block(Focus.host("adulto.test"), f.siteModes, 0) == null,
            "Sem configuração as duas categorias ficam desativadas");
        f.siteModes.set(PORN, ALWAYS);
        f.siteModes.set(GAMBLING, IN_FOCUS);
        for (String address : new String[]{"adulto.test", "HTTPS://A.B.Adulto.Test.:8443/a", "https://café.test/foto",
                "adulto.test/?origem=https://livre.test", "https://adulto.test/minha pasta?pasta=C:\\teste",
                "adulto.test/pagina?" + String.join("",java.util.Collections.nCopies(4500,"a"))}) {
            Limits.Block block = catalog.block(Focus.host(address), f.siteModes, 0);
            check(block != null && block.end == Long.MAX_VALUE && block.reason.contains("Pornografia"),
                "Sempre cobre o domínio, subdomínios e IDN sem horário de liberação");
        }
        for (String address : new String[]{"falsoadulto.test", "adulto.test.livre.test",
                "livre.test/adulto.test?aposta.test", "procurar adulto.test", "chrome://adulto.test", "aposta.test"}) {
            check(catalog.block(Focus.host(address), f.siteModes, 0) == null,
                "Outro domínio, busca, endereço interno e categoria fora do foco permanecem livres");
        }
        f.start(at, 10);
        long quickEnd = f.focusForSites(at,zone);
        check(quickEnd == at + 600000 && catalog.block("aposta.test", f.siteModes, quickEnd).end == quickEnd,
            "Durante o foco segue o tempo da sessão rápida");
        check(catalog.block("ambos.test", f.siteModes, quickEnd).end == Long.MAX_VALUE,
            "Um domínio presente nas duas categorias respeita Sempre");
        f.stop(at,zone);
        check(f.focusForSites(at,zone) == 0, "Encerrar a sessão rápida libera a categoria");
        f.siteModes.set(PORN, OFF);
        check(!f.siteModes.active(0) && f.siteModes.active(at+1000), "Apenas o período de foco observa a categoria restante");

        Focus.Routine immediate = new Focus.Routine("Imediato", 1, 12*60, 45);
        Focus.Routine allowance = new Focus.Routine("Com saldo", 1, 12*60, 60);
        allowance.setHourLimit(3);
        f.routines.add(immediate); f.routines.add(allowance); f.tick(at,zone);
        long routineEnd = at + 1800000;
        check(f.focusForSites(at,zone) == routineEnd, "Agendamentos sobrepostos cobrem a categoria até o mais longo");
        f.stop(at,zone); f.tick(at,zone);
        check(f.active == null && f.focusForSites(at,zone) == routineEnd,
            "Cancelar a ocorrência imediata mantém a proteção do horário com saldo");
        Limits.Window window = Limits.Window.at(at,zone);
        Limits.Block block = catalog.block("sub.aposta.test", f.siteModes, f.focusForSites(at,zone));
        f.sites.add("manual.test"); f.limits.set(Limits.SHARED, 5, 10);
        check(block != null && block.end == routineEnd && block.reason.contains("Apostas")
            && f.targets("browser", "sub.aposta.test").isEmpty()
            && f.limits.forTargets(f.targets("browser","sub.aposta.test")).isEmpty()
            && allowance.quota.hourUsed(window) == 0 && f.restriction(f.targets("browser", "manual.test"),window) == null,
            "A categoria bloqueia com saldo positivo sem virar alvo nem cobrar do site manual");
        Limits.consume(Collections.singletonList(allowance.quota),at-180000,at,zone);
        check(f.focusForSites(at,zone) == routineEnd && f.restriction(f.targets("browser","manual.test"),window) != null,
            "O saldo esgotado continua aplicando a regra manual independentemente da categoria");
        allowance.enabled = false; f.tick(at,zone);
        check(f.focusForSites(at,zone) == 0, "Desativar o último horário libera a categoria sem reabrir a ocorrência cancelada");
        allowance.enabled = true;
        check(f.focusForSites(routineEnd,zone) == 0, "A categoria volta a ficar livre no fim exato do agendamento");
        if (args.length>0) bundled(Paths.get(args[0]));
    }
}
