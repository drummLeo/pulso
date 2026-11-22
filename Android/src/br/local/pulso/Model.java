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
import java.util.List;
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
    Runnable beforeEdit;

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
    void edit(Runnable change) {
        if (beforeEdit!=null) beforeEdit.run();
        change.run();
        state.tick(System.currentTimeMillis(),ZoneId.systemDefault());
        save();
    }
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
        String encoded = usage(state).toString();
        if (!encoded.equals(lastUsage)) {
            edit.putString("usoAnterior",lastUsage).putString("uso",encoded);
            lastUsage = encoded;
        }
    }
    private static List<Limits.Quota> budgets(Focus f) {
        List<Limits.Quota> all=new ArrayList<>(f.limits.configured.values());
        for (Focus.Routine r:f.routines) if (r.quota.hourMinutes>0) all.add(r.quota);
        return all;
    }
    static JSONObject usage(Focus f) throws JSONException {
        JSONObject usage = new JSONObject();
        for (Limits.Quota q : budgets(f))
            usage.put(q.key,new JSONArray().put(q.hourKey).put(q.hourMillis).put(q.dayKey).put(q.dayMillis));
        return usage;
    }
    static boolean restoreUsage(Focus f,String raw) {
        if (raw == null) return false;
        try {
            JSONObject usage = new JSONObject(raw);
            for (Limits.Quota q : budgets(f)) {
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
    static JSONObject encode(Focus f) throws JSONException {
        JSONObject o = new JSONObject().put("versao", 4).put("minutos", f.minutes)
            .put("apps", new JSONArray(f.apps)).put("sites", new JSONArray(f.sites));
        JSONObject categories = new JSONObject();
        for (SiteCatalog.Category category : SiteCatalog.Category.values())
            categories.put(category.key,f.siteModes.get(category).key);
        o.put("categoriasSites",categories);
        JSONObject limits = new JSONObject();
        for (Limits.Quota q : f.limits.configured.values())
            limits.put(q.key,new JSONArray().put(q.hourMinutes).put(q.dayMinutes));
        o.put("limites",limits);
        JSONArray routines = new JSONArray(), history = new JSONArray();
        for (Focus.Routine r : f.routines) routines.put(new JSONArray().put(r.name).put(r.days).put(r.startMinute)
            .put(r.minutes).put(r.enabled).put(r.lastDay).put(r.id).put(r.quota.hourMinutes));
        for (Focus.Entry e : f.history) history.put(new JSONArray().put(e.title).put(e.start).put(e.end).put(e.completed));
        if (f.active != null) {
            JSONArray session=new JSONArray().put(f.active.title).put(f.active.start).put(f.active.end).put(f.active.scheduled);
            if (f.active.routineId!=null) session.put(f.active.routineId);
            o.put("sessao",session);
        }
        return o.put("agenda", routines).put("historico", history);
    }
    static Focus decode(String raw) {
        if (raw == null) return null;
        try {
            JSONObject o = new JSONObject(raw);
            int version = o.getInt("versao");
            if (version<1 || version>4) return null;
            Focus f = new Focus();
            JSONObject categories = o.optJSONObject("categoriasSites");
            if (version >= 4 && categories != null) for (SiteCatalog.Category category : SiteCatalog.Category.values())
                f.siteModes.set(category,SiteCatalog.Mode.read(categories.optString(category.key)));
            int minutes = o.getInt("minutos");
            if (Focus.validMinutes(minutes)) f.minutes = minutes;
            JSONArray apps = o.getJSONArray("apps"), sites = o.getJSONArray("sites");
            for (int i = 0; i < apps.length(); i++) f.apps.add(apps.getString(i));
            for (int i = 0; i < sites.length(); i++) {
                String domain = Focus.rule(sites.getString(i)); if (domain != null) f.sites.add(domain);
            }
            JSONObject limits = o.optJSONObject("limites");
            if (version >= 2 && limits != null) {
                Iterator<String> keys = limits.keys();
                while (keys.hasNext()) {
                    String key = keys.next();
                    JSONArray p = limits.optJSONArray(key);
                    if (f.hasTarget(key) && p != null && Limits.valid(p.optInt(0,-1),p.optInt(1,-1)))
                        f.limits.set(key,p.getInt(0),p.getInt(1));
                }
            }
            JSONArray routines = o.getJSONArray("agenda"), history = o.getJSONArray("historico");
            Set<String> ids=new HashSet<>();
            for (int i = 0; i < routines.length(); i++) {
                JSONArray a = routines.getJSONArray(i);
                String name=a.getString(0);
                int days=a.getInt(1), start=a.getInt(2), duration=a.getInt(3);
                Focus.Routine r=version<3 ? new Focus.Routine(name,days,start,duration)
                    : new Focus.Routine(a.getString(6),name,days,start,duration);
                if (r.id.isEmpty() || !ids.add(r.id)) return null;
                if (version>=3) r.setHourLimit(a.getInt(7));
                if (r.days <= 0 || r.days > 127 || r.startMinute < 0 || r.startMinute >= 1440 || !Focus.validMinutes(r.minutes)) return null;
                r.enabled = a.getBoolean(4); r.lastDay = a.getLong(5); f.routines.add(r);
            }
            for (int i = 0; i < Math.min(history.length(), Focus.MAX_HISTORY); i++) {
                JSONArray a = history.getJSONArray(i);
                f.history.add(new Focus.Entry(a.getString(0), a.getLong(1), a.getLong(2), a.getBoolean(3)));
            }
            JSONArray a = o.optJSONArray("sessao");
            if (a != null && a.getLong(2) > a.getLong(1)) {
                String title=a.getString(0), owner=a.optString(4,null);
                long start=a.getLong(1), end=a.getLong(2);
                boolean scheduled=a.getBoolean(3);
                if (scheduled && version<3) for (Focus.Routine r:f.routines) {
                    if (!r.name.equals(title)) continue;
                    Focus.Slot s=Focus.current(r,start,ZoneId.systemDefault());
                    if (s!=null && s.day==r.lastDay && s.end==end) { owner=r.id; break; }
                }
                f.active=new Focus.Session(title,start,end,scheduled,owner);
            }
            return f;
        } catch (JSONException | IllegalArgumentException corrupt) { return null; }
    }
}
