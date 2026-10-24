using System;
using System.Reflection;
using System.Threading.Tasks;

namespace NabyScape;

internal static class ClosedLyricsFallbackDiagnostics
{
    internal static async Task Run(App app,OverlayWindow dock,Action<string,bool> check)
    {
        var s=app.Settings;
        s.ClosedMediaContent="Lyrics";s.HideFullscreen=s.HideMaximized=s.AlwaysTucked=false;
        s.SmartActivities=false;s.ReduceMotion=false;s.LiveActivity=true;s.LyricsOffset=0;
        s.ClosedMediaIcon="None";s.CoverAmbient=false;s.ClosedAmbient=false;
        app.Session.Timer.Reset();app.Session.Focus.Reset();
        foreach(var widget in s.Widgets)widget.Enabled=widget.Id is "media" or "clock";
        app.Audio.DiagnosticPeak=()=>.35f;app.Media.DiagnosticPlayback(true);
        // Import before enabling the dock consumer so every transition stays offline.
        void Import(string lrc)=>app.Lyrics.Import(app.Media.Title,app.Media.Artist,app.Media.Timeline().duration,lrc);
        void Paint()=>typeof(OverlayWindow).GetMethod("PaintClosedLyrics",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dock,null);
        Import("[00:00]Vocal");app.ChangeSettings();dock.Collapse();await Task.Delay(350);
        check("Lyrics fallback: vocal line hides waveform ("+dock.HeaderLabel.Text+")",!dock.ClosedTitleWaveVisible && dock.HeaderLabel.Text=="Vocal");
        Import("[00:00]\n[04:00]Fixture later line");await Task.Delay(120);
        check("Lyrics fallback: blank timed section shows waveform",dock.ClosedTitleWaveVisible);
        check("Lyrics fallback: waveform responds to existing audio meter",dock.ClosedTitleWaveLevel>0 && dock.ClosedTitleWaveWidth>0);
        Import("[00:00][Instrumental]");await Task.Delay(100);
        check("Lyrics fallback: instrumental marker shows waveform",dock.ClosedTitleWaveVisible);
        Import("[00:00]Back");await Task.Delay(100);
        check("Lyrics fallback: vocals restore text without changing mode ("+dock.HeaderLabel.Text+")",!dock.ClosedTitleWaveVisible && dock.HeaderLabel.Text=="Back" && s.ClosedMediaContent=="Lyrics");
        Import("[04:01]Fixture future line");await Task.Delay(100);
        check("Lyrics fallback: intro before first timestamp shows waveform",dock.ClosedTitleWaveVisible);
        typeof(LyricsService).GetMethod("Set",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app.Lyrics,new object[]{new LyricResult(Array.Empty<LyricLine>(),"No synced lyrics for this track.")});
        await Task.Delay(100);check("Lyrics fallback: no synced data shows waveform",dock.ClosedTitleWaveVisible);
        s.ReduceMotion=true;app.ChangeSettings();await Task.Delay(100);
        check("Lyrics fallback: reduce motion keeps the waveform static",dock.ClosedTitleWaveVisible && dock.ClosedTitleWaveLevel==0);
        s.ReduceMotion=false;s.LiveActivity=false;app.ChangeSettings();await Task.Delay(100);
        check("Lyrics fallback: disabled live animation stays static",dock.ClosedTitleWaveVisible && dock.ClosedTitleWaveLevel==0);
        app.Session.Timer.Start(600,DateTimeOffset.UtcNow);s.Widgets.Find(w=>w.Id=="timer")!.Enabled=true;app.ChangeSettings();
        check("Lyrics fallback: timer keeps priority",!dock.ClosedTitleWaveVisible && dock.HeaderLabel.Text.StartsWith("Timer",StringComparison.Ordinal));
        app.Session.Timer.Reset();s.Widgets.Find(w=>w.Id=="timer")!.Enabled=false;app.ChangeSettings();
        app.Media.DiagnosticPlayback(false);await Task.Delay(100);
        check("Lyrics fallback: paused playback has no media waveform",!dock.ClosedTitleWaveVisible);
        app.Media.DiagnosticPlayback(true);Import("[00:00]Fixture vocal returns");app.ChangeSettings();dock.Reveal(false);await Task.Delay(100);
        Paint();check("Lyrics fallback: expanded panel has no closed waveform",!dock.ClosedTitleWaveVisible);
        dock.Collapse();await Task.Delay(100);app.Audio.DiagnosticPeak=null;
    }
}
