using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace NabyScape;

internal static class AmbientPolicy
{
    internal static bool Includes(Settings s,string widget)=>s.CoverAmbient && s.AmbientStrength>0 && (widget=="closed"?s.ClosedAmbient:s.AmbientScope switch
    {
        "Music only"=>widget is "media" or "volume" or "closed",
        "Selected widgets"=>s.AmbientWidgets.Contains(widget),
        _=>true
    });
    internal static CoverPalette ColorsFor(Settings s,CoverPalette cover)
    {
        if(s.AmbientColorSource=="Album cover" && cover.Primary.A>0)return s.AmbientPalette=="Single color"?new(cover.Primary,cover.Primary):cover;
        if(s.AmbientColorSource=="Custom blend")return new(UI.Brush(s.AmbientCustomColor).Color,UI.Brush(s.AmbientSecondaryColor).Color);
        Color color=UI.Brush(s.AmbientColorSource=="Custom color"?s.AmbientCustomColor:s.Accent).Color;
        return new(color,color);
    }
}

// Attack/release filtering of the measured media-session envelope, never a simulated beat.
internal sealed class AmbientEnvelope
{
    internal double Level {get;private set;}
    internal double Sample(double peak,double seconds,string response,double sensitivity)
    {
        peak=double.IsFinite(peak)?Math.Clamp(peak,0,1):0;
        sensitivity=double.IsFinite(sensitivity)?Math.Clamp(sensitivity,.5,3):1.4;
        seconds=double.IsFinite(seconds)?Math.Clamp(seconds,.001,.25):1d/30;
        double target=peak<.002?0:Math.Pow(Math.Clamp(peak*sensitivity,0,1),response=="Punchy"?.8:.6);
        double attack=response=="Soft"?.1:response=="Punchy"?.018:.035;
        double release=response=="Soft"?.45:response=="Punchy"?.13:.25;
        Level=target+(Level-target)*Math.Exp(-seconds/(target>Level?attack:release));
        if(Level<.001)Level=0;
        return .32+.68*Level;
    }
    internal void Reset()=>Level=0;
}

