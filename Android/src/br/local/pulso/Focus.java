package br.local.pulso;

import java.net.IDN;
import java.net.URI;
import java.time.Instant;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/** All timestamps are wall-clock milliseconds; repeat rules use local civil time. */
final class Focus {
    static final int MAX_MINUTES = 720;
    static final int MAX_HISTORY = 2000;
    int minutes = 25;
    final Set<String> apps = new LinkedHashSet<>();
    final Set<String> sites = new LinkedHashSet<>();
    final Limits limits = new Limits();
    final List<Routine> routines = new ArrayList<>();
    final List<Entry> history = new ArrayList<>();
    Session active;

    static final class Routine {
        String name;
        int days, startMinute, minutes;
        boolean enabled = true;
        long lastDay = Long.MIN_VALUE;
        Routine(String name, int days, int startMinute, int minutes) {
            this.name = name; this.days = days; this.startMinute = startMinute; this.minutes = minutes;
        }
    }
    static final class Session {
        final String title;
        final long start, end;
        final boolean scheduled;
        Session(String title, long start, long end, boolean scheduled) {
            this.title = title; this.start = start; this.end = end; this.scheduled = scheduled;
        }
    }
    static final class Entry {
        final String title;
        final long start, end;
        final boolean completed;
        Entry(String title, long start, long end, boolean completed) {
            this.title = title; this.start = start; this.end = end; this.completed = completed;
        }
    }
    static final class Slot {
        final Routine routine;
        final long day, start, end;
        Slot(Routine routine, LocalDate day, long start) {
            this.routine = routine; this.day = day.toEpochDay(); this.start = start;
            this.end = start + routine.minutes * 60000L;
        }
    }
    static boolean validMinutes(int n) { return n >= 1 && n <= MAX_MINUTES; }
    boolean start(long now, int duration) {
        if (active != null || !validMinutes(duration)) return false;
        minutes = duration;
        active = new Session("Foco rápido", now, now + duration * 60000L, false);
        return true;
    }
    private static Slot slot(Routine r, LocalDate day, ZoneId zone) {
        if (!r.enabled || (r.days & (1 << (day.getDayOfWeek().getValue() - 1))) == 0) return null;
        LocalDateTime time = day.atStartOfDay().plusMinutes(r.startMinute);
        List<ZoneOffset> offsets = zone.getRules().getValidOffsets(time);
        if (offsets.isEmpty()) return null;
        return new Slot(r, day, time.toInstant(offsets.get(0)).toEpochMilli());
    }
    private static LocalDate localDay(long now, ZoneId zone) {
        return Instant.ofEpochMilli(now).atZone(zone).toLocalDate();
    }
    boolean tick(long now, ZoneId zone) {
        boolean changed = false;
        if (active != null && active.end <= now) { finish(active.end, true); changed = true; }
        if (active != null || routines.isEmpty()) return changed;
        LocalDate today = localDay(now, zone);
        Slot due = null;
        for (Routine r : routines) {
            for (int offset = -1; offset <= 0; offset++) {
                Slot w = slot(r, today.plusDays(offset), zone);
                if (w != null && w.day != r.lastDay && w.start <= now && now < w.end && (due == null || w.start < due.start)) due = w;
            }
        }
        if (due == null) return changed;
        due.routine.lastDay = due.day;
        active = new Session(due.routine.name, now, due.end, true);
        return true;
    }
    boolean stop(long now, ZoneId zone) {
        if (active == null) return false;
        LocalDate today = localDay(now, zone);
        for (Routine r : routines) {
            for (int offset = -1; offset <= 0; offset++) {
                Slot w = slot(r, today.plusDays(offset), zone);
                if (w != null && w.start <= now && now < w.end) r.lastDay = w.day;
            }
        }
        finish(now, now >= active.end);
        return true;
    }
    private void finish(long now, boolean completed) {
        long end = Math.max(active.start, Math.min(active.end, now));
        history.add(0, new Entry(active.title, active.start, end, completed));
        if (history.size() > MAX_HISTORY) history.remove(history.size() - 1);
        active = null;
    }
    Slot next(long now, ZoneId zone) {
        if (routines.isEmpty()) return null;
        LocalDate today = localDay(now, zone);
        Slot next = null;
        for (Routine r : routines) {
            for (int offset = 0; offset <= 7; offset++) {
                Slot w = slot(r, today.plusDays(offset), zone);
                if (w != null && w.start > now && w.day != r.lastDay && (next == null || w.start < next.start)) next = w;
            }
        }
        return next;
    }
    long todayMillis(long now, ZoneId zone) {
        long dayStart = localDay(now, zone).atStartOfDay(zone).toInstant().toEpochMilli();
        long total = 0;
        for (Entry e : history) total += overlap(e.start, e.end, dayStart, now);
        if (active != null) total += overlap(active.start, active.end, dayStart, now);
        return total;
    }
    private static long overlap(long a, long b, long low, long high) {
        return Math.max(0L, Math.min(b, high) - Math.max(a, low));
    }
    /** Accept a domain or an HTTP(S) URL. Never interpret a search term as a site. */
    static String host(String input) {
        if (input == null) return null;
        String text = input.trim();
        if (text.isEmpty() || text.length() > 4096 || text.indexOf('\\') >= 0) return null;
        try {
            URI uri = new URI(text.contains("://") ? text : "https://" + text);
            String scheme = uri.getScheme(), authority = uri.getRawAuthority();
            if (!("https".equalsIgnoreCase(scheme) || "http".equalsIgnoreCase(scheme))
                    || authority == null || authority.contains("@") || authority.contains("%")) return null;
            int colon = authority.lastIndexOf(':');
            if (colon >= 0) {
                if (!authority.substring(colon + 1).matches("[0-9]{1,5}")) return null;
                if (Integer.parseInt(authority.substring(colon + 1)) > 65535) return null;
                authority = authority.substring(0, colon);
            }
            if (authority.endsWith(".")) authority = authority.substring(0, authority.length() - 1);
            String domain = IDN.toASCII(authority, IDN.USE_STD3_ASCII_RULES).toLowerCase(Locale.ROOT);
            if (domain.length() > 253 || domain.indexOf('.') < 1) return null;
            for (String label : domain.split("\\.", -1)) if (label.isEmpty() || label.length() > 63) return null;
            return domain;
        } catch (IllegalArgumentException | java.net.URISyntaxException invalid) { return null; }
    }
    static String rule(String text) {
        String name = host(text);
        if (name != null && name.startsWith("www.") && name.substring(4).contains(".")) return name.substring(4);
        return name;
    }
    boolean blocksHost(String host) {
        if (host == null) return false;
        for (String domain : sites) if (matches(host,domain)) return true;
        return false;
    }
    private static boolean matches(String host,String domain) {
        return host.equals(domain) || host.endsWith("." + domain);
    }
    List<String> targets(String pkg,String host) {
        List<String> result = new ArrayList<>();
        if (apps.contains(pkg)) result.add(Limits.app(pkg));
        if (host != null) for (String domain : sites)
            if (matches(host,domain)) result.add(Limits.site(domain));
        return result;
    }
    boolean hasTarget(String key) {
        return key.equals(Limits.SHARED) || key.startsWith("app:") && apps.contains(key.substring(4))
            || key.startsWith("site:") && sites.contains(key.substring(5));
    }
    boolean monitorsSites() {
        if (sites.isEmpty()) return false;
        if (active != null || limits.get(Limits.SHARED) != null) return true;
        for (String domain : sites) if (limits.get(Limits.site(domain)) != null) return true;
        return false;
    }
    Limits.Block restriction(List<String> targets, Limits.Window window) {
        if (targets.isEmpty()) return null;
        Limits.Block block = active != null && active.end > window.now
            ? new Limits.Block(active.end,"Sessão de foco em andamento") : null;
        for (Limits.Quota q : limits.forTargets(targets)) block = Limits.Block.later(block,q.blocked(window));
        return block;
    }
}
