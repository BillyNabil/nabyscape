using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class ClosedWaveformDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,flags)!.GetValue(island)!;
        BitmapSource Snapshot(double dpi=1)
        {
            island.UpdateLayout();var full=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth*dpi),(int)Math.Ceiling(island.ActualHeight*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);full.Render(island);
            int width=(int)Math.Ceiling(island.PaintedWidth*dpi),height=(int)Math.Ceiling(island.PaintedHeight*dpi);
            return new CroppedBitmap(full,new Int32Rect(Math.Max(0,(full.PixelWidth-width)/2),0,width,height));
        }
        void Capture(string name,double dpi=1)
        {var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(Snapshot(dpi)));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);}
        app.Media.Dispose();app.Media.DiagnosticFixture=true;app.Media.DiagnosticPlayback(true);app.Audio.DiagnosticPeak=()=>0;
        var art=new DrawingGroup();using(var dc=art.Open()){dc.DrawRectangle(UI.Brush("#94B7CF"),null,new Rect(0,0,20,20));dc.DrawEllipse(UI.Brush("#E5D9B8"),null,new Point(10,10),7,7);}art.Freeze();typeof(MediaService).GetProperty("Artwork")!.SetValue(app.Media,new DrawingImage(art));
        var s=app.Settings;s.ClosedMediaIcon="Artwork";s.ClosedMediaContent="Waveform";s.HideMaximized=s.HideFullscreen=s.AlwaysTucked=false;s.SmartActivities=false;s.Preset="Mac";s.Top=0;s.Width=327;s.Height=36;s.ReduceMotion=true;s.LiveActivity=true;s.Clock24Hour=true;app.Assistant.Options.IslandCharacter=true;
        app.ChangeSettings();island.Collapse();await Task.Delay(160);island.UpdateLayout();
        var wave=Field<ActivityVisual>("closedTitleWave");var clock=Field<TextBlock>("compactClock");
        var bands=new float[25];for(int i=0;i<25;i++)bands[i]=(float)(.08+.8*Math.Exp(-Math.Pow((i-6)/4d,2))+.55*Math.Exp(-Math.Pow((i-17)/2d,2)));
        void Feed(){for(int i=0;i<15;i++)wave.Sample(.4,1/60d,0,false,false,true,i/60d,true,false,bands,s.WaveformSensitivity);}
        Feed();check("Closed waveform uses 104 by 22 DIP at the user's 327 by 36 dock size",island.ClosedTitleWaveVisible && island.ClosedCoverVisible && Math.Abs(wave.ActualWidth-104)<1 && wave.ActualHeight==22);
        var bounds=wave.TransformToAncestor(island).TransformBounds(new Rect(wave.RenderSize));var clockBounds=clock.TransformToAncestor(island).TransformBounds(new Rect(clock.RenderSize));
        check("Enlarged closed waveform leaves the clock and companion their own space",bounds.Right<clockBounds.Left && wave.WaveBarHeight(6,22)>17 && wave.WaveBarHeight(0,22)<10);
        Capture("closed-waveform-327");Capture("closed-waveform-327-150",1.5);Capture("closed-waveform-327-200",2);
        var gif=new GifBitmapEncoder();
        for(int frame=0;frame<36;frame++)
        {
            for(int i=0;i<25;i++)bands[i]=(float)Math.Clamp(.03+.8*Math.Exp(-Math.Pow((i-(8+5*Math.Sin(frame*.27)))/3d,2))+.42*Math.Exp(-Math.Pow((i-19)/2d,2))*(.5+.5*Math.Sin(frame*.5)),0,1);
            wave.Sample(.4,1/30d,0,false,false,true,frame/30d,true,false,bands,s.WaveformSensitivity);await Task.Delay(16);
            var metadata=new BitmapMetadata("gif");metadata.SetQuery("/grctlext/Delay",(ushort)4);gif.Frames.Add(BitmapFrame.Create(Snapshot(2),null,metadata,null));
        }
        using(var file=File.Create(Path.Combine(directory,"closed-waveform.gif")))gif.Save(file);
        foreach(var size in new[]{(Width:180d,Height:28d),(Width:400d,Height:72d)})
        {
            s.Width=size.Width;s.Height=size.Height;app.ChangeSettings();await Task.Delay(80);island.UpdateLayout();Feed();
            check("Closed waveform fits dock bounds at "+size.Width+" by "+size.Height,wave.ActualWidth>5 && wave.ActualWidth<=104 && wave.ActualHeight<=s.Height && wave.WaveBarHeight(6,wave.ActualHeight)<=wave.ActualHeight);
            Capture("closed-waveform-"+size.Width);
        }
        wave.Sample(0,.1,0,false,false,false,0);check("Paused compact waveform settles to a quiet baseline",Enumerable.Range(0,25).All(i=>wave.WaveBarHeight(i,22)==1.4));
        check("Compact waveform checks never connect voice, share a screen or capture audio",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing && app.Audio.SpectrumCaptureCount==0);
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Synthetic frequency-band fixtures, not a recording of music. Full dock WPF renderings at 100/150/200% raster scale, not physical monitor movement or GPU-presented-frame measurement. No keys, microphone, screen capture, media transport command or audio recording used.");
    }
}
