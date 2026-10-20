using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace NabyScape;

internal sealed partial class IslandSurface
{
    private RadialGradientBrush? ambient,ambientAccent;
    private readonly DrawingGroup ambientLayer=new();
    private readonly RectangleGeometry ambientBounds=new();
    private (CoverPalette Colors,double Strength,double Spread,string Motion,bool Running,int FrameRate)? ambientKey;
    internal bool AmbientAnimating=>ambient?.HasAnimatedProperties==true || ambient?.RelativeTransform.HasAnimatedProperties==true;
    internal Color AmbientColor=>ambientKey?.Colors.Primary??Colors.Transparent;
    internal bool AmbientVisible=>ambient!=null;
    internal double AmbientAudioGain=>ambientLayer.Opacity;
    internal void SetAmbientAudio(double gain)
    {
        gain=double.IsFinite(gain)?Math.Clamp(gain,.32,1):.32;
        if(Math.Abs(ambientLayer.Opacity-gain)>.004)Motion.To(ambientLayer,DrawingGroup.OpacityProperty,gain,55);
    }
    internal void ResetAmbientAudio(){ambientLayer.BeginAnimation(DrawingGroup.OpacityProperty,null);ambientLayer.Opacity=1;}
    public IslandSurface(){IsVisibleChanged+=(_,_)=>ConfigureAmbientClock();Unloaded+=(_,_)=>StopAmbientClock();Loaded+=(_,_)=>ConfigureAmbientClock();}
    internal void SetAmbient(CoverPalette colors,double strength,double spread,string motion,bool running)
    {
        var next=(colors,strength,spread,motion,Motion.Enabled && running,Math.Clamp(Motion.FrameLimit,30,240));
        if(ambientKey==next)return;
        bool samePaint=ambientKey is {} old && old.Colors==colors && old.Strength==strength && old.Spread==spread;
        StopAmbientClock();ambientKey=next;
        if(!samePaint)
        {
            RadialGradientBrush Glow(Color color,double x,double amount)=>new(new GradientStopCollection{new(Color.FromArgb((byte)(Math.Clamp(strength,0,1)*amount),color.R,color.G,color.B),0),new(Color.FromArgb(0,color.R,color.G,color.B),1)}){Center=new(x,1.08),GradientOrigin=new(x,1.08),RadiusX=.28+spread*.58,RadiusY=.3+spread*.7,RelativeTransform=new TranslateTransform()};
            ambient=strength>0 && colors.Primary.A>0?Glow(colors.Primary,.4,210):null;
            ambientAccent=ambient==null?null:Glow(colors.Secondary,.78,125);
            ambientLayer.Children.Clear();
            if(ambient!=null){ambientLayer.Children.Add(new GeometryDrawing(ambient,null,ambientBounds));ambientLayer.Children.Add(new GeometryDrawing(ambientAccent,null,ambientBounds));}
            InvalidateVisual();
        }
        ConfigureAmbientClock();
    }
    private void ConfigureAmbientClock()
    {
        StopAmbientClock();
        if(!IsVisible || !IsLoaded || ambient==null || ambientKey is not {} key || !key.Running || !Motion.Enabled || key.Motion=="Still")return;
        var loop=new DoubleAnimation(key.Motion=="Breathe"?.72:-.1,key.Motion=="Breathe"?1:.1,TimeSpan.FromSeconds(key.Motion=="Breathe"?3.6:5.5)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        Timeline.SetDesiredFrameRate(loop,key.FrameRate);
        if(key.Motion=="Breathe")ambient.BeginAnimation(Brush.OpacityProperty,loop);
        else ((TranslateTransform)ambient.RelativeTransform).BeginAnimation(TranslateTransform.XProperty,loop);
    }
    private void StopAmbientClock()
    {
        if(ambient==null)return;
        ambient.BeginAnimation(Brush.OpacityProperty,null);
        ((TranslateTransform)ambient.RelativeTransform).BeginAnimation(TranslateTransform.XProperty,null);
    }
    private void DrawAmbient(DrawingContext dc)
    {
        if(ambient==null)return;
        var rect=new Rect(Math.Max(0,(ActualWidth-SurfaceWidth)/2),0,SurfaceWidth,SurfaceHeight);
        if(ambientBounds.Rect!=rect)ambientBounds.Rect=rect;
        dc.DrawDrawing(ambientLayer);
    }
}
