package br.local.pulso;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.TimePickerDialog;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.widget.*;
import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import static br.local.pulso.Ui.*;
import static br.local.pulso.R.color.*;

public final class MainActivity extends Activity {
    private static final Locale PT = new Locale("pt","BR");
    private static final String[] TABS = {"Foco","Lista","Agenda","Histórico"};
    private static final String[] DAYS = {"Seg","Ter","Qua","Qui","Sex","Sáb","Dom"};
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable observer = () -> render(false);
    private Model model;
    private Focus f;
    private AppCatalog appCatalog;
    private LinearLayout root, body, nav;
    private ScrollView scroll;
    private TextView status, today;
    private Button themeButton;
    private AlertDialog openDialog;
    private TimePickerDialog timePicker;
    private interface SaveDialog { void save(Bundle state); }
    private SaveDialog saveDialog;
    private Bundle restoring = Bundle.EMPTY;
    private Ui.Dial dial;
    private final Map<Limits.Quota,TextView> usageLabels = new LinkedHashMap<>();
    private final Map<Focus.Routine,TextView> routineUsage = new LinkedHashMap<>();
    private int tab, historyLimit=30, pendingScroll=-1;
    private boolean lastService;
    private final Runnable pulse = new Runnable() {
        @Override public void run() {
            long now=System.currentTimeMillis();
            model.tick(now);
            if (lastService != FocusService.connected) render(false);
            updateClock(now);
            handler.postDelayed(this, 1000 - Math.floorMod(now,1000));
        }
    };

