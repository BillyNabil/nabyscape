using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NabyScape;
internal static class RecentUxDiagnostics
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task Run(App app,OverlayWindow overlay,string directory,Action<string,bool> check)
    {
        T Field<T>(object obj,string name)=>(T)obj.GetType().GetField(name,Private)!.GetValue(obj)!;
        object? Call(object obj,string method,params object[] args)=>obj.GetType().GetMethod(method,Private)!.Invoke(obj,args);
        void Capture(Window window,string name){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);}
        SettingsWindow? settings=null;WelcomeWindow? welcome=null;
        try
        {
            app.Settings.SmartActivities=false;app.Settings.HideMaximized=app.Settings.HideFullscreen=false;app.Settings.ReduceMotion=true;app.ChangeSettings();
            settings=new SettingsWindow(app){ShowActivated=false};settings.Show();await Task.Delay(200);settings.UpdateLayout();
            check("Start here exposes five readiness statuses without API or microphone checks",Diagnostics.LogicalDescendants(settings).OfType<Border>().Any(b=>b.Tag as string=="Your setup") && Diagnostics.Descendants(settings).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Voice ·")));Capture(settings,"settings-home-current");
            double original=app.Settings.Width;app.Settings.Width=original+24;Call(settings,"Save");await Task.Delay(500);
            var undo=Field<Button>(settings,"undoSettings");check("Undo offered after a saved layout edit",undo.Visibility==Visibility.Visible);undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);check("Undo restores live and persisted width",app.Settings.Width==original && app.Store.Load<Settings>("settings").Width==original);
            Diagnostics.NavigateSettings(settings,"Content");await Task.Delay(100);check("Feature usage is directly visible",Diagnostics.Descendants(settings).OfType<TextBlock>().Any(t=>t.Text=="Feature usage"));Capture(settings,"settings-widgets-current");
            Diagnostics.NavigateSettings(settings,"Audio");await Task.Delay(100);check("Closed media has all four direct choices",new[]{"Waveform","Live lyrics","Song title","None"}.All(label=>Diagnostics.Descendants(settings).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==label)));Capture(settings,"settings-music-current");
            Diagnostics.NavigateSettings(settings,"AI");var ai=Field<AssistantProviderSettings>(settings,"aiSettings");ai.OpenVoiceSetup();await Task.Delay(100);check("Voice setup shows source choices without expander gate",Diagnostics.Descendants(settings).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="On this PC" && b.IsVisible));Capture(settings,"settings-voice-current");
            ai.OpenVoiceChecks();await Task.Delay(100);check("Audio checks directly expose test actions",Diagnostics.Descendants(settings).OfType<Button>().Any(b=>b.Content?.ToString()=="Test microphone" && b.IsVisible));
            welcome=new WelcomeWindow(app){ShowActivated=false};welcome.Show();await Task.Delay(100);check("Welcome has four purpose choices",new[]{"Music","Productivity","AI","Custom"}.All(label=>Diagnostics.Descendants(welcome).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==label)));Capture(welcome,"welcome-current");
            var music=Diagnostics.Descendants(welcome).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Music");music.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var next=Field<Button>(welcome,"next");next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(70);check("Music onboarding exposes only selected widgets",Diagnostics.Descendants(welcome).OfType<CheckBox>().Count()==4);next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
            check("Music preset applies without enabling AI, clipboard or download permissions",app.Settings.Widgets.Where(w=>w.Enabled).Select(w=>w.Id).Order().SequenceEqual(new[]{"media","volume","clock","weather"}.Order()) && !app.Settings.ClipboardHistory && !app.Settings.DownloadsEnabled && !app.Assistant.Options.Enabled);
            app.Settings.ClosedMediaContent="Lyrics";app.Settings.Widgets.Single(w=>w.Id=="media").Enabled=true;app.ChangeSettings();overlay.Collapse();await Task.Delay(500);var host=Field<Border>(overlay,"closedSummaryHost");
            foreach(string line in new[]{"I've been living a lie, there's nothing inside","朝のサンプルテキストが長くても表示できる","Café and a wonderfully long unbrokenwordthatneedstobesplit"})
            {
                for(int i=0;i<12;i++)
                {
                    string phrase=(string)Call(overlay,"ClosedLyricPhrase",line,0d,12d,(double)i)!;
                    var summary=Field<TextBlock>(overlay,"summary");var measured=new FormattedText(phrase,System.Globalization.CultureInfo.CurrentUICulture,summary.FlowDirection,new Typeface(summary.FontFamily,summary.FontStyle,summary.FontWeight,summary.FontStretch),summary.FontSize,summary.Foreground,VisualTreeHelper.GetDpi(summary).PixelsPerDip);
                    check("Lyric phrase fits its measured viewport "+i+" / "+line[..4],measured.WidthIncludingTrailingWhitespace<=host.ActualWidth-12);
                }
            }
            app.Settings.CompactLayout=false;app.Settings.PanelWidth=520;app.Settings.PanelMaxHeight=320;app.ChangeSettings();overlay.Reveal(false);await Task.Delay(500);
            var classicScroll=Field<ScrollViewer>(overlay,"scroll");classicScroll.ScrollToEnd();await Task.Delay(120);overlay.UpdateLayout();
            check("Classic widget viewport remains scrollable at a custom height",classicScroll.ScrollableHeight>0 && classicScroll.VerticalOffset>0);
            app.Media.DiagnosticFixture=true;app.Media.DiagnosticPlayback(true);app.Settings.MediaIntegrationEnabled=false;app.ChangeSettings();check("Media opt-out releases selected session and artwork",app.Media.Selected==null && app.Media.Artwork==null && app.Media.Sessions.Count==0);app.Settings.MediaIntegrationEnabled=true;app.ChangeSettings();
        }
        finally{welcome?.Close();settings?.Close();}
    }
}
