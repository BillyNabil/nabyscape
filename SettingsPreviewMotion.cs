using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private readonly DispatcherTimer previewTick=new(){Interval=TimeSpan.FromMilliseconds(33)};
    private readonly Stopwatch previewClock=Stopwatch.StartNew();
    private readonly List<Action<double>> previewFrames=new();
    private ComboBox? previewExamples;
    private bool previewClosed,previewReplaying;
    private string livePreviewKey="";
    private void InitializePreviewMotion(StackPanel column)
    {
        var replay=UI.Button("Replay transition",async()=>
        {
            if(previewReplaying)return;previewReplaying=true;string previous=previewMode;
            try{foreach(string mode in new[]{"Closed","Hover","Panel"}){if(previewClosed)return;previewMode=mode;RefreshPreview();await System.Threading.Tasks.Task.Delay(Math.Max(500,(int)app.Settings.AnimationMs+180));}}
            finally{previewReplaying=false;if(!previewClosed){previewMode=previous;RefreshPreview();}}
        });replay.Margin=new(0,12,0,0);replay.FontSize=11;column.Children.Add(replay);
        preview.SizeChanged+=(_,_)=>RefreshPreviewClip();
        previewTick.Tick+=(_,_)=>{if(!IsVisible || WindowState==WindowState.Minimized)return;previewTick.Interval=TimeSpan.FromMilliseconds(app.Settings.ReduceMotion?1000:33);double seconds=previewClock.Elapsed.TotalSeconds;foreach(var frame in previewFrames.ToArray())frame(seconds);};
        IsVisibleChanged+=(_,_)=>{if(IsVisible)previewTick.Start();else previewTick.Stop();};
        StateChanged+=(_,_)=>{if(IsVisible && WindowState!=WindowState.Minimized)previewTick.Start();else previewTick.Stop();};
        Loaded+=(_,_)=>previewTick.Start();Closed+=(_,_)=>{previewClosed=true;previewTick.Stop();previewFrames.Clear();};
    }
    private void RefreshPreviewClip()
    {
        double width=preview.ActualWidth>0?preview.ActualWidth:preview.Width,height=preview.ActualHeight>0?preview.ActualHeight:preview.Height;
        if(double.IsFinite(width) && double.IsFinite(height))preview.Clip=IslandGeometry.Create(Math.Max(1,width),Math.Max(1,height),app.Settings.Radius,app.Settings.Preset=="Mac");
    }
    private void AnimatePreviewSize(double width,double height)
    {
        void Resize(DependencyProperty property,double next)
        {
            double before=(double)preview.GetValue(property);preview.BeginAnimation(property,null);preview.SetValue(property,next);
            if(!app.Settings.ReduceMotion && app.Settings.AnimationMs>0 && SystemParameters.ClientAreaAnimation && double.IsFinite(before) && Math.Abs(before-next)>.5)
                preview.BeginAnimation(property,new DoubleAnimation(before,next,TimeSpan.FromMilliseconds(Math.Clamp(app.Settings.AnimationMs,80,600))){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
        }
        Resize(WidthProperty,width);Resize(HeightProperty,height);
    }
    private ActivityVisual PreviewWave(bool slender=false)
    {
        var wave=new ActivityVisual{Width=60,Height=slender?12:14,WaveOnly=true,Slender=slender,Margin=new(0,8,8,0)};
        previewFrames.Add(seconds=>{double peak=app.Media.Playing?app.Audio.MediaPeak(app.Media.Selected?.SourceAppUserModelId):0;bool animate=!app.Settings.ReduceMotion && app.Settings.LiveActivity && app.Media.Playing && !app.Audio.MediaMeterMuted;wave.Sample(animate?peak:0,.033,0,false,false,animate,seconds,true,app.Settings.WaveformIdleMotion);});return wave;
    }
    private void RefreshLivePreviewData()
    {
        string key=app.Media.Title+"|"+app.Media.Artist+"|"+app.Media.Playing;
        if(key!=livePreviewKey){livePreviewKey=key;RefreshPreview();}
    }
    private void ConfigurePreviewForSetting(string name)
    {
        if(name=="Reveal shelf when dragging files"){previewMode="Hover";previewContent="Drop zone";}
        else if(name=="Allow detachable previews"){previewMode="Hover";previewContent="Detached preview";}
        else if(name=="Control playback from album cover"){previewMode="Hover";previewContent="Cover gestures";}
        else if(name is "Show companion beside music" or "Companion beside music"){previewMode="Hover";previewContent="Two activities";}
        else if(name.Contains("media title",StringComparison.OrdinalIgnoreCase)){previewMode="Panel";previewContent="Music";}
        else if(name.Contains("heading",StringComparison.OrdinalIgnoreCase)){previewMode="Panel";previewContent="Timer";}
        else if(name.Contains("volume indicator",StringComparison.OrdinalIgnoreCase) || name=="Volume step"){previewMode="Closed";previewContent="Volume feedback";}
        else if(name.Contains("motion",StringComparison.OrdinalIgnoreCase) || name.Contains("activity",StringComparison.OrdinalIgnoreCase) || name=="Animation duration"){previewMode="Hover";previewContent="Music";}
        else if(name.Contains("hover",StringComparison.OrdinalIgnoreCase) || name=="Two-stage reveal" || name=="Swipe distance"){previewMode="Hover";}
        else if(name.Contains("Focus duration",StringComparison.OrdinalIgnoreCase) || name.Contains("Break duration",StringComparison.OrdinalIgnoreCase)){previewMode="Panel";previewContent="Focus";}
        else if(current=="System"){previewMode="Panel";previewContent="System";}
    }
}
