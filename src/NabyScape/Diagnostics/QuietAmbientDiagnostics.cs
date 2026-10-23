using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace NabyScape;

// Isolated synthetic playback. No real media transport, microphone or provider.
internal static class QuietAmbientDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var s=app.Settings;s.CoverAmbient=s.ClosedAmbient=s.LiveActivity=true;s.AmbientStrength=.8;
        s.AmbientScope="All widgets";s.AmbientColorSource="Custom color";s.AmbientCustomColor="#64A6DE";
        s.AmbientMotion="Still";s.SmartActivities=false;s.ReduceMotion=false;s.HideFullscreen=s.HideMaximized=s.AlwaysTucked=false;
        foreach(var widget in s.Widgets)widget.Enabled=true;
        app.Session.Focus.Reset();app.Session.Timer.Reset();app.Media.SelectAutomatic();app.Media.DiagnosticPlayback(false);app.ChangeSettings();
        var surface=(IslandSurface)typeof(OverlayWindow).GetField("surface",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(island)!;
        app.Notifications.Clear();island.Collapse();await Task.Delay(650);
        check("Idle closed dock has no ambience and stays black",!surface.AmbientVisible && !surface.GlassStyle && surface.Fill is SolidColorBrush fill && fill.Color==Colors.Black);
        app.Media.DiagnosticPlayback(true);app.ChangeSettings();island.Reveal(false);island.SelectPage("media",false);await Task.Delay(100);
        check("Active media retains configured ambience",surface.AmbientVisible);
        var detachedSurface=new IslandSurface{Width=220,Height=120,SurfaceWidth=220,SurfaceHeight=120,Fill=Brushes.Black,GlassStyle=false};
        var window=new Window{Content=detachedSurface,Width=220,Height=120,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false};
        using var presenter=new AmbientPresenter(app,detachedSurface,"notes");
        try
        {
            window.Show();await Task.Delay(100);check("Detached widget initially retains configured ambience",detachedSurface.AmbientVisible);
            app.Media.SelectNone();await app.Media.Refresh();await Task.Delay(100);
            check("None removes ambience from open media and detached widgets",!surface.AmbientVisible && !detachedSurface.AmbientVisible);
            app.Session.Focus.Start(300,DateTimeOffset.UtcNow);app.SaveSession();island.SelectPage("focus",false);await Task.Delay(100);
            check("None keeps ambience off with an active focus session",!surface.AmbientVisible && app.Session.Focus.Remaining(DateTimeOffset.UtcNow)>0);
            island.Collapse();await Task.Delay(650);check("None keeps closed focus dock dark",!surface.AmbientVisible);
            check("None releases every ambient audio consumer",app.AmbientAudio.ConsumerCount==0 && !app.AmbientAudio.Running);
            check("Quiet states preserve ambience preferences",s.CoverAmbient && s.ClosedAmbient && s.AmbientStrength==.8 && s.AmbientScope=="All widgets");
            app.Media.SelectAutomatic();app.Media.DiagnosticPlayback(true);app.ChangeSettings();island.Reveal(false);island.SelectPage("media",false);await Task.Delay(100);
            check("Selecting Auto restores ambience without toggling preferences",surface.AmbientVisible && detachedSurface.AmbientVisible);
        }
        finally{window.Close();app.Session.Focus.Reset();app.Session.Timer.Reset();app.Media.SelectAutomatic();}
    }
}
