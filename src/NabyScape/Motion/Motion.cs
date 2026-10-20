using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NabyScape;

internal static class Motion
{
    public static bool Enabled {get;set;}=true;
    public static double Duration {get;set;}=420;
    internal static int FrameLimit {get;set;}=180;
    internal static string Interaction {get;private set;}="Subtle";
    public static void Configure(Settings s)
    {
        Enabled=!s.ReduceMotion && SystemParameters.ClientAreaAnimation && s.AnimationMs>0;Duration=s.AnimationMs;Interaction=s.InteractionMotion;
        if(Application.Current!=null)Application.Current.Resources["PopupMotion"]=Enabled?System.Windows.Controls.Primitives.PopupAnimation.Fade:System.Windows.Controls.Primitives.PopupAnimation.None;
    }
    public static void To(Animatable target,DependencyProperty property,double value,double milliseconds=180,double delay=0)
    {
        var current=(double)target.GetValue(property);
        target.BeginAnimation(property,null);target.SetValue(property,value);
        if(!Enabled || Math.Abs(current-value)<.001)return;
        target.BeginAnimation(property,new DoubleAnimation(current,value,TimeSpan.FromMilliseconds(milliseconds))
        {BeginTime=TimeSpan.FromMilliseconds(delay),EasingFunction=new ExponentialEase{Exponent=5,EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
    public static void Fade(UIElement element,double value,double milliseconds=150,double delay=0,Action? completed=null)
    {
        double current=element.Opacity;element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=value;
        if(Enabled && Math.Abs(current-value)>.001)
        {
            var animation=new DoubleAnimation(current,value,TimeSpan.FromMilliseconds(milliseconds)){BeginTime=TimeSpan.FromMilliseconds(delay),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
            if(completed!=null)animation.Completed+=(_,_)=>completed();element.BeginAnimation(UIElement.OpacityProperty,animation);
        }
        else completed?.Invoke();
    }
    public static void Enter(FrameworkElement element,double distance=8,double delay=0)
    {
        if(!Enabled) {element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=1;element.RenderTransform=Transform.Identity;return;}
        element.Opacity=0;var move=new TranslateTransform(0,distance);element.RenderTransform=move;
        Fade(element,1,220,delay);To(move,TranslateTransform.YProperty,0,300,delay);
    }
    public static void Text(TextBlock element,string value)
    {
        if(element.Text==value)return;
        element.Text=value;
        // Live values must never fade to zero on every timer/media update.
    }
    public static void Icon(Button button,string name,double size,Brush? brush=null)
    {
        string key=name+"|"+size+"|"+brush;
        if(button.GetValue(IconKeyProperty) as string==key)return;
        button.SetValue(IconKeyProperty,key);button.Content=Icons.Create(name,size,brush);
    }
    private static readonly DependencyProperty IconKeyProperty=DependencyProperty.RegisterAttached("IconKey",typeof(string),typeof(Motion));
    public static void Button(Button button)
    {
        var scale=new ScaleTransform(1,1);var lift=new TranslateTransform();button.RenderTransformOrigin=new(.5,.5);button.RenderTransform=new TransformGroup{Children={scale,lift}};
        void State(bool hover,bool pressed=false)
        {
            bool active=Enabled && Interaction!="Off" && button.IsEnabled;bool expressive=Interaction=="Expressive";
            double value=!active?1:pressed?(expressive?.94:.965):hover?(expressive?1.035:1.018):1;
            To(scale,ScaleTransform.ScaleXProperty,value,pressed?100:220);To(scale,ScaleTransform.ScaleYProperty,value,pressed?100:220);To(lift,TranslateTransform.YProperty,active && hover && !pressed?(expressive?-1.5:-.6):0,180);
        }
        button.MouseEnter+=(_,_)=>State(true);button.MouseLeave+=(_,_)=>State(false);
        button.PreviewMouseLeftButtonDown+=(_,_)=>State(false,true);button.PreviewMouseLeftButtonUp+=(_,_)=>State(button.IsMouseOver);
        button.LostMouseCapture+=(_,_)=>State(button.IsMouseOver);button.IsEnabledChanged+=(_,_)=>State(button.IsMouseOver);
        button.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Space or Key.Enter)State(false,true);};button.PreviewKeyUp+=(_,_)=>State(false);
        button.Unloaded+=(_,_)=>State(false);
    }
    public static void Progress(System.Windows.Controls.Primitives.RangeBase bar,double value)
    {
        double current=bar.Value;bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,null);bar.Value=value;
        if(Enabled && bar.IsVisible && Math.Abs(current-value)>.05)bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,new DoubleAnimation(current,value,TimeSpan.FromMilliseconds(360)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
}

// Critically damped motion, integrated analytically so rapid reversals retain velocity.
// It drives only the painted surface. The native HWND never resizes on a render tick.
internal sealed class SurfaceMotion : IDisposable
{
    private sealed class Channel
    {
        public DependencyObject Target=null!;public DependencyProperty Property=null!;
        public double Value,Velocity,Destination;
    }
    private readonly List<Channel> channels=new();
    private bool subscribed;
    private TimeSpan? lastFrame;
    private double omega=16;
    private Action? completed;
    private readonly FramePacer pacer=new();
    internal bool Active=>subscribed;
    public void Go(DependencyObject target,DependencyProperty property,double destination,double milliseconds)
    {
        var channel=channels.Find(c=>c.Target==target && c.Property==property);
        if(channel==null){channel=new(){Target=target,Property=property,Value=(double)target.GetValue(property)};channels.Add(channel);}
        channel.Destination=destination;
        omega=7/Math.Max(.12,milliseconds/1000);
        if(!Motion.Enabled) {channel.Value=destination;channel.Velocity=0;target.SetValue(property,destination);return;}
        if(!subscribed){lastFrame=null;pacer.Reset();CompositionTarget.Rendering+=Render;subscribed=true;}
    }
    public void OnComplete(Action action)
    {completed=action;if(!subscribed){completed=null;action();}}
    private void Render(object? sender,EventArgs args)
    {
        var time=((RenderingEventArgs)args).RenderingTime;
        if(!pacer.Accept(time,Motion.FrameLimit))return;
        double dt=lastFrame.HasValue?Math.Clamp((time-lastFrame.Value).TotalSeconds,0,.05):1/60d;lastFrame=time;
        if(dt==0)return;
        bool finished=true;
        foreach(var channel in channels)if(channel.Target is IslandSurface surface)surface.BeginUpdate();
        try
        {
        foreach(var c in channels)
        {
            if(!Motion.Enabled){c.Value=c.Destination;c.Velocity=0;c.Target.SetValue(c.Property,c.Value);continue;}
            var delta=c.Value-c.Destination;var temp=(c.Velocity+omega*delta)*dt;var decay=Math.Exp(-omega*dt);
            c.Value=c.Destination+(delta+temp)*decay;c.Velocity=(c.Velocity-omega*temp)*decay;
            if(Math.Abs(c.Value-c.Destination)<.05 && Math.Abs(c.Velocity)<.5){c.Value=c.Destination;c.Velocity=0;}
            else finished=false;
            c.Target.SetValue(c.Property,c.Value);
        }
        }
        finally{foreach(var channel in channels)if(channel.Target is IslandSurface surface)surface.EndUpdate();}
        if(finished){Stop();var callback=completed;completed=null;callback?.Invoke();}
    }
    public void Snap(DependencyObject target,DependencyProperty property,double value)
    {channels.RemoveAll(c=>c.Target==target && c.Property==property);target.SetValue(property,value);}
    private void Stop() {if(subscribed)CompositionTarget.Rendering-=Render;subscribed=false;lastFrame=null;}
    public void Dispose(){Stop();channels.Clear();completed=null;}
}
