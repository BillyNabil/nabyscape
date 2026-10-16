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
internal static class Stage5Diagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        void Capture(FrameworkElement element,string name)
        {element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);}
        var s=app.Settings;s.ReduceMotion=true;s.LiveActivity=false;s.SmartActivities=false;s.HideFullscreen=s.HideMaximized=false;s.Widgets.Single(w=>w.Id=="stopwatch").Enabled=true;s.HoverViews.Single(w=>w.Id=="stopwatch").Enabled=true;app.ChangeSettings();
        foreach(double width in new[]{384d,340d})
        {
            s.PanelWidth=width;app.ChangeSettings();island.Reveal(false);island.SelectPage("focus",false);await Task.Delay(250);island.UpdateLayout();
            check("Stage5 Focus includes readable Stopwatch at "+width,Diagnostics.Descendants(island).OfType<StopwatchView>().Any(v=>v.IsVisible && v.ActualWidth>140));
            Capture(island,"stage5-focus-"+width);
        }
        var main=Diagnostics.Descendants(island).OfType<StopwatchView>().Single(v=>v.IsVisible);
        int builds=island.BodyBuilds;
        var editor=Diagnostics.Descendants(island).OfType<TextBox>().First(t=>t.IsVisible);editor.Text="12.5";editor.Focus();editor.Select(1,2);
        app.Previews.Open("stopwatch");await Task.Delay(200);var detached=Application.Current.Windows.Cast<Window>().Single(w=>w.Title=="NabyScape · Stopwatch");var mini=Diagnostics.Descendants(detached).OfType<StopwatchView>().Single();var location=new Point(detached.Left,detached.Top);
        ((Button)main.Children.OfType<StackPanel>().Single().Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(1200);check("Stage5 shared main and detached stopwatch update without rebuilding editors",main.Refreshes>1 && mini.Refreshes>1 && app.Session.Stopwatch.Running && island.BodyBuilds==builds && editor.Text=="12.5" && editor.SelectionStart==1 && editor.SelectionLength==2);
        Capture(detached,"stage5-detached-stopwatch");
        var saved=app.Store.Load<SessionState>("session");check("Stage5 running Stopwatch persisted in isolated fixture",saved.Stopwatch.Running && saved.Stopwatch.UtcAnchor.HasValue);
        app.Session.Stopwatch.Toggle();app.SaveSession();double paused=app.Session.Stopwatch.Elapsed;await Task.Delay(1100);check("Stage5 pause shared and stable",!app.Session.Stopwatch.Running && app.Session.Stopwatch.Elapsed==paused);
        s.DisplayMode="Follow";app.ChangeSettings();check("Stage5 changing source policy does not relocate detached windows",location==new Point(detached.Left,detached.Top));
        detached.Close();island.Collapse();island.PreviewActivity();island.SelectPeek("stopwatch");await Task.Delay(200);Capture(island,"stage5-hover-stopwatch");check("Stage5 Stopwatch hover shares controls",island.PeekPage=="stopwatch" && Diagnostics.Descendants(island).OfType<StopwatchView>().Any(v=>v.IsVisible));
        island.ToggleVisibility();int updates=main.Refreshes;int frames=island.LiveFrames;await Task.Delay(1100);check("Stage5 manual hide stops stopwatch view updates and visual frames",main.Refreshes==updates && island.LiveFrames==frames);island.ToggleVisibility();
        var settings=new SettingsWindow(app);settings.Show();typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(settings,new object[]{"Content"});await Task.Delay(200);Capture(settings,"stage5-settings-content");check("Stage5 Settings has exact shared Stopwatch controls",Diagnostics.Descendants(settings).OfType<StopwatchView>().Any());typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(settings,new object[]{"Layout"});await Task.Delay(200);Capture(settings,"stage5-settings-follow-default");check("Stage5 Layout explains follow delay and preserves explicit Follow selection",Diagnostics.Descendants(settings).OfType<ComboBox>().Any(box=>AutomationProperties.GetName(box)=="Display mode" && box.SelectedIndex==2) && Diagnostics.Descendants(settings).OfType<TextBlock>().Any(t=>t.Text.Contains("Follow cursor moves after 250 ms")));settings.Width=760;settings.Height=600;await Task.Delay(150);Capture(settings,"stage5-settings-follow-small");settings.Close();
        app.Session.Stopwatch.Reset();app.SaveSession();
    }
}

