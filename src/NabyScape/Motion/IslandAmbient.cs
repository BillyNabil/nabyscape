using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace NabyScape;

internal sealed partial class IslandSurface
{
    private LinearGradientBrush? ambient;
    private Brush? ambientFade;
    private readonly DrawingGroup ambientLayer=new();
    private readonly RectangleGeometry ambientBounds=new();
    private (CoverPalette Colors,double Strength,double Spread,string Motion,bool Running,int FrameRate,AmbientLook Look)? ambientKey;
    internal bool AmbientAnimating=>ambient?.HasAnimatedProperties==true || ambient?.RelativeTransform.HasAnimatedProperties==true || ambientFade?.RelativeTransform.HasAnimatedProperties==true;
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
    internal void SetAmbient(CoverPalette colors,double strength,double spread,string motion,bool running,AmbientLook? appearance=null)
    {
        var look=appearance??AmbientLook.Default;
        var next=(colors,strength,spread,motion,Motion.Enabled && running,Math.Clamp(Motion.FrameLimit,30,240),look);
        if(ambientKey==next)return;
        bool samePaint=ambientKey is {} old && old.Colors==colors && old.Strength==strength && old.Spread==spread && old.Look==look;
        StopAmbientClock();ambientKey=next;
        if(!samePaint)
        {
            ambient=strength>0 && colors.Primary.A>0?AmbientPaint.ColorBand(colors,strength,look):null;
            ambientLayer.Children.Clear();ambientLayer.OpacityMask=null;ambientFade=null;
            if(ambient!=null)
            {
                ambientFade=AmbientPaint.BottomFade(spread,look);ambientLayer.OpacityMask=ambientFade;
                ambientLayer.Children.Add(new GeometryDrawing(ambient,null,ambientBounds));
            }
            InvalidateVisual();
        }
        ConfigureAmbientClock();
    }
    private void ConfigureAmbientClock()
    {
        StopAmbientClock();
        if(!IsVisible || !IsLoaded || ambient==null || ambientKey is not {} key || !key.Running || !Motion.Enabled || key.Motion=="Still")return;
        var loop=new DoubleAnimation(key.Motion=="Breathe"?.72:-.035,key.Motion=="Breathe"?1:.035,TimeSpan.FromSeconds(key.Motion=="Breathe"?3.6:5.5)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        Timeline.SetDesiredFrameRate(loop,key.FrameRate);
        if(key.Motion=="Breathe")ambient.BeginAnimation(Brush.OpacityProperty,loop);
        else if(ambientFade!=null)
        {
            if(ambientFade.IsFrozen){ambientFade=ambientFade.Clone();ambientFade.RelativeTransform=new TranslateTransform();ambientLayer.OpacityMask=ambientFade;}
            ((TranslateTransform)ambientFade.RelativeTransform).BeginAnimation(key.Look.Direction is "Left" or "Right"?TranslateTransform.XProperty:TranslateTransform.YProperty,loop);
        }
    }
    private void StopAmbientClock()
    {
        if(ambient==null)return;
        ambient.BeginAnimation(Brush.OpacityProperty,null);
        ((TranslateTransform)ambient.RelativeTransform).BeginAnimation(TranslateTransform.XProperty,null);
        if(ambientFade?.RelativeTransform is TranslateTransform {IsFrozen:false} fadeMove){fadeMove.BeginAnimation(TranslateTransform.YProperty,null);fadeMove.BeginAnimation(TranslateTransform.XProperty,null);}
    }
    private void DrawAmbient(DrawingContext dc)
    {
        if(ambient==null)return;
        var rect=new Rect(Math.Max(0,(ActualWidth-SurfaceWidth)/2),0,SurfaceWidth,SurfaceHeight);
        if(ambientBounds.Rect!=rect)ambientBounds.Rect=rect;
        dc.DrawDrawing(ambientLayer);
    }
}

// Retained gradients keep the title region clear and concentrate album color at
// the base. No blur bitmap, per-frame allocation or additional render loop.
internal static class AmbientPaint
{
    internal static LinearGradientBrush ColorBand(CoverPalette palette,double strength,AmbientLook? appearance=null)
    {
        var look=appearance??AmbientLook.Default;
        byte alpha=(byte)Math.Round(255*Math.Sqrt(Math.Clamp(strength,0,1)));
        Color Tint(Color c)=>Color.FromArgb((byte)(alpha*c.A/255),c.R,c.G,c.B);
        Color Mix(Color a,Color b,double t)=>Color.FromArgb((byte)(a.A+(b.A-a.A)*t),(byte)(a.R+(b.R-a.R)*t),(byte)(a.G+(b.G-a.G)*t),(byte)(a.B+(b.B-a.B)*t));
        var left=look.Balance<=.5?palette.Primary:Mix(palette.Primary,palette.Secondary,(look.Balance-.5)*2);
        var right=look.Balance>=.5?palette.Secondary:Mix(palette.Primary,palette.Secondary,look.Balance*2);
        return new LinearGradientBrush(Tint(left),Tint(right),look.Direction is "Left" or "Right"?90:0){RelativeTransform=new TranslateTransform()};
    }
    internal static Brush BottomFade(double spread,AmbientLook? appearance=null)
    {
        var look=appearance??AmbientLook.Default;
        double start=1-(1-(.58-Math.Clamp(spread,.25,2.5)*.30))*look.Height;
        var stops=new GradientStopCollection();
        for(int i=0;i<=8;i++)
        {
            double offset=i/8d,t=Math.Clamp((offset-start)/(1-start),0,1);
            byte alpha=(byte)Math.Round(255*Math.Pow(t,3.6-look.Softness*2.8));
            stops.Add(new(Color.FromArgb(alpha,255,255,255),offset));
        }
        Brush fade;
        if(look.Direction=="Surround")fade=new RadialGradientBrush(stops){Center=new(.5,.5),GradientOrigin=new(.5,.5),RadiusX=.55,RadiusY=.65};
        else
        {
            var (from,to)=look.Direction switch{"Top"=>(new Point(.5,1),new Point(.5,0)),"Left"=>(new Point(1,.5),new Point(0,.5)),"Right"=>(new Point(0,.5),new Point(1,.5)),_=>(new Point(.5,0),new Point(.5,1))};
            fade=new LinearGradientBrush(stops,from,to);
        }
        fade.Freeze();return fade;
    }
}
