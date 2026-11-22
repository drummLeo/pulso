package br.local.pulso;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.Locale;
import static br.local.pulso.R.color.*;

final class Ui {
    static final int FULL=ViewGroup.LayoutParams.MATCH_PARENT, WRAP=ViewGroup.LayoutParams.WRAP_CONTENT;
    static int dp(Context c, float value) { return Math.round(c.getResources().getDisplayMetrics().density*value); }
    static GradientDrawable bg(Context c,int color,int radius,boolean line) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(c.getColor(color)); d.setCornerRadius(dp(c,radius)); if(line)d.setStroke(dp(c,1),c.getColor(outline));
        return d;
    }
    static void pad(View v,int a,int b,int c,int d) {
        Context x=v.getContext(); v.setPadding(dp(x,a),dp(x,b),dp(x,c),dp(x,d));
    }
    static LinearLayout col(Context c) { LinearLayout l=new LinearLayout(c); l.setOrientation(LinearLayout.VERTICAL); return l; }
    static LinearLayout row(Context c) { LinearLayout l=new LinearLayout(c); l.setGravity(Gravity.CENTER_VERTICAL); return l; }
    static TextView text(Context c, String s, int sp, int color, boolean strong) {
        TextView t=new TextView(c); t.setText(s); t.setTextSize(sp); t.setTextColor(c.getColor(color)); t.setIncludeFontPadding(false);
        t.setTypeface(Typeface.create(strong?"sans-serif-medium":"sans-serif",Typeface.NORMAL)); return t;
    }
    static void put(LinearLayout parent,View child,int top) {
        LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(FULL,WRAP); p.topMargin=dp(parent.getContext(),top); parent.addView(child,p);
    }
    static void weighted(LinearLayout parent,View child,int left) {
        LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(0,WRAP,1f); p.leftMargin=dp(parent.getContext(),left); parent.addView(child,p);
    }
    static Button button(Context c,String s,int fg,int fill,Runnable click) {
        Button b=new Button(c); b.setAllCaps(false); b.setText(s); b.setTextSize(14); b.setTextColor(c.getColor(fg));
        b.setTypeface(Typeface.create("sans-serif-medium",0)); b.setMinWidth(0); b.setMinimumWidth(0);
        b.setMinHeight(dp(c,48)); b.setMinimumHeight(dp(c,48)); b.setStateListAnimator(null); b.setLetterSpacing(0);
        pad(b,7,5,7,5); b.setGravity(Gravity.CENTER);
        b.setBackground(new RippleDrawable(ColorStateList.valueOf(c.getColor(button_ripple)),bg(c,fill,12,false),null));
        b.setOnClickListener(v->click.run()); return b;
    }
    static LinearLayout card(LinearLayout parent) {
        Context c=parent.getContext(); LinearLayout box=col(c); pad(box,17,16,17,16);
        box.setBackground(bg(c,card_surface,17,true)); put(parent,box,12); return box;
    }
    static TextView heading(LinearLayout parent,String title,String hint) {
        Context c=parent.getContext(); TextView label=text(c,title,20,text_primary,true); put(parent,label,0);
        if(!hint.isEmpty())put(parent,text(c,hint,14,text_muted,false),6);
        return label;
    }
    static String duration(long millis) {
        long m=Math.max(0,millis/60000L);
        return m<60?m+" min":m/60+" h"+(m%60==0?"":String.format(Locale.ROOT," %02d min",m%60));
    }
    static final class Dial extends View {
        final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
        final int activeTick, majorTick, minorTick, digits;
        long seconds=-1; double fraction; boolean active;
        Dial(Context c) {
            super(c);
            activeTick=c.getColor(focus_action); majorTick=c.getColor(on_focus_secondary);
            minorTick=c.getColor(dial_tick); digits=c.getColor(on_focus);
            setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_YES);
        }
        void show(long value,double progress,boolean running) {
            if(seconds==value && active==running && Math.abs(fraction-progress)<0.0001)return;
            seconds=value; fraction=Math.max(0,Math.min(1,progress)); active=running;
            invalidate();
        }
        @Override public void onInitializeAccessibilityNodeInfo(android.view.accessibility.AccessibilityNodeInfo info) {
            super.onInitializeAccessibilityNodeInfo(info);
            info.setContentDescription((active?"Tempo restante, ":"Duração, ")+textTime());
        }
        String textTime() { return seconds>=3600?String.format(Locale.ROOT,"%d:%02d:%02d",seconds/3600,seconds/60%60,seconds%60):
            String.format(Locale.ROOT,"%02d:%02d",seconds/60,seconds%60); }
        @Override protected void onDraw(Canvas canvas) {
            float unit=Math.min(getWidth()/240f,getHeight()/157f);
            canvas.save(); canvas.translate(getWidth()/2f,getHeight()*.55f); canvas.scale(unit,unit);
            float radius=74; paint.setStrokeCap(Paint.Cap.ROUND);
            for(int i=0;i<=50;i++){
                double a=Math.toRadians(139+i*262.0/50);
                float x=(float)Math.cos(a),y=(float)Math.sin(a); boolean major=i%5==0;
                paint.setColor(i<=fraction*50?activeTick:major?majorTick:minorTick); paint.setStrokeWidth(major?2.2f:1.2f);
                float inside=radius-(major?10:5); canvas.drawLine(x*inside,y*inside,x*radius,y*radius,paint);
            }
            paint.setTextAlign(Paint.Align.CENTER); paint.setColor(digits); paint.setTypeface(Typeface.create("sans-serif-medium",0));
            paint.setTextSize(seconds>=3600?27:35); canvas.drawText(textTime(),0,4,paint);
            paint.setTypeface(Typeface.create("sans-serif",0)); paint.setColor(majorTick); paint.setTextSize(10);
            canvas.drawText(active?"RESTANTE":"DURAÇÃO",0,23,paint); canvas.restore();
        }
    }
}
