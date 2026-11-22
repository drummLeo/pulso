package br.local.pulso;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Handler;
import android.os.Looper;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.time.ZoneId;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.Iterator;
import java.util.Set;

/** One main-thread model shared by the activity and the accessibility service. */
final class Model {
    private static Model instance;
    final Focus state;
    final boolean recovered;
    private final SharedPreferences prefs;
    private final Set<Runnable> listeners = new HashSet<>();
    private final Handler handler = new Handler(Looper.getMainLooper());
    private String lastGood, lastUsage;
    private boolean notifyPending;

    static Model get(Context context) {
        if (instance == null) instance = new Model(context.getApplicationContext());
        return instance;
    }
    private Model(Context context) {
        prefs = context.getSharedPreferences("pulso", Context.MODE_PRIVATE);
        lastGood = prefs.getString("atual", null);
        Focus loaded = decode(lastGood);
        recovered = lastGood != null && loaded == null;
        if (recovered) {
            lastGood = prefs.getString("anterior", null);
            loaded = decode(lastGood);
        }
        state = loaded == null ? new Focus() : loaded;
        lastUsage = prefs.getString("uso", null);
        if (!restoreUsage(state,lastUsage)) {
            lastUsage = prefs.getString("usoAnterior", null);
            restoreUsage(state,lastUsage);
        }
    }
    void listen(Runnable r) { listeners.add(r); }
    void unlisten(Runnable r) { listeners.remove(r); }
    void tick(long now) { if (state.tick(now, ZoneId.systemDefault())) save(); }
    void save() {
        try {
            state.limits.configured.keySet().removeIf(key -> !state.hasTarget(key));
            String encoded = encode(state).toString();
            SharedPreferences.Editor edit = prefs.edit().putString("anterior", lastGood).putString("atual", encoded);
            writeUsage(edit);
            edit.apply();
            lastGood = encoded;
        } catch (JSONException e) { throw new IllegalStateException(e); }
        if (notifyPending) return;
        notifyPending = true;
        handler.post(() -> {
            notifyPending = false;
            for (Runnable r : new ArrayList<>(listeners)) r.run();
        });
    }
    // Periodic checkpoints don't serialize the session history or notify the service again.
    void saveUsage() {
        try {
            SharedPreferences.Editor edit = prefs.edit();
            writeUsage(edit);
            edit.apply();
        } catch (JSONException e) { throw new IllegalStateException(e); }
    }
    private void writeUsage(SharedPreferences.Editor edit) throws JSONException {
        JSONObject usage = new JSONObject();
        for (Limits.Quota q : state.limits.configured.values())
            usage.put(q.key,new JSONArray().put(q.hourKey).put(q.hourMillis).put(q.dayKey).put(q.dayMillis));
        String encoded = usage.toString();
        if (!encoded.equals(lastUsage)) {
            edit.putString("usoAnterior",lastUsage).putString("uso",encoded);
            lastUsage = encoded;
        }
    }
    private static boolean restoreUsage(Focus f,String raw) {
        if (raw == null) return false;
        try {
            JSONObject usage = new JSONObject(raw);
            for (Limits.Quota q : f.limits.configured.values()) {
                JSONArray a = usage.optJSONArray(q.key);
                if (a == null || a.length() != 4) continue;
                if (q.hourMinutes > 0) {
                    q.hourKey = a.optLong(0,Long.MIN_VALUE);
                    q.hourMillis = Math.max(0,Math.min(Limits.MAX_HOUR * Limits.MINUTE,a.optLong(1)));
                }
                if (q.dayMinutes > 0) {
                    q.dayKey = a.optLong(2,Long.MIN_VALUE);
                    q.dayMillis = Math.max(0,Math.min(Limits.MAX_DAY * Limits.MINUTE,a.optLong(3)));
                }
            }
            return true;
        } catch (JSONException corrupt) { return false; }
    }
    private static JSONObject encode(Focus f) throws JSONException {
        JSONObject o = new JSONObject().put("versao", 2).put("minutos", f.minutes)
            .put("apps", new JSONArray(f.apps)).put("sites", new JSONArray(f.sites));
        JSONObject limits = new JSONObject();
        for (Limits.Quota q : f.limits.configured.values())
            limits.put(q.key,new JSONArray().put(q.hourMinutes).put(q.dayMinutes));
        o.put("limites",limits);
        JSONArray routines = new JSONArray(), history = new JSONArray();
        for (Focus.Routine r : f.routines) routines.put(new JSONArray().put(r.name).put(r.days).put(r.startMinute)
            .put(r.minutes).put(r.enabled).put(r.lastDay));
        for (Focus.Entry e : f.history) history.put(new JSONArray().put(e.title).put(e.start).put(e.end).put(e.completed));
        if (f.active != null) o.put("sessao", new JSONArray().put(f.active.title).put(f.active.start).put(f.active.end).put(f.active.scheduled));
        return o.put("agenda", routines).put("historico", history);
    }
    private static Focus decode(String raw) {
        if (raw == null) return null;
        try {
            JSONObject o = new JSONObject(raw);
            int version = o.getInt("versao");
            if (version != 1 && version != 2) return null;
            Focus f = new Focus();
            int minutes = o.getInt("minutos");
            if (Focus.validMinutes(minutes)) f.minutes = minutes;
            JSONArray apps = o.getJSONArray("apps"), sites = o.getJSONArray("sites");
            for (int i = 0; i < apps.length(); i++) f.apps.add(apps.getString(i));
            for (int i = 0; i < sites.length(); i++) {
                String domain = Focus.rule(sites.getString(i)); if (domain != null) f.sites.add(domain);
            }
            JSONObject limits = o.optJSONObject("limites");
            if (version == 2 && limits != null) {
                Iterator<String> keys = limits.keys();
                while (keys.hasNext()) {
                    String key = keys.next();
                    JSONArray p = limits.optJSONArray(key);
                    if (f.hasTarget(key) && p != null && Limits.valid(p.optInt(0,-1),p.optInt(1,-1)))
                        f.limits.set(key,p.getInt(0),p.getInt(1));
                }
            }
            JSONArray routines = o.getJSONArray("agenda"), history = o.getJSONArray("historico");
            for (int i = 0; i < routines.length(); i++) {
                JSONArray a = routines.getJSONArray(i);
                Focus.Routine r = new Focus.Routine(a.getString(0), a.getInt(1), a.getInt(2), a.getInt(3));
                if (r.days <= 0 || r.days > 127 || r.startMinute < 0 || r.startMinute >= 1440 || !Focus.validMinutes(r.minutes)) return null;
                r.enabled = a.getBoolean(4); r.lastDay = a.getLong(5); f.routines.add(r);
            }
            for (int i = 0; i < Math.min(history.length(), Focus.MAX_HISTORY); i++) {
                JSONArray a = history.getJSONArray(i);
                f.history.add(new Focus.Entry(a.getString(0), a.getLong(1), a.getLong(2), a.getBoolean(3)));
            }
            JSONArray a = o.optJSONArray("sessao");
            if (a != null && a.getLong(2) > a.getLong(1)) f.active = new Focus.Session(a.getString(0), a.getLong(1), a.getLong(2), a.getBoolean(3));
            return f;
        } catch (JSONException | IllegalArgumentException corrupt) { return null; }
    }
}
