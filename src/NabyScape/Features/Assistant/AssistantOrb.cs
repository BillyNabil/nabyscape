using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NabyScape;

// Idle motion uses retained WPF transforms; managed frame callbacks are for live states.
internal sealed class AssistantOrb : FrameworkElement
{
    private AssistantState state;
    private bool rendering,reduceMotion,live,hovered,pressed,idleAnimating,music;
    private readonly ScaleTransform headphonesReveal=new(0,0,50,50);
    private readonly TranslateTransform idleFloat=new();
    private readonly ScaleTransform idleBreath=new(1,1,50,50);
    private readonly RotateTransform idleSway=new(0,50,50);
    private readonly TranslateTransform gaze=new(),lift=new();
    private readonly RotateTransform lean=new(0,50,50);
    private readonly ScaleTransform squash=new(1,1,50,50),expression=new(1,1,50,41),leftLid=new(1,1,35,41),rightLid=new(1,1,65,41);
    private readonly DispatcherTimer blinkTimer=new(){Interval=TimeSpan.FromSeconds(1.6)};
    private readonly FramePacer pacer=new();
    private double phase,level,displayLevel,last,lookX;
    private int blinks;
    private static readonly Brush body=Ink("#23262D"),eyes=Ink("#FAFBFC"),edge=Ink("#777D88"),errorEdge=Ink("#D79696"),approvalEdge=Ink("#CDBB8E");
    private static readonly Geometry headphoneBand=Band();
    private static Geometry Band(){var geometry=Geometry.Parse("M12,50 L12,43 C12,-3 88,-3 88,43 L88,50");geometry.Freeze();return geometry;}
    private static Brush Ink(string color){var brush=UI.Brush(color);brush.Freeze();return brush;}
    internal static Button Launcher(string name,Action action,AssistantOrb orb)
    {
        // The face owns press/hover motion; the ordinary button scale would double it.
        var button=new Button{Content=orb,Padding=new(0),Margin=new(0),Background=Brushes.Transparent,Tag=new CornerRadius(100),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(button,name);button.Click+=(_,_)=>action();
        button.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Space or Key.Enter)orb.Press(true);};
        button.PreviewKeyUp+=(_,e)=>{if(e.Key is Key.Space or Key.Enter)orb.Press(false);};
        button.LostMouseCapture+=(_,_)=>orb.Press(false);button.LostKeyboardFocus+=(_,_)=>orb.Press(false);
        return button;
    }
    internal AssistantOrb()
    {
        Width=Height=52;
        Loaded+=(_,_)=>{Refresh();if(!reduceMotion && Motion.Enabled)Bounce(false);};
        Unloaded+=(_,_)=>{Detach();StopIdle();blinkTimer.Stop();ResetInteraction();};
        IsVisibleChanged+=(_,_)=>Refresh();
        MouseEnter+=(_,_)=>SetHover(true);MouseLeave+=(_,_)=>{SetHover(false);if(TrackPointerLocally)LookAt(0,0);Press(false);};
        MouseMove+=(_,e)=>{if(!TrackPointerLocally)return;var p=e.GetPosition(this);LookAt(p.X/Math.Max(1,ActualWidth)*2-1,p.Y/Math.Max(1,ActualHeight)*2-1);};
        PreviewMouseLeftButtonDown+=(_,_)=>Press(true);PreviewMouseLeftButtonUp+=(_,_)=>Press(false);
        blinkTimer.Tick+=(_,_)=>
        {
            Blink();blinkTimer.Interval=TimeSpan.FromSeconds(2.2+(++blinks%4)*.55);
            if(state==AssistantState.Idle && !hovered && Math.Abs(lookX)<.05 && blinks%2==0)
            {
                // Occasional glances layer over breathing, sharing the blink timer.
                Animate(gaze,TranslateTransform.XProperty,gaze.X,blinks%4==0?4:-4,0,850,130);
                Animate(gaze,TranslateTransform.YProperty,gaze.Y,-1.5,0,850,130);
                Animate(lean,RotateTransform.AngleProperty,lean.Angle,blinks%4==0?3:-3,0,950,130);
            }
        };
    }
    internal bool Live {get=>live;set{if(live==value)return;live=value;InvalidateVisual();}}
    internal bool Music
    {
        get=>music;
        set{if(music==value)return;music=value;Move(headphonesReveal,ScaleTransform.ScaleXProperty,value?1:0,260);Move(headphonesReveal,ScaleTransform.ScaleYProperty,value?1:0,260);StopIdle();RefreshIdle();InvalidateVisual();}
    }
    internal AssistantState State
    {
        get=>state;
        set{if(state==value)return;state=value;Move(expression,ScaleTransform.ScaleYProperty,EyeScale,220);Refresh();if(IsVisible && !reduceMotion && Motion.Enabled){Blink(state==AssistantState.Approval);Bounce(state==AssistantState.Error);}}
    }
    internal bool ReduceMotion {get=>reduceMotion;set{reduceMotion=value;Refresh();}}
    internal double Level {get=>level;set=>level=Math.Clamp(double.IsFinite(value)?value:0,0,1);}
    internal bool Rendering=>rendering;
    internal bool IdleAnimating=>idleAnimating;
    internal double IdleOffset=>idleFloat.Y;
    internal bool BlinkScheduled=>blinkTimer.IsEnabled;
    internal bool Pressed=>pressed;
    internal bool TrackPointerLocally {get;set;}=true;
    internal Point Gaze=>new(gaze.X,gaze.Y);
    private double EyeScale=>(state==AssistantState.Listening?34:state==AssistantState.Thinking?25:state==AssistantState.Error?23:hovered?34:30)/30d;
    internal void LookAt(double x,double y)
    {
        x=Math.Clamp(double.IsFinite(x)?x:0,-1,1);y=Math.Clamp(double.IsFinite(y)?y:0,-1,1);lookX=x;
        Move(gaze,TranslateTransform.XProperty,x*6,120);Move(gaze,TranslateTransform.YProperty,y*4,120);Move(lean,RotateTransform.AngleProperty,x*5,180);
    }
    internal void SetHover(bool value)
    {
        if(hovered==value)return;hovered=value;if(!IsVisible)return;
        RefreshIdle();
        if(!pressed){Move(squash,ScaleTransform.ScaleXProperty,value?1.06:1,180);Move(squash,ScaleTransform.ScaleYProperty,value?1.06:1,180);}
        Move(lift,TranslateTransform.YProperty,value?-2:0,180);Move(expression,ScaleTransform.ScaleYProperty,EyeScale,140);
    }
    internal void Press(bool value)
    {
        if(pressed==value)return;pressed=value;
        RefreshIdle();
        if(value){Move(squash,ScaleTransform.ScaleXProperty,1.07,90);Move(squash,ScaleTransform.ScaleYProperty,.94,90);Blink(true);}else Bounce(false);
    }
    private void Move(Animatable target,DependencyProperty property,double value,double duration)
    {
        if(reduceMotion || !Motion.Enabled || !IsVisible){target.BeginAnimation(property,null);target.SetValue(property,value);}else Motion.To(target,property,value,duration);
    }
    internal void Blink(bool wink=false)
    {
        if(!IsLoaded || !IsVisible || reduceMotion || !Motion.Enabled)return;
        Animate(leftLid,ScaleTransform.ScaleYProperty,leftLid.ScaleY,.06,1,240);if(!wink)Animate(rightLid,ScaleTransform.ScaleYProperty,rightLid.ScaleY,.06,1,240,18);
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
        double target=hovered?1.06:1;
        if(reduceMotion || !Motion.Enabled){Move(squash,ScaleTransform.ScaleXProperty,1,0);Move(squash,ScaleTransform.ScaleYProperty,1,0);return;}
        Animate(squash,ScaleTransform.ScaleXProperty,squash.ScaleX,1.085,target,280);Animate(squash,ScaleTransform.ScaleYProperty,squash.ScaleY,1.085,target,280);
        if(shake)Animate(lean,RotateTransform.AngleProperty,lean.Angle,7,lookX*5,300);
    }
    private void ResetInteraction()
    {
        hovered=pressed=false;lookX=0;
        gaze.BeginAnimation(TranslateTransform.XProperty,null);gaze.BeginAnimation(TranslateTransform.YProperty,null);lift.BeginAnimation(TranslateTransform.YProperty,null);lean.BeginAnimation(RotateTransform.AngleProperty,null);
        squash.BeginAnimation(ScaleTransform.ScaleXProperty,null);squash.BeginAnimation(ScaleTransform.ScaleYProperty,null);leftLid.BeginAnimation(ScaleTransform.ScaleYProperty,null);rightLid.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        expression.BeginAnimation(ScaleTransform.ScaleYProperty,null);expression.ScaleY=EyeScale;
        headphonesReveal.BeginAnimation(ScaleTransform.ScaleXProperty,null);headphonesReveal.BeginAnimation(ScaleTransform.ScaleYProperty,null);headphonesReveal.ScaleX=headphonesReveal.ScaleY=music?1:0;
        gaze.X=gaze.Y=lift.Y=lean.Angle=0;squash.ScaleX=squash.ScaleY=leftLid.ScaleY=rightLid.ScaleY=1;
    }
    private void Refresh()
    {
        bool visible=IsLoaded && IsVisible;
        bool animate=visible && !reduceMotion && Motion.Enabled && state is AssistantState.Connecting or AssistantState.Listening or AssistantState.Thinking or AssistantState.Speaking;
        if(animate && !rendering){rendering=true;last=0;pacer.Reset();CompositionTarget.Rendering+=Frame;}else if(!animate)Detach();
        if(visible && !reduceMotion && Motion.Enabled){if(!blinkTimer.IsEnabled)blinkTimer.Start();}else{blinkTimer.Stop();ResetInteraction();}
        RefreshIdle();
        InvalidateVisual();
    }
    private void RefreshIdle()
    {
        bool animate=IsLoaded && IsVisible && state==AssistantState.Idle && !hovered && !pressed && !reduceMotion && Motion.Enabled;
        if(!animate){StopIdle();return;}
        if(idleAnimating)return;
        idleAnimating=true;
        void Loop(Animatable target,DependencyProperty property,double from,double to,double seconds)
        {
            target.BeginAnimation(property,new DoubleAnimation(from,to,TimeSpan.FromSeconds(seconds))
            {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
        }
        // Uniform breathing preserves the round silhouette. Independent transforms
        // leave pointer, hover and press feedback free to interrupt immediately.
        Loop(idleBreath,ScaleTransform.ScaleXProperty,1,1.035,music?.7:2.4);
        Loop(idleBreath,ScaleTransform.ScaleYProperty,1,1.035,music?.7:2.4);
        Loop(idleFloat,TranslateTransform.YProperty,0,-3.5,music?.65:2.1);
        Loop(idleSway,RotateTransform.AngleProperty,0,music?-5:-3,music?1.1:3.4);
    }
    private void StopIdle()
    {
        if(!idleAnimating)return;
        idleAnimating=false;
        idleBreath.BeginAnimation(ScaleTransform.ScaleXProperty,null);idleBreath.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        idleFloat.BeginAnimation(TranslateTransform.YProperty,null);idleSway.BeginAnimation(RotateTransform.AngleProperty,null);
        idleBreath.ScaleX=idleBreath.ScaleY=1;idleFloat.Y=idleSway.Angle=0;
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
        dc.PushTransform(new MatrixTransform(size/100,0,0,size/100,(ActualWidth-size)/2,(ActualHeight-size)/2));dc.PushTransform(idleFloat);dc.PushTransform(idleSway);dc.PushTransform(idleBreath);dc.PushTransform(lift);dc.PushTransform(lean);dc.PushTransform(squash);
        dc.PushTransform(new RotateTransform(state==AssistantState.Thinking && moving?Math.Sin(t*2.3)*5:0,50,50));
        // Keep a circular silhouette in every resting/live state. Expression lives in
        // the eyes and posture; only an intentional press briefly compresses it.
        double radius=43*(1+energy*.018);
        dc.DrawEllipse(body,new Pen(state==AssistantState.Error?errorEdge:state==AssistantState.Approval?approvalEdge:edge,Math.Max(.85,65/size)),new(50,50+bob),radius,radius);
        dc.PushTransform(headphonesReveal);
        dc.DrawGeometry(null,new Pen(eyes,5),headphoneBand);
        dc.DrawRoundedRectangle(edge,new Pen(eyes,1.5),new Rect(5,43,14,27),6,6);
        dc.DrawRoundedRectangle(edge,new Pen(eyes,1.5),new Rect(81,43,14,27),6,6);
        dc.Pop();dc.PushTransform(gaze);dc.PushTransform(expression);
        double searching=state==AssistantState.Thinking && moving?Math.Sin(t*2.3)*4:0,height=30;
        for(int i=0;i<2;i++)
        {
            double x=i==0?35:65,y=41+bob+(state==AssistantState.Approval?(i==0?-3:3):0),h=height+(state==AssistantState.Speaking && moving?Math.Sin(t*11+i*.9)*energy*8+energy*5:0);
            dc.PushTransform(i==0?leftLid:rightLid);dc.PushTransform(new RotateTransform(state==AssistantState.Error?(i==0?-18:18):-8+(state==AssistantState.Thinking?searching:0),x,y));
            dc.DrawRoundedRectangle(eyes,null,new(x-8+searching,y-h/2,16,h),8,8);dc.Pop();dc.Pop();
        }
        dc.Pop();dc.Pop();dc.Pop();dc.Pop();dc.Pop();dc.Pop();
        if(live)dc.DrawEllipse(eyes,new Pen(body,Math.Max(2,110/size)),new(83,83),6.5,6.5);
        dc.Pop();dc.Pop();dc.Pop();dc.Pop();
    }
}
