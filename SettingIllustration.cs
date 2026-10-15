using System;
using System.Windows;
using System.Windows.Media;
namespace NabyScape;
// Small native diagrams, paired with the setting they explain. No bitmap assets or timers.
internal sealed class SettingIllustration : FrameworkElement
{
    private readonly string name;
    internal double Value {get;set;}=.65;
    internal SettingIllustration(string name){this.name=name.ToLowerInvariant();Width=64;Height=38;IsHitTestVisible=false;VerticalAlignment=VerticalAlignment.Center;}
    protected override void OnRender(DrawingContext dc)
    {
        double v=Math.Clamp(Value,0,1);var faint=UI.Brush("#34373E");var light=UI.Brush("#D5D7DE");var surface=UI.Brush("#090A0C");
        void Box(double x,double y,double w,double h,Brush brush,double radius=3)=>dc.DrawRoundedRectangle(brush,null,new Rect(x,y,Math.Max(0,w),Math.Max(0,h)),radius,radius);
        Box(0,0,64,38,UI.Brush("#1A1C20"),6);
        if(name.Contains("startup"))
        {Box(24,8,32,24,surface,4);Box(32,10,16,6,v>.5?light:faint,2);dc.DrawLine(new Pen(light,1.4),new Point(6,19),new Point(20,19));dc.DrawLine(new Pen(light,1.4),new Point(16,15),new Point(20,19));dc.DrawLine(new Pen(light,1.4),new Point(16,23),new Point(20,19));return;}
        if(name.Contains("tuck") || name.Contains("fullscreen") || name.Contains("maximized"))
        {Box(7,5,50,28,faint,3);Box(9,name.Contains("maximized")?12:7,46,name.Contains("maximized")?19:24,surface,2);Box(21,6,22,v>.5?3:8,light,2);return;}
        if(name.Contains("appearance") || name.Contains("navigation"))
        {for(int i=0;i<3;i++){if(v<.75)Box(10+i*17,10,10,10,light,2);if(v<.25 || v>.75)Box(8+i*17,v>.75?18:25,14,2,light,1);}return;}
        if(name.Contains("bold") || name.Contains("typography"))
        {
            var text=new FormattedText("Aa",System.Globalization.CultureInfo.GetCultureInfo("en-US"),FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,v>.5?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal),22,light,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(text,new Point(16,4));return;
        }
        if(name.Contains("volume") || name.Contains("audio") || name.Contains("motion") || name.Contains("activity") || name.Contains("waveform") || name.Contains("performance"))
        {for(int i=0;i<9;i++){double h=3+(v*18)*(.3+.7*((i*7+3)%11)/10d);Box(8+i*5.6,(38-h)/2,2,h,light,1);}return;}
        if(name.Contains("color") || name.Contains("surface") || name.Contains("glass"))
        {Box(8,8,48,22,surface,6);Box(12,12,14,14,UI.Brush(name.Contains("accent")?App.Current.Settings.Accent:App.Current.Settings.Background),4);Box(31,15,19,2,light);Box(31,21,12,2,faint);return;}
        if(name.Contains("clock") || name.Contains("seconds") || name.Contains("date"))
        {var text=new FormattedText("12:45",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),14,light,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(text,new Point(14,9));return;}
        if(name.Contains("shortcut") || name.Contains("key"))
        {for(int i=0;i<3;i++)Box(7+i*18,11,14,16,i==2?light:faint);return;}
        if(name.Contains("network") || name.Contains("system") || name.Contains("storage") || name.Contains("process") || name.Contains("memory"))
        {for(int i=0;i<3;i++){Box(9,9+i*8,46,3,faint);Box(9,9+i*8,10+(i+1)*8*v,3,light);}return;}
        if(name.Contains("focus") || name.Contains("timer") || name.Contains("duration"))
        {dc.DrawEllipse(null,new Pen(faint,2),new Point(32,19),12,12);dc.DrawLine(new Pen(light,1.6),new Point(32,19),new Point(32,10));dc.DrawLine(new Pen(light,1.6),new Point(32,19),new Point(39,22));return;}
        // Island geometry also explains placement, size, hover, visibility and navigation.
        Box(7,5,50,28,surface,4);double width=name.Contains("width")?20+26*v:34;double height=name.Contains("height")?7+14*v:10;
        double x=name.Contains("position")?8+(48-width)*v:32-width/2;
        Box(x,name.Contains("offset")?6+10*v:6,width,height,v==0?faint:light,name.Contains("radius")?1+5*v:3);
        if(name.Contains("hover") || name.Contains("preview") || name.Contains("panel")){Box(13,19,38,3,faint);Box(13,25,26,2,faint);}
    }
}
