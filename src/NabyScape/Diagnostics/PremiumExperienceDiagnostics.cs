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

internal static class PremiumExperienceDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,flags)!.GetValue(island)!;
        var surface=Field<IslandSurface>("surface");var s=app.Settings;
        s.AmbientScope="Music only";s.AmbientAudioResponse="Off";s.ReduceMotion=true;s.CoverAmbient=true;s.AmbientStrength=.65;s.AmbientSpread=.85;s.GlassStyle=true;s.PanelWidth=526;s.PanelMaxHeight=650;s.PanelPadding=26;s.PanelScale=1;s.CompactLayout=true;s.CategoryStyle="Dots";s.AlwaysTucked=s.HideMaximized=s.HideFullscreen=false;s.SmartActivities=false;s.NotificationDelivery="Notch";s.CountdownSound="Off";
        foreach(var widget in s.Widgets)widget.Enabled=true;app.ChangeSettings();app.Media.DiagnosticPlayback(true);app.Audio.DiagnosticPeak=()=>.25f;
        void Cover(Color color)
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(new LinearGradientBrush(color,Colors.Black,90),null,new Rect(0,0,96,96));dc.DrawEllipse(UI.Brush("#D9CFBA"),null,new Point(65,28),15,15);dc.DrawRectangle(new SolidColorBrush(color),null,new Rect(0,72,96,24));}
            var bitmap=new RenderTargetBitmap(96,96,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();typeof(MediaService).GetProperty(nameof(MediaService.Artwork))!.SetValue(app.Media,bitmap);app.PublishUpdate(RuntimeChange.Media);
        }
        typeof(MediaService).GetProperty(nameof(MediaService.Title))!.SetValue(app.Media,"Afterglow");typeof(MediaService).GetProperty(nameof(MediaService.Artist))!.SetValue(app.Media,"Original cover · local preview");
        void Capture(FrameworkElement element,string name,bool crop=false,double scale=1)
        {
            element.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(element.ActualWidth*scale)),Math.Max(1,(int)Math.Ceiling(element.ActualHeight*scale)),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(element);
            BitmapSource source=bitmap;if(crop){int w=Math.Min(bitmap.PixelWidth,(int)Math.Ceiling(island.PaintedWidth*scale)),h=Math.Min(bitmap.PixelHeight,(int)Math.Ceiling(island.PaintedHeight*scale));source=new CroppedBitmap(bitmap,new Int32Rect(Math.Max(0,(bitmap.PixelWidth-w)/2),0,w,h));}
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(source));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        island.Reveal(false);island.SelectPage("media",false);Cover(Color.FromRgb(231,74,41));await Task.Delay(100);Capture(island,"ambient-warm",true);
        check("Cover ambient samples a warm dominant color",surface.AmbientColor.R>surface.AmbientColor.B+70);
        int builds=island.BodyBuilds;s.AmbientStrength=.82;app.ChangeSettings();check("Ambient slider updates preserve the existing island controls",island.BodyBuilds==builds);
        Cover(Color.FromRgb(40,121,237));await Task.Delay(80);Capture(island,"ambient-cool",true);Capture(island,"ambient-cool-150",true,1.5);Capture(island,"ambient-cool-200",true,2);
        check("Changing cover changes the dominant ambient color",surface.AmbientColor.B>surface.AmbientColor.R+70);
        s.CoverAmbient=false;app.ChangeSettings();Capture(island,"ambient-off",true);check("Ambient can be disabled independently of glass",!surface.AmbientVisible && !surface.AmbientAnimating && s.GlassStyle);
        s.CoverAmbient=true;s.ReduceMotion=false;s.AmbientMotion="Drift";app.ChangeSettings();await Task.Delay(100);
        check("Visible playing artwork starts the retained drift clock",!Motion.Enabled || surface.AmbientAnimating);
        island.SelectPage("focus",false);check("Other panels retain their dark surface",!surface.AmbientVisible && !surface.AmbientAnimating);island.SelectPage("media",false);
        var cover=Field<Image>("artwork").Parent as FrameworkElement;cover!.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});await Task.Delay(310);
        check("Artwork reacts to hover without changing playback",!Motion.Enabled || cover.RenderTransform is ScaleTransform {ScaleX:>1.03});cover.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});
        app.Media.DiagnosticPlayback(false);app.PublishUpdate(RuntimeChange.Media);check("Pausing playback stops ambient motion",!surface.AmbientAnimating);
        app.Media.DiagnosticPlayback(true);app.PublishUpdate(RuntimeChange.Media);s.AmbientMotion="Still";app.ChangeSettings();check("Still mode keeps color without an animation clock",!surface.AmbientAnimating);
        s.AmbientMotion="Breathe";app.ChangeSettings();check("Breathe starts a retained opacity animation",!Motion.Enabled || surface.AmbientAnimating);
        island.ToggleTuck();await Task.Delay(100);check("Tucking stops continuous ambient animation",island.IsTucked && !surface.AmbientAnimating);island.ToggleTuck();
        island.ToggleVisibility();await Task.Delay(80);check("Manual hide releases ambient clocks",!surface.AmbientAnimating);island.ToggleVisibility();
        s.ReduceMotion=true;app.ChangeSettings();check("Reduce motion removes all ambient clocks",!surface.AmbientAnimating);
        Cover(Color.FromRgb(231,74,41));s.PanelWidth=300;app.ChangeSettings();island.Reveal(false);island.SelectPage("media",false);await Task.Delay(80);Capture(island,"ambient-narrow",true);s.PanelWidth=526;app.ChangeSettings();

        var window=new SettingsWindow(app){ShowActivated=false};window.Show();await Task.Delay(80);
        try
        {
            void Navigate(string page)=>typeof(SettingsWindow).GetMethod("Navigate",flags)!.Invoke(window,new object[]{page});
            Navigate("Appearance");await Task.Delay(70);Capture(window,"settings-appearance");

            var slider=Diagnostics.Descendants(window).OfType<Slider>().First(c=>c.IsVisible && c.IsLoaded);slider.ApplyTemplate();var thumb=((Track)slider.Template.FindName("PART_Track",slider)).Thumb;
            s.ReduceMotion=false;s.InteractionMotion="Expressive";app.ChangeSettings();thumb.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});await Task.Delay(190);
            check("Sliders provide hover feedback without changing their value",!Motion.Enabled || thumb.RenderTransform is ScaleTransform {ScaleX:>1.1});
            var toggle=Diagnostics.Descendants(window).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Bold media titles");toggle.IsChecked=!toggle.IsChecked;
            check("Checkboxes animate their selection feedback",!Motion.Enabled || (toggle.Template.FindName("Knob",toggle)??toggle.Template.FindName("Ring",toggle)) is FrameworkElement ring && ring.RenderTransform.HasAnimatedProperties);
            s.InteractionMotion="Off";app.ChangeSettings();thumb.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});await Task.Delay(190);check("Interaction Off restores the neutral thumb",thumb.RenderTransform is ScaleTransform {ScaleX:1});
            s.InteractionMotion="Subtle";s.ReduceMotion=true;app.ChangeSettings();
            Navigate("Ambience");await Task.Delay(60);
            check("Ambience exposes movement",Diagnostics.Descendants(window).OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="Ambient movement"));
            Diagnostics.ExpandSettingsExamples(window,"Ambient light");
            check("Ambient example is rendered",Diagnostics.SettingsExamples(window).Any(c=>c.Section=="Ambient light" && c.ActualWidth>0 && c.ActualHeight>0));Capture(window,"settings-ambience");
            Navigate("Motion");await Task.Delay(60);check("Motion exposes interaction feedback",Diagnostics.Descendants(window).OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="Interaction feedback"));
            Navigate("Notifications");await Task.Delay(60);Capture(window,"settings-notifications");check("Notifications exposes presentation and quiet schedule",Diagnostics.Descendants(window).OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="Notification animation") && Diagnostics.Descendants(window).OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="Quiet hours start"));
            Navigate("AI");await Task.Delay(60);
            var presets=Diagnostics.Descendants(window).OfType<ComboBox>().Single(c=>ReferenceEquals(c.ItemsSource,AssistantProviderCatalog.Presets));Diagnostics.Descendants(window).OfType<AssistantProviderSettings>().Single().RevealSetupTarget(presets);presets.SelectedItem="Hermes Agent";
            Diagnostics.Descendants(window).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Add provider").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);
            check("Hermes preset fills its agent alias and local gateway",app.Assistant.Options.Providers.Any(p=>p.Backend=="Hermes" && p.Model=="hermes-agent" && p.BaseUrl.Contains(":8642/v1/")));
            window.OpenAiHermesSettings();await Task.Delay(100);
            check("Hermes connection UI distinguishes backend permissions",Diagnostics.Descendants(window).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text.Contains("skills, memory, tools and permissions")));Capture(window,"settings-hermes");
        }
        finally{window.Close();}

        island.Collapse();await Task.Delay(50);app.Notifications.Clear();
        var fixture=new Settings{NotificationDelivery="Notch"};
        foreach(string kind in new[]{"timer","focus","updates"})app.Notifications.Post(fixture,new(kind,kind=="timer"?"Timer finished":kind=="focus"?"Focus complete":"Update available","An original notification example."),false,_=>{},DateTimeOffset.UtcNow);
        island.OpenNotice(true);await Task.Delay(80);Capture(island,"notification-queue",true);
        var card=Field<Border>("noticeCard");check("Notification queue is visible and action names stay clear",Diagnostics.Descendants(card).OfType<TextBlock>().Any(t=>t.Text=="3 queued") && Diagnostics.Descendants(card).OfType<Button>().Any(b=>Equals(b.Content,"Clear all")));
        check("Notifications stay on a clean dark surface",!surface.AmbientVisible);
        check("Notification details hide the closed companion to avoid overlap",!Field<FrameworkElement>("assistantCharacterHost").IsVisible);
        app.Notifications.Post(fixture,new("failure","An additional update","Local fixture"),false,_=>{},DateTimeOffset.UtcNow);check("Arrivals retain the displayed card instead of rebuilding under the pointer",ReferenceEquals(card,Field<Border>("noticeCard")));
        Diagnostics.Descendants(card).OfType<Button>().Single(b=>Equals(b.Content,"Next")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);check("Next advances to the next queued notification",app.Notifications.Count==3 && app.Notifications.Current?.Kind=="focus");
        Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<Button>().Single(b=>Equals(b.Content,"Clear all")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check("Clear all empties the ephemeral notification queue",app.Notifications.Count==0);
        s.NotificationsSnoozedUntil=DateTimeOffset.UtcNow.AddMinutes(15);app.Notifications.Post(fixture,new("preview","Notification preview","Explicit preview during snooze"),false,_=>{},DateTimeOffset.UtcNow);island.OpenNotice(true);await Task.Delay(1200);
        check("An explicitly requested preview remains available during snooze",app.Notifications.Current?.Kind=="preview");s.NotificationsSnoozedUntil=null;island.Collapse();app.Notifications.Clear();
        foreach(string animation in new[]{"Lift","Fade","Soft scale"})
        {
            island.Collapse();app.Notifications.Clear();s.NotificationAnimation=animation;s.ReduceMotion=false;app.ChangeSettings();
            app.Notifications.Post(fixture,new("focus","Focus complete","Time for a short break."),false,_=>{},DateTimeOffset.UtcNow);island.OpenNotice(true);
            card=Field<Border>("noticeCard");check(animation+" starts a finite notification entrance",!Motion.Enabled || card.HasAnimatedProperties || card.RenderTransform.HasAnimatedProperties);await Task.Delay(420);Capture(island,"notification-"+animation.Replace(' ','-').ToLowerInvariant(),true);
        }
        s.ReduceMotion=true;app.ChangeSettings();island.Collapse();app.Notifications.Clear();
        check("Fixtures keep microphone and screen sharing inactive",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Original drawn covers and synthetic messages only. No real Hermes gateway request, API key, microphone, clipboard or screen capture. Scaled WPF raster exports do not replace physical monitor DPI testing.");
    }
}
