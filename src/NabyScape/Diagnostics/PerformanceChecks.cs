using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class PerformanceChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task Run(App app,OverlayWindow island,Action<string,bool> check)
    {
        void Check(string name,bool pass)=>check("Performance: "+name,pass);
        SettingsWindow? settings=null;
        try
        {
            var s=app.Settings;s.HideFullscreen=s.HideMaximized=false;s.SmartActivities=false;s.ActivitySatellite=false;s.LiveActivity=true;s.ReduceMotion=false;s.AnimationMs=420;s.PanelWidth=514;s.PanelMaxHeight=504;s.CompactLayout=true;
            // This suite isolates waveform demand. Audio-reactive ambient has its
            // own sampler-demand tests and legitimately reads an otherwise quiet UI.
            s.CoverAmbient=false;
            foreach(var widget in s.Widgets)widget.Enabled=true;app.ChangeSettings();
            app.Audio.DiagnosticPeak=()=>.3f;app.Media.DiagnosticPlayback(false);await Task.Delay(1600);
            Check("closed startup defers expanded panel controls",island.BodyBuilds==0);
            long reads=app.Audio.DiagnosticMeterReads;int frames=island.LiveFrames;await Task.Delay(1500);
            Check("paused closed island has no meter reads or continuous rendering",app.Audio.DiagnosticMeterReads==reads && island.LiveFrames==frames && !island.LiveRunning && !island.NeedsAdaptiveCpuSample);
            var cover=BitmapSource.Create(8,8,96,96,PixelFormats.Bgra32,null,new byte[256],32);cover.Freeze();typeof(MediaService).GetProperty(nameof(MediaService.Artwork))!.SetValue(app.Media,cover);
            s.ClosedMediaIcon="Artwork";s.ClosedMediaContent="Title";app.ChangeSettings();app.Media.DiagnosticPlayback(true);await Task.Delay(1500);
            reads=app.Audio.DiagnosticMeterReads;frames=island.LiveFrames;await Task.Delay(1200);
            Check("music with cover and title does not sample an invisible waveform",app.Audio.DiagnosticMeterReads==reads && island.LiveFrames==frames);
            s.ClosedMediaContent="Waveform";app.ChangeSettings();await Task.Delay(1200);
            reads=app.Audio.DiagnosticMeterReads;await Task.Delay(2100);long single=app.Audio.DiagnosticMeterReads-reads;
            Check("visible waveform keeps its live render loop and 30 Hz sample ceiling",island.LiveRunning && single>30 && single<=65);
            Check("Auto frame policy retains shared CPU sampling during visible animation",island.NeedsAdaptiveCpuSample);
            settings=new SettingsWindow(app);settings.Show();typeof(SettingsWindow).GetMethod("Navigate",Private)!.Invoke(settings,new object[]{"Audio"});typeof(SettingsWindow).GetField("previewMode",Private)!.SetValue(settings,"Panel");typeof(SettingsWindow).GetMethod("RefreshPreview",Private)!.Invoke(settings,null);await Task.Delay(1000);
            reads=app.Audio.DiagnosticMeterReads;await Task.Delay(2100);long shared=app.Audio.DiagnosticMeterReads-reads;
            Check("Settings and island share one audio sample stream",shared>30 && shared<=65 && shared<=single+8);
            settings.Width=480;settings.Height=380;await Task.Delay(180);settings.UpdateLayout();
            // The responsive preview is now an expander too. Keep it collapsed
            // while exposing form controls, rather than reopening the preview.
            foreach(var expander in Diagnostics.Descendants(settings).OfType<Expander>().ToArray())expander.IsExpanded=AutomationProperties.GetName(expander)!="Settings live preview";
            await Task.Delay(180);settings.UpdateLayout();
            var signal=(ActivityVisual)typeof(SettingsWindow).GetField("audioSignalWave",Private)!.GetValue(settings)!;signal.BringIntoView();await Task.Delay(180);
            var stage=(Border)typeof(SettingsWindow).GetField("previewStage",Private)!.GetValue(settings)!;
            Check("visible audio signal keeps rendering when the narrow preview rail is hidden",!stage.IsVisible && (bool)typeof(SettingsWindow).GetProperty("AudioSignalVisible",Private)!.GetValue(settings)! && (bool)typeof(SettingsWindow).GetField("previewRendering",Private)!.GetValue(settings)!);
            var form=(ScrollViewer)typeof(SettingsWindow).GetField("settingsScroll",Private)!.GetValue(settings)!;form.ScrollToTop();await Task.Delay(180);
            Check("offscreen audio signal and hidden preview rail release the Settings render callback",!(bool)typeof(SettingsWindow).GetField("previewRendering",Private)!.GetValue(settings)!);
            settings.Width=1100;settings.Height=780;
            typeof(SettingsWindow).GetMethod("Navigate",Private)!.Invoke(settings,new object[]{"Layout"});
            var preview=(Border)typeof(SettingsWindow).GetField("preview",Private)!.GetValue(settings)!;object? child=preview.Child;
            var width=Diagnostics.Descendants(settings).OfType<Slider>().Single(c=>AutomationProperties.GetName(c)=="Closed width");width.Value=310;await Task.Delay(300);
            Check("geometry edits preserve the preview control tree",ReferenceEquals(child,preview.Child));
            settings.WindowState=WindowState.Minimized;await Task.Delay(80);Check("minimized Settings unsubscribes continuous preview rendering",!(bool)typeof(SettingsWindow).GetField("previewRendering",Private)!.GetValue(settings)!);settings.Close();settings=null;
            app.Media.DiagnosticPlayback(false);island.ShowTools("notes",false);island.Activate();await Task.Delay(100);
            var editor=Diagnostics.Descendants(island).OfType<TextBox>().Single(c=>AutomationProperties.GetName(c)=="Quick notes");editor.Text="Retained note draft";editor.Focus();editor.Select(3,4);await Task.Delay(400);int builds=island.BodyBuilds;
            s.Background="#182028";s.BoldHeadings=!s.BoldHeadings;app.ChangeSettings();s.Width=330;s.PanelWidth=340;s.PanelPadding=24;app.ChangeSettings();await Task.Delay(100);
            Check("theme and responsive dimensions preserve editor identity, text and selection",island.BodyBuilds==builds && Diagnostics.Descendants(island).Contains(editor) && editor.Text=="Retained note draft" && editor.SelectionStart==3 && editor.SelectionLength==4);
            island.SelectPage("status",false);island.SelectMonitorTab("overview");await Task.Delay(4500);reads=app.Audio.DiagnosticMeterReads;frames=island.LiveFrames;
            var cpuRow=Diagnostics.Descendants(island).OfType<TextBlock>().FirstOrDefault(t=>t.Text=="CPU");await Task.Delay(2400);
            Check("static System panel does not sample hidden media or retain a render loop",app.Audio.DiagnosticMeterReads==reads && island.LiveFrames==frames && !island.LiveRunning);
            Check("system updates retain existing metric controls",cpuRow!=null && Diagnostics.Descendants(island).Contains(cpuRow));
            app.Previews.OpenAt("notes",new Point(100,180),false);await Task.Delay(80);var mini=Application.Current.Windows.OfType<DetachedPreviewWindow>().Single();var miniEditor=Diagnostics.Descendants(mini).OfType<TextBox>().First();string text=miniEditor.Text;await Task.Delay(1200);
            Check("Notes mini window retains its document and editor across clock ticks",Diagnostics.Descendants(mini).Contains(miniEditor) && miniEditor.Text==text);
            island.ToggleVisibility();reads=app.Audio.DiagnosticMeterReads;frames=island.LiveFrames;await Task.Delay(1500);Check("manual hide stops visual sampling and hides mini windows",app.Audio.DiagnosticMeterReads==reads && island.LiveFrames==frames && !mini.IsVisible);
            island.ToggleVisibility();await Task.Delay(100);Check("manual show restores existing mini windows",mini.IsVisible);mini.Close();
        }
        finally{settings?.Close();foreach(var window in Application.Current.Windows.OfType<DetachedPreviewWindow>().ToArray())window.Close();app.Audio.DiagnosticPeak=null;island.Collapse();}
    }
}
