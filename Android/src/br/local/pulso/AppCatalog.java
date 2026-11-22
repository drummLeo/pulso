package br.local.pulso;

import android.content.pm.ApplicationInfo;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.Reader;
import java.text.Collator;
import java.text.Normalizer;
import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.regex.Pattern;

final class AppCatalog {
    private static final Locale PT = new Locale("pt", "BR");
    private static final Pattern ACCENTS = Pattern.compile("\\p{M}+");
    private final Map<String, String> labels = new LinkedHashMap<>();
    private final Map<String, String> packages = new HashMap<>();

    AppCatalog(Reader table) throws IOException {
        BufferedReader lines = new BufferedReader(table);
        String line;
        while ((line = lines.readLine()) != null) {
            if (line.trim().isEmpty() || line.startsWith("#")) continue;
            String[] fields = line.split("\t", -1);
            if (fields.length != 4 || fields[0].isEmpty() || fields[1].isEmpty())
                throw new IOException("Categoria de aplicativos inválida");
            labels.put(fields[0], fields[1]);
            for (String pkg : fields[3].split("\\|")) {
                if (!pkg.isEmpty() && !pkg.equals("-")) packages.put(pkg, fields[0]);
            }
        }
        if (!labels.containsKey("other")) throw new IOException("Categoria padrão ausente");
    }

    static final class Item {
        final String pkg, name, category;
        private final String searchText;
        private Item(String pkg, String name, String category) {
            this.pkg = pkg;
            this.name = name;
            this.category = category;
            searchText = fold(name) + "\n" + fold(pkg);
        }
    }

    Item item(String pkg, String name, int nativeCategory, boolean game) {
        String category = packages.get(pkg);
        if (category == null) category = nativeCategory(nativeCategory, game);
        return new Item(pkg, name, labels.containsKey(category) ? category : "other");
    }

    private static String nativeCategory(int category, boolean game) {
        switch (category) {
            case ApplicationInfo.CATEGORY_GAME: return "games";
            case ApplicationInfo.CATEGORY_AUDIO: return "audio";
            case ApplicationInfo.CATEGORY_VIDEO: return "video";
            case ApplicationInfo.CATEGORY_IMAGE: return "photos";
            case ApplicationInfo.CATEGORY_SOCIAL: return "social";
            case ApplicationInfo.CATEGORY_NEWS: return "reading";
            case ApplicationInfo.CATEGORY_MAPS: return "maps";
            case ApplicationInfo.CATEGORY_PRODUCTIVITY: return "work";
            case ApplicationInfo.CATEGORY_ACCESSIBILITY: return "tools";
            default: return game ? "games" : "other";
        }
    }

    static final class Group {
        final String label;
        final List<Item> items;
        private Group(String label, List<Item> items) {
            this.label = label;
            this.items = items;
        }
    }

    List<Group> groups(Collection<Item> apps) {
        Map<String, List<Item>> byCategory = new HashMap<>();
        for (Item app : apps) byCategory.computeIfAbsent(app.category, key -> new ArrayList<>()).add(app);
        List<Group> groups = new ArrayList<>();
        Collator collator = Collator.getInstance(PT);
        for (Map.Entry<String, String> category : labels.entrySet()) {
            List<Item> items = byCategory.get(category.getKey());
            if (items == null) continue;
            items.sort((a, b) -> {
                int order = collator.compare(a.name, b.name);
                return order != 0 ? order : a.pkg.compareTo(b.pkg);
            });
            groups.add(new Group(category.getValue(), items));
        }
        return groups;
    }

    private static String fold(String text) {
        return ACCENTS.matcher(Normalizer.normalize(text, Normalizer.Form.NFD)).replaceAll("").toLowerCase(PT);
    }

    static List<Group> filter(List<Group> groups, String query) {
        String term = fold(query.trim());
        if (term.isEmpty()) return groups;
        List<Group> found = new ArrayList<>();
        for (Group group : groups) {
            if (fold(group.label).contains(term)) {
                found.add(group);
                continue;
            }
            List<Item> matches = new ArrayList<>();
            for (Item app : group.items) if (app.searchText.contains(term)) matches.add(app);
            if (!matches.isEmpty()) found.add(new Group(group.label, matches));
        }
        return found;
    }

    static final class Selection {
        final Set<String> original, checked;
        Selection(Collection<String> original, Collection<String> checked) {
            this.original = new LinkedHashSet<>(original);
            this.checked = new LinkedHashSet<>(checked);
        }
        void toggle(Item app) {
            if (!checked.remove(app.pkg)) checked.add(app.pkg);
        }
        int count(Group group) {
            int result = 0;
            for (Item app : group.items) if (checked.contains(app.pkg)) result++;
            return result;
        }
        void toggle(Group visible) {
            boolean remove = count(visible) == visible.items.size();
            for (Item app : visible.items) {
                if (remove) checked.remove(app.pkg);
                else checked.add(app.pkg);
            }
        }
        void applyTo(Set<String> saved) {
            for (String pkg : original) if (!checked.contains(pkg)) saved.remove(pkg);
            for (String pkg : checked) if (!original.contains(pkg)) saved.add(pkg);
        }
    }
}
