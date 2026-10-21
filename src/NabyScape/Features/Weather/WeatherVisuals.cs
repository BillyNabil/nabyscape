using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
namespace NabyScape;

// Retained vector sky and a data-driven temperature curve: no bitmap blur or new timer.
internal sealed class WeatherSky : FrameworkElement
{
    internal bool Day {get;set;}
    internal int Code {get;set;}
    internal WeatherSky(){IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc)
    {
        double w=ActualWidth,h=ActualHeight;if(w<=0 || h<=0)return;
        bool wet=Code>=51;Color top=Day?(wet?Color.FromRgb(28,43,58):Color.FromRgb(36,66,88)):Color.FromRgb(20,29,55);
        Color bottom=Day?(wet?Color.FromRgb(15,24,35):Color.FromRgb(16,29,43)):Color.FromRgb(10,16,33);
        dc.DrawRoundedRectangle(new LinearGradientBrush(top,bottom,90),null,new Rect(0,0,w,h),16,16);
        dc.PushClip(new RectangleGeometry(new Rect(0,0,w,h),16,16));
        Color light=Day?Color.FromArgb(wet?(byte)25:(byte)65,255,209,131):Color.FromArgb(35,131,157,234);
        dc.DrawEllipse(new RadialGradientBrush(light,Colors.Transparent),null,new Point(w*.88,h*.18),w*.6,h*1.1);
        if(!Day)
        {
            var star=UI.Brush("#76C9D9EF");foreach(var p in new[]{new Point(.56,.19),new Point(.76,.12),new Point(.92,.43),new Point(.61,.45),new Point(.84,.67)})dc.DrawEllipse(star,null,new Point(w*p.X,h*p.Y),.9,.9);
        }
        var far=new StreamGeometry();using(var c=far.Open()){c.BeginFigure(new(0,h*.86),true,true);c.BezierTo(new(w*.25,h*.60),new(w*.48,h*.96),new(w,h*.72),true,false);c.LineTo(new(w,h),true,false);c.LineTo(new(0,h),true,false);}far.Freeze();dc.DrawGeometry(UI.Brush("#123E7397"),null,far);
        var near=new StreamGeometry();using(var c=near.Open()){c.BeginFigure(new(0,h*.93),true,true);c.BezierTo(new(w*.35,h*.75),new(w*.65,h*1.04),new(w,h*.85),true,false);c.LineTo(new(w,h),true,false);c.LineTo(new(0,h),true,false);}near.Freeze();dc.DrawGeometry(UI.Brush("#142B4A68"),null,near);dc.Pop();
    }
}
internal sealed class WeatherTrend : FrameworkElement
{
    internal IReadOnlyList<WeatherHour> Hours {get;set;}=Array.Empty<WeatherHour>();
    internal WeatherTrend(){Height=26;IsHitTestVisible=false;ToolTip="Temperature trend for the next hours";}
    protected override void OnRender(DrawingContext dc)
    {
        if(Hours.Count<2 || ActualWidth<8)return;double min=double.MaxValue,max=double.MinValue;foreach(var hour in Hours){min=Math.Min(min,hour.Temperature);max=Math.Max(max,hour.Temperature);}double range=Math.Max(6,max-min);
        Point P(int i)=>new(6+(ActualWidth-12)*i/(Hours.Count-1),ActualHeight/2-(Hours[i].Temperature-(min+max)/2)/range*Math.Max(4,ActualHeight-6));
        var curve=new StreamGeometry();using(var c=curve.Open())
        {
            c.BeginFigure(P(0),false,false);for(int i=1;i<Hours.Count;i++){var a=P(i-1);var b=P(i);c.BezierTo(new(a.X+(b.X-a.X)*.45,a.Y),new(b.X-(b.X-a.X)*.45,b.Y),b,true,false);}
        }
        curve.Freeze();dc.DrawGeometry(null,new Pen(UI.Brush("#8FBEDC"),1.6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},curve);foreach(var i in new[]{0,Hours.Count-1})dc.DrawEllipse(UI.Brush("#BDDCEF"),null,P(i),2.2,2.2);
    }
}
