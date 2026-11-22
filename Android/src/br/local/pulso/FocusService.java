package br.local.pulso;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.AccessibilityServiceInfo;
import android.app.KeyguardManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.content.res.Configuration;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.PowerManager;
import android.os.SystemClock;
import android.view.Gravity;
import android.view.ContextThemeWrapper;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.Collections;
import java.util.List;
import java.util.Set;
import static br.local.pulso.R.color.*;

public final class FocusService extends AccessibilityService {
    static boolean connected;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable inspect = this::check;
    private final Runnable changed = this::queueCheck;
    private final BroadcastReceiver clockChanges = new BroadcastReceiver() {
        @Override public void onReceive(Context c, Intent i) {
            if (Intent.ACTION_SCREEN_OFF.equals(i.getAction())) {
                settle(System.currentTimeMillis(),SystemClock.uptimeMillis(),ZoneId.systemDefault());
            } else {
                tracker.clear();
            }
            lastTargets = Collections.emptyList();
            check();
        }
    };
    private final Limits.Tracker tracker = new Limits.Tracker();
    private List<String> lastTargets = Collections.emptyList();
    private boolean registered, pending, watchingBar, dirty, enforcing;
    private long eventAt, lastCheckpoint;
    private Model model;
    private Set<String> essential, launchers;
    private WindowManager manager;
    private PowerManager power;
    private KeyguardManager keyguard;
    private ScrollView cover;
    private ScrollView notice;
    private String noticeName, noticeTarget;
    private Limits.Block noticeBlock;
    private boolean noticeSite;
    private boolean coverDark;
    private final SharedPreferences.OnSharedPreferenceChangeListener appearanceChanged = (prefs,key)->{
        if (notice!=null) drawNotice();
        if (cover != null) queueCheck();
    };
    private final Rect covered = new Rect();
    private String showingHost = "", showingReason = "", ejected = "";
    private long showingEnd;

