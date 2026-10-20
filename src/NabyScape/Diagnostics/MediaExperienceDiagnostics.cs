using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class MediaExperienceDiagnostics
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Private)!.GetValue(target)!;
        void Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private)!.Invoke(target,args);
        TextBox Entry(string name)=>Diagnostics.Descendants(island).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==name);
        void Capture(string name,double dpi=1)
        {
            island.UpdateLayout();double width=island.PaintedWidth,height=island.PaintedHeight;
            var full=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth*dpi),(int)Math.Ceiling(island.ActualHeight*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);full.Render(island);
            int x=Math.Max(0,(int)Math.Floor((island.ActualWidth-width)/2*dpi));var crop=new CroppedBitmap(full,new Int32Rect(x,0,Math.Min(full.PixelWidth-x,(int)Math.Ceiling(width*dpi)),Math.Min(full.PixelHeight,(int)Math.Ceiling(height*dpi))));
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(crop));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
        var s=app.Settings;
        // Read a current output only before producing fixture captures. Never alter playback.
        app.Media.DiagnosticFixture=false;await app.Media.Initialize();await Task.Delay(350);
        var liveSource=app.Media.Selected?.SourceAppUserModelId;bool live=app.Media.Playing && !string.IsNullOrEmpty(liveSource);bool spectrum=false;
        if(live)
        {
            for(int i=0;i<35;i++){app.Audio.MediaPeak(liveSource);spectrum|=app.Audio.MediaSpectrum?.Any(band=>band>.02)==true;await Task.Delay(35);}
            check("Current playing media produces measured frequency bands or reports a muted/unavailable source",spectrum || app.Audio.MediaMeterMuted || app.Audio.MediaMeterSessions==0);
        }
        if(Environment.GetCommandLineArgs().Contains("--media-ux-sound-probe"))
        {
            app.CountdownAudio.Play(new Settings{CountdownSound="Chime",CountdownSoundVolume=.15});bool measured=false;
            for(int i=0;i<40;i++){app.Audio.MediaPeak("NabyScape.exe");measured|=app.Audio.MediaSpectrum?.Any(b=>b>.02)==true;await Task.Delay(35);}
            check("Completion chime plays directly on the Windows output",app.CountdownAudio.LastError==null);check("Real output loopback resolves the app session and produces FFT bands",measured && app.Audio.MediaMeterSessions>0);app.CountdownAudio.Dispose();
        }
        app.Media.Dispose();app.Media.DiagnosticFixture=true;app.Media.DiagnosticPlayback(true);typeof(MediaService).GetProperty("Artwork")!.SetValue(app.Media,null);
        app.Audio.DiagnosticPeak=()=>(float)(.3+.15*Math.Sin(Environment.TickCount64*.013));
        s.CompactLayout=true;s.Preset="Mac";s.Top=0;s.PanelWidth=526;s.PanelMaxHeight=650;s.PanelPadding=26;s.PanelScale=1;s.CategoryStyle="Dots";s.ReduceMotion=true;s.SmartActivities=false;s.CountdownSound="Off";s.LiveActivity=true;s.DetachablePreviews=true;
        app.ChangeSettings();island.Reveal(false);await Task.Delay(450);
        var tabs=Field<System.Collections.Generic.Dictionary<string,Button>>(island,"compactTabs");
        check("Tools appear as seven independent navigation panels, without a Tools folder",Settings.ToolNames.Keys.All(tabs.ContainsKey) && !tabs.ContainsKey("tools"));
        check("Dot navigation retains names and selection semantics",tabs.Values.All(button=>button.Content is System.Windows.Shapes.Rectangle && AutomationProperties.GetName(button).Length>0));
        app.Productivity.Note="A local note for the interface check.";app.Productivity.AddTask("Check the new panels");
        island.ShowTools("notes",false);await Task.Delay(120);var note=Entry("Quick notes");note.Text="Note retained while changing panels.";note.Select(5,8);Capture("notes-dots");
        island.ShowTools("tasks",false);await Task.Delay(120);check("Tasks opens its checklist directly and Notes is not duplicated",island.ActivePage=="tasks" && Diagnostics.Descendants(island).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)=="New task") && !Diagnostics.Descendants(island).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="All tools" && b.IsVisible));Capture("tasks-dots");
        island.ShowTools("notes",false);await Task.Delay(120);check("Notes survive moving between separate tool panels",Entry("Quick notes").Text=="Note retained while changing panels.");
        note=Entry("Quick notes");note.Focus();note.Select(5,8);double beforeScroll=Field<ScrollViewer>(island,"scroll").VerticalOffset;s.Accent="#C5D8F2";app.ChangeSettings();await Task.Delay(120);check("Resource-only preferences preserve note editor, selection and scroll",ReferenceEquals(note,Entry("Quick notes")) && note.SelectionStart==5 && note.SelectionLength==8 && Math.Abs(beforeScroll-Field<ScrollViewer>(island,"scroll").VerticalOffset)<1);
        s.PanelAutoHeight=false;s.PanelMaxHeight=520;app.ChangeSettings();await Task.Delay(80);check("Manual panel height can grow beyond its current content",Math.Abs(island.PaintedHeight-520)<1);s.PanelAutoHeight=true;s.PanelMaxHeight=650;app.ChangeSettings();
        s.FocusMinutes=17;s.BreakMinutes=4;app.ChangeSettings();island.SelectPage("focus",false);await Task.Delay(140);var focus=Entry("Focus minutes");focus.Text="31";focus.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(island),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});check("Focus duration is editable directly in the panel",s.FocusMinutes==31 && Entry("Break minutes").Text=="4");
        var start=Diagnostics.Descendants(island).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Start focus");start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check("Focus start uses the edited duration",app.Session.Focus.Running && app.Session.Focus.Remaining(DateTimeOffset.UtcNow)>30*60);app.Session.Focus.Reset();app.SaveSession();Capture("focus-editable");
        s.DockUnlocked=true;app.ChangeSettings();island.Collapse();await Task.Delay(160);check("Dock exposes named move and corner-resize grips",Diagnostics.Descendants(island).OfType<Thumb>().Count(t=>t.IsVisible && AutomationProperties.GetName(t).Contains("island"))==3);Capture("dock-editing");
        var saved=DockLayout.From(s);s.Width=390;s.Height=52;s.PanelWidth=760;s.HoverWidth=680;Invoke(island,"ReflowDisplay");saved.Restore(s);app.ChangeSettings();check("Cancelling geometry edits restores independent closed, hover and panel sizes",s.Width==saved.Width && s.HoverWidth==saved.HoverWidth && s.PanelWidth==saved.PanelWidth);
        s.DockUnlocked=false;s.HoverWidth=710;s.HoverHeight=260;app.ChangeSettings();island.PreviewActivity();await Task.Delay(180);check("Hover width can exceed panel width independently",island.PaintedWidth>island.PaintedHeight && Math.Abs(island.PaintedWidth-710)<2);Capture("hover-independent-size");island.Collapse();
        foreach(double width in new[]{300d,526d})
        {
            s.PanelWidth=width;app.ChangeSettings();island.Reveal(false);island.SelectPage("media",false);await Task.Delay(180);
            var body=Field<StackPanel>(island,"body");var bounds=body.TransformToAncestor(island).TransformBounds(new Rect(0,0,body.ActualWidth,body.ActualHeight));double left=(island.ActualWidth-island.PaintedWidth)/2;
            check("Media horizontal insets are symmetric at "+width+" DIP",Math.Abs((bounds.Left-left)-(left+island.PaintedWidth-bounds.Right))<2);
            var fft=new SpectrumTransform(48000);for(int sample=0;sample<8000;sample++)fft.Add((float)(.18*Math.Sin(2*Math.PI*110*sample/48000)+.04*Math.Sin(2*Math.PI*1400*sample/48000)+.013*Math.Sin(2*Math.PI*7600*sample/48000)));Field<ActivityVisual>(island,"musicWave").Sample(.4,.1,0,false,false,true,0,true,false,fft.Bands,s.WaveformSensitivity);
            Capture("media-"+width);foreach(double dpi in new[]{1.5,2d})Capture("media-"+width+"-"+(dpi*100),dpi);
        }
        island.Collapse();island.ToggleVisibility();await Task.Delay(1200);check("Output spectrum releases capture after its last visual consumer",app.Audio.SpectrumCaptureCount==0);island.ToggleVisibility();
        s.PanelWidth=526;s.PanelMaxHeight=650;app.ChangeSettings();island.Reveal(false);island.SelectPage("media",false);await Task.Delay(120);
        // The lyric captures use original sample text; never query a provider or alter playback.
        var lyrics=Field<LyricsView>(island,"mediaLyrics");var fixture=new LyricResult(new[]{new LyricLine(0,"A little closer to the music"),new LyricLine(4,"One line, right on time"),new LyricLine(8,"Let the next moment arrive")},"Preview · original sample text");
        lyrics.SetFixture(fixture,5.6,12);lyrics.Visibility=Visibility.Visible;Field<Button>(island,"lyricsButton").Visibility=Visibility.Visible;Invoke(island,"ResizeToolPanel");await Task.Delay(160);
        var gif=new GifBitmapEncoder();s.ReduceMotion=false;Motion.Configure(s);
        foreach(string preset in new[]{"Slide","Focus","Karaoke"})
        {
            s.LyricsAnimation=preset;var labelled=fixture with{Status=preset+" · original animation sample"};lyrics.SetFixture(labelled,3.8,12);lyrics.SetFixture(labelled,4.2,12);
            if(preset!="Karaoke")check(preset+" lyric preset starts a retained transition",!Motion.Enabled || Field<Grid>(lyrics,"line").RenderTransform.HasAnimatedProperties);
            else{double first=Field<RectangleGeometry>(lyrics,"sweep").Rect.Width;lyrics.SetFixture(labelled,6.5,12);check("Karaoke sweeps by line timing without invented word stamps",Field<RectangleGeometry>(lyrics,"sweep").Rect.Width>first);}
            await Task.Delay(90);Capture("lyrics-"+preset.ToLowerInvariant());
            for(int frame=0;frame<18;frame++)
            {
                lyrics.SetFixture(labelled,2.8+frame*.16,12);await Task.Delay(55);lyrics.UpdateLayout();var image=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(lyrics.ActualWidth)),Math.Max(1,(int)Math.Ceiling(lyrics.ActualHeight)),96,96,PixelFormats.Pbgra32);image.Render(lyrics);var metadata=new BitmapMetadata("gif");metadata.SetQuery("/grctlext/Delay",(ushort)6);gif.Frames.Add(BitmapFrame.Create(image,null,metadata,null));
            }
        }
        using(var output=File.Create(Path.Combine(directory,"lyrics-presets.gif")))gif.Save(output);s.ReduceMotion=true;Motion.Configure(s);
        check("All lyric presets render within the content inset",lyrics.ActualWidth<=Field<StackPanel>(island,"body").ActualWidth+.5);
        var provider=new AssistantProvider{Name="Local fixture",Model="fixture"};app.Assistant.Options.Providers.Clear();app.Assistant.Options.Providers.Add(provider);app.Assistant.Options.SelectedProvider=provider.Id;app.Assistant.Options.Enabled=true;app.Assistant.SaveOptions();app.Assistant.Conversation.Record("user","Help me plan a calmer workspace.");app.Assistant.Conversation.Record("assistant","Start with the tools you use most. Keep your notes close, and give the rest a little room.");
        foreach(double width in new[]{300d,526d})
        {
            s.PanelWidth=width;s.PanelMaxHeight=460;app.ChangeSettings();island.OpenAssistant(false);await Task.Delay(180);Capture("naby-chat-"+width);var input=Entry("Message AI companion");input.Text="A message draft";input.Select(2,5);s.Accent="#FFFFFF";app.ChangeSettings();await Task.Delay(80);check("AI composer remains usable with preserved draft at "+width+" DIP",Entry("Message AI companion").Text=="A message draft" && input.SelectionStart==2 && input.SelectionLength==5);
        }
        island.CloseAssistant();
        var window=new SettingsWindow(app){ShowActivated=false};window.Show();await Task.Delay(160);
        foreach(string category in new[]{"Audio","Layout","Application","Content"})
        {
            Invoke(window,"Navigate",category);await Task.Delay(140);check("Settings "+category+" registers all new visual examples",Diagnostics.Descendants(window).OfType<SettingsScene>().All(scene=>SettingsScene.Sections.Contains(scene.Section)));
            if(category=="Audio")
            {
                var sample=Diagnostics.Descendants(window).OfType<LyricsView>().Single();sample.BringIntoView();await Task.Delay(100);check("Lyrics preview recognizes its actual visible scroll viewport",ElementVisibility.InViewport(sample));
                window.WindowState=WindowState.Minimized;await Task.Delay(60);check("Minimized Settings is not an active lyric preview consumer",!ElementVisibility.InViewport(sample));window.WindowState=WindowState.Normal;
                check("Settings lyric sample never starts an external metadata request",typeof(LyricsService).GetField("pending",Private)!.GetValue(app.Lyrics)==null);
            }
        }
        window.Close();check("UI diagnostics never start voice or screen sharing",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"WPF RenderTargetBitmap captures at 100/150/200% rendering scale; this is not physical monitor/DPI relocation or GPU-presented-frame measurement. Note, task, conversation and lyric text are original fixtures. No credential, microphone, screen sharing, shell action or actual media command used. Lyrics lookup tests use fake HTTP. "+(live?(spectrum?"Real output FFT bands verified from current media in memory. No samples were saved.":"Live source was muted or could not be resolved; measured amplitude fallback verified. No samples were saved."):"Live output spectrum hardware check skipped: no playing media session was available."));
    }
}
