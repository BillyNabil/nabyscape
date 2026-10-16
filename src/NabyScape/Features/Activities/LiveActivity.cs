using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NabyScape;

// Output-envelope history, not synthetic frequency data. No loopback capture or microphone.
internal sealed class ActivityVisual : FrameworkElement
{
    private readonly double[] history=new double[25];
    private readonly double[] levels=new double[25];
    internal double WaveLevel(int index)=>levels[index];
    private double envelope,progress,pulse=1,levelFloor,levelCeiling=.15;
    private bool countdown,focus;
    private static readonly Brush focusColor=UI.Brush("#30D158"),timerColor=UI.Brush("#FF9F0A");
    private static readonly Pen trackPen=new(UI.Brush("#353537"),1.8),focusPen=new(focusColor,1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},timerPen=new(timerColor,1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
    internal bool WaveOnly {get;init;}
    internal bool Slender {get;init;}
    internal double Envelope=>envelope;
    internal ActivityVisual(){Width=18;Height=18;VerticalAlignment=VerticalAlignment.Center;IsHitTestVisible=false;}
    internal void Sample(double peak,double dt,double fraction,bool session,bool focused,bool animate,double seconds,bool advanceHistory=true,bool decorative=false)
    {
        peak=double.IsFinite(peak)?Math.Clamp(peak,0,1):0;dt=Math.Clamp(dt,.001,.5);
        double target=0;
        if(animate && peak>.002)
        {
            // Music is often mastered into a narrow peak range. Expand its measured dynamics
            // locally instead of adding decorative oscillation to an audible signal.
            levelFloor=peak+(levelFloor-peak)*Math.Exp(-dt/(peak<levelFloor?.045:2.4));
            levelCeiling=peak+(levelCeiling-peak)*Math.Exp(-dt/(peak>levelCeiling?.018:1.2));
            double dynamics=Math.Clamp((peak-levelFloor)/Math.Max(.04,levelCeiling-levelFloor),0,1);
            target=.22*Math.Sqrt(peak)+.78*dynamics;
        }
        if(!animate){levelFloor=0;levelCeiling=.15;}
        if(animate && decorative && peak<.002)target=Math.Max(target,.055+.035*Math.Sin(seconds*3.1)+.02*Math.Sin(seconds*5.7));
        double before=envelope;envelope=animate?target+(envelope-target)*Math.Exp(-dt/(target>envelope?.018:.09)):0;
        if(!animate)Array.Clear(history);
        if(envelope<.001)envelope=0;
        bool changed=Math.Abs(before-envelope)>.0003 || Math.Abs(progress-fraction)>.00001 || session!=countdown || focused!=focus;
        if(advanceHistory){for(int i=history.Length-1;i>0;i--){if(Math.Abs(history[i]-history[i-1])>.001)changed=true;history[i]=history[i-1];}history[0]=envelope;}
        for(int i=0;i<levels.Length;i++)
        {
            // Staggered amplitude envelopes, not frequency bands. Avoid mirrored motion.
            int delay=(i*7+i/4)%19;
            double recent=Math.Clamp(history[delay]+(envelope-history[0])*Math.Exp(-delay*.12),0,1);
            double transient=Math.Abs(history[delay]-history[Math.Min(24,delay+3)]);
            double gain=.62+.38*((i*11+3)%17)/16d;
            double targetBar=Math.Clamp(recent*gain+transient*(.2+.4*(i%3)/2d),0,1);
            double old=levels[i],response=targetBar>old?.022+.004*(i%5):.11+.019*(i%7);
            levels[i]=animate?targetBar+(old-targetBar)*Math.Exp(-dt/response):0;
            if(levels[i]<.0001)levels[i]=0;
            if(Math.Abs(old-levels[i])>.0002)changed=true;
        }
        progress=fraction;countdown=session;focus=focused;
        double breathing=animate && session && focused?.76+.24*Math.Sin(seconds*Math.PI/1.9):1;
        if(Math.Abs(breathing-pulse)>.002)changed=true;pulse=breathing;
        if(changed)InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w=ActualWidth,h=ActualHeight;
        if(w<2 || h<2)return;
        if(countdown && !WaveOnly)
        {
            var c=new Point(w/2,h/2);double r=Math.Min(w,h)/2-1.5;
            dc.DrawEllipse(null,trackPen,c,r,r);
            var brush=focus?focusColor:timerColor;
            dc.PushOpacity(pulse);dc.DrawEllipse(brush,null,c,2.2,2.2);
            double angle=Math.Clamp(progress,0,.99999)*Math.PI*2;
            if(angle>.001)
            {
                var path=new StreamGeometry();using(var s=path.Open()){s.BeginFigure(new Point(c.X,c.Y-r),false,false);s.ArcTo(new Point(c.X+r*Math.Sin(angle),c.Y-r*Math.Cos(angle)),new Size(r,r),0,angle>Math.PI,SweepDirection.Clockwise,true,false);}path.Freeze();
                dc.DrawGeometry(null,focus?focusPen:timerPen,path);
            }
            dc.Pop();return;
        }
        int count=WaveOnly?Math.Min(25,Math.Max(5,(int)(w/3))):5;double gap=WaveOnly?1.5:1.4,bw=Math.Max(.4,(w-gap*(count-1))/count);
        if(Slender){bw=Math.Min(1.2,Math.Max(.4,(w-(count-1)*1.8)/count));gap=Math.Min(1.8,Math.Max(.8,(w-count*bw)/Math.Max(1,count-1)));}
        double start=Slender?(w-(count*bw+(count-1)*gap))/2:0;
        for(int i=0;i<count;i++)
        {
            int index=(int)Math.Round(i*24d/Math.Max(1,count-1));
            double v=levels[index];double baseline=Slender?1:1.8;double height=baseline+(h-2)*v;
            dc.DrawRoundedRectangle(Brushes.White,null,new Rect(start+i*(bw+gap),(h-height)/2,bw,height),bw/2,bw/2);
        }
    }
}

public sealed partial class OverlayWindow
{
    private readonly ActivityVisual activity=new(),musicWave=new(){Width=96,Height=16,WaveOnly=true,HorizontalAlignment=HorizontalAlignment.Center};
    private readonly DispatcherTimer liveTick=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(250)};
    private DateTimeOffset lastLive,quietSince,feedbackUntil,timelineAt;
    private long audioSequence;
    private double sampledPeak,reflectionX=.5,reflectionY=.25;
    private bool liveRendering;
    private TimeSpan? renderedAt;
    internal int LiveFrames {get;private set;}
    private double timelineElapsed,timelineDuration;
    private bool audioSeen;
    private float lastVolume;
    private bool lastMuted;
    private string lastDevice="",feedbackText="";
    internal bool LiveRunning=>liveTick.IsEnabled || liveRendering;
    internal bool DisplaySynchronized=>liveRendering;
    internal double LiveInterval=>liveTick.Interval.TotalMilliseconds;
    private bool FeedbackVisible=>app.Settings.ReactiveFeedback && (feedbackBrightness?app.Settings.BrightnessFeedback:app.Settings.VolumeFeedback) && !expanded && !peeking && IsVisible && !TuckRequested && !userHidden && DateTimeOffset.UtcNow<feedbackUntil;
    private void InitializeLiveActivity()
    {
        root.MouseMove+=(_,_)=>{if(app.Settings.GlassStyle && (expanded || peeking))WakeLiveRendering();};
        InitializeFeedback();
        IsVisibleChanged+=(_,_)=>{ConfigureLiveActivity();if(!IsVisible)ClearFeedback();};
    }
    private bool AudioVisualDemand=>IsVisible && !IsTucked && app.Settings.LiveActivity && app.Media.Playing && WidgetOn("media") &&
        ((!expanded && !peeking && !FeedbackVisible && (activity.IsVisible || closedTitleWave.IsVisible)) || (expanded && musicWave.IsVisible) || (peeking && peekId=="media" && peekWave.IsVisible));
    private bool ReflectionMoving
    {
        get
        {
            if(!app.Settings.GlassStyle || !(expanded || peeking || motion.Active))return false;
            var point=Mouse.GetPosition(this);
            double x=Math.Clamp((point.X-(ActualWidth-surface.SurfaceWidth)/2)/Math.Max(1,surface.SurfaceWidth),0,1),y=Math.Clamp(point.Y/Math.Max(1,surface.SurfaceHeight),0,1);
            return Math.Abs(x-reflectionX)+Math.Abs(y-reflectionY)>.001;
        }
    }
    private bool HasLiveWork=>AudioVisualDemand || motion.Active || ReflectionMoving ||
        (app.Media.Playing && timelineDuration>0 && ((peeking && peekId=="media") || (expanded && musicWave.IsVisible))) ||
        (ActiveCountdown()?.Running==true && ((!expanded && !peeking && activity.IsVisible) || (peeking && peekId is "timer" or "focus")));
    private void ConfigureLiveActivity()
    {
        StopLiveRendering();liveTick.Stop();lastLive=DateTimeOffset.UtcNow;quietSince=lastLive;
        if(IsVisible && !IsTucked && app.Settings.LiveActivity && Motion.Enabled){if(HasLiveWork)WakeLiveRendering();}
        else{activity.Sample(0,1,ActiveCountdown()?.Progress(lastLive)??0,ActiveCountdown()!=null,app.Session.Focus.Remaining(lastLive)>0,false,0);closedTitleWave.Sample(0,1,0,false,false,false,0);musicWave.Sample(0,1,0,false,false,false,0);peekWave.Sample(0,1,0,false,false,false,0);}
    }
    private void WakeLiveRendering()
    {
        if(liveRendering || !IsVisible || IsTucked || !app.Settings.LiveActivity || !Motion.Enabled || !HasLiveWork)return;
        lastLive=quietSince=DateTimeOffset.UtcNow;renderedAt=null;livePacer.Reset();liveRendering=true;CompositionTarget.Rendering+=DisplayFrame;
    }
    private void StopLiveRendering(){if(liveRendering)CompositionTarget.Rendering-=DisplayFrame;liveRendering=false;renderedAt=null;}
    private void DisplayFrame(object? sender,EventArgs args)
    {
        var stamp=((RenderingEventArgs)args).RenderingTime;if(renderedAt==stamp || !livePacer.Accept(stamp,Motion.FrameLimit))return;renderedAt=stamp;LiveFrames++;FrameTelemetry.Sample(stamp);LiveFrame();
    }
    private Countdown? ActiveCountdown()
    {
        var now=DateTimeOffset.UtcNow;
        if(ShowMediaPrimary)return null;
        return WidgetOn("focus") && app.Session.Focus.Remaining(now)>0?app.Session.Focus:WidgetOn("timer") && app.Session.Timer.Remaining(now)>0?app.Session.Timer:null;
    }
    private void LiveFrame()
    {
        var now=DateTimeOffset.UtcNow;double dt=Math.Clamp((now-lastLive).TotalSeconds,.001,.5);lastLive=now;
        bool audioWanted=AudioVisualDemand,sample=false;
        if(audioWanted){sampledPeak=app.Audio.MediaPeak(app.Media.Selected?.SourceAppUserModelId);sample=audioSequence!=app.Audio.MediaLevel.Sequence;audioSequence=app.Audio.MediaLevel.Sequence;}
        else sampledPeak=0;
        double peak=app.Media.Playing && !app.Audio.MediaMeterMuted?sampledPeak:0;
        var session=ActiveCountdown();bool running=session?.Running==true;
        if(HasLiveWork)quietSince=now;
        if((now-quietSince).TotalSeconds>1.2){StopLiveRendering();return;}
        bool decorative=app.Settings.WaveformIdleMotion && app.Media.Playing && !app.Audio.MediaMeterMuted;
        double seconds=now.ToUnixTimeMilliseconds()/1000d;
        if(!expanded && !peeking && activity.IsVisible)activity.Sample(peak,dt,session?.Progress(now)??0,session!=null,session==app.Session.Focus,running || session==null,seconds,sample,decorative);
        if(!expanded && !peeking && closedTitleWave.IsVisible)closedTitleWave.Sample(peak,dt,0,false,false,app.Media.Playing && !app.Audio.MediaMeterMuted,seconds,sample,decorative);
        if(expanded && musicWave.IsVisible)musicWave.Sample(peak,dt,0,false,false,app.Media.Playing && !app.Audio.MediaMeterMuted,seconds,sample,decorative);
        if(peeking && peekId=="media" && peekWave.IsVisible)peekWave.Sample(peak,dt,0,false,false,app.Media.Playing && !app.Audio.MediaMeterMuted,seconds,sample,decorative);
        if(app.Settings.GlassStyle && (peeking || expanded || motion.Active))
        {
            var p=Mouse.GetPosition(this);double x=Math.Clamp((p.X-(ActualWidth-surface.SurfaceWidth)/2)/surface.SurfaceWidth,0,1),y=Math.Clamp(p.Y/surface.SurfaceHeight,0,1);
            double follow=1-Math.Exp(-dt/ .14);reflectionX+=(x-reflectionX)*follow;reflectionY+=(y-reflectionY)*follow;
            surface.Light(reflectionX,reflectionY);peekArtSlide.X=(reflectionX-.5)*2.5;peekArtSlide.Y=(reflectionY-.5)*1.5;
        }
        if(peeking && timelineDuration>0 && app.Media.Playing)peekProgress.Value=Math.Clamp((timelineElapsed+(now-timelineAt).TotalSeconds)/timelineDuration,0,1);
        else if(peeking && session!=null)peekProgress.Value=session.Progress(now);
        if(expanded && activePage=="media" && timelineDuration>0 && app.Media.Playing && !seekSlider.IsMouseCaptureWithin && !seekSlider.IsKeyboardFocusWithin && !seekDelay.IsEnabled)
        {
            updating=true;try{seekSlider.Value=Math.Clamp((timelineElapsed+(now-timelineAt).TotalSeconds)/timelineDuration,0,1);}finally{updating=false;}
        }
    }
    private void UpdateAudioFeedback()
    {
        RefreshFeedback();var audio=app.Audio;if(!audio.Available)return;
        if(audioSeen && app.Settings.ReactiveFeedback && app.Settings.VolumeFeedback && !expanded && !peeking && IsVisible && !TuckRequested && !userHidden)
        {
            string text=audio.Device!=lastDevice?"Output · "+audio.Device:audio.Muted!=lastMuted || Math.Abs(audio.Volume-lastVolume)>.005?audio.Muted?"Audio muted":$"Volume  {audio.Volume*100:0}%":"";
            if(text!="")ShowFeedback(false,audio.Volume,audio.Muted);
        }
        RefreshFeedback();
        audioSeen=true;lastVolume=audio.Volume;lastMuted=audio.Muted;lastDevice=audio.Device;
    }
    internal bool DiagnosticPointerPresent {get;set;}
    private bool PointerInside()
    {
        if(DiagnosticRun)return DiagnosticPointerPresent;
        if(!IsVisible || !Native.CursorPoint(out var point))return false;
        return VisiblePoint(PointFromScreen(new Point(point.X,point.Y)));
    }
}
