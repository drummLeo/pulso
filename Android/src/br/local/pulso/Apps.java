package br.local.pulso;

import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.net.Uri;
import android.provider.Settings;
import android.telecom.TelecomManager;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.Reader;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.Collection;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

final class Apps {
    static Set<String> launchers(Context context) {
        Set<String> result=new HashSet<>();
        Intent home=new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME);
        for (ResolveInfo r:context.getPackageManager().queryIntentActivities(home,0)) result.add(r.activityInfo.packageName);
        return result;
    }
    static Set<String> essential(Context context) {
        Set<String> result = new HashSet<>(Arrays.asList(context.getPackageName(), "android", "com.android.systemui",
            "com.android.settings", "com.android.phone", "com.android.server.telecom", "com.android.emergency",
            "com.samsung.android.incallui", "com.samsung.android.dialer", "com.google.android.dialer",
            "com.android.permissioncontroller", "com.google.android.permissioncontroller"));
        PackageManager pm = context.getPackageManager();
        result.addAll(launchers(context));
        for (Intent intent : new Intent[]{new Intent(Settings.ACTION_SETTINGS), new Intent(Intent.ACTION_DIAL, Uri.parse("tel:"))}) {
            for (ResolveInfo r : pm.queryIntentActivities(intent, 0)) result.add(r.activityInfo.packageName);
        }
        TelecomManager telecom = (TelecomManager) context.getSystemService(Context.TELECOM_SERVICE);
        if (telecom != null) result.add(telecom.getDefaultDialerPackage());
        return result;
    }
    static AppCatalog catalog(Context context) {
        try (Reader table = new InputStreamReader(context.getResources().openRawResource(R.raw.app_categories),
                StandardCharsets.UTF_8)) {
            return new AppCatalog(table);
        } catch (IOException invalid) {
            throw new IllegalStateException("Falha ao ler as categorias de aplicativos", invalid);
        }
    }

    static List<AppCatalog.Group> groups(Context context, AppCatalog catalog, Collection<String> saved, boolean includeLauncherApps) {
        if (!includeLauncherApps && saved.isEmpty()) return java.util.Collections.emptyList();
        PackageManager pm = context.getPackageManager();
        Set<String> essential = essential(context);
        Map<String, AppCatalog.Item> unique = new LinkedHashMap<>();
        if (includeLauncherApps) {
            Intent launcher = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
            for (ResolveInfo resolved : pm.queryIntentActivities(launcher, 0)) {
                ApplicationInfo app = resolved.activityInfo.applicationInfo;
                if (!essential.contains(app.packageName) && !unique.containsKey(app.packageName))
                    unique.put(app.packageName, item(catalog, pm, app));
            }
        }
        for (String pkg : saved) {
            if (essential.contains(pkg) || unique.containsKey(pkg)) continue;
            try {
                unique.put(pkg, item(catalog, pm, pm.getApplicationInfo(pkg, 0)));
            } catch (PackageManager.NameNotFoundException missing) {
                unique.put(pkg, catalog.item(pkg, pkg, ApplicationInfo.CATEGORY_UNDEFINED, false));
            }
        }
        return catalog.groups(unique.values());
    }
    @SuppressWarnings("deprecation")
    private static AppCatalog.Item item(AppCatalog catalog, PackageManager pm, ApplicationInfo app) {
        return catalog.item(app.packageName, pm.getApplicationLabel(app).toString(), app.category,
            (app.flags & ApplicationInfo.FLAG_IS_GAME) != 0);
    }
    static String label(Context context, String pkg) {
        try {
            PackageManager pm = context.getPackageManager();
            return pm.getApplicationLabel(pm.getApplicationInfo(pkg, 0)).toString();
        } catch (PackageManager.NameNotFoundException missing) { return pkg; }
    }
}