    @Override protected void attachBaseContext(Context base) {
        super.attachBaseContext(base);
        if (Build.VERSION.SDK_INT < 31) Appearance.read(base).applyToContext(this);
    }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        Appearance.read(this).applyToPlatform(this);
        model=Model.get(this); f=model.state;
        if(saved!=null) {
            tab=Math.max(0,Math.min(TABS.length-1,saved.getInt("aba")));
            historyLimit=saved.getInt("historicoVisivel",30);
            pendingScroll=saved.getInt("rolagem");
        }
        model.tick(System.currentTimeMillis());
        buildFrame();
        render(false);
        if(saved!=null) restoreDialog(saved.getBundle("dialogo"));
        if(model.recovered) toast("O Pulso recuperou os dados locais disponíveis.");
    }
    private void buildFrame() {
        getWindow().setStatusBarColor(Color.TRANSPARENT);
        getWindow().setNavigationBarColor(Color.TRANSPARENT);
        getWindow().setNavigationBarContrastEnforced(false);
        boolean light=getResources().getBoolean(R.bool.light_system_bars);
        if(Build.VERSION.SDK_INT>=30) getWindow().setDecorFitsSystemWindows(false);
        else getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LAYOUT_STABLE|View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN|
            View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION|(light?View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR|View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR:0));
        root=col(this); root.setBackgroundColor(getColor(page_background));
        root.setOnApplyWindowInsetsListener((v,insets)->{
            if(Build.VERSION.SDK_INT>=30) {
                android.graphics.Insets edge=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout()|WindowInsets.Type.ime());
                v.setPadding(edge.left,edge.top,edge.right,edge.bottom);
            } else v.setPadding(insets.getSystemWindowInsetLeft(),insets.getSystemWindowInsetTop(),insets.getSystemWindowInsetRight(),insets.getSystemWindowInsetBottom());
            return insets;
        });
        setContentView(root);
        if(Build.VERSION.SDK_INT>=30) {
            int flags=WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS|WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS;
            getWindow().getInsetsController().setSystemBarsAppearance(light?flags:0,flags);
        }
        LinearLayout top=row(this); pad(top,19,8,19,5);
        ImageView mark=new ImageView(this); mark.setImageResource(R.drawable.pulso);
        mark.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        top.addView(mark,new LinearLayout.LayoutParams(dp(this,32),dp(this,32)));
        TextView brand=text(this,"Pulso",23,text_primary,true); weighted(top,brand,8);
        status=text(this,"",13,text_primary,true); pad(status,12,7,12,7); top.addView(status);
        themeButton=button(this,"Tema",accent,accent_surface,this::chooseAppearance);
        themeButton.setMinWidth(dp(this,52));
        LinearLayout.LayoutParams themeParams=new LinearLayout.LayoutParams(WRAP,WRAP);
        themeParams.leftMargin=dp(this,8); top.addView(themeButton,themeParams);
        updateThemeLabel();
        put(root,top,0);
        scroll=new ScrollView(this); scroll.setFillViewport(false); scroll.setClipToPadding(false);
        root.addView(scroll,new LinearLayout.LayoutParams(FULL,0,1));
        scroll.getViewTreeObserver().addOnGlobalLayoutListener(()->{
            if(pendingScroll>=0) { scroll.scrollTo(0,pendingScroll); pendingScroll=-1; }
        });
        body=col(this); pad(body,18,12,18,18); scroll.addView(body);
        View line=new View(this); line.setBackgroundColor(getColor(outline)); root.addView(line,new LinearLayout.LayoutParams(FULL,dp(this,1)));
        nav=row(this); nav.setBackgroundColor(getColor(card_surface)); pad(nav,5,4,5,4); put(root,nav,0);
        root.requestApplyInsets();
    }
    @Override protected void onResume() { super.onResume(); model.listen(observer); render(false); handler.removeCallbacks(pulse); pulse.run(); }
    @Override protected void onPause() { handler.removeCallbacks(pulse); model.unlisten(observer); super.onPause(); }
    @Override protected void onSaveInstanceState(Bundle out) {
        super.onSaveInstanceState(out);
        out.putInt("aba",tab); out.putInt("historicoVisivel",historyLimit);
        out.putInt("rolagem",pendingScroll>=0?pendingScroll:scroll.getScrollY());
        if(openDialog!=null && openDialog.isShowing()) {
            Bundle state=new Bundle();
            saveDialog.save(state);
            if(timePicker!=null && timePicker.isShowing()) state.putBundle("relogio",timePicker.onSaveInstanceState());
            out.putBundle("dialogo",state);
        }
    }
    @Override protected void onDestroy() {
        if(timePicker!=null) timePicker.dismiss();
        if(openDialog!=null) openDialog.dismiss();
        super.onDestroy();
    }
    private void remember(AlertDialog dialog,String kind,SaveDialog details,EditText... inputs) {
        openDialog=dialog;
        for(int i=0;i<inputs.length;i++) {
            String text=restoring.getString("campo"+i);
            if(text!=null) inputs[i].setText(text);
        }
        int focus=restoring.getInt("campoAtivo",-1);
        if(focus>=0 && focus<inputs.length) {
            EditText input=inputs[focus];
            input.requestFocus();
            int length=input.length();
            input.setSelection(Math.min(length,restoring.getInt("selecaoInicio")),Math.min(length,restoring.getInt("selecaoFim")));
        }
        saveDialog=state->{
            state.putString("tipo",kind);
            if(details!=null) details.save(state);
            for(int i=0;i<inputs.length;i++) {
                EditText input=inputs[i];
                state.putString("campo"+i,input.getText().toString());
                if(input.hasFocus()) {
                    state.putInt("campoAtivo",i);
                    state.putInt("selecaoInicio",Math.max(0,input.getSelectionStart()));
                    state.putInt("selecaoFim",Math.max(0,input.getSelectionEnd()));
                }
            }
        };
        dialog.setOnDismissListener(ignored->{
            if(openDialog==dialog) {openDialog=null; saveDialog=null;}
        });
    }
    private void restoreDialog(Bundle state) {
        if(state==null)return;
        restoring=state;
        switch(state.getString("tipo","")) {
            case "tema": chooseAppearance(); break;
            case "permissao": permission(); break;
            case "duracao": if(f.active==null)customDuration(); break;
            case "site": addSite(); break;
            case "categoriaSite":
                for (SiteCatalog.Category category : SiteCatalog.Category.values())
                    if (category.key.equals(state.getString("categoria"))) chooseSiteMode(category);
                break;
            case "apps": appPicker(); break;
            case "limite":
                String key=state.getString("alvo","");
                if(f.hasTarget(key))editLimits(key,state.getString("titulo"));
                break;
            case "encerrar":
                if(f.active!=null && f.active.start==state.getLong("inicio")) confirmEnd(f.active);
                break;
            case "agenda":
            case "excluir":
                int index=state.getInt("indice",-1);
                if(state.getBoolean("nova")) editRoutine(null);
                else if(index>=0 && index<f.routines.size() && f.routines.get(index).name.equals(state.getString("nomeOriginal"))) {
                    Focus.Routine routine=f.routines.get(index);
                    if("agenda".equals(state.getString("tipo")))editRoutine(routine);
                    else confirmDelete(routine);
                }
                break;
        }
        restoring=Bundle.EMPTY;
    }
    private void updateThemeLabel() {
        if(Build.VERSION.SDK_INT>=30) themeButton.setStateDescription(Appearance.read(this).label);
    }
    private void chooseAppearance() {
        Appearance[] options=Appearance.values();
        String[] labels=new String[options.length];
        for(int i=0;i<options.length;i++) labels[i]=options[i].label;
        Appearance current=Appearance.read(this);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Tema").setNegativeButton("Cancelar",null)
            .setSingleChoiceItems(labels,current.ordinal(),(choice,index)->{
                choice.dismiss();
                Appearance next=options[index];
                if(next==current)return;
                next.save(this);
                updateThemeLabel();
                if(Build.VERSION.SDK_INT<31) recreate();
            }).show();
        remember(dialog,"tema",null);
    }
    private static String time(long epoch,String pattern) {
        return Instant.ofEpochMilli(epoch).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern(pattern,PT));
    }
    private void go(int next) { tab=next; render(true); }
    private void render(boolean resetScroll) {
        if(body==null)return;
        if(resetScroll) pendingScroll=0;
        else if(pendingScroll<0) pendingScroll=scroll.getScrollY();
        body.removeAllViews(); nav.removeAllViews(); today=null; dial=null; usageLabels.clear(); routineUsage.clear();
        lastService=FocusService.connected;
        for(int i=0;i<TABS.length;i++) {
            final int dest=i;
            Button b=button(this,TABS[i],tab==i?accent:text_muted,tab==i?accent_surface:card_surface,()->go(dest));
            b.setSelected(tab==i); weighted(nav,b,i==0?0:2);
        }
        String[] titles={"Sessão de foco","Lista de bloqueio","Agendamentos","Histórico"};
        TextView title=text(this,titles[tab],29,text_primary,true); title.setAccessibilityHeading(true); put(body,title,0);
        if(tab==0)home();
        else if(tab==1)rules();
        else if(tab==2)agenda();
        else history();
        updateClock(System.currentTimeMillis());
    }
    private void updateClock(long now) {
        boolean running=f.active!=null && f.active.end>now;
        String label=running?"Em foco":"Em espera";
        if(!label.contentEquals(status.getText())) {
            status.setText(label); status.setTextColor(getColor(running?on_focus_action:accent));
            status.setBackground(bg(this,running?focus_action:accent_surface,20,false));
        }
        if(dial!=null) {
            long remaining=running?Math.max(0,(f.active.end-now+999)/1000):f.minutes*60L;
            double portion=running?(double)(now-f.active.start)/(f.active.end-f.active.start):Math.min(1,f.minutes/60.0);
            dial.show(remaining,portion,running);
        }
        if(today!=null) {
            String total=duration(f.todayMillis(now,ZoneId.systemDefault()));
            if(!total.contentEquals(today.getText()))today.setText(total);
        }
        if (!usageLabels.isEmpty() || !routineUsage.isEmpty()) {
            Limits.Window window=Limits.Window.at(now,ZoneId.systemDefault());
            for (Map.Entry<Limits.Quota,TextView> row:usageLabels.entrySet()) {
                String value=usageSummary(row.getKey(),window,Long.MAX_VALUE);
                if (!value.contentEquals(row.getValue().getText())) row.getValue().setText(value);
            }
            for (Map.Entry<Focus.Routine,TextView> row:routineUsage.entrySet()) {
                Focus.Routine r=row.getKey();
                Focus.Slot slot=Focus.current(r,now,window.zone);
                String value=slot==null ? "Fora do horário" : usageSummary(r.quota,window,slot.end);
                if (!value.contentEquals(row.getValue().getText())) row.getValue().setText(value);
            }
        }
    }
    private void home() {
        long now=System.currentTimeMillis();
        String date=time(now,"EEEE, d 'de' MMMM");
        put(body,text(this,Character.toUpperCase(date.charAt(0))+date.substring(1),14,text_muted,false),3);
        boolean active=f.active!=null;
        LinearLayout hero=col(this); hero.setBackground(bg(this,focus_surface,21,false)); pad(hero,17,15,17,15); put(body,hero,14);
        TextView label=text(this,active?f.active.title:"Foco rápido",19,on_focus,true);
        label.setGravity(Gravity.CENTER); put(hero,label,0);
        if(active) {
            TextView detail=text(this,(f.active.scheduled?"Sessão agendada":"Sessão manual")+" até "+time(f.active.end,"HH:mm"),13,on_focus_secondary,false);
            detail.setGravity(Gravity.CENTER); put(hero,detail,3);
        }
        dial=new Dial(this); hero.addView(dial,new LinearLayout.LayoutParams(FULL,dp(this,157)));
        if(!active) {
            LinearLayout choices=row(this); put(hero,choices,0);
            for(int value:new int[]{15,25,50,0}) {
                boolean custom=f.minutes!=15 && f.minutes!=25 && f.minutes!=50;
                boolean chosen=value==0?custom:f.minutes==value;
                String s=value==0?(custom?f.minutes+" min":"Outro"):value+" min";
                Button b=button(this,s,chosen?on_focus_choice:on_focus,chosen?focus_choice:focus_secondary_surface,()->{
                    if(value==0)customDuration();
                    else if(f.active==null){f.minutes=value; model.save();}
                });
                b.setSelected(chosen); weighted(choices,b,value==15?0:5);
            }
        }
        put(hero,button(this,active?"Encerrar sessão":"Iniciar sessão",on_focus_action,focus_action,()->{
            long at=System.currentTimeMillis(); model.tick(at);
            if(f.active==null) model.edit(()->f.start(at,f.minutes));
            else confirmEnd(f.active);
        }),9);
        LinearLayout protection=card(body);
        heading(protection,lastService?"Bloqueio ativado":"Ativar o bloqueio",lastService
            ? count(f.apps.size(),"aplicativo","aplicativos")+" e "+count(f.sites.size(),"site","sites")+" na sua lista."
            : "Ative o serviço para aplicar as sessões, os limites de uso e o bloqueio de categorias de sites.");
        put(protection,button(this,lastService?"Editar lista":"Configurar acessibilidade",accent,accent_surface,()->{if(lastService)go(1);else permission();}),10);
        LinearLayout summary=card(body); heading(summary,"Foco de hoje",""); today=text(this,"",25,accent,true); put(summary,today,6);
        Focus.Slot next=f.next(now,ZoneId.systemDefault());
        if(next!=null) put(summary,text(this,"Próximo agendamento · "+next.routine.name+" · "+time(next.start,"EEE HH:mm"),14,text_muted,false),6);
    }
    private static String count(int n,String one,String many){return n+" "+(n==1?one:many);}
    private void toast(String s){Toast.makeText(this,s,Toast.LENGTH_SHORT).show();}
    private void confirmEnd(Focus.Session current) {
        AlertDialog dialog=confirm("Encerrar a sessão?","O tempo decorrido fica no histórico. Os limites de uso continuam valendo.","Encerrar sessão",()->{
            if(f.active==current) model.edit(()->f.stop(System.currentTimeMillis(),ZoneId.systemDefault()));
        });
        remember(dialog,"encerrar",state->state.putLong("inicio",current.start));
    }
    private AlertDialog confirm(String title,String message,String action,Runnable yes){
        return new AlertDialog.Builder(this).setTitle(title).setMessage(message).setNegativeButton("Cancelar",null)
            .setPositiveButton(action,(d,w)->yes.run()).show();
    }
    private void permission() {
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Serviço de bloqueio")
            .setMessage("O Pulso identifica o app em primeiro plano e lê a barra de endereço visível no Chrome, Edge e Samsung Internet. Ele usa o domínio para aplicar as categorias escolhidas e os limites de uso. Um app em pausa volta à tela inicial e um site em pausa recebe um aviso sobre a página. A opção Sempre protege sites também fora do foco. Os dados ficam neste celular.\n\nEm Acessibilidade, abra Aplicativos instalados e selecione Pulso • Bloqueio de foco. Você pode desligá-lo no mesmo lugar.")
            .setNegativeButton("Agora não",null).setPositiveButton("Abrir Acessibilidade",(d,w)->{
                try {startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));}
                catch(ActivityNotFoundException e){toast("Abra Acessibilidade nas Configurações do celular.");}
            }).show();
        remember(dialog,"permissao",null);
    }
    private EditText field(String value,String label,boolean numeric) {
        EditText edit=new EditText(this); edit.setSingleLine(true); edit.setTextSize(16); edit.setTextColor(getColor(text_primary));
        edit.setHintTextColor(getColor(text_muted));
        edit.setInputType(numeric?InputType.TYPE_CLASS_NUMBER:InputType.TYPE_CLASS_TEXT);
        edit.setText(value); edit.setHint(label); edit.setSelectAllOnFocus(true); edit.setId(View.generateViewId()); return edit;
    }
    private void fieldLabel(LinearLayout box,String label,EditText field) {
        TextView t=text(this,label,14,text_primary,true); t.setLabelFor(field.getId()); put(box,t,10); put(box,field,2);
    }
    private LinearLayout fields(){LinearLayout l=col(this);pad(l,22,5,22,5);return l;}
    private interface Validate { boolean submit(); }
    private AlertDialog form(String title,LinearLayout fields,String save,Validate callback) {
        return form(title,fields,save,callback,null);
    }
    private AlertDialog form(String title,LinearLayout fields,String save,Validate callback,Runnable disable) {
        ScrollView wrap=new ScrollView(this); wrap.addView(fields);
        AlertDialog.Builder builder=new AlertDialog.Builder(this).setTitle(title).setView(wrap).setNegativeButton("Cancelar",null).setPositiveButton(save,null);
        if(disable!=null)builder.setNeutralButton("Desativar limites",(d,w)->disable.run());
        AlertDialog dialog=builder.create();
        dialog.setOnShowListener(d->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->{if(callback.submit())dialog.dismiss();}));
        dialog.show();
        return dialog;
    }
    private int validDuration(EditText edit) {
        return requiredMinutes(edit,Focus.MAX_MINUTES);
    }
    private int requiredMinutes(EditText edit,int max) {
        try{int n=Integer.parseInt(edit.getText().toString().trim());if(n>=1 && n<=max)return n;}catch(NumberFormatException ignored){}
        edit.setError("Digite de 1 a "+max+" minutos.");edit.requestFocus();return 0;
    }
    private void customDuration(){
        LinearLayout box=fields();EditText edit=field(""+f.minutes,"Minutos",true);fieldLabel(box,"Duração em minutos",edit);
        AlertDialog dialog=form("Duração da sessão",box,"Usar duração",()->{
            int n=validDuration(edit);if(n==0)return false;
            if(f.active==null){f.minutes=n;model.save();}return true;
        });
        remember(dialog,"duracao",null,edit);
    }
    private void rules() {
        if (appCatalog == null) appCatalog = Apps.catalog(this);
        List<AppCatalog.Group> groups = Apps.groups(this, appCatalog, f.apps, false);
        int selected = 0;
        for (AppCatalog.Group group : groups) selected += group.items.size();
        LinearLayout apps=card(body);
        heading(apps,"Aplicativos",selected==0?"Escolha os apps para pausar durante o foco ou limitar por tempo de uso.":count(selected,"app selecionado","apps selecionados"));
        put(apps,button(this,"Escolher aplicativos",accent,accent_surface,this::appPicker),10);
        LinearLayout sites=card(body);
        TextView siteHeading=heading(sites,"Sites","No Chrome, Edge e Samsung Internet, o Pulso verifica o domínio visível na barra de endereço. As regras incluem subdomínios.");
        siteHeading.setAccessibilityHeading(true);
        if (selected>=5) {
            siteHeading.setFocusableInTouchMode(true);
            put(apps,button(this,"Ir para sites",accent,accent_surface,()->{
                siteHeading.requestFocus();
                android.view.accessibility.AccessibilityManager accessibility=getSystemService(android.view.accessibility.AccessibilityManager.class);
                if (accessibility!=null && accessibility.isTouchExplorationEnabled())
                    siteHeading.performAccessibilityAction(android.view.accessibility.AccessibilityNodeInfo.ACTION_ACCESSIBILITY_FOCUS,null);
                scroll.smoothScrollTo(0,sites.getTop());
            }),6);
        }
        for (AppCatalog.Group group : groups) {
            TextView category = text(this, group.label + " · " + count(group.items.size(), "app", "apps"), 16, accent, true);
            category.setAccessibilityHeading(true);
            put(apps, category, 15);
            for (AppCatalog.Item app : group.items) ruleRow(apps, app.name, Limits.app(app.pkg), null);
        }
        for(String site:f.sites)ruleRow(sites,site,Limits.site(site),()->model.edit(()->f.sites.remove(site)));
        put(sites,button(this,"Adicionar site",accent,accent_surface,this::addSite),9);
        put(sites,text(this,"Categorias de sites",16,text_primary,true),14);
        put(sites,text(this,"Durante o foco inclui a sessão rápida e os agendamentos, mesmo com saldo disponível. Sempre vale enquanto o serviço de bloqueio está ativo.",14,text_muted,false),4);
        for (SiteCatalog.Category category : SiteCatalog.Category.values()) {
            String label = category.label+" · "+f.siteModes.get(category).label;
            put(sites,button(this,label,accent,accent_surface,()->chooseSiteMode(category)),6);
        }
        LinearLayout shared=card(body);
        heading(shared,"Limite compartilhado","Os apps e sites da lista usam o mesmo saldo. Você também pode definir um limite em cada item.");
        usageLabel(shared,Limits.SHARED);
        put(shared,button(this,"Editar limite compartilhado",accent,accent_surface,()->editLimits(Limits.SHARED,"Limite compartilhado")),8);
        put(shared,text(this,"Para usar um saldo próprio só em certos dias e horários, configure-o na Agenda.",14,text_muted,false),10);
        put(shared,button(this,"Limites por agendamento",accent,accent_surface,()->go(2)),6);
        LinearLayout setup=card(body); heading(setup,"Serviço de bloqueio",lastService?"O serviço está ativo neste celular.":"Ative em Acessibilidade para contar o uso e aplicar os bloqueios.");
        put(setup,button(this,lastService?"Gerenciar serviço":"Configurar acessibilidade",accent,accent_surface,this::permission),8);
    }
    private void chooseSiteMode(SiteCatalog.Category category) {
        SiteCatalog.Mode[] options=SiteCatalog.Mode.values();
        String[] labels=new String[options.length];
        for (int i=0; i<options.length; i++) labels[i]=options[i].label;
        SiteCatalog.Mode current=f.siteModes.get(category);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(category.label).setNegativeButton("Cancelar",null)
            .setSingleChoiceItems(labels,current.ordinal(),(choice,index)->{
                choice.dismiss();
                if (options[index]!=current) model.edit(()->f.siteModes.set(category,options[index]));
            }).show();
        remember(dialog,"categoriaSite",state->state.putString("categoria",category.key));
    }
    private void ruleRow(LinearLayout parent,String label,String key,Runnable removeItem) {
        LinearLayout line=row(this); put(parent,line,5);
        weighted(line,text(this,label,16,text_primary,false),0);
        String title="Limite de "+label;
        Button edit=button(this,"Limite",accent,accent_surface,()->editLimits(key,title));
        edit.setContentDescription(title); line.addView(edit);
        if(removeItem!=null) {
            Button remove=button(this,"Remover",accent,card_surface,removeItem);
            remove.setContentDescription("Remover "+label); line.addView(remove);
        }
        usageLabel(parent,key);
    }
    private void usageLabel(LinearLayout parent,String key) {
        Limits.Quota q=f.limits.get(key);
        if(q==null)return;
        TextView label=text(this,"",14,text_muted,false); put(parent,label,3); usageLabels.put(q,label);
    }
    private static String usageTime(long ms) {
        long seconds=ms/1000;
        if(seconds<60)return seconds+" s";
        return seconds/60+" min"+(seconds%60==0?"":" "+seconds%60+" s");
    }
    private static String usageSummary(Limits.Quota q,Limits.Window w,long windowEnd) {
        List<String> lines=new ArrayList<>();
        if(q.hourMinutes>0)lines.add("Nesta hora · "+usageTime(q.hourUsed(w))+" de "+q.hourMinutes+" min");
        if(q.dayMinutes>0)lines.add("Hoje · "+usageTime(q.dayUsed(w))+" de "+q.dayMinutes+" min");
        Limits.Block block=q.blocked(w);
        if(block!=null)lines.add("Em pausa até "+time(Math.min(block.end,windowEnd),"HH:mm"));
        return String.join("\n",lines);
    }
    private int limitValue(EditText edit,int max) {
        String value=edit.getText().toString().trim();
        if(value.isEmpty())return 0;
        try {
            int n=Integer.parseInt(value);
            if(n>=1 && n<=max)return n;
        } catch(NumberFormatException invalid) {}
        edit.setError("Digite de 1 a "+max+" minutos ou deixe em branco."); edit.requestFocus(); return -1;
    }
    private void editLimits(String key,String title) {
        Limits.Quota q=f.limits.get(key);
        LinearLayout box=fields();
        put(box,text(this,"O saldo por hora se renova na hora cheia e o diário, à meia-noite. Deixe o campo vazio para desativar o período.",14,text_muted,false),5);
        EditText hour=field(q==null || q.hourMinutes==0?"":Integer.toString(q.hourMinutes),"1 a 60",true);
        EditText day=field(q==null || q.dayMinutes==0?"":Integer.toString(q.dayMinutes),"1 a 1440",true);
        fieldLabel(box,"Minutos por hora",hour); fieldLabel(box,"Minutos por dia",day);
        AlertDialog dialog=form(title,box,"Salvar limites",()->{
            int h=limitValue(hour,Limits.MAX_HOUR); if(h<0)return false;
            int d=limitValue(day,Limits.MAX_DAY); if(d<0)return false;
            model.edit(()->f.limits.set(key,h,d)); return true;
        },q==null?null:()->model.edit(()->f.limits.set(key,0,0)));
        remember(dialog,"limite",state->{state.putString("alvo",key); state.putString("titulo",title);},hour,day);
    }
    private void addSite(){
        LinearLayout box=fields();EditText e=field("","exemplo.com",false);
        e.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_URI);fieldLabel(box,"Domínio ou link",e);
        AlertDialog dialog=form("Adicionar site",box,"Adicionar",()->{
            String host=Focus.rule(e.getText().toString());
            if(host==null){e.setError("Digite um domínio válido, como exemplo.com.");return false;}
            if(f.sites.contains(host)){e.setError("Este domínio já está na lista.");return false;}
            model.edit(()->f.sites.add(host));return true;
        });
        remember(dialog,"site",null,e);
    }
    private void appPicker() {
        List<String> original = restoring.getStringArrayList("originais");
        List<String> draft = restoring.getStringArrayList("selecionados");
        AppCatalog.Selection selection = new AppCatalog.Selection(original == null ? f.apps : original,
            draft == null ? f.apps : draft);
        Set<String> retained = new LinkedHashSet<>(f.apps);
        retained.addAll(selection.original);
        retained.addAll(selection.checked);
        List<AppCatalog.Group> groups = Apps.groups(this, appCatalog, retained, true);

        LinearLayout box = fields();
        EditText search = field("", "Buscar aplicativo", false);
        TextView label = text(this, "Nome, pacote ou categoria", 14, text_primary, true);
        label.setLabelFor(search.getId());
        put(box, label, 6);
        LinearLayout searchBar = row(this);
        weighted(searchBar, search, 0);
        Button clear = button(this, "Limpar busca", accent, accent_surface, () -> search.setText(""));
        clear.setVisibility(View.INVISIBLE);
        searchBar.addView(clear);
        put(box, searchBar, 2);
        TextView total = text(this, "", 14, text_muted, false);
        total.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        put(box, total, 5);
        ListView list = new ListView(this);
        list.setItemsCanFocus(true);
        TextView empty = text(this, "Nenhum aplicativo encontrado.", 14, text_muted, false);
        empty.setGravity(Gravity.CENTER);
        FrameLayout results = new FrameLayout(this);
        results.addView(list);
        results.addView(empty);
        list.setEmptyView(empty);
        int maxHeight = (int)(getResources().getDisplayMetrics().heightPixels * .42f);
        LinearLayout.LayoutParams space = new LinearLayout.LayoutParams(FULL, Math.min(dp(this, 360), maxHeight), 1);
        space.topMargin = dp(this, 6);
        box.addView(results, space);
        AppPickerAdapter adapter = new AppPickerAdapter(this, groups, selection, total);
        list.setAdapter(adapter);
        search.addTextChangedListener(new TextWatcher(){
            public void beforeTextChanged(CharSequence s,int start,int count,int after){}
            public void onTextChanged(CharSequence s,int start,int before,int count){
                clear.setVisibility(s.length() == 0 ? View.INVISIBLE : View.VISIBLE);
                adapter.filter(s.toString());
                list.setSelection(0);
            }
            public void afterTextChanged(Editable s){}
        });
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Escolher aplicativos").setView(box).setNegativeButton("Cancelar",null)
            .setPositiveButton("Salvar lista",(d,w)->{
                if (!selection.original.equals(selection.checked)) model.edit(() -> selection.applyTo(f.apps));
            }).show();
        remember(dialog,"apps",state->{
            state.putStringArrayList("originais",new ArrayList<>(selection.original));
            state.putStringArrayList("selecionados",new ArrayList<>(selection.checked));
            state.putInt("item",list.getFirstVisiblePosition());
            if(list.getChildCount()>0)state.putInt("deslocamento",list.getChildAt(0).getTop());
        },search);
        if(restoring.containsKey("item"))list.setSelectionFromTop(restoring.getInt("item"),restoring.getInt("deslocamento"));
    }
    private static String hm(int value) { return String.format(Locale.ROOT,"%02d:%02d",value/60,value%60); }
    private static String repeat(int mask) {
        if(mask==127)return "Todos os dias"; if(mask==31)return "Segunda a sexta";
        StringBuilder s=new StringBuilder(); for(int i=0;i<7;i++)if((mask&(1<<i))!=0){if(s.length()>0)s.append(" · ");s.append(DAYS[i]);}return s.toString();
    }
    private void agenda(){
        put(body,text(this,"Os horários seguem o relógio do celular. Com o serviço ativo, o bloqueio funciona em segundo plano.",14,text_muted,false),6);
        put(body,button(this,"Novo agendamento",on_accent,accent,()->editRoutine(null)),12);
        if(f.routines.isEmpty()){heading(card(body),"Nenhum agendamento","Escolha os dias, o horário e a duração do seu primeiro agendamento.");return;}
        for(Focus.Routine r:f.routines){
            LinearLayout c=card(body);heading(c,r.name,repeat(r.days)+"  ·  "+hm(r.startMinute)+"  ·  "+r.minutes+" min");
            put(c,text(this,r.quota.hourMinutes==0?"Bloqueio imediato":"Saldo compartilhado · "+r.quota.hourMinutes+" min por hora",15,text_primary,true),7);
            if(r.quota.hourMinutes>0 && r.enabled) {
                TextView usage=text(this,"",14,text_muted,false); put(c,usage,3); routineUsage.put(r,usage);
            }
            Switch sw=new Switch(this);sw.setText(r.enabled?"Agendamento ativo":"Agendamento desativado");sw.setTextColor(getColor(text_primary));
            sw.setMinHeight(dp(this,48));sw.setChecked(r.enabled);sw.setContentDescription("Ativar "+r.name);put(c,sw,4);
            sw.setOnCheckedChangeListener((v,on)->model.edit(()->r.enabled=on));
            LinearLayout line=row(this);put(c,line,3);weighted(line,button(this,"Editar",accent,accent_surface,()->editRoutine(r)),0);
            weighted(line,button(this,"Excluir",accent,card_surface,()->confirmDelete(r)),6);
        }
    }
    private void saveRoutineIdentity(Bundle state,Focus.Routine routine) {
        state.putBoolean("nova",routine==null);
        if(routine!=null) {
            state.putInt("indice",f.routines.indexOf(routine));
            state.putString("nomeOriginal",routine.name);
        }
    }
    private void confirmDelete(Focus.Routine routine) {
        AlertDialog dialog=confirm("Excluir agendamento?","O Pulso deixará de aplicar as regras de “"+routine.name+"”.","Excluir",
            ()->model.edit(()->f.routines.remove(routine)));
        remember(dialog,"excluir",state->saveRoutineIdentity(state,routine));
    }
    private void pickTime(Button clock,int[] start,Bundle saved) {
        TimePickerDialog picker=new TimePickerDialog(this,(view,h,m)->{
            start[0]=h*60+m; clock.setText("Início · "+hm(start[0]));
        },start[0]/60,start[0]%60,true);
        timePicker=picker;
        picker.setOnDismissListener(ignored->{if(timePicker==picker)timePicker=null;});
        picker.show();
        if(saved!=null)picker.onRestoreInstanceState(saved);
    }
    private RadioButton scheduleOption(RadioGroup group,String label) {
        RadioButton option=new RadioButton(this);
        option.setId(View.generateViewId()); option.setText(label); option.setTextColor(getColor(text_primary));
        option.setMinHeight(dp(this,48)); group.addView(option); return option;
    }
    private void editRoutine(Focus.Routine original){
        boolean fresh=original==null;LinearLayout box=fields();
        EditText name=field(fresh?"":original.name,"Nome da sessão",false);name.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(60)});
        fieldLabel(box,"Nome da sessão",name);int[] start={restoring.getInt("hora",fresh?540:original.startMinute)};
        Button clock=button(this,"Início · "+hm(start[0]),accent,accent_surface,()->{});put(box,clock,9);
        clock.setOnClickListener(v->pickTime(clock,start,null));
        EditText minutes=field(""+(fresh?f.minutes:original.minutes),"Minutos",true);fieldLabel(box,"Duração em minutos",minutes);
        put(box,text(this,"Repetir nos dias",14,text_primary,true),10);
        CheckBox[] checks=new CheckBox[7];int mask=restoring.getInt("dias",fresh?31:original.days);
        LinearLayout line=null;
        for(int i=0;i<7;i++){
            if(i%3==0){line=row(this);put(box,line,0);}
            checks[i]=new CheckBox(this);checks[i].setText(DAYS[i]);checks[i].setChecked((mask&(1<<i))!=0);checks[i].setMinHeight(dp(this,48));weighted(line,checks[i],0);
        }
        put(box,text(this,"Uso da lista durante o agendamento",14,text_primary,true),10);
        RadioGroup mode=new RadioGroup(this); put(box,mode,2);
        RadioButton direct=scheduleOption(mode,"Bloqueio imediato");
        RadioButton metered=scheduleOption(mode,"Limitar minutos por hora");
        LinearLayout budget=col(this); put(box,budget,0);
        int savedLimit=fresh?0:original.quota.hourMinutes;
        EditText hour=field(savedLimit==0?"":Integer.toString(savedLimit),"1 a 60",true);
        fieldLabel(budget,"Minutos compartilhados por hora",hour);
        put(budget,text(this,"Apps e sites da lista dividem este saldo. Ele se renova na hora cheia e só vale neste agendamento.",14,text_muted,false),5);
        mode.setOnCheckedChangeListener((group,id)->budget.setVisibility(id==metered.getId()?View.VISIBLE:View.GONE));
        mode.check(restoring.getBoolean("porUso",savedLimit>0)?metered.getId():direct.getId());
        TextView error=text(this,"",14,error_text,false);error.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);put(box,error,2);
        AlertDialog dialog=form(fresh?"Novo agendamento":"Editar agendamento",box,"Salvar",()->{
            String label=name.getText().toString().trim();if(label.isEmpty()){name.setError("Dê um nome à sessão.");return false;}
            int n=validDuration(minutes);if(n==0)return false;
            int days=checkedDays(checks);
            if(days==0){error.setText("Selecione pelo menos um dia.");return false;}
            int limit=metered.isChecked()?requiredMinutes(hour,Limits.MAX_HOUR):0;
            if(metered.isChecked() && limit==0)return false;
            model.edit(()->{
                Focus.Routine r=fresh?new Focus.Routine(label,days,start[0],n):original;
                if(r.days!=days || r.startMinute!=start[0] || (r.quota.hourMinutes==0)!=(limit==0)) r.lastDay=Long.MIN_VALUE;
                r.name=label; r.days=days; r.startMinute=start[0]; r.minutes=n; r.setHourLimit(limit);
                if(fresh)f.routines.add(r);
            });
            return true;
        });
        remember(dialog,"agenda",state->{
            saveRoutineIdentity(state,original);
            state.putInt("hora",start[0]); state.putInt("dias",checkedDays(checks)); state.putBoolean("porUso",metered.isChecked());
        },name,minutes,hour);
        Bundle savedClock=restoring.getBundle("relogio");
        if(savedClock!=null)pickTime(clock,start,savedClock);
    }
    private static int checkedDays(CheckBox[] checks) {
        int days=0;
        for(int i=0;i<checks.length;i++) if(checks[i].isChecked()) days|=1<<i;
        return days;
    }
    private void history(){
        LinearLayout summary=card(body);heading(summary,"Tempo de foco hoje","");today=text(this,"",28,accent,true);put(summary,today,6);
        if(f.active!=null)put(summary,text(this,"Em andamento · "+f.active.title,14,text_muted,false),5);
        if(f.history.isEmpty()){heading(card(body),"Nenhuma sessão encerrada","O tempo decorrido aparece aqui quando você conclui ou encerra uma sessão.");return;}
        LinearLayout c=card(body);heading(c,"Sessões anteriores","");
        for(int i=0;i<Math.min(historyLimit,f.history.size());i++){
            Focus.Entry e=f.history.get(i);put(c,text(this,e.title,17,text_primary,true),13);
            put(c,text(this,time(e.start,"dd/MM/yyyy · HH:mm")+"  ·  "+(e.end-e.start<60000?"< 1 min":duration(e.end-e.start)),14,text_muted,false),4);
            put(c,text(this,e.completed?"Concluída":"Encerrada antes do fim",13,accent,false),3);
        }
        if(historyLimit<f.history.size())put(c,button(this,"Mostrar mais sessões",accent,accent_surface,()->{historyLimit+=30;render(false);}),10);
    }
}
