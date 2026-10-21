using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace NabyScape;

// Vector artwork: one retained idle transform, only while visible and motion is enabled.
internal sealed class WeatherArtwork : FrameworkElement
{
    internal int Code {get;set;}
    internal bool Day {get;set;}=true;
    internal bool ReduceMotion {get;set;}
    internal bool DecorativeMotion {get;set;}=true;
    private readonly TranslateTransform drift=new();
    private Window? host;
    internal WeatherArtwork()
    {
        Width=132;Height=110;RenderTransform=drift;IsHitTestVisible=false;
        Loaded+=(_,_)=>{host=Window.GetWindow(this);if(host!=null)host.StateChanged+=HostChanged;RefreshMotion();};
        Unloaded+=(_,_)=>{if(host!=null)host.StateChanged-=HostChanged;host=null;drift.BeginAnimation(TranslateTransform.YProperty,null);};
        IsVisibleChanged+=(_,_)=>RefreshMotion();
    }
    private void HostChanged(object? sender,EventArgs e)=>RefreshMotion();
    internal void RefreshMotion()
    {
        drift.BeginAnimation(TranslateTransform.YProperty,null);
        if(!IsLoaded || !IsVisible || !DecorativeMotion || ReduceMotion || !Motion.Enabled || host?.WindowState==WindowState.Minimized)return;
        var animation=new DoubleAnimation(-2,2,TimeSpan.FromSeconds(4.5)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        Timeline.SetDesiredFrameRate(animation,Math.Min(Motion.FrameLimit,60));drift.BeginAnimation(TranslateTransform.YProperty,animation);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.PushTransform(new ScaleTransform(ActualWidth/132,ActualHeight/110));
        bool rain=Code is >=51 and <=67 or >=80 and <=82 or >=95,snow=Code is >=71 and <=77 or 85 or 86,cloud=Code>=2;
        Brush sky=UI.Brush(Day?"#FFD888":"#D3DDFB");
        if(Day && Code<=2)
        {
            var halo=new RadialGradientBrush(Color.FromArgb(65,255,190,90),Colors.Transparent);dc.DrawEllipse(halo,null,new Point(76,42),48,42);
            dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(255,236,171),Color.FromRgb(237,171,73),90),null,new Point(76,39),22,22);
            if(!cloud){var ray=new Pen(UI.Brush("#AAEBC57D"),1.5);for(int i=0;i<8;i++){double angle=i*Math.PI/4;dc.DrawLine(ray,new Point(76+Math.Cos(angle)*29,39+Math.Sin(angle)*29),new Point(76+Math.Cos(angle)*34,39+Math.Sin(angle)*34));}}
        }
        else if(!Day && Code<=2)
        {
            var moon=Geometry.Combine(new EllipseGeometry(new Point(79,35),23,23),new EllipseGeometry(new Point(89,26),22,22),GeometryCombineMode.Exclude,null);dc.DrawGeometry(sky,null,moon);
            foreach(var p in new[]{new Point(34,20),new Point(111,58),new Point(40,50)}){dc.DrawLine(new Pen(sky,1),new(p.X-2,p.Y),new(p.X+2,p.Y));dc.DrawLine(new Pen(sky,1),new(p.X,p.Y-2),new(p.X,p.Y+2));}
        }
        if(cloud)
        {
            var geometry=new GeometryGroup{FillRule=FillRule.Nonzero};geometry.Children.Add(new EllipseGeometry(new Point(53,58),21,19));geometry.Children.Add(new EllipseGeometry(new Point(75,53),25,24));geometry.Children.Add(new EllipseGeometry(new Point(97,65),20,17));geometry.Children.Add(new RectangleGeometry(new Rect(34,57,64,25),10,10));
            dc.DrawGeometry(new LinearGradientBrush(Day?Color.FromRgb(238,244,250):Color.FromRgb(167,185,214),Day?Color.FromRgb(153,178,201):Color.FromRgb(89,116,157),90),null,geometry);
        }
        if(rain){var pen=new Pen(UI.Brush("#78BDF8"),2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};for(int i=0;i<4;i++)dc.DrawLine(pen,new Point(45+i*16,88),new Point(41+i*16,97));}
        if(snow)for(int i=0;i<4;i++)dc.DrawEllipse(Brushes.White,null,new Point(43+i*18,92+(i%2)*5),1.8,1.8);
        if(Code>=95){var bolt=Geometry.Parse("M74,79 L65,96 H75 L70,108 L87,89 H76 L82,79 Z");dc.DrawGeometry(UI.Brush("#FFE2A2"),null,bolt);}
        if(Code is 45 or 48){var pen=new Pen(UI.Brush("#A5B5C8"),2);dc.DrawLine(pen,new(30,90),new(105,90));dc.DrawLine(pen,new(42,97),new(115,97));}
        dc.Pop();
    }
}
