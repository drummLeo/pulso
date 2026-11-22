package br.local.pulso;

import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.net.Uri;
import android.provider.Settings;
import android.telecom.TelecomManager;
import java.text.Collator;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

final class Apps {
    static Set<String> essential(Context context) {
        Set<String> result = new HashSet<>(Arrays.asList(context.getPackageName(), "android", "com.android.systemui",
            "com.android.settings", "com.android.phone", "com.android.server.telecom", "com.android.emergency",
            "com.samsung.android.incallui", "com.samsung.android.dialer", "com.google.android.dialer",
            "com.android.permissioncontroller", "com.google.android.permissioncontroller"));
        PackageManager pm = context.getPackageManager();
        for (Intent intent : new Intent[]{new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME),
                new Intent(Settings.ACTION_SETTINGS), new Intent(Intent.ACTION_DIAL, Uri.parse("tel:"))}) {
            for (ResolveInfo r : pm.queryIntentActivities(intent, 0)) result.add(r.activityInfo.packageName);
        }
        TelecomManager telecom = (TelecomManager) context.getSystemService(Context.TELECOM_SERVICE);
        if (telecom != null) result.add(telecom.getDefaultDialerPackage());
        return result;
    }
    static final class Item {
        final String pkg, name;
        Item(String pkg, String name) { this.pkg = pkg; this.name = name; }
    }
    static List<Item> launchable(Context context) {
        PackageManager pm = context.getPackageManager();
        Set<String> essential = essential(context);
        Map<String,Item> unique = new LinkedHashMap<>();
        Intent launcher = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        for (ResolveInfo r : pm.queryIntentActivities(launcher, 0)) {
            String pkg = r.activityInfo.packageName;
            if (!essential.contains(pkg)) unique.put(pkg, new Item(pkg, r.loadLabel(pm).toString()));
        }
        List<Item> items = new ArrayList<>(unique.values());
        Collator collator = Collator.getInstance(new Locale("pt","BR"));
        items.sort((a,b) -> collator.compare(a.name,b.name));
        return items;
    }
    static String label(Context context, String pkg) {
        try {
            PackageManager pm = context.getPackageManager();
            return pm.getApplicationLabel(pm.getApplicationInfo(pkg, 0)).toString();
        } catch (PackageManager.NameNotFoundException missing) { return pkg; }
    }
}
