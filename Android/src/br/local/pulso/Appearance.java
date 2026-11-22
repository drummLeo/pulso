package br.local.pulso;

import android.app.UiModeManager;
import android.content.Context;
import android.content.SharedPreferences;
import android.content.res.Configuration;
import android.os.Build;
import android.view.ContextThemeWrapper;

enum Appearance {
    SYSTEM("Sistema", UiModeManager.MODE_NIGHT_AUTO, Configuration.UI_MODE_NIGHT_UNDEFINED),
    LIGHT("Claro", UiModeManager.MODE_NIGHT_NO, Configuration.UI_MODE_NIGHT_NO),
    DARK("Escuro", UiModeManager.MODE_NIGHT_YES, Configuration.UI_MODE_NIGHT_YES);

    final String label;
    private final int platformMode;
    private final int night;

    Appearance(String label, int platformMode, int night) {
        this.label = label;
        this.platformMode = platformMode;
        this.night = night;
    }

    static SharedPreferences preferences(Context context) {
        return context.getSharedPreferences("aparencia", Context.MODE_PRIVATE);
    }

    static Appearance read(Context context) {
        String saved = preferences(context).getString("tema", null);
        for (Appearance option : values()) if (option.name().equals(saved)) return option;
        return SYSTEM;
    }

    void save(Context context) {
        preferences(context).edit().putString("tema", name()).apply();
        applyToPlatform(context);
    }

    void applyToPlatform(Context context) {
        if (Build.VERSION.SDK_INT >= 31) {
            context.getSystemService(UiModeManager.class).setApplicationNightMode(platformMode);
        }
    }

    void applyToContext(ContextThemeWrapper context) {
        if (this == SYSTEM) return;
        Configuration override = new Configuration();
        override.uiMode = night;
        context.applyOverrideConfiguration(override);
    }

    boolean isDark(Context context) {
        int effective = this == SYSTEM ? context.getResources().getConfiguration().uiMode : night;
        return (effective & Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES;
    }
}
