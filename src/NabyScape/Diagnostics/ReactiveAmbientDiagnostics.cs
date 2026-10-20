using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NabyScape;
internal static class ReactiveAmbientDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var surface=(IslandSurface)typeof(OverlayWindow).GetField("surface",flags)!.GetValue(island)!;
        var s=app.Settings;
        s.ReduceMotion=false;s.CoverAmbient=true;s.AmbientScope="All widgets";s.AmbientMotion="Still";s.AmbientColorSource="Custom color";s.AmbientCustomColor="#64A6DE";s.AmbientAudioResponse="Responsive";s.AmbientStrength=.7;s.LiveActivity=true;s.CompactLayout=true;s.PanelWidth=526;s.PanelMaxHeight=550;s.SmartActivities=false;s.HideFullscreen=s.HideMaximized=s.AlwaysTucked=false;s.DetachablePreviews=true;
        foreach(var w in s.Widgets)w.Enabled=true;
        float peak=.02f;app.Audio.DiagnosticPeak=()=>peak;app.Media.DiagnosticPlayback(true);app.ChangeSettings();
        void Capture(FrameworkElement element,string name,double scale=1,bool crop=false)
        {
            element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*scale),(int)Math.Ceiling(element.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(element);
            BitmapSource result=bitmap;
            if(crop){int width=Math.Min(bitmap.PixelWidth,(int)Math.Ceiling(island.PaintedWidth*scale)),height=Math.Min(bitmap.PixelHeight,(int)Math.Ceiling(island.PaintedHeight*scale));result=new CroppedBitmap(bitmap,new Int32Rect((bitmap.PixelWidth-width)/2,0,width,height));}
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(result));using var stream=File.Create(Path.Combine(directory,name+".png"));encoder.Save(stream);
        }
        island.Reveal(false);
        foreach(string page in new[]{"media","focus","status","notes","tasks","clips","calendar","files","access"})
        {
            island.SelectPage(page,false);await Task.Delay(50);check("All-widgets ambient appears on "+page,surface.AmbientVisible);
        }
        island.SelectPage("notes",false);await Task.Delay(450);
        double quiet=surface.AmbientAudioGain;peak=.85f;await Task.Delay(450);double loud=surface.AmbientAudioGain;
        check("Notes ambient reacts to actual high and low audio levels",loud>quiet+.2 && loud<=1);
        Capture(island,"notes-audio-high",1,true);Capture(island,"notes-audio-high-150",1.5,true);Capture(island,"notes-audio-high-200",2,true);
        peak=0;await Task.Delay(1700);check("Silence returns ambient to its quiet floor",surface.AmbientAudioGain<.34);Capture(island,"notes-audio-quiet",1,true);
        check("Still movement retains audio response without a decorative loop",app.AmbientAudio.Running && !surface.AmbientAnimating);
        int builds=island.BodyBuilds;var entry=Diagnostics.Descendants(island).OfType<TextBox>().FirstOrDefault(t=>t.IsVisible && t.AcceptsReturn);
        if(entry!=null){entry.Text="A quiet place to collect ideas.";entry.Select(2,5);}
        s.AmbientSpread=.9;s.AmbientStrength=.8;app.ChangeSettings();
        check("Ambient adjustments preserve controls and text selection",island.BodyBuilds==builds && (entry==null || entry.SelectionStart==2 && entry.SelectionLength==5));
        s.AmbientScope="Selected widgets";s.AmbientWidgets=new(){"notes"};app.ChangeSettings();island.SelectPage("focus",false);check("Unselected widgets keep their existing surface",!surface.AmbientVisible);island.SelectPage("notes",false);check("Selected Notes retains ambient",surface.AmbientVisible);
        s.AmbientScope="Music only";app.ChangeSettings();check("Music-only scope removes ambient from Notes",!surface.AmbientVisible);island.SelectPage("media",false);check("Music-only retains Media ambient",surface.AmbientVisible);
        island.Collapse();island.PreviewActivity();island.SelectPeek("notes");check("Hover navigation immediately applies the active widget scope",!surface.AmbientVisible);island.SelectPeek("media");check("Hover Media immediately restores its ambient",surface.AmbientVisible);island.Reveal(false);
        s.AmbientScope="All widgets";s.AmbientColorSource="Accent color";s.Accent="#AF80DA";app.ChangeSettings();check("Accent source applies to actual surface",surface.AmbientColor==UI.Brush(s.Accent).Color);
        s.AmbientColorSource="Album cover";typeof(MediaService).GetProperty(nameof(MediaService.Artwork))!.SetValue(app.Media,null);app.PublishUpdate(RuntimeChange.Media);check("Missing cover falls back to accent on other widgets",surface.AmbientColor==UI.Brush(s.Accent).Color);
        s.AmbientColorSource="Custom color";app.ChangeSettings();island.SelectPage("notes",false);
        s.AmbientAudioResponse="Off";app.ChangeSettings();await Task.Delay(100);check("Audio Off stops the ambient sampler and restores static intensity",!app.AmbientAudio.Running && surface.AmbientAudioGain==1);
        s.AmbientAudioResponse="Punchy";app.ChangeSettings();peak=.5f;await Task.Delay(150);
        app.Previews.Open("notes");app.Previews.Open("system");await Task.Delay(200);
        var mini=Application.Current.Windows.OfType<DetachedPreviewWindow>().ToArray();check("Notes and System mini windows share ambient",mini.Length==2 && mini.All(w=>Diagnostics.Descendants(w).OfType<IslandSurface>().Any(p=>p.AmbientVisible)) && app.AmbientAudio.ConsumerCount==3);
        long spectra=app.Audio.DiagnosticSpectrumSamples;
        long reads=app.Audio.DiagnosticMeterReads;var elapsed=Stopwatch.StartNew();await Task.Delay(1000);elapsed.Stop();long multipleReads=app.Audio.DiagnosticMeterReads-reads;
        check("Three ambient consumers stay within the shared 30 Hz sampling budget",multipleReads>=1 && multipleReads<=Math.Ceiling(elapsed.Elapsed.TotalSeconds*30)+2);
        check("Ambient-only Notes and System do not request output spectrum capture",app.Audio.DiagnosticSpectrumSamples==spectra && app.Audio.SpectrumCaptureCount==0);
        Capture(mini.First(w=>w.Title.Contains("Notes")),"notes-mini");
        app.Previews.SetHidden(true);await Task.Delay(60);check("Hidden mini windows release their audio demand",app.AmbientAudio.ConsumerCount==1);
        app.Previews.SetHidden(false);await Task.Delay(60);foreach(var w in mini)w.Close();await Task.Delay(60);check("Closing mini windows releases their audio consumers",app.AmbientAudio.ConsumerCount==1);
        int registered=app.AmbientAudio.RegisteredCount;
        for(int i=0;i<20;i++){app.Previews.Open("notes");await Task.Delay(10);Application.Current.Windows.OfType<DetachedPreviewWindow>().Single().Close();}
        await Task.Delay(80);check("Repeated mini open/close does not accumulate consumers or registered surfaces",app.AmbientAudio.ConsumerCount==1 && app.AmbientAudio.RegisteredCount==registered);
        island.ToggleTuck();await Task.Delay(150);check("Tucked island stops reactive sampling",!app.AmbientAudio.Running);island.ToggleTuck();await Task.Delay(150);
        island.ToggleVisibility();await Task.Delay(100);reads=app.Audio.DiagnosticMeterReads;await Task.Delay(250);check("Hidden island performs no ambient audio reads",!app.AmbientAudio.Running && app.Audio.DiagnosticMeterReads==reads);island.ToggleVisibility();island.Reveal(false);island.SelectPage("notes",false);
        app.Media.DiagnosticPlayback(false);app.PublishUpdate(RuntimeChange.Media);check("Paused playback stops sampling and keeps a static glow",!app.AmbientAudio.Running && surface.AmbientVisible && surface.AmbientAudioGain==1);
        app.Media.DiagnosticPlayback(true);app.PublishUpdate(RuntimeChange.Media);s.ReduceMotion=true;app.ChangeSettings();check("Reduce motion stops ambient sampling and all ambient clocks",!app.AmbientAudio.Running && !surface.AmbientAnimating);
        s.ReduceMotion=false;s.LiveActivity=false;app.ChangeSettings();check("Live activity Off releases audio demand",!app.AmbientAudio.Running);s.LiveActivity=true;s.CoverAmbient=false;app.ChangeSettings();check("Ambient master Off stops light and sampling",!surface.AmbientVisible && !app.AmbientAudio.Running);
        s.CoverAmbient=true;s.ReduceMotion=true;s.AmbientScope="Selected widgets";app.ChangeSettings();
        var window=new SettingsWindow(app){ShowActivated=false};window.Show();await Task.Delay(80);
        try
        {
            typeof(SettingsWindow).GetMethod("Navigate",flags)!.Invoke(window,new object[]{"Appearance"});await Task.Delay(100);
            var choices=Diagnostics.Descendants(window).OfType<ComboBox>().ToArray();
            check("Appearance exposes ambient scope, colors, response and movement",new[]{"Ambient scope","Ambient color source","Ambient audio response","Ambient movement"}.All(name=>choices.Any(c=>AutomationProperties.GetName(c)==name)));
            var scope=choices.Single(c=>AutomationProperties.GetName(c)=="Ambient scope");scope.SelectedItem="Selected widgets";
            var section=Diagnostics.Descendants(window).OfType<TextBlock>().First(t=>t.Text=="Ambient light");
            var scroll=(ScrollViewer)typeof(SettingsWindow).GetField("settingsScroll",flags)!.GetValue(window)!;
            scroll.ScrollToVerticalOffset(section.TransformToAncestor((Visual)scroll.Content).Transform(new Point()).Y-12);await Task.Delay(100);Capture(window,"settings-ambient");
            var responseLabel=Diagnostics.Descendants(window).OfType<TextBlock>().First(t=>t.Text=="Audio response");
            scroll.ScrollToVerticalOffset(responseLabel.TransformToAncestor((Visual)scroll.Content).Transform(new Point()).Y-12);await Task.Delay(100);Capture(window,"settings-audio-response");
            check("Widget choices stay available within a collapsible selector",Diagnostics.Descendants(window).OfType<Expander>().Any(e=>Equals(e.Header,"Choose widgets") && e.IsVisible));
            check("Ambient has a dedicated settings illustration",Diagnostics.Descendants(window).OfType<SettingsScene>().Any(c=>c.Section=="Ambient light"));
        }
        finally{window.Close();}
        File.WriteAllText(Path.Combine(directory,"sampling.txt"),$"3 visible ambient consumers: {multipleReads} shared meter reads in {elapsed.Elapsed.TotalSeconds:0.000} seconds. Counts reflect dispatched callbacks, not guaranteed wall-clock presentation or GPU frames. Synthetic audio envelope, no real microphone or provider connection. Raster exports at 1x/1.5x/2x do not replace physical mixed-DPI monitor testing.");
    }
}
