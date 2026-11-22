package br.local.pulso;

import android.content.pm.ApplicationInfo;
import java.io.StringReader;
import java.util.Arrays;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

public final class AppCatalogTest {
    private static final String TABLE =
        "# id, nome, Windows, Android\n" +
        "social\tRedes sociais\tprograma\texemplo.Rede|exemplo.outra\n" +
        "games\tJogos\t-\t-\n" +
        "audio\tMúsica e áudio\t-\t-\n" +
        "video\tVídeo\t-\t-\n" +
        "photos\tFotos\t-\t-\n" +
        "reading\tLeitura\t-\t-\n" +
        "maps\tMapas\t-\t-\n" +
        "work\tTrabalho\t-\t-\n" +
        "tools\tFerramentas\t-\t-\n" +
        "other\tOutros\t-\t-\n";

    private static void check(boolean ok, String message) {
        if (!ok) throw new AssertionError(message);
        System.out.println("OK  " + message);
    }

    public static void main(String[] args) throws Exception {
        AppCatalog catalog = new AppCatalog(new StringReader(TABLE));
        check(catalog.item("exemplo.Rede", "Rede", ApplicationInfo.CATEGORY_GAME, true).category.equals("social")
            && catalog.item("exemplo.outra", "Rede", -1, false).category.equals("social"),
            "Pacotes exatos da tabela têm precedência sobre a categoria do sistema");
        String[] similar = {"exemplo.rede", "exemplo.Rede.beta", "programa", "-"};
        for (String pkg : similar) {
            check(catalog.item(pkg, "Rede", -1, false).category.equals("other"),
                "Nomes, prefixos e aliases de outra plataforma não identificam o pacote " + pkg);
        }
        int[] nativeCategories = {ApplicationInfo.CATEGORY_GAME, ApplicationInfo.CATEGORY_AUDIO,
            ApplicationInfo.CATEGORY_VIDEO, ApplicationInfo.CATEGORY_IMAGE, ApplicationInfo.CATEGORY_SOCIAL,
            ApplicationInfo.CATEGORY_NEWS, ApplicationInfo.CATEGORY_MAPS, ApplicationInfo.CATEGORY_PRODUCTIVITY,
            ApplicationInfo.CATEGORY_ACCESSIBILITY, 999};
        String[] expected = {"games", "audio", "video", "photos", "social", "reading", "maps", "work", "tools", "other"};
        for (int i = 0; i < nativeCategories.length; i++) {
            check(catalog.item("exemplo.novo", "Novo", nativeCategories[i], false).category.equals(expected[i]),
                "Categoria declarada " + nativeCategories[i] + " reconhecida como " + expected[i]);
        }
        check(catalog.item("exemplo.antigo", "Jogo", -1, true).category.equals("games"),
            "Jogos antigos que só informam a marcação de jogo entram em Jogos");

        AppCatalog.Item beta = catalog.item("exemplo.Rede", "Zebra", -1, false);
        AppCatalog.Item alpha = catalog.item("exemplo.outra", "Árvore", -1, false);
        AppCatalog.Item music = catalog.item("exemplo.radio", "Rádio", ApplicationInfo.CATEGORY_AUDIO, false);
        AppCatalog.Item missing = catalog.item("exemplo.ausente", "exemplo.ausente", -1, false);
        List<AppCatalog.Group> groups = catalog.groups(Arrays.asList(missing, music, beta, alpha));
        check(groups.size() == 3 && groups.get(0).label.equals("Redes sociais")
            && groups.get(0).items.equals(Arrays.asList(alpha, beta))
            && groups.get(1).label.equals("Música e áudio") && groups.get(2).items.get(0) == missing,
            "Categorias seguem a ordem do arquivo e os aplicativos a ordenação portuguesa");
        List<AppCatalog.Group> filtered = AppCatalog.filter(groups, "  ARV  ");
        check(filtered.size() == 1 && filtered.get(0).items.equals(Arrays.asList(alpha)),
            "Busca por nome ignora caixa, espaços externos e acentos");
        check(AppCatalog.filter(groups, "EXEMPLO.RAD").get(0).items.equals(Arrays.asList(music))
            && AppCatalog.filter(groups, "MUSICA").get(0).items.equals(Arrays.asList(music))
            && AppCatalog.filter(groups, "inexistente").isEmpty(),
            "Busca encontra pacotes e categorias e permite zero resultados");

        Set<String> saved = new LinkedHashSet<>(Arrays.asList(beta.pkg, music.pkg, missing.pkg));
        AppCatalog.Selection whole = new AppCatalog.Selection(saved, saved);
        whole.toggle(groups.get(0));
        check(whole.checked.contains(alpha.pkg) && whole.checked.contains(beta.pkg),
            "Marcar uma categoria parcialmente selecionada inclui todos os seus apps");
        whole.toggle(groups.get(0));
        check(whole.checked.equals(new LinkedHashSet<>(Arrays.asList(music.pkg, missing.pkg))),
            "Desmarcar a categoria inteira conserva as escolhas dos outros grupos");
        AppCatalog.Selection draft = new AppCatalog.Selection(saved, saved);
        draft.toggle(filtered.get(0));
        draft.toggle(groups.get(1));
        check(draft.checked.equals(new LinkedHashSet<>(Arrays.asList(alpha.pkg, beta.pkg, missing.pkg)))
            && draft.count(groups.get(0)) == 2 && saved.contains(music.pkg) && !saved.contains(alpha.pkg),
            "Marcar só os resultados visíveis conserva outros grupos, pacotes ausentes e a lista salva");
        AppCatalog.Selection restored = new AppCatalog.Selection(draft.original, draft.checked);
        restored.toggle(filtered.get(0));
        check(restored.checked.contains(beta.pkg) && !restored.checked.contains(alpha.pkg),
            "Desmarcar uma categoria filtrada após recriar preserva apps ocultos da mesma categoria");
        restored.toggle(alpha);
        saved.add("exemplo.em.outra.janela");
        restored.applyTo(saved);
        check(saved.equals(new LinkedHashSet<>(Arrays.asList(alpha.pkg, beta.pkg, missing.pkg, "exemplo.em.outra.janela"))),
            "Salvar aplica apenas as mudanças individuais e de categoria feitas neste diálogo");
    }
}
