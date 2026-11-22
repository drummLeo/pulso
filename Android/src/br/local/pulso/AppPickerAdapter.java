package br.local.pulso;

import android.content.Context;
import android.view.Gravity;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.CheckedTextView;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.ArrayList;
import java.util.List;
import static br.local.pulso.R.color.*;
import static br.local.pulso.Ui.*;

final class AppPickerAdapter extends BaseAdapter {
    private final Context context;
    private final List<AppCatalog.Group> groups;
    private final AppCatalog.Selection selection;
    private final TextView total;
    private final List<Object> rows = new ArrayList<>();
    private boolean filtered;

    AppPickerAdapter(Context context, List<AppCatalog.Group> groups, AppCatalog.Selection selection, TextView total) {
        this.context = context;
        this.groups = groups;
        this.selection = selection;
        this.total = total;
        filter("");
        updateTotal();
    }

    void filter(String query) {
        filtered = !query.trim().isEmpty();
        rows.clear();
        for (AppCatalog.Group group : AppCatalog.filter(groups, query)) {
            rows.add(group);
            rows.addAll(group.items);
        }
        notifyDataSetChanged();
    }

    private void updateTotal() {
        int selected = 0;
        for (AppCatalog.Group group : groups) selected += selection.count(group);
        total.setText(selected + (selected == 1 ? " app selecionado" : " apps selecionados"));
    }

    private void selectionChanged() {
        updateTotal();
        notifyDataSetChanged();
    }

    @Override public int getCount() { return rows.size(); }
    @Override public Object getItem(int position) { return rows.get(position); }
    @Override public long getItemId(int position) { return position; }
    @Override public int getViewTypeCount() { return 2; }
    @Override public int getItemViewType(int position) { return rows.get(position) instanceof AppCatalog.Group ? 0 : 1; }

    @Override public View getView(int position, View recycled, ViewGroup parent) {
        Object entry = getItem(position);
        if (entry instanceof AppCatalog.Group) {
            Header header = recycled == null ? new Header() : (Header)recycled;
            header.bind((AppCatalog.Group)entry);
            return header;
        }
        AppCatalog.Item app = (AppCatalog.Item)entry;
        CheckedTextView item = (CheckedTextView)recycled;
        if (item == null) {
            item = (CheckedTextView)LayoutInflater.from(context)
                .inflate(android.R.layout.simple_list_item_multiple_choice, parent, false);
            item.setTextColor(context.getColor(text_primary));
            item.setMinHeight(dp(context, 48));
            item.setFocusable(true);
        }
        item.setText(app.name);
        item.setChecked(selection.checked.contains(app.pkg));
        item.setOnClickListener(view -> {
            selection.toggle(app);
            ((CheckedTextView)view).setChecked(selection.checked.contains(app.pkg));
            selectionChanged();
        });
        return item;
    }

    private final class Header extends LinearLayout {
        private final TextView name, count;
        private final Button action;
        private AppCatalog.Group group;

        Header() {
            super(context);
            setGravity(Gravity.CENTER_VERTICAL);
            setBackgroundColor(context.getColor(page_background));
            pad(this, 9, 5, 4, 5);
            LinearLayout labels = col(context);
            name = text(context, "", 16, text_primary, true);
            name.setAccessibilityHeading(true);
            put(labels, name, 0);
            count = text(context, "", 13, text_muted, false);
            put(labels, count, 3);
            weighted(this, labels, 0);
            action = button(context, "", accent, accent_surface, () -> {
                selection.toggle(group);
                selectionChanged();
            });
            addView(action);
        }

        void bind(AppCatalog.Group group) {
            this.group = group;
            int marked = selection.count(group);
            int size = group.items.size();
            name.setText(group.label);
            String noun = filtered ? (size == 1 ? "resultado" : "resultados") : (size == 1 ? "app" : "apps");
            count.setText(marked + " de " + size + " " + noun + (size == 1 ? " marcado" : " marcados"));
            String verb = marked == size ? "Desmarcar" : "Marcar";
            action.setText(verb);
            action.setContentDescription(verb + (filtered ? " resultados de " : " categoria ") + group.label);
        }
    }
}
