package br.local.pulso;

import java.time.Instant;
import java.time.ZoneId;
import java.time.ZonedDateTime;
import java.time.zone.ZoneOffsetTransition;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** Configured budgets and measured foreground time, in milliseconds. */
final class Limits {
    static final String SHARED = "lista";
    static final int MAX_HOUR = 60, MAX_DAY = 1440;
    static final long MINUTE = 60000L;
    final Map<String,Quota> configured = new LinkedHashMap<>();

    static String app(String pkg) { return "app:" + pkg; }
    static String site(String host) { return "site:" + host; }
    static boolean valid(int hour, int day) {
        return hour >= 0 && hour <= MAX_HOUR && day >= 0 && day <= MAX_DAY;
    }
    Quota get(String key) { return configured.get(key); }
    void set(String key, int hour, int day) {
        if (!valid(hour, day)) throw new IllegalArgumentException("Limite fora da faixa.");
        if (hour == 0 && day == 0) { configured.remove(key); return; }
        Quota q = configured.get(key);
        if (q == null) { q = new Quota(key); configured.put(key, q); }
        if (hour == 0) { q.hourKey = Long.MIN_VALUE; q.hourMillis = 0; }
        if (day == 0) { q.dayKey = Long.MIN_VALUE; q.dayMillis = 0; }
        q.hourMinutes = hour;
        q.dayMinutes = day;
    }
    List<Quota> forTargets(List<String> targets) {
        if (targets.isEmpty() || configured.isEmpty()) return Collections.emptyList();
        List<Quota> result = new ArrayList<>();
        Quota shared = get(SHARED);
        if (shared != null) result.add(shared);
        for (String key : targets) {
            Quota q = get(key);
            if (q != null) result.add(q);
        }
        return result;
    }

    static final class Window {
        final ZoneId zone;
        final long now, hourKey, hourEnd, dayKey, dayEnd;
        private Window(ZoneId zone,long now, long hourKey, long hourEnd, long dayKey, long dayEnd) {
            this.zone=zone;
            this.now=now; this.hourKey=hourKey; this.hourEnd=hourEnd; this.dayKey=dayKey; this.dayEnd=dayEnd;
        }
        static Window at(long now, ZoneId zone) {
            Instant instant = Instant.ofEpochMilli(now);
            ZonedDateTime local = instant.atZone(zone);
            long inHour = local.getMinute() * MINUTE + local.getSecond() * 1000L + local.getNano() / 1000000;
            long key = now - inHour, end = key + 60 * MINUTE;
            ZoneOffsetTransition transition = zone.getRules().nextTransition(instant);
            if (transition != null) end = Math.min(end, transition.getInstant().toEpochMilli());
            long midnight = local.toLocalDate().plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli();
            return new Window(zone,now, key, end, local.toLocalDate().toEpochDay(), midnight);
        }
    }
    static final class Block {
        final long end;
        final String reason;
        Block(long end, String reason) { this.end=end; this.reason=reason; }
        static Block later(Block a, Block b) {
            if (b == null) return a;
            return a == null || b.end >= a.end ? b : a;
        }
    }
    static final class Quota {
        final String key;
        int hourMinutes, dayMinutes;
        long hourKey = Long.MIN_VALUE, dayKey = Long.MIN_VALUE, hourMillis, dayMillis;
        Quota(String key) { this.key = key; }
        long hourUsed(Window w) { return hourMinutes > 0 && hourKey == w.hourKey ? hourMillis : 0; }
        long dayUsed(Window w) { return dayMinutes > 0 && dayKey == w.dayKey ? dayMillis : 0; }
        long remaining(Window w) {
            long hour = hourMinutes == 0 ? Long.MAX_VALUE : hourMinutes * MINUTE - hourUsed(w);
            long day = dayMinutes == 0 ? Long.MAX_VALUE : dayMinutes * MINUTE - dayUsed(w);
            return Math.max(0, Math.min(hour, day));
        }
        Block blocked(Window w) {
            boolean shared = key.equals(SHARED);
            if (dayMinutes > 0 && dayUsed(w) >= dayMinutes * MINUTE)
                return new Block(w.dayEnd, shared ? "Limite diário compartilhado atingido" : "Limite diário atingido");
            if (hourMinutes > 0 && hourUsed(w) >= hourMinutes * MINUTE)
                return new Block(w.hourEnd, shared ? "Limite por hora compartilhado atingido" : "Limite por hora atingido");
            return null;
        }
        private void add(Window w, long elapsed) {
            if (hourMinutes > 0) { hourMillis = hourUsed(w) + elapsed; hourKey = w.hourKey; }
            if (dayMinutes > 0) { dayMillis = dayUsed(w) + elapsed; dayKey = w.dayKey; }
        }
    }
    static long remaining(List<Quota> rules, Window w) {
        long remaining = Long.MAX_VALUE;
        for (Quota q : rules) remaining = Math.min(remaining, q.remaining(w));
        return remaining;
    }
    static List<Quota> at(List<Quota> base,List<Focus.Slot> schedules,long now) {
        if (schedules.isEmpty()) return base;
        List<Quota> result=new ArrayList<>(base);
        for (Focus.Slot s:schedules) if (s.start<=now && now<s.end) result.add(s.routine.quota);
        return result;
    }
    static boolean consume(List<Quota> rules, long from, long until, ZoneId zone) {
        if (rules.isEmpty()) return false;
        boolean changed = false;
        while (from < until) {
            Window w = Window.at(from, zone);
            long end = Math.min(until, Math.min(w.hourEnd, w.dayEnd));
            long allowed = Math.min(end - from, remaining(rules, w));
            if (allowed > 0) {
                for (Quota q : rules) q.add(w, allowed);
                changed = true;
            }
            from = end;
        }
        return changed;
    }

