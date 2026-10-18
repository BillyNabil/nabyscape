using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NabyScape;

// Retained vector face: sparse idle blinks, rendering only during an active session.
internal sealed class AssistantOrb : FrameworkElement
{
    private AssistantState state;
    private bool rendering,reduceMotion,live,hovered,pressed;
    private readonly TranslateTransform gaze=new(),lift=new();
    private readonly RotateTransform lean=new(0,50,50);
    private readonly ScaleTransform squash=new(1,1,50,50),leftLid=new(1,1,35,41),rightLid=new(1,1,65,41);
    private readonly DispatcherTimer blinkTimer=new(){Interval=TimeSpan.FromSeconds(4.1)};
    private readonly FramePacer pacer=new();
    private double phase,level,displayLevel,last,lookX;
    private int blinks;
    private static readonly Brush body=Ink("#23262D"),eyes=Ink("#FAFBFC"),edge=Ink("#777D88"),errorEdge=Ink("#D79696"),approvalEdge=Ink("#CDBB8E");
    private static Brush Ink(string color){var brush=UI.Brush(color);brush.Freeze();return brush;}
    internal AssistantOrb()
    {
        Width=Height=52;
        Loaded+=(_,_)=>{Refresh();if(!reduceMotion && Motion.Enabled)Bounce(false);};
        Unloaded+=(_,_)=>{Detach();blinkTimer.Stop();ResetInteraction();};
        IsVisibleChanged+=(_,_)=>Refresh();
        MouseEnter+=(_,_)=>SetHover(true);MouseLeave+=(_,_)=>{SetHover(false);LookAt(0,0);Press(false);};
        MouseMove+=(_,e)=>{var p=e.GetPosition(this);LookAt(p.X/Math.Max(1,ActualWidth)*2-1,p.Y/Math.Max(1,ActualHeight)*2-1);};
        PreviewMouseLeftButtonDown+=(_,_)=>Press(true);PreviewMouseLeftButtonUp+=(_,_)=>Press(false);
        blinkTimer.Tick+=(_,_)=>{Blink();blinkTimer.Interval=TimeSpan.FromSeconds(3.3+(++blinks%4)*.7);};
    }
    internal bool Live {get=>live;set{if(live==value)return;live=value;InvalidateVisual();}}
    internal AssistantState State
    {
        get=>state;
        set{if(state==value)return;state=value;Refresh();if(IsVisible && !reduceMotion && Motion.Enabled){Blink(state==AssistantState.Approval);Bounce(state==AssistantState.Error);}}
    }
    internal bool ReduceMotion {get=>reduceMotion;set{reduceMotion=value;Refresh();}}
    internal double Level {get=>level;set=>level=Math.Clamp(double.IsFinite(value)?value:0,0,1);}
    internal bool Rendering=>rendering;
    internal bool BlinkScheduled=>blinkTimer.IsEnabled;
    internal bool Pressed=>pressed;
    internal Point Gaze=>new(gaze.X,gaze.Y);
    internal void LookAt(double x,double y)
    {
        x=Math.Clamp(double.IsFinite(x)?x:0,-1,1);y=Math.Clamp(double.IsFinite(y)?y:0,-1,1);lookX=x;
        Move(gaze,TranslateTransform.XProperty,x*6,160);Move(gaze,TranslateTransform.YProperty,y*4,160);Move(lean,RotateTransform.AngleProperty,x*5,220);
    }
    internal void SetHover(bool value)
    {
        if(hovered==value)return;hovered=value;if(!IsVisible)return;
        Move(squash,ScaleTransform.ScaleXProperty,value?1.045:1,200);Move(squash,ScaleTransform.ScaleYProperty,value?1.045:1,200);Move(lift,TranslateTransform.YProperty,value?-2:0,220);
        if(value)Blink();
    }
    internal void Press(bool value)
    {
        if(pressed==value)return;pressed=value;
        if(value){Move(squash,ScaleTransform.ScaleXProperty,1.13,100);Move(squash,ScaleTransform.ScaleYProperty,.86,100);Blink(true);}else Bounce(false);
    }
    private void Move(Animatable target,DependencyProperty property,double value,double duration)
    {
        if(reduceMotion || !Motion.Enabled || !IsVisible){target.BeginAnimation(property,null);target.SetValue(property,value);}else Motion.To(target,property,value,duration);
    }
    internal void Blink(bool wink=false)
    {
        if(!IsLoaded || !IsVisible || reduceMotion || !Motion.Enabled)return;
        Animate(leftLid,ScaleTransform.ScaleYProperty,1,.08,1,180);if(!wink)Animate(rightLid,ScaleTransform.ScaleYProperty,1,.08,1,180,24);
    }
    private static void Animate(Animatable target,DependencyProperty property,double start,double middle,double end,double duration,double delay=0)
    {
        target.BeginAnimation(property,null);target.SetValue(property,end);
        var animation=new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromMilliseconds(duration),BeginTime=TimeSpan.FromMilliseconds(delay),FillBehavior=FillBehavior.Stop};
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(start,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(middle,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration*.42)),new CubicEase{EasingMode=EasingMode.EaseOut}));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(end,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration)),new CubicEase{EasingMode=EasingMode.EaseInOut}));target.BeginAnimation(property,animation);
    }
    private void Bounce(bool shake)
    {
        double target=hovered?1.045:1;
        if(reduceMotion || !Motion.Enabled){Move(squash,ScaleTransform.ScaleXProperty,1,0);Move(squash,ScaleTransform.ScaleYProperty,1,0);return;}
        Animate(squash,ScaleTransform.ScaleXProperty,squash.ScaleX,1.075,target,280);Animate(squash,ScaleTransform.ScaleYProperty,squash.ScaleY,.95,target,280);
        if(shake)Animate(lean,RotateTransform.AngleProperty,-7,7,lookX*5,300);
    }
    private void ResetInteraction()
    {
        hovered=pressed=false;lookX=0;
        gaze.BeginAnimation(TranslateTransform.XProperty,null);gaze.BeginAnimation(TranslateTransform.YProperty,null);lift.BeginAnimation(TranslateTransform.YProperty,null);lean.BeginAnimation(RotateTransform.AngleProperty,null);
        squash.BeginAnimation(ScaleTransform.ScaleXProperty,null);squash.BeginAnimation(ScaleTransform.ScaleYProperty,null);leftLid.BeginAnimation(ScaleTransform.ScaleYProperty,null);rightLid.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        gaze.X=gaze.Y=lift.Y=lean.Angle=0;squash.ScaleX=squash.ScaleY=leftLid.ScaleY=rightLid.ScaleY=1;
    }
    private void Refresh()
    {
        bool visible=IsLoaded && IsVisible;
        bool animate=visible && !reduceMotion && Motion.Enabled && state is AssistantState.Connecting or AssistantState.Listening or AssistantState.Thinking or AssistantState.Speaking;
        if(animate && !rendering){rendering=true;last=0;pacer.Reset();CompositionTarget.Rendering+=Frame;}else if(!animate)Detach();
        if(visible && !reduceMotion && Motion.Enabled){if(!blinkTimer.IsEnabled)blinkTimer.Start();}else{blinkTimer.Stop();ResetInteraction();}
        InvalidateVisual();
    }
    private void Detach(){if(rendering)CompositionTarget.Rendering-=Frame;rendering=false;last=0;}
    private void Frame(object? sender,EventArgs args)
    {
        if(args is not RenderingEventArgs frame || !pacer.Accept(frame.RenderingTime,Motion.FrameLimit))return;
        double now=frame.RenderingTime.TotalSeconds,dt=last==0?0:Math.Clamp(now-last,0,.05);last=now;phase+=dt;displayLevel+=(level-displayLevel)*(1-Math.Exp(-dt*16));InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        double size=Math.Min(ActualWidth,ActualHeight);if(size<=0)return;
        bool moving=rendering && !reduceMotion && Motion.Enabled;double t=moving?phase:0,energy=moving && state is AssistantState.Listening or AssistantState.Speaking?displayLevel:0;
        double bob=moving?Math.Sin(t*(state==AssistantState.Thinking?3:2))*1.2:0;
        dc.PushTransform(new MatrixTransform(size/100,0,0,size/100,(ActualWidth-size)/2,(ActualHeight-size)/2));dc.PushTransform(lift);dc.PushTransform(lean);dc.PushTransform(squash);
        dc.PushTransform(new RotateTransform(state==AssistantState.Thinking && moving?Math.Sin(t*2.3)*5:0,50,50));
        var shape=new StreamGeometry();
        Point PointAt(int i){double a=i*Math.PI*2/12,r=43*(1+.025*Math.Cos(a*3+t*1.8)+.012*Math.Sin(a*5-t)+energy*.035*Math.Sin(a*2+t*9));return new(50+Math.Cos(a)*r,51+bob+Math.Sin(a)*r);}
        using(var path=shape.Open())
        {
            path.BeginFigure(PointAt(0),true,true);
            for(int i=0;i<12;i++){var p0=PointAt(i-1);var p1=PointAt(i);var p2=PointAt(i+1);var p3=PointAt(i+2);path.BezierTo(new(p1.X+(p2.X-p0.X)/6,p1.Y+(p2.Y-p0.Y)/6),new(p2.X-(p3.X-p1.X)/6,p2.Y-(p3.Y-p1.Y)/6),p2,true,false);}
        }
        dc.DrawGeometry(body,new Pen(state==AssistantState.Error?errorEdge:state==AssistantState.Approval?approvalEdge:edge,Math.Max(.85,65/size)),shape);dc.PushTransform(gaze);
        double searching=state==AssistantState.Thinking && moving?Math.Sin(t*2.3)*4:0,height=state==AssistantState.Listening?31:state==AssistantState.Thinking?23:state==AssistantState.Error?20:27;
        for(int i=0;i<2;i++)
        {
            double x=i==0?35:65,y=41+bob+(state==AssistantState.Approval?(i==0?-3:3):0),h=height+(state==AssistantState.Speaking && moving?Math.Sin(t*11+i*.9)*energy*8+energy*5:0);
            dc.PushTransform(i==0?leftLid:rightLid);dc.PushTransform(new RotateTransform(state==AssistantState.Error?(i==0?-18:18):-8+(state==AssistantState.Thinking?searching:0),x,y));
            dc.DrawRoundedRectangle(eyes,null,new(x-7.5+searching,y-h/2,15,h),7.5,7.5);dc.Pop();dc.Pop();
        }
        dc.Pop();dc.Pop();dc.Pop();dc.Pop();dc.Pop();
        if(live)dc.DrawEllipse(eyes,new Pen(body,Math.Max(2,110/size)),new(83,83),6.5,6.5);
        dc.Pop();
    }
}
