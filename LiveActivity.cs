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
    private double envelope,progress,pulse=1;
    private bool countdown,focus;
    private static readonly Brush focusColor=UI.Brush("#30D158"),timerColor=UI.Brush("#FF9F0A");
    private static readonly Pen trackPen=new(UI.Brush("#353537"),1.8),focusPen=new(focusColor,1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},timerPen=new(timerColor,1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
    internal bool WaveOnly {get;init;}
    internal bool Slender {get;init;}
    internal double Envelope=>envelope;
    internal ActivityVisual(){Width=18;Height=18;VerticalAlignment=VerticalAlignment.Center;IsHitTestVisible=false;}
    internal void Sample(double peak,double dt,double fraction,bool session,bool focused,bool animate,double seconds,bool advanceHistory=true,bool decorative=false)
    {
        double target=animate?Math.Sqrt(Math.Clamp(peak,0,1)):0;
        if(animate && decorative && target<.12)target=Math.Max(target,.055+.035*Math.Sin(seconds*3.1)+.02*Math.Sin(seconds*5.7));
        double before=envelope;envelope=animate?target+(envelope-target)*Math.Exp(-dt/(target>envelope?.035:.18)):0;
        if(!animate)Array.Clear(history);
        if(envelope<.001)envelope=0;
        bool changed=Math.Abs(before-envelope)>.0003 || Math.Abs(progress-fraction)>.00001 || session!=countdown || focused!=focus;
        if(advanceHistory){for(int i=history.Length-1;i>0;i--){if(Math.Abs(history[i]-history[i-1])>.001)changed=true;history[i]=history[i-1];}history[0]=envelope;}
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
            double shape=.4+.6*Math.Pow(Math.Sin((i+1)*Math.PI/(count+1)),.65);
            int index=WaveOnly?Math.Min(24,(int)(Math.Abs(i-(count-1)/2d)*48/Math.Max(1,count-1))):i*3;
            double v=Math.Clamp(history[index]+(envelope-history[0])*Math.Exp(-index*.15),0,1)*shape;double baseline=Slender?1:1.8;double height=baseline+(h-2)*v;
            dc.DrawRoundedRectangle(Brushes.White,null,new Rect(start+i*(bw+gap),(h-height)/2,bw,height),bw/2,bw/2);
        }
    }
}

public sealed partial class OverlayWindow
{
    private readonly ActivityVisual activity=new(),musicWave=new(){Width=96,Height=16,WaveOnly=true,HorizontalAlignment=HorizontalAlignment.Center};
    private readonly DispatcherTimer liveTick=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(250)};
    private DateTimeOffset lastLive,quietSince,feedbackUntil,timelineAt;
    private DateTimeOffset audioSampleAt;
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
    private bool FeedbackVisible=>app.Settings.ReactiveFeedback && (feedbackBrightness?app.Settings.BrightnessFeedback:app.Settings.VolumeFeedback) && !expanded && !peeking && IsVisible && !environmentHidden && !userHidden && DateTimeOffset.UtcNow<feedbackUntil;
    private void InitializeLiveActivity()
    {
        liveTick.Tick+=(_,_)=>{sampledPeak=WidgetOn("media") && app.Media.Playing?app.Audio.MediaPeak(app.Media.Selected?.SourceAppUserModelId):0;audioSampleAt=DateTimeOffset.UtcNow;if(sampledPeak>.002 || expanded || peeking || app.Media.Playing || ActiveCountdown()?.Running==true)WakeLiveRendering();};
        InitializeFeedback();
        IsVisibleChanged+=(_,_)=>{ConfigureLiveActivity();if(!IsVisible)ClearFeedback();};
    }
    private void ConfigureLiveActivity()
    {
        StopLiveRendering();lastLive=DateTimeOffset.UtcNow;quietSince=lastLive;
        if(IsVisible && !IsTucked && app.Settings.LiveActivity && Motion.Enabled){liveTick.Interval=TimeSpan.FromMilliseconds(250);liveTick.Start();}
        else{liveTick.Stop();activity.Sample(0,1,ActiveCountdown()?.Progress(lastLive)??0,ActiveCountdown()!=null,app.Session.Focus.Remaining(lastLive)>0,false,0);closedTitleWave.Sample(0,1,0,false,false,false,0);musicWave.Sample(0,1,0,false,false,false,0);peekWave.Sample(0,1,0,false,false,false,0);}
    }
    private void WakeLiveRendering()
    {
        if(liveRendering || !IsVisible || IsTucked || !app.Settings.LiveActivity || !Motion.Enabled)return;
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
        return WidgetOn("focus") && app.Session.Focus.Remaining(now)>0?app.Session.Focus:WidgetOn("timer") && app.Session.Timer.Remaining(now)>0?app.Session.Timer:null;
    }
    private void LiveFrame()
    {
        var now=DateTimeOffset.UtcNow;double dt=Math.Clamp((now-lastLive).TotalSeconds,.001,.5);lastLive=now;
        bool sample=(now-audioSampleAt).TotalSeconds>=1/30d;
        if(sample){sampledPeak=WidgetOn("media") && app.Media.Playing?app.Audio.MediaPeak(app.Media.Selected?.SourceAppUserModelId):0;audioSampleAt=now;}
        double peak=app.Media.Playing && !app.Audio.MediaMeterMuted?sampledPeak:0;
        var session=ActiveCountdown();bool running=session?.Running==true;
        if(peak>.002 || running || expanded || peeking || app.Media.Playing || motion.Active)quietSince=now;
        if((now-quietSince).TotalSeconds>1.2){StopLiveRendering();return;}
        bool decorative=app.Settings.WaveformIdleMotion && app.Media.Playing && !app.Audio.MediaMeterMuted;
        double seconds=now.ToUnixTimeMilliseconds()/1000d;
        activity.Sample(peak,dt,session?.Progress(now)??0,session!=null,session==app.Session.Focus,running || session==null,seconds,sample,decorative);
        if(closedTitleWave.IsVisible)closedTitleWave.Sample(peak,dt,0,false,false,app.Media.Playing && !app.Audio.MediaMeterMuted,seconds,sample,decorative);
        if(musicWave.IsVisible)musicWave.Sample(peak,dt,0,false,false,app.Media.Playing && !app.Audio.MediaMeterMuted,seconds,sample,decorative);
        if(peekWave.IsVisible)peekWave.Sample(peak,dt,0,false,false,app.Media.Playing && !app.Audio.MediaMeterMuted,seconds,sample,decorative);
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
        if(audioSeen && app.Settings.ReactiveFeedback && app.Settings.VolumeFeedback && !expanded && !peeking && IsVisible && !environmentHidden && !userHidden)
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
        if(!IsVisible || !Native.GetCursorPos(out var point))return false;
        return VisiblePoint(PointFromScreen(new Point(point.X,point.Y)));
    }
}
