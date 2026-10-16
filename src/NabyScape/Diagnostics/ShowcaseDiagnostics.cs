using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

// Opt-in documentation capture, reachable only through the isolated diagnostic profile.
internal static class ShowcaseDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var args=Environment.GetCommandLineArgs();int layout=Array.IndexOf(args,"--showcase-layout");
        if(layout>=0 && layout+1<args.Length)CustomizationProfile.Import(File.ReadAllText(args[layout+1]),app.Settings);
        // Import only visual preferences. Never read notes, clipboard, targets or session data.
        app.Settings.ClipboardHistory=false;app.Settings.HideFullscreen=app.Settings.HideMaximized=false;
        app.ChangeSettings();island.DiagnosticPointerPresent=true;
        app.Media.DiagnosticPlayback(true);
        typeof(MediaService).GetProperty("Title")!.SetValue(app.Media,"Small Hours");
        typeof(MediaService).GetProperty("Artist")!.SetValue(app.Media,"NabyScape · demo session");
        var cover=new DrawingVisual();using(var dc=cover.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(31,41,64),Color.FromRgb(180,197,225),45),null,new Rect(0,0,192,192));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(235,230,209)),null,new Point(125,61),29,29);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(28,39,58)),null,Geometry.Parse("M0,160 L75,70 L146,160 L192,115 L192,192 L0,192 Z"));
        }
        var art=new RenderTargetBitmap(192,192,96,96,PixelFormats.Pbgra32);art.Render(cover);art.Freeze();
        typeof(MediaService).GetProperty("Artwork")!.SetValue(app.Media,art);
        var clock=Stopwatch.StartNew();app.Audio.DiagnosticPeak=()=>{double t=clock.Elapsed.TotalSeconds;return (float)Math.Clamp(.21+.12*Math.Sin(t*7)+.08*Math.Sin(t*17)+.05*Math.Sin(t*29),0,1);};
        app.Productivity.Note="A little space to think.\n\nSketch the idea. Keep the useful bits.\nCome back when you're ready.";
        app.Productivity.AddTask("Sketch the next idea");app.Productivity.AddTask("Make room for a quiet afternoon");app.Productivity.AddTask("Share something worth keeping");app.Productivity.Save();
        string samples=Path.Combine(directory,"sample-files");Directory.CreateDirectory(samples);
        File.WriteAllText(Path.Combine(samples,"Project notes.txt"),"NabyScape showcase sample.");
        Write(art,Path.Combine(samples,"Cover study.png"));app.Productivity.AddFiles(Directory.GetFiles(samples));
        app.PublishUpdate(RuntimeChange.All);
        var chapters=new System.Collections.Generic.List<object>();int frame=0;
        string frames=Path.Combine(directory,"frames");Directory.CreateDirectory(frames);
        void Snapshot(string name,FrameworkElement? element=null)=>Write(Capture(element??island),Path.Combine(directory,name+".png"));
        async Task Scene(string name,string caption,double seconds,Action action)
        {
            chapters.Add(new{name,caption,start=frame/30d,seconds});action();
            var watch=Stopwatch.StartNew();int count=(int)(seconds*30);
            for(int i=0;i<count;i++)
            {
                int delay=(int)(i*1000d/30-watch.Elapsed.TotalMilliseconds);if(delay>0)await Task.Delay(delay);
                var image=Capture(island);Write(image,Path.Combine(frames,$"{frame++:D5}.png"));
                if(i==count-1)Write(image,Path.Combine(directory,name+".png"));
                await Task.Yield();
            }
        }
        SettingsWindow? settings=null;
        try
        {
            await Scene("closed","Your music. A little closer.",2.5,()=>island.Collapse());
            await Scene("hover-music","Hover for the essentials.",3.5,()=>{island.SelectPeek("media");island.PreviewActivity();});
            await Scene("media","Click for the full picture.",3.5,()=>{island.Reveal(false);island.SelectPage("media",false);});
            await Scene("system","A quick look at your PC.",3,()=>{island.SelectPage("status",false);island.SelectMonitorTab("overview");});
            await Scene("notes","Keep a thought within reach.",3,()=>island.ShowTools("notes",false));
            await Scene("tasks","Small tasks. Less switching.",2.5,()=>island.ShowTools("tasks",false));
            await Scene("shelf","Stage files. Leave originals in place.",3,()=>island.ShowTools("files",false));
            await Scene("calendar","A calendar that stays on your device.",2.5,()=>island.ShowTools("calendar",false));
            island.ShowTools("home",false);await Task.Delay(650);Snapshot("tools");
            island.SelectPage("focus",false);await Task.Delay(650);Snapshot("focus");
            app.Previews.OpenAt("notes",new Point(120,160),false);await Task.Delay(400);
            var mini=Application.Current.Windows.OfType<DetachedPreviewWindow>().Single();Snapshot("detached-notes",mini);mini.Close();
            await Scene("return","And back to your desktop.",2.5,()=>island.Collapse());
            settings=new SettingsWindow(app){Width=1100,Height=780};settings.Show();
            foreach(string category in new[]{"Appearance","Layout","Content","Controls","Audio","System","Launch","Application"})
            {
                typeof(SettingsWindow).GetMethod("Navigate",flags)!.Invoke(settings,new object[]{category});
                await Task.Delay(450);Snapshot("settings-"+category.ToLowerInvariant(),settings);
            }
            File.WriteAllText(Path.Combine(directory,"chapters.json"),JsonSerializer.Serialize(new{fps=30,frames=frame,chapters},new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Actual WPF views, rendered at 30 capture frames per second. Layout uses the supplied visual profile. Sample text, artwork, tasks and waveform signal; no recorded music or personal content. System values are this machine's live readings. Diagnostic safety disables native clipboard access and automatic tucking.\n");
        }
        finally{settings?.Close();app.Audio.DiagnosticPeak=null;}
    }
    private static BitmapSource Capture(FrameworkElement element)
    {
        element.UpdateLayout();int width=(int)Math.Ceiling(element.ActualWidth),height=(int)Math.Ceiling(element.ActualHeight);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        return bitmap;
    }
    private static void Write(BitmapSource bitmap,string path)
    {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
}
