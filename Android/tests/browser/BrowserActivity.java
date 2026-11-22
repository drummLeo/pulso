package br.local.pulso.testbrowser;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.view.Gravity;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

/** Two in-memory tabs. This test APK declares no permissions and opens no URLs. */
public final class BrowserActivity extends Activity {
    private TextView address;
    private String first, second;
    private boolean other;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        boolean bottom = getPackageName().equals("com.sec.android.app.sbrowser");
        LinearLayout page = new LinearLayout(this);
        page.setOrientation(LinearLayout.VERTICAL);
        LinearLayout toolbar = new LinearLayout(this);
        toolbar.setGravity(Gravity.CENTER_VERTICAL);
        address = new TextView(this);
        address.setId(getResources().getIdentifier(bottom ? "location_bar_edit_text" : "url_bar", "id", getPackageName()));
        address.setTextSize(16);
        address.setSingleLine(true);
        toolbar.addView(address,new LinearLayout.LayoutParams(0,-2,1));
        Button tabs = new Button(this);
        tabs.setText("Outra aba");
        tabs.setOnClickListener(view -> {
            other = !other;
            address.setText(other ? second : first);
        });
        toolbar.addView(tabs);
        TextView local = new TextView(this);
        local.setText("Página de teste local");
        local.setGravity(Gravity.CENTER);
        if (!bottom) page.addView(toolbar);
        page.addView(local,new LinearLayout.LayoutParams(-1,0,1));
        if (bottom) page.addView(toolbar);
        setContentView(page);
        open(getIntent());
    }
    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        open(intent);
    }
    private void open(Intent intent) {
        first = intent.getStringExtra("endereco");
        second = intent.getStringExtra("outra");
        other = false;
        address.setText(first);
    }
}
