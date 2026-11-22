package br.local.pulso;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.TimePickerDialog;
import android.content.ActivityNotFoundException;
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

public final class MainActivity extends Activity {
    private static final Locale PT = new Locale("pt","BR");
    private static final String[] TABS = {"Foco","Lista","Agenda","Histórico"};
    private static final String[] DAYS = {"Seg","Ter","Qua","Qui","Sex","Sáb","Dom"};
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable observer = () -> render(false);
    private Model model;
    private Focus f;
    private LinearLayout root, body, nav;
    private ScrollView scroll;
    private TextView status, today;
    private Ui.Dial dial;
    private final Map<Limits.Quota,TextView> usageLabels = new LinkedHashMap<>();
    private int tab, historyLimit=30;
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

    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        model=Model.get(this); f=model.state;
        if(saved!=null)tab=Math.max(0,Math.min(3,saved.getInt("aba")));
        model.tick(System.currentTimeMillis());
        buildFrame();
        render(true);
        if(model.recovered) toast("O Pulso recuperou os dados locais disponíveis.");
    }
    private void buildFrame() {
        getWindow().setStatusBarColor(Color.TRANSPARENT);
        getWindow().setNavigationBarColor(Color.TRANSPARENT);
        if(Build.VERSION.SDK_INT>=30) getWindow().setDecorFitsSystemWindows(false);
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LAYOUT_STABLE|View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN|
            View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION|View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR|View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        root=col(this); root.setBackgroundColor(PAPER);
        root.setOnApplyWindowInsetsListener((v,insets)->{
            if(Build.VERSION.SDK_INT>=30) {
                android.graphics.Insets edge=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout()|WindowInsets.Type.ime());
                v.setPadding(edge.left,edge.top,edge.right,edge.bottom);
            } else v.setPadding(insets.getSystemWindowInsetLeft(),insets.getSystemWindowInsetTop(),insets.getSystemWindowInsetRight(),insets.getSystemWindowInsetBottom());
            return insets;
        });
        setContentView(root);
        LinearLayout top=row(this); pad(top,19,8,19,5);
        ImageView mark=new ImageView(this); mark.setImageResource(R.drawable.pulso);
        mark.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        top.addView(mark,new LinearLayout.LayoutParams(dp(this,32),dp(this,32)));
        TextView brand=text(this,"Pulso",23,INK,true); weighted(top,brand,8);
        status=text(this,"",13,INK,true); pad(status,12,7,12,7); top.addView(status);
        put(root,top,0);
        scroll=new ScrollView(this); scroll.setFillViewport(false); scroll.setClipToPadding(false);
        root.addView(scroll,new LinearLayout.LayoutParams(FULL,0,1));
        body=col(this); pad(body,18,12,18,18); scroll.addView(body);
        View line=new View(this); line.setBackgroundColor(LINE); root.addView(line,new LinearLayout.LayoutParams(FULL,dp(this,1)));
        nav=row(this); nav.setBackgroundColor(WHITE); pad(nav,5,4,5,4); put(root,nav,0);
        root.requestApplyInsets();
    }
    @Override protected void onResume() { super.onResume(); model.listen(observer); render(false); handler.removeCallbacks(pulse); pulse.run(); }
    @Override protected void onPause() { handler.removeCallbacks(pulse); model.unlisten(observer); super.onPause(); }
    @Override protected void onSaveInstanceState(Bundle out) { super.onSaveInstanceState(out); out.putInt("aba",tab); }
    private static String time(long epoch,String pattern) {
        return Instant.ofEpochMilli(epoch).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern(pattern,PT));
    }
    private void go(int next) { tab=next; render(true); }
    private void render(boolean resetScroll) {
        if(body==null)return;
        int y=resetScroll?0:scroll.getScrollY();
        body.removeAllViews(); nav.removeAllViews(); today=null; dial=null; usageLabels.clear();
        lastService=FocusService.connected;
        for(int i=0;i<TABS.length;i++) {
            final int dest=i;
            Button b=button(this,TABS[i],tab==i?ACCENT:MUTED,tab==i?LILAC:WHITE,()->go(dest));
            b.setSelected(tab==i); weighted(nav,b,i==0?0:2);
        }
        String[] titles={"Sessão de foco","Lista de bloqueio","Agendamentos","Histórico"};
        TextView title=text(this,titles[tab],29,INK,true); title.setAccessibilityHeading(true); put(body,title,0);
        if(tab==0)home();
        else if(tab==1)rules();
        else if(tab==2)agenda();
        else history();
        updateClock(System.currentTimeMillis());
        scroll.post(()->scroll.scrollTo(0,y));
    }
    private void updateClock(long now) {
        boolean running=f.active!=null && f.active.end>now;
        String label=running?"Em foco":"Em espera";
        if(!label.contentEquals(status.getText())) {
            status.setText(label); status.setBackground(bg(this,running?MINT:LILAC,20,false));
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
        if (!usageLabels.isEmpty()) {
            Limits.Window window=Limits.Window.at(now,ZoneId.systemDefault());
            for (Map.Entry<Limits.Quota,TextView> row:usageLabels.entrySet()) {
                String value=usageSummary(row.getKey(),window);
                if (!value.contentEquals(row.getValue().getText())) row.getValue().setText(value);
            }
        }
    }
    private void home() {
        long now=System.currentTimeMillis();
        String date=time(now,"EEEE, d 'de' MMMM");
        put(body,text(this,Character.toUpperCase(date.charAt(0))+date.substring(1),14,MUTED,false),3);
        boolean active=f.active!=null;
        LinearLayout hero=col(this); hero.setBackground(bg(this,DARK,21,false)); pad(hero,17,15,17,15); put(body,hero,14);
        TextView label=text(this,active?f.active.title:"Foco rápido",19,WHITE,true);
        label.setGravity(Gravity.CENTER); put(hero,label,0);
        if(active) {
            TextView detail=text(this,(f.active.scheduled?"Sessão agendada":"Sessão manual")+" até "+time(f.active.end,"HH:mm"),13,LIGHT,false);
            detail.setGravity(Gravity.CENTER); put(hero,detail,3);
        }
        dial=new Dial(this); hero.addView(dial,new LinearLayout.LayoutParams(FULL,dp(this,157)));
        if(!active) {
            LinearLayout choices=row(this); put(hero,choices,0);
            for(int value:new int[]{15,25,50,0}) {
                boolean custom=f.minutes!=15 && f.minutes!=25 && f.minutes!=50;
                boolean chosen=value==0?custom:f.minutes==value;
                String s=value==0?(custom?f.minutes+" min":"Outro"):value+" min";
                Button b=button(this,s,chosen?INK:WHITE,chosen?LILAC:SLATE,()->{
                    if(value==0)customDuration();
                    else if(f.active==null){f.minutes=value; model.save();}
                });
                b.setSelected(chosen); weighted(choices,b,value==15?0:5);
            }
        }
        put(hero,button(this,active?"Encerrar sessão":"Iniciar sessão",INK,MINT,()->{
            long at=System.currentTimeMillis(); model.tick(at);
            if(f.active==null) { if(f.start(at,f.minutes))model.save(); }
            else {
                Focus.Session current=f.active;
                confirm("Encerrar a sessão?","O tempo decorrido fica no histórico. Os limites de uso continuam valendo.","Encerrar sessão",()->{
                    if(f.active==current && f.stop(System.currentTimeMillis(),ZoneId.systemDefault()))model.save();
                });
            }
        }),9);
        LinearLayout protection=card(body);
        heading(protection,lastService?"Bloqueio ativado":"Ativar o bloqueio",lastService
            ? count(f.apps.size(),"aplicativo","aplicativos")+" e "+count(f.sites.size(),"site","sites")+" na sua lista."
            : "Ative o serviço para aplicar as sessões e os limites de uso aos apps e sites da lista.");
        put(protection,button(this,lastService?"Editar lista":"Configurar acessibilidade",ACCENT,LILAC,()->{if(lastService)go(1);else permission();}),10);
        LinearLayout summary=card(body); heading(summary,"Foco de hoje",""); today=text(this,"",25,ACCENT,true); put(summary,today,6);
        Focus.Slot next=f.next(now,ZoneId.systemDefault());
        if(next!=null) put(summary,text(this,"Próxima sessão · "+next.routine.name+" · "+time(next.start,"EEE HH:mm"),14,MUTED,false),6);
    }
    private static String count(int n,String one,String many){return n+" "+(n==1?one:many);}
    private void toast(String s){Toast.makeText(this,s,Toast.LENGTH_SHORT).show();}
    private void confirm(String title,String message,String action,Runnable yes){
        new AlertDialog.Builder(this).setTitle(title).setMessage(message).setNegativeButton("Cancelar",null)
            .setPositiveButton(action,(d,w)->yes.run()).show();
    }
    private void permission() {
        new AlertDialog.Builder(this).setTitle("Serviço de bloqueio")
            .setMessage("O Pulso identifica o app em primeiro plano e lê a barra de endereço visível dos navegadores compatíveis para acompanhar os limites que você configurar, inclusive fora do foco. Durante uma sessão ou ao esgotar um limite, ele volta à tela inicial ao abrir um app da lista ou cobre o site em pausa. Os dados ficam neste celular.\n\nEm Acessibilidade, abra Aplicativos instalados e selecione Pulso • Bloqueio de foco. Você pode desligá-lo no mesmo lugar.")
            .setNegativeButton("Agora não",null).setPositiveButton("Abrir Acessibilidade",(d,w)->{
                try {startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));}
                catch(ActivityNotFoundException e){toast("Abra Acessibilidade nas Configurações do celular.");}
            }).show();
    }
    private EditText field(String value,String label,boolean numeric) {
        EditText edit=new EditText(this); edit.setSingleLine(true); edit.setTextSize(16); edit.setTextColor(INK);
        edit.setInputType(numeric?InputType.TYPE_CLASS_NUMBER:InputType.TYPE_CLASS_TEXT);
        edit.setText(value); edit.setHint(label); edit.setSelectAllOnFocus(true); edit.setId(View.generateViewId()); return edit;
    }
    private void fieldLabel(LinearLayout box,String label,EditText field) {
        TextView t=text(this,label,14,INK,true); t.setLabelFor(field.getId()); put(box,t,10); put(box,field,2);
    }
    private LinearLayout fields(){LinearLayout l=col(this);pad(l,22,5,22,5);return l;}
    private interface Validate { boolean submit(); }
    private void form(String title,LinearLayout fields,String save,Validate callback) {
        form(title,fields,save,callback,null);
    }
    private void form(String title,LinearLayout fields,String save,Validate callback,Runnable disable) {
        ScrollView wrap=new ScrollView(this); wrap.addView(fields);
        AlertDialog.Builder builder=new AlertDialog.Builder(this).setTitle(title).setView(wrap).setNegativeButton("Cancelar",null).setPositiveButton(save,null);
        if(disable!=null)builder.setNeutralButton("Desativar limites",(d,w)->disable.run());
        AlertDialog dialog=builder.create();
        dialog.setOnShowListener(d->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->{if(callback.submit())dialog.dismiss();}));
        dialog.show();
    }
    private int validDuration(EditText edit) {
        try{int n=Integer.parseInt(edit.getText().toString().trim());if(Focus.validMinutes(n))return n;}catch(NumberFormatException ignored){}
        edit.setError("Digite de 1 a 720 minutos.");edit.requestFocus();return 0;
    }
    private void customDuration(){
        LinearLayout box=fields();EditText edit=field(""+f.minutes,"Minutos",true);fieldLabel(box,"Duração em minutos",edit);
        form("Duração da sessão",box,"Usar duração",()->{
            int n=validDuration(edit);if(n==0)return false;
            if(f.active==null){f.minutes=n;model.save();}return true;
        });
    }
    private void rules() {
        LinearLayout apps=card(body);
        heading(apps,"Aplicativos",f.apps.isEmpty()?"Escolha os apps para pausar durante o foco ou limitar por tempo de uso.":count(f.apps.size(),"app selecionado","apps selecionados"));
        for(String pkg:f.apps)ruleRow(apps,Apps.label(this,pkg),Limits.app(pkg),null);
        put(apps,button(this,"Escolher aplicativos",ACCENT,LILAC,this::appPicker),10);
        LinearLayout sites=card(body);
        heading(sites,"Sites","No Chrome, Edge e Samsung Internet, o Pulso cobre a página quando a barra de endereço mostra um domínio da lista. Inclui subdomínios.");
        for(String site:f.sites)ruleRow(sites,site,Limits.site(site),()->{f.sites.remove(site);model.save();});
        put(sites,button(this,"Adicionar site",ACCENT,LILAC,this::addSite),9);
        LinearLayout shared=card(body);
        heading(shared,"Limite compartilhado","Os apps e sites da lista usam o mesmo saldo. Você também pode definir um limite em cada item.");
        usageLabel(shared,Limits.SHARED);
        put(shared,button(this,"Editar limite compartilhado",ACCENT,LILAC,()->editLimits(Limits.SHARED,"Limite compartilhado")),8);
        LinearLayout setup=card(body); heading(setup,"Serviço de bloqueio",lastService?"O serviço está ativo neste celular.":"Ative em Acessibilidade para contar o uso e aplicar os bloqueios.");
        put(setup,button(this,lastService?"Gerenciar serviço":"Configurar acessibilidade",ACCENT,LILAC,this::permission),8);
    }
    private void ruleRow(LinearLayout parent,String label,String key,Runnable removeItem) {
        LinearLayout line=row(this); put(parent,line,5);
        weighted(line,text(this,label,16,INK,false),0);
        String title="Limite de "+label;
        Button edit=button(this,"Limite",ACCENT,LILAC,()->editLimits(key,title));
        edit.setContentDescription(title); line.addView(edit);
        if(removeItem!=null) {
            Button remove=button(this,"Remover",ACCENT,WHITE,removeItem);
            remove.setContentDescription("Remover "+label); line.addView(remove);
        }
        usageLabel(parent,key);
    }
    private void usageLabel(LinearLayout parent,String key) {
        Limits.Quota q=f.limits.get(key);
        if(q==null)return;
        TextView label=text(this,"",14,MUTED,false); put(parent,label,3); usageLabels.put(q,label);
    }
    private static String usageTime(long ms) {
        long seconds=ms/1000;
        if(seconds<60)return seconds+" s";
        return seconds/60+" min"+(seconds%60==0?"":" "+seconds%60+" s");
    }
    private static String usageSummary(Limits.Quota q,Limits.Window w) {
        List<String> lines=new ArrayList<>();
        if(q.hourMinutes>0)lines.add("Nesta hora · "+usageTime(q.hourUsed(w))+" de "+q.hourMinutes+" min");
        if(q.dayMinutes>0)lines.add("Hoje · "+usageTime(q.dayUsed(w))+" de "+q.dayMinutes+" min");
        Limits.Block block=q.blocked(w);
        if(block!=null)lines.add("Em pausa até "+time(block.end,"HH:mm"));
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
        put(box,text(this,"O saldo por hora se renova na hora cheia e o diário, à meia-noite. Deixe o campo vazio para desativar o período.",14,MUTED,false),5);
        EditText hour=field(q==null || q.hourMinutes==0?"":Integer.toString(q.hourMinutes),"1 a 60",true);
        EditText day=field(q==null || q.dayMinutes==0?"":Integer.toString(q.dayMinutes),"1 a 1440",true);
        fieldLabel(box,"Minutos por hora",hour); fieldLabel(box,"Minutos por dia",day);
        form(title,box,"Salvar limites",()->{
            int h=limitValue(hour,Limits.MAX_HOUR); if(h<0)return false;
            int d=limitValue(day,Limits.MAX_DAY); if(d<0)return false;
            f.limits.set(key,h,d); model.save(); return true;
        },q==null?null:()->{f.limits.set(key,0,0);model.save();});
    }
    private void addSite(){
        LinearLayout box=fields();EditText e=field("","exemplo.com",false);
        e.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_URI);fieldLabel(box,"Domínio ou link",e);
        form("Adicionar site",box,"Adicionar",()->{
            String host=Focus.rule(e.getText().toString());
            if(host==null){e.setError("Digite um domínio válido, como exemplo.com.");return false;}
            if(!f.sites.add(host)){e.setError("Este domínio já está na lista.");return false;}
            model.save();return true;
        });
    }
    private void appPicker() {
        List<Apps.Item> all=Apps.launchable(this); Set<String> included=new LinkedHashSet<>();
        for(Apps.Item i:all) included.add(i.pkg);
        for(String pkg:f.apps)if(!included.contains(pkg))all.add(new Apps.Item(pkg,Apps.label(this,pkg)));
        Set<String> selected=new LinkedHashSet<>(f.apps);
        List<Apps.Item> visible=new ArrayList<>(); LinearLayout box=fields();
        EditText search=field("","Buscar aplicativo",false);fieldLabel(box,"Buscar aplicativo",search);
        ListView list=new ListView(this); list.setChoiceMode(ListView.CHOICE_MODE_MULTIPLE);
        int maxHeight=(int)(getResources().getDisplayMetrics().heightPixels*.41f);
        box.addView(list,new LinearLayout.LayoutParams(FULL,Math.min(dp(this,340),maxHeight)));
        TextView empty=text(this,"Nenhum aplicativo encontrado.",14,MUTED,false);put(box,empty,4);
        Runnable filter=()->{
            String term=search.getText().toString().trim().toLowerCase(PT); visible.clear();
            List<String> labels=new ArrayList<>();
            for(Apps.Item i:all)if(i.name.toLowerCase(PT).contains(term)||i.pkg.toLowerCase(PT).contains(term)){visible.add(i);labels.add(i.name);}
            list.clearChoices();list.setAdapter(new ArrayAdapter<>(this,android.R.layout.simple_list_item_multiple_choice,labels));
            for(int j=0;j<visible.size();j++)list.setItemChecked(j,selected.contains(visible.get(j).pkg));
            empty.setVisibility(visible.isEmpty()?View.VISIBLE:View.GONE);
        };
        filter.run();list.setOnItemClickListener((p,v,pos,id)->{String pkg=visible.get(pos).pkg;if(list.isItemChecked(pos))selected.add(pkg);else selected.remove(pkg);});
        search.addTextChangedListener(new TextWatcher(){
            public void beforeTextChanged(CharSequence s,int start,int count,int after){}
            public void onTextChanged(CharSequence s,int start,int before,int count){filter.run();}
            public void afterTextChanged(Editable s){}
        });
        new AlertDialog.Builder(this).setTitle("Escolher aplicativos").setView(box).setNegativeButton("Cancelar",null)
            .setPositiveButton("Salvar lista",(d,w)->{f.apps.clear();f.apps.addAll(selected);model.save();}).show();
    }
    private static String hm(int value) { return String.format(Locale.ROOT,"%02d:%02d",value/60,value%60); }
    private static String repeat(int mask) {
        if(mask==127)return "Todos os dias"; if(mask==31)return "Segunda a sexta";
        StringBuilder s=new StringBuilder(); for(int i=0;i<7;i++)if((mask&(1<<i))!=0){if(s.length()>0)s.append(" · ");s.append(DAYS[i]);}return s.toString();
    }
    private void agenda(){
        put(body,text(this,"Os horários seguem o relógio do celular. Com o serviço ativo, o bloqueio funciona em segundo plano.",14,MUTED,false),6);
        put(body,button(this,"Novo agendamento",WHITE,ACCENT,()->editRoutine(null)),12);
        if(f.routines.isEmpty()){heading(card(body),"Nenhum agendamento","Escolha os dias, o horário e a duração da sua primeira sessão recorrente.");return;}
        for(Focus.Routine r:f.routines){
            LinearLayout c=card(body);heading(c,r.name,repeat(r.days)+"  ·  "+hm(r.startMinute)+"  ·  "+r.minutes+" min");
            Switch sw=new Switch(this);sw.setText(r.enabled?"Agendamento ativo":"Agendamento desativado");sw.setTextColor(INK);
            sw.setMinHeight(dp(this,48));sw.setChecked(r.enabled);sw.setContentDescription("Ativar "+r.name);put(c,sw,4);
            sw.setOnCheckedChangeListener((v,on)->{r.enabled=on;model.save();});
            LinearLayout line=row(this);put(c,line,3);weighted(line,button(this,"Editar",ACCENT,LILAC,()->editRoutine(r)),0);
            weighted(line,button(this,"Excluir",ACCENT,WHITE,()->confirm("Excluir agendamento?","As próximas sessões de “"+r.name+"” serão removidas.","Excluir",()->{f.routines.remove(r);model.save();})),6);
        }
    }
    private void editRoutine(Focus.Routine original){
        boolean fresh=original==null;LinearLayout box=fields();
        EditText name=field(fresh?"":original.name,"Nome da sessão",false);name.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(60)});
        fieldLabel(box,"Nome da sessão",name);int[] start={fresh?540:original.startMinute};
        Button clock=button(this,"Início · "+hm(start[0]),ACCENT,LILAC,()->{});put(box,clock,9);
        clock.setOnClickListener(v->new TimePickerDialog(this,(view,h,m)->{start[0]=h*60+m;clock.setText("Início · "+hm(start[0]));},start[0]/60,start[0]%60,true).show());
        EditText minutes=field(""+(fresh?f.minutes:original.minutes),"Minutos",true);fieldLabel(box,"Duração em minutos",minutes);
        put(box,text(this,"Repetir nos dias",14,INK,true),10);
        CheckBox[] checks=new CheckBox[7];int mask=fresh?31:original.days;
        LinearLayout line=null;
        for(int i=0;i<7;i++){
            if(i%3==0){line=row(this);put(box,line,0);}
            checks[i]=new CheckBox(this);checks[i].setText(DAYS[i]);checks[i].setChecked((mask&(1<<i))!=0);checks[i].setMinHeight(dp(this,48));weighted(line,checks[i],0);
        }
        TextView error=text(this,"",14,0xff9b3246,false);error.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);put(box,error,2);
        form(fresh?"Novo agendamento":"Editar agendamento",box,"Salvar",()->{
            String label=name.getText().toString().trim();if(label.isEmpty()){name.setError("Dê um nome à sessão.");return false;}
            int n=validDuration(minutes);if(n==0)return false;
            int days=0;for(int i=0;i<7;i++)if(checks[i].isChecked())days|=1<<i;
            if(days==0){error.setText("Selecione pelo menos um dia.");return false;}
            if(fresh)f.routines.add(new Focus.Routine(label,days,start[0],n));
            else{
                if(original.days!=days||original.startMinute!=start[0])original.lastDay=Long.MIN_VALUE;
                original.name=label;original.days=days;original.startMinute=start[0];original.minutes=n;
            }
            model.save();return true;
        });
    }
    private void history(){
        LinearLayout summary=card(body);heading(summary,"Tempo de foco hoje","");today=text(this,"",28,ACCENT,true);put(summary,today,6);
        if(f.active!=null)put(summary,text(this,"Em andamento · "+f.active.title,14,MUTED,false),5);
        if(f.history.isEmpty()){heading(card(body),"Nenhuma sessão encerrada","O tempo decorrido aparece aqui quando você conclui ou encerra uma sessão.");return;}
        LinearLayout c=card(body);heading(c,"Sessões anteriores","");
        for(int i=0;i<Math.min(historyLimit,f.history.size());i++){
            Focus.Entry e=f.history.get(i);put(c,text(this,e.title,17,INK,true),13);
            put(c,text(this,time(e.start,"dd/MM/yyyy · HH:mm")+"  ·  "+(e.end-e.start<60000?"< 1 min":duration(e.end-e.start)),14,MUTED,false),4);
            put(c,text(this,e.completed?"Concluída":"Encerrada antes do fim",13,ACCENT,false),3);
        }
        if(historyLimit<f.history.size())put(c,button(this,"Mostrar mais sessões",ACCENT,LILAC,()->{historyLimit+=30;render(false);}),10);
    }
}
