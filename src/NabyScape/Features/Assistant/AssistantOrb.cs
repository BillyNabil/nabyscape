using System;
using System.Windows;
using System.Windows.Media;

namespace NabyScape;


// Original vector character, no video/WebView/shader or hidden idle render loop.
internal sealed class AssistantOrb : FrameworkElement
{
    private AssistantState state;
    private bool rendering,reduceMotion;
    private double phase,level,last;
    private readonly FramePacer pacer=new();
    private static readonly Brush body=Frozen(new RadialGradientBrush(Color.FromRgb(250,251,255),Color.FromRgb(118,128,146)){GradientOrigin=new(.35,.25),Center=new(.42,.35),RadiusX=.8,RadiusY=.8});
    private static Brush Frozen(Brush brush){brush.Freeze();return brush;}
    internal AssistantOrb(){Width=Height=52;Loaded+=(_,_)=>Refresh();Unloaded+=(_,_)=>Detach();IsVisibleChanged+=(_,_)=>Refresh();}
    internal AssistantState State {get=>state;set{state=value;Refresh();}}
    internal bool ReduceMotion {get=>reduceMotion;set{reduceMotion=value;Refresh();}}
    internal double Level {get=>level;set{level=Math.Clamp(double.IsFinite(value)?value:0,0,1);if(!rendering)InvalidateVisual();}}
    internal bool Rendering=>rendering;
    private void Refresh()
    {
        bool animate=IsLoaded && IsVisible && !reduceMotion && state is AssistantState.Connecting or AssistantState.Listening or AssistantState.Thinking or AssistantState.Speaking;
        if(animate && !rendering){rendering=true;last=0;pacer.Reset();CompositionTarget.Rendering+=Frame;}
        else if(!animate)Detach();
        InvalidateVisual();
    }
    private void Detach(){if(rendering)CompositionTarget.Rendering-=Frame;rendering=false;last=0;}
    private void Frame(object? sender,EventArgs args)
    {
        if(args is not RenderingEventArgs frame || !pacer.Accept(frame.RenderingTime,Motion.FrameLimit))return;
        double now=frame.RenderingTime.TotalSeconds,dt=last==0?0:Math.Clamp(now-last,0,.05);last=now;phase+=dt;InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        double size=Math.Min(ActualWidth,ActualHeight),cx=ActualWidth/2,cy=ActualHeight/2,r=size*.30;
        if(size<=0)return;
        double energy=state is AssistantState.Listening or AssistantState.Speaking?level:state==AssistantState.Thinking ? .25 : 0;
        var outline=new Pen(UI.Brush(state==AssistantState.Error?"#E6A3A3":state==AssistantState.Approval?"#DDD0A9":"#555D6D"),Math.Max(.7,size*.012));
        dc.DrawEllipse(null,outline,new(cx,cy),size*.43,size*.43);
        var geometry=new StreamGeometry();
        using(var context=geometry.Open())
        {
            for(int i=0;i<=64;i++)
            {
                double a=i*Math.PI*2/64,delta=reduceMotion?0:Math.Sin(a*3+phase*2.6)*(.018+energy*.075)+Math.Sin(a*5-phase*1.6)*energy*.03;
                var point=new Point(cx+Math.Cos(a)*r*(1+delta),cy+Math.Sin(a)*r*(1+delta));
                if(i==0)context.BeginFigure(point,true,true);else context.LineTo(point,true,false);
            }
        }
        dc.DrawGeometry(body,null,geometry);
        var ink=UI.Brush("#161A22");double eye=size*.026;
        dc.DrawEllipse(ink,null,new(cx-size*.085,cy-size*.026),eye,eye);
        dc.DrawEllipse(ink,null,new(cx+size*.085,cy-size*.026),eye,eye);
        if(state==AssistantState.Speaking && energy>.02)dc.DrawRoundedRectangle(ink,null,new(cx-size*.045,cy+size*.075,size*.09,size*(.018+energy*.09)),eye,eye);
        else dc.DrawLine(new Pen(ink,Math.Max(1,size*.022)),new(cx-size*.035,cy+size*.085),new(cx+size*.035,cy+size*.085));
    }
}