    @Override protected void onServiceConnected() {
        super.onServiceConnected();
        model = Model.get(this); essential = Apps.essential(this); launchers=Apps.launchers(this);
        manager = (WindowManager)getSystemService(WINDOW_SERVICE);
        power = (PowerManager)getSystemService(POWER_SERVICE);
        keyguard = (KeyguardManager)getSystemService(KEYGUARD_SERVICE);
        model.listen(changed);
        model.beforeEdit=()->{
            if (unlocked()) settle(System.currentTimeMillis(),SystemClock.uptimeMillis(),ZoneId.systemDefault());
            else tracker.clear();
        };
        Appearance.preferences(this).registerOnSharedPreferenceChangeListener(appearanceChanged);
        if (!registered) {
            IntentFilter filter = new IntentFilter();
            filter.addAction(Intent.ACTION_SCREEN_OFF); filter.addAction(Intent.ACTION_SCREEN_ON); filter.addAction(Intent.ACTION_TIME_CHANGED);
            filter.addAction(Intent.ACTION_TIMEZONE_CHANGED); filter.addAction(Intent.ACTION_USER_PRESENT);
            if (Build.VERSION.SDK_INT >= 33) registerReceiver(clockChanges,filter,Context.RECEIVER_NOT_EXPORTED);
            else registerReceiver(clockChanges,filter);
            registered = true;
        }
        connected = true; tracker.clear(); lastTargets = Collections.emptyList();
        check();
    }
    private void queueCheck() {
        if (!connected || pending) return;
        pending = true;
        handler.postDelayed(inspect, 60);
    }
    @Override public void onAccessibilityEvent(AccessibilityEvent event) {
        if (!connected || event == null) return;
        if (!enforcing && notice==null) return;
        String pkg=String.valueOf(event.getPackageName());
        if (event.getEventType()==AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED && launchers.contains(pkg)) ejected="";
        if (event.getEventType() == AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED
                && !browser(pkg)) return;
        if (eventAt == 0) eventAt = event.getEventTime();
        queueCheck();
    }
    private static boolean browser(String pkg) {
        return pkg.equals("com.android.chrome") || pkg.equals("com.sec.android.app.sbrowser") || pkg.equals("com.microsoft.emmx");
    }
    private void watchBar(boolean next) {
        if (next == watchingBar) return;
        AccessibilityServiceInfo info = getServiceInfo();
        if (info == null) return;
        info.eventTypes = AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED |
            (next ? AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED : 0);
        setServiceInfo(info);
        watchingBar = next;
    }
    private void check() {
        if (!connected || model == null) return;
        handler.removeCallbacks(inspect);
        long now = System.currentTimeMillis(), awake = SystemClock.uptimeMillis(), transitionAt = eventAt;
        eventAt = 0; pending = false;
        ZoneId zone = ZoneId.systemDefault();
        model.tick(now);
        Focus f = model.state;
        Focus.Slot upcoming=f.next(now,zone);
        long next=upcoming==null?Long.MAX_VALUE:upcoming.start;
        if (f.active!=null) next=Math.min(next,f.active.end);
        List<Focus.Slot> schedules=f.meteredSlots(now,now+1,zone);
        for (Focus.Slot s:schedules) next=Math.min(next,s.end);
        boolean interactive = unlocked();
        enforcing=(!f.apps.isEmpty() || !f.sites.isEmpty())
            && (f.active!=null || !f.limits.configured.isEmpty() || !schedules.isEmpty());
        boolean observing=interactive && enforcing;
        Visible visible=observing ? foreground(f,!schedules.isEmpty()) : null;
        List<String> targets=visible==null || essential.contains(visible.pkg) ? Collections.emptyList() : f.targets(visible.pkg,visible.host());
        if (interactive) {
            // Window events carry the monotonic time of the switch, even when Android batches them.
            long lag = !targets.equals(lastTargets) && transitionAt > 0 ? Math.max(0, awake - transitionAt) : 0;
            settle(now - lag,awake - lag,zone);
        } else tracker.clear();
        lastTargets = targets;
        watchBar(visible != null && visible.readBar);
        boolean tracking = false;
        Limits.Window w=Limits.Window.at(now,zone);
        if (notice!=null) {
            List<String> shown=f.targets(noticeSite?null:noticeTarget,noticeSite?noticeTarget:null);
            Limits.Block current=observing?f.restriction(shown,w):null;
            if (current==null) hideNotice();
            else if (current.end!=noticeBlock.end || !current.reason.equals(noticeBlock.reason)) {
                noticeBlock=current; drawNotice();
            }
        }
        if (visible == null || targets.isEmpty()) {
            hideCover();
            if (!observing || visible!=null) ejected="";
            if (visible!=null && !launchers.contains(visible.pkg)) hideNotice();
            if (observing && visible == null) next = Math.min(next,now + 1000);
        } else {
            List<String> app = f.apps.contains(visible.pkg) ? Collections.singletonList(Limits.app(visible.pkg)) : Collections.emptyList();
            Limits.Block appBlock = f.restriction(app,w);
            Limits.Block block = f.restriction(targets,w);
            if (appBlock != null) {
                hideCover();
                returnHome(visible.pkg,appBlock,false);
                next = Math.min(next,Math.min(now + 1000,appBlock.end));
            } else if (block != null && visible.address != null) {
                if (visible.area.width() > 0 && visible.area.height() >= Ui.dp(this,60)) {
                    ejected=""; hideNotice();
                    showCover(visible.area,visible.host(),block);
                } else {
                    hideCover();
                    returnHome(visible.host(),block,true);
                }
                next = Math.min(next,block.end);
            } else {
                hideCover(); hideNotice(); ejected="";
                List<Limits.Quota> rules=Limits.at(f.limits.forTargets(targets),schedules,now);
                if (!rules.isEmpty() && f.active == null) {
                    Focus.Slot immediate=f.next(now,zone,true);
                    tracker.watch(f,targets,now,awake,zone,immediate==null?Long.MAX_VALUE:immediate.start);
                    tracking = true;
                    long delay = Math.min(Limits.Tracker.CHECKPOINT,Limits.remaining(rules,w));
                    next = Math.min(next,Math.min(now + delay,Math.min(w.hourEnd,w.dayEnd)));
                }
            }
        }
        // Some browser transitions do not publish a new window event.
        if (visible != null && visible.readBar) next = Math.min(next,now + 2000);
        if (notice!=null) next=Math.min(next,noticeBlock.end);
        checkpoint(awake,!tracking);
        if (next != Long.MAX_VALUE) handler.postDelayed(inspect,Math.max(20,next-System.currentTimeMillis()));
    }
    private boolean unlocked() {
        return power != null && power.isInteractive() && keyguard != null && !keyguard.isKeyguardLocked();
    }
    private void settle(long now,long awake,ZoneId zone) {
        dirty |= tracker.finish(now,awake,zone);
    }
    private void checkpoint(long awake,boolean force) {
        if (dirty && (force || awake - lastCheckpoint >= Limits.Tracker.CHECKPOINT)) {
            model.saveUsage(); dirty = false; lastCheckpoint = awake;
        }
    }
    private void returnHome(String target,Limits.Block block,boolean site) {
        if (target.equals(ejected)) return;
        if (performGlobalAction(GLOBAL_ACTION_HOME)) {
            ejected=target;
            if (notice!=null && target.equals(noticeTarget) && site==noticeSite
                    && block.end==noticeBlock.end && block.reason.equals(noticeBlock.reason)) return;
            noticeTarget=target;
            noticeName=site?target:Apps.label(this,target);
            noticeBlock=block; noticeSite=site;
            drawNotice();
        }
    }
    private void drawNotice() {
        removeNoticeView();
        ContextThemeWrapper themed=new ContextThemeWrapper(this,R.style.PulsoTheme);
        Appearance.read(this).applyToContext(themed);
        String title=noticeSite?"Site em pausa":"Aplicativo em pausa";
        ScrollView sheet=new ScrollView(themed);
        sheet.setBackground(Ui.bg(themed,card_surface,16,true));
        sheet.setAccessibilityPaneTitle(title);
        LinearLayout content=Ui.col(themed); Ui.pad(content,18,14,18,12); sheet.addView(content);
        TextView heading=Ui.text(themed,title,21,text_primary,true); heading.setAccessibilityHeading(true);
        Ui.put(content,heading,0);
        Ui.put(content,Ui.text(themed,noticeName,18,text_primary,true),5);
        String detail=noticeBlock.reason+". Liberação às "+releaseTime(noticeBlock.end)+".";
        Ui.put(content,Ui.text(themed,detail,16,text_muted,false),6);
        Ui.put(content,Ui.button(themed,"Entendi",on_focus_action,focus_action,()->{hideNotice(); ejected=""; queueCheck();}),10);
        int width=Math.min(Ui.dp(this,408),getResources().getDisplayMetrics().widthPixels-Ui.dp(this,24));
        WindowManager.LayoutParams p=new WindowManager.LayoutParams(width,Ui.WRAP,WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE|WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,PixelFormat.TRANSLUCENT);
        p.gravity=Gravity.TOP|Gravity.CENTER_HORIZONTAL; p.y=Ui.dp(this,8); p.setTitle("Pulso · "+title);
        try {
            manager.addView(sheet,p); notice=sheet;
        } catch (WindowManager.BadTokenException | WindowManager.InvalidDisplayException | SecurityException unavailable) { hideNotice(); }
    }
    private void removeNoticeView() {
        if (notice==null) return;
        try { manager.removeViewImmediate(notice); } catch (IllegalArgumentException detached) { }
        notice=null;
    }
    private void hideNotice() {
        removeNoticeView(); noticeBlock=null; noticeName=null; noticeTarget=null;
    }
    private static String releaseTime(long end) {
        return Instant.ofEpochMilli(end).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("HH:mm"));
    }
    private static final class Visible {
        final String pkg;
        final boolean readBar;
        final Address address;
        final Rect area;
        Visible(String pkg,boolean readBar,Address address,Rect area) {
            this.pkg=pkg; this.readBar=readBar; this.address=address; this.area=area;
        }
        String host() { return address == null ? null : address.host; }
    }
    private Visible foreground(Focus f,boolean meteredSchedule) {
        AccessibilityNodeInfo root = getRootInActiveWindow();
        if (root == null) return null;
        try {
            String pkg = String.valueOf(root.getPackageName());
            if (essential.contains(pkg)) return new Visible(pkg,false,null,null);
            boolean readBar = browser(pkg) && f.monitorsSites(meteredSchedule);
            Address address = readBar ? address(root,pkg) : null;
            Rect area = null;
            if (address != null) {
                area = new Rect(); root.getBoundsInScreen(area);
                if (address.bar.centerY() < area.centerY()) area.top = Math.max(area.top,address.bar.bottom);
                else area.bottom = Math.min(area.bottom,address.bar.top);
            }
            return new Visible(pkg,readBar,address,area);
        } finally { release(root); }
    }
    private static final class Address {
        final String host; final Rect bar;
        Address(String host,Rect bar) { this.host=host; this.bar=bar; }
    }
    private static Address address(AccessibilityNodeInfo root,String pkg) {
        String[] ids = pkg.equals("com.sec.android.app.sbrowser")
            ? new String[]{"location_bar_edit_text","location_bar_url","url_bar","location_bar_title"}
            : new String[]{"url_bar"};
        for (String id : ids) {
            List<AccessibilityNodeInfo> nodes = root.findAccessibilityNodeInfosByViewId(pkg+":id/"+id);
            try {
                for (AccessibilityNodeInfo n : nodes) {
                    if (!pkg.contentEquals(n.getPackageName() == null ? "" : n.getPackageName()) || !n.isVisibleToUser()) continue;
                    if (n.isEditable() && n.isFocused()) return null;
                    CharSequence value = n.getText();
                    if (value == null) continue;
                    String host = Focus.host(value.toString());
                    if (host != null) {
                        Rect bar = new Rect(); n.getBoundsInScreen(bar);
                        if (!bar.isEmpty()) return new Address(host,bar);
                    }
                }
            } finally { for (AccessibilityNodeInfo n : nodes) release(n); }
        }
        return null;
    }
    @SuppressWarnings("deprecation") private static void release(AccessibilityNodeInfo node) {
        if (Build.VERSION.SDK_INT < 33) node.recycle();
    }
    private void showCover(Rect area,String host,Limits.Block block) {
        Appearance appearance = Appearance.read(this);
        boolean dark = appearance.isDark(this);
        if (cover != null && coverDark==dark && showingHost.equals(host) && showingEnd==block.end && showingReason.equals(block.reason) && covered.equals(area)) return;
        hideCover();
        ContextThemeWrapper themed = new ContextThemeWrapper(this,R.style.PulsoTheme);
        appearance.applyToContext(themed);
        ScrollView sheet = new ScrollView(themed);
        sheet.setBackgroundColor(themed.getColor(page_background)); sheet.setFillViewport(true);
        LinearLayout content = Ui.col(themed); content.setGravity(Gravity.CENTER); Ui.pad(content,24,20,24,20);
        sheet.addView(content);
        TextView brand = Ui.text(themed,"Pulso",17,accent,true); brand.setGravity(Gravity.CENTER); Ui.put(content,brand,0);
        TextView title = Ui.text(themed,"Site em pausa",32,text_primary,true); title.setGravity(Gravity.CENTER); Ui.put(content,title,12);
        TextView detail=Ui.text(themed,block.reason+". "+host+" fica em pausa até "+releaseTime(block.end)+". Use a barra de endereço para abrir outro site.",16,text_muted,false);
        detail.setGravity(Gravity.CENTER); Ui.put(content,detail,10);
        Ui.put(content,Ui.button(themed,"Voltar uma página",on_focus_action,focus_action,()->{ hideCover(); performGlobalAction(GLOBAL_ACTION_BACK); queueCheck(); }),20);
        Ui.put(content,Ui.button(themed,"Tela inicial",accent,accent_surface,()->{ hideCover(); performGlobalAction(GLOBAL_ACTION_HOME); }),8);
        WindowManager.LayoutParams p = new WindowManager.LayoutParams(area.width(),area.height(),
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY, WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE |
            WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN, PixelFormat.OPAQUE);
        if (Build.VERSION.SDK_INT >= 30) p.setFitInsetsTypes(0);
        p.gravity=Gravity.TOP|Gravity.LEFT; p.x=area.left; p.y=area.top; p.setTitle("Pulso · Site em pausa");
        try {
            manager.addView(sheet,p); cover=sheet; coverDark=dark; showingHost=host; showingEnd=block.end; showingReason=block.reason; covered.set(area);
        } catch (WindowManager.BadTokenException | WindowManager.InvalidDisplayException | SecurityException unavailable) {
            performGlobalAction(GLOBAL_ACTION_HOME);
        }
    }
    private void hideCover() {
        if (cover != null) {
            try { manager.removeViewImmediate(cover); } catch (IllegalArgumentException detached) { }
            cover=null; showingHost=""; covered.setEmpty();
        }
    }
    private void disconnect() {
        if (model != null) {
            long awake=SystemClock.uptimeMillis();
            if (connected && unlocked()) settle(System.currentTimeMillis(),awake,ZoneId.systemDefault());
            tracker.clear(); checkpoint(awake,true);
        }
        connected=false; pending=false; watchingBar=false; enforcing=false; ejected="";
        eventAt=0; lastTargets=Collections.emptyList();
        handler.removeCallbacksAndMessages(null); hideCover(); hideNotice();
        if(model!=null){model.beforeEdit=null;model.unlisten(changed);}
        Appearance.preferences(this).unregisterOnSharedPreferenceChangeListener(appearanceChanged);
        if(registered){ unregisterReceiver(clockChanges); registered=false; }
    }
    @Override public void onConfigurationChanged(Configuration configuration) {
        super.onConfigurationChanged(configuration);
        if (notice!=null) drawNotice();
        if (cover != null) queueCheck();
    }
    @Override public void onInterrupt() { tracker.clear(); hideCover(); hideNotice(); queueCheck(); }
    @Override public boolean onUnbind(Intent intent) { disconnect(); return super.onUnbind(intent); }
    @Override public void onDestroy() { disconnect(); super.onDestroy(); }
}