    /** Only the running service owns an observation. Never restore it from disk. */
    static final class Tracker {
        static final long CHECKPOINT = 5000L;
        private static final long MAX_SAMPLE = 4 * CHECKPOINT;
        private long wall, uptime, stopAt;
        private ZoneId zone;
        private List<Quota> observed = Collections.emptyList();
        private List<Focus.Slot> schedules = Collections.emptyList();

        void watch(Focus f, List<String> targets, long now, long awake, ZoneId local, long nextFocus) {
            clear();
            if (targets.isEmpty() || f.active != null) return;
            observed = f.limits.forTargets(targets);
            schedules = f.meteredSlots(now,now+MAX_SAMPLE,local);
            if (remaining(at(observed,schedules,now),Window.at(now,local))==0) { clear(); return; }
            wall = now; uptime = awake; zone = local; stopAt = nextFocus;
        }
        void clear() { observed = Collections.emptyList(); schedules=Collections.emptyList(); }
        boolean finish(long now, long awake, ZoneId local) {
            List<Quota> base = observed;
            List<Focus.Slot> slots = schedules;
            clear();
            if ((base.isEmpty() && slots.isEmpty()) || !local.equals(zone)) return false;
            long elapsed = awake - uptime, wallDelta = now - wall;
            // A delayed or discontinuous sample cannot establish what stayed visible.
            if (elapsed <= 0 || wallDelta < 0 || elapsed > MAX_SAMPLE || Math.abs(wallDelta - elapsed) > 1000)
                return false;
            long until=Math.min(wall+elapsed,stopAt);
            boolean changed=false;
            for (long from=wall; from<until;) {
                long end=until;
                for (Focus.Slot s:slots) {
                    if (s.start>from) end=Math.min(end,s.start);
                    if (s.end>from) end=Math.min(end,s.end);
                }
                changed|=consume(at(base,slots,from),from,end,local);
                from=end;
            }
            return changed;
        }
    }
}