// One demand-driven timer for all ambient consumers. AudioService's shared 30 Hz
// sample gate also coalesces reads with waveform and Settings preview consumers.
internal sealed class AmbientAudioDriver : IDisposable
{
    private readonly App app;
    private readonly DispatcherTimer timer=new(DispatcherPriority.Render){Interval=TimeSpan.FromSeconds(1d/30)};
    private readonly HashSet<IslandSurface> surfaces=new();
    private readonly AmbientEnvelope envelope=new();
    private long lastStamp;
    private ImageSource? artwork;
    private CoverPalette palette;
    private bool disposed;
    internal bool Running=>timer.IsEnabled;
    internal int ConsumerCount=>surfaces.Count(Active);
    internal int RegisteredCount=>surfaces.Count;
    private static readonly DependencyProperty DemandProperty=DependencyProperty.RegisterAttached("AmbientDemand",typeof(bool),typeof(AmbientAudioDriver),new PropertyMetadata(false));
    private static readonly DependencyProperty AttachedProperty=DependencyProperty.RegisterAttached("AmbientAttached",typeof(bool),typeof(AmbientAudioDriver),new PropertyMetadata(false));
    internal AmbientAudioDriver(App app){this.app=app;timer.Tick+=Tick;app.Updated+=Changed;}
    internal CoverPalette CoverColors
    {
        get
        {
            if(!ReferenceEquals(artwork,app.Media.Artwork))
            {
                artwork=app.Media.Artwork;
                try{palette=CoverPalette.Extract(artwork);}catch(Exception ex){app.Store.Log(ex);palette=default;}
            }
            return palette;
        }
    }
    internal void Set(IslandSurface surface,bool wanted)
    {
        if(disposed)return;
        surface.SetValue(DemandProperty,wanted);
        if(!(bool)surface.GetValue(AttachedProperty))
        {
            surface.SetValue(AttachedProperty,true);
            surface.Loaded+=Loaded;surface.Unloaded+=Unloaded;surface.IsVisibleChanged+=VisibilityChanged;
        }
        if(surface.IsLoaded && wanted)surfaces.Add(surface);
        else{surfaces.Remove(surface);surface.ResetAmbientAudio();}
        Reconcile();
    }
    private bool Active(IslandSurface surface)=>(bool)surface.GetValue(DemandProperty) && surface.IsLoaded && surface.IsVisible && surface.AmbientVisible && Window.GetWindow(surface)?.WindowState!=WindowState.Minimized;
    private void Loaded(object sender,RoutedEventArgs e){var surface=(IslandSurface)sender;if((bool)surface.GetValue(DemandProperty))surfaces.Add(surface);Reconcile();}
    private void Unloaded(object sender,RoutedEventArgs e){surfaces.Remove((IslandSurface)sender);((IslandSurface)sender).ResetAmbientAudio();Reconcile();}
    private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)=>Reconcile();
    internal void Remove(IslandSurface surface)
    {
        surface.SetValue(DemandProperty,false);surface.SetValue(AttachedProperty,false);
        surface.Loaded-=Loaded;surface.Unloaded-=Unloaded;surface.IsVisibleChanged-=VisibilityChanged;
        surfaces.Remove(surface);surface.ResetAmbientAudio();Reconcile();
    }
    private void Changed(){if((app.CurrentUpdate&(RuntimeChange.Settings|RuntimeChange.Media))!=0)Reconcile();}
    internal void Reconcile()
    {
        bool enabled=!disposed && app.Settings.CoverAmbient && app.Settings.LiveActivity && Motion.Enabled && app.Settings.AmbientAudioResponse!="Off" && app.Media.Playing && surfaces.Any(Active);
        if(enabled){if(!timer.IsEnabled){lastStamp=Stopwatch.GetTimestamp();envelope.Reset();timer.Start();}}
        else{timer.Stop();envelope.Reset();}
        foreach(var surface in surfaces)if(!enabled || !Active(surface))surface.ResetAmbientAudio();
    }
    private void Tick(object? sender,EventArgs e)
    {
        Reconcile();if(!timer.IsEnabled)return;
        long now=Stopwatch.GetTimestamp();double dt=(now-lastStamp)/(double)Stopwatch.Frequency;lastStamp=now;
        app.Audio.MediaPeak(app.Media.Selected?.SourceAppUserModelId,includeSpectrum:false);
        var sample=app.Audio.MediaLevel;
        double gain=envelope.Sample(sample.Muted?0:sample.Peak,dt,app.Settings.AmbientAudioResponse,app.Settings.AmbientSensitivity);
        foreach(var surface in surfaces)if(Active(surface))surface.SetAmbientAudio(gain);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;timer.Stop();timer.Tick-=Tick;app.Updated-=Changed;
        foreach(var surface in surfaces.ToArray()){surface.Loaded-=Loaded;surface.Unloaded-=Unloaded;surface.IsVisibleChanged-=VisibilityChanged;surface.ResetAmbientAudio();}
        surfaces.Clear();
    }
}

// Mini windows use the same palette, settings and sampler as the island.
internal sealed class AmbientPresenter : IDisposable
{
    private readonly App app;
    private readonly IslandSurface surface;
    private readonly string widget;
    private bool subscribed;
    internal AmbientPresenter(App app,IslandSurface surface,string widget)
    {
        this.app=app;this.surface=surface;this.widget=widget;
        surface.Loaded+=Loaded;surface.Unloaded+=Unloaded;surface.IsVisibleChanged+=VisibilityChanged;
        if(surface.IsLoaded)Loaded(surface,new());
        Refresh();
    }
    private void Loaded(object sender,RoutedEventArgs e){if(!subscribed){app.Updated+=Changed;subscribed=true;}Refresh();}
    private void Unloaded(object sender,RoutedEventArgs e){if(subscribed){app.Updated-=Changed;subscribed=false;}}
    private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)=>Refresh();
    private void Changed(){if((app.CurrentUpdate&(RuntimeChange.Settings|RuntimeChange.Media))!=0)Refresh();}
    internal void Refresh()
    {
        var s=app.Settings;bool allowed=AmbientPolicy.Includes(s,widget);
        bool moving=app.Media.Playing && s.LiveActivity && surface.IsVisible && Window.GetWindow(surface)?.WindowState!=WindowState.Minimized;
        surface.SetAmbient(AmbientPolicy.ColorsFor(s,app.AmbientAudio.CoverColors),allowed?s.AmbientStrength:0,s.AmbientSpread,s.AmbientMotion,moving,AmbientLook.From(s));
        app.AmbientAudio.Set(surface,allowed && moving);
    }
    public void Dispose(){Unloaded(surface,new());surface.Loaded-=Loaded;surface.Unloaded-=Unloaded;surface.IsVisibleChanged-=VisibilityChanged;app.AmbientAudio.Remove(surface);}
}
