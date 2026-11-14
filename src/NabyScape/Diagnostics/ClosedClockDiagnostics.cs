using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

// Issue #3: renders the real closed dock for each clock position, idle and with music, at 400 and 600 px.
internal static class ClosedClockDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,flags)!.GetValue(island)!;
        void Capture(string name)
        {
            island.UpdateLayout();var full=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth),(int)Math.Ceiling(island.ActualHeight),96,96,PixelFormats.Pbgra32);full.Render(island);
            int width=(int)Math.Ceiling(island.PaintedWidth),height=(int)Math.Ceiling(island.PaintedHeight);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(new CroppedBitmap(full,new Int32Rect(Math.Max(0,(full.PixelWidth-width)/2),0,Math.Min(width,full.PixelWidth),Math.Min(height,full.PixelHeight)))));
            using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        app.Media.Dispose();app.Media.DiagnosticFixture=true;app.Media.DiagnosticPlayback(true);app.Audio.DiagnosticPeak=()=>0;
        var s=app.Settings;s.HideMaximized=s.HideFullscreen=s.AlwaysTucked=false;s.SmartActivities=false;s.Preset="Mac";s.Top=0;s.Height=36;s.ReduceMotion=true;s.LiveActivity=true;s.Clock24Hour=true;s.ShowDate=true;s.ClosedMediaIcon="Waveform";
        var clock=Field<TextBlock>("compactClock");var date=Field<TextBlock>("idleDate");var wave=Field<FrameworkElement>("closedTitleWave");var activity=Field<FrameworkElement>("activity");
        Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(island).TransformBounds(new Rect(element.RenderSize));
        foreach(double width in new[]{400d,600d})
        foreach(string position in Settings.ClosedClockPositions)
        foreach(bool music in new[]{false,true})
        {
            s.Width=width;s.ClosedClockPosition=position;s.ClosedMediaContent=music?"Waveform":"None";app.ChangeSettings();island.Collapse();
            for(int i=0;i<3;i++){await Task.Delay(120);app.PublishUpdate(RuntimeChange.All);island.UpdateLayout();}
            string name=$"{position}/{(music?"music":"idle")}/{width}";var c=Bounds(clock);double dock=island.ActualWidth/2;
            check("Closed dock keeps its configured width "+name,Math.Abs(island.PaintedWidth-width)<1);
            check("Closed clock is visible "+name,clock.IsVisible && c.Width>10);
            var slot=ClosedClockLayout.Slot(position,!music);
            if(slot==ClosedClockSlot.Center)check($"Centered clock sits on the dock center {name} (clock {c.Left+c.Width/2:0.0}, dock {dock:0.0})",Math.Abs(c.Left+c.Width/2-dock)<1);
            else if(slot==ClosedClockSlot.Leading)check("Left clock sits left of center "+name,c.Right<dock-20);
            else check("Right clock sits right of center "+name,c.Left>dock+20);
            if(!music && date.IsVisible){var d=Bounds(date);check("Idle date never overlaps the clock "+name,d.Right<=c.Left+.5 || d.Left>=c.Right-.5);}
            if(music)
            {
                if(wave.IsVisible){var w=Bounds(wave);check("Waveform keeps clear of the clock "+name,w.Right<=c.Left+.5 || w.Left>=c.Right-.5);}
                if(activity.IsVisible && activity.ActualWidth>0){var a=Bounds(activity);check("Activity icon keeps clear of the clock "+name,a.Right<=c.Left+.5 || a.Left>=c.Right-.5);}
            }
            Capture($"closed-clock-{position.ToLowerInvariant()}-{(music?"music":"idle")}-{width:0}");
        }
        check("Closed clock checks never connect voice or share a screen",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);
        File.WriteAllText(Path.Combine(directory,"closed-clock-notes.txt"),"Isolated profile, synthetic media playback, WPF renderings of the closed dock for Automatic/Left/Center/Right at 400 and 600 px. No network, microphone or screen capture.");
    }
}
