using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;
internal static class MediaFooterDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,flags)!.GetValue(island)!;
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Escape()=>island.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(island),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        void Capture(string name)
        {
            island.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth),(int)Math.Ceiling(island.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(island);
            int width=Math.Min(bitmap.PixelWidth,(int)Math.Ceiling(island.PaintedWidth)),height=Math.Min(bitmap.PixelHeight,(int)Math.Ceiling(island.PaintedHeight));var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap,new Int32Rect((bitmap.PixelWidth-width)/2,0,width,height))));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
        var s=app.Settings;s.CompactLayout=true;s.ReduceMotion=true;s.HideMaximized=s.HideFullscreen=s.AlwaysTucked=false;s.PanelWidth=526;s.PanelMaxHeight=417;s.PanelPadding=32;s.PanelScale=1;s.PanelAutoHeight=true;s.GlassStyle=false;s.SmartActivities=false;s.CategoryStyle="Dots";
        foreach(var widget in s.Widgets)if(widget.Id is "media" or "volume")widget.Enabled=true;
        app.ChangeSettings();app.Media.DiagnosticPlayback(true);app.Audio.DiagnosticPeak=()=>.25f;
        typeof(MediaService).GetProperty("Title")!.SetValue(app.Media,"Evening light");typeof(MediaService).GetProperty("Artist")!.SetValue(app.Media,"Original artwork · local demo");
        var art=new DrawingVisual();using(var dc=art.RenderOpen()){dc.DrawRectangle(UI.Brush("#D6C65D"),null,new Rect(0,0,120,120));dc.DrawEllipse(UI.Brush("#B3A241"),null,new Point(50,55),34,34);dc.DrawEllipse(null,new Pen(UI.Brush("#756C34"),2),new Point(50,55),18,18);}var artwork=new RenderTargetBitmap(120,120,96,96,PixelFormats.Pbgra32);artwork.Render(art);artwork.Freeze();typeof(MediaService).GetProperty("Artwork")!.SetValue(app.Media,artwork);
        island.Reveal(false);island.SelectPage("media",false);app.PublishUpdate(RuntimeChange.Media);await Task.Delay(180);
        var controls=Field<MediaControls>("mediaControls");double closedHeight=island.PaintedHeight;
        check("Default media has two compact footer actions",controls.SourceButton.IsVisible && controls.VolumeButton.IsVisible);
        check("Default hides the full-width selector and slider",!Diagnostics.Descendants(island).OfType<Slider>().Any(slider=>slider.IsVisible && AutomationProperties.GetName(slider)=="Windows volume") && !Field<ComboBox>("sessions").IsVisible);
        Click(controls.VolumeButton);await Task.Delay(70);island.UpdateLayout();
        check("Volume mounts the original Windows slider",Field<Slider>("volume").IsVisible && Diagnostics.Descendants(controls.Popover).Contains(Field<Slider>("volume")));
        check("Mute, mixer, devices and microphone remain accessible",new[]{"Mute or unmute","Open volume mixer","Audio devices and microphone"}.All(name=>Diagnostics.Descendants(controls.Popover).OfType<Button>().Any(button=>AutomationProperties.GetName(button)==name)));
        check("Volume disclosure opens below the footer without covering playback",controls.Popover.TransformToAncestor(controls).Transform(new Point()).Y>=controls.VolumeButton.TransformToAncestor(controls).Transform(new Point(0,controls.VolumeButton.ActualHeight)).Y);Capture("media-volume-integrated");
        check("Open popover defers cursor following",(bool)typeof(OverlayWindow).GetProperty("FollowInteraction",flags)!.GetValue(island)!);
        Escape();check("Escape dismisses before collapsing the dock",!controls.IsOpen && island.IsExpanded);
        Click(controls.VolumeButton);Click(controls.VolumeButton);check("The same footer button toggles closed",!controls.IsOpen);
        Click(controls.SourceButton);Click(controls.VolumeButton);check("Only the most recently chosen popover is open",controls.OpenKind=="volume");
        var dismiss=typeof(OverlayWindow).GetMethod("DismissMediaOrCollapse",flags)!;dismiss.Invoke(island,null);dismiss.Invoke(island,null);check("Duplicate external click notifications do not collapse the dock",!controls.IsOpen && island.IsExpanded);

        // Inject only deterministic sources and a sample slider for captures. Never
        // issue a real media/volume/device command or provider request.
        string current="Spotify.exe";int selections=0;var choices=new List<MediaSourceChoice>();
        void ResetChoices(){choices.Clear();choices.Add(new("Spotify.exe","Spotify",()=>{current="Spotify.exe";selections++;}));choices.Add(new("chrome.exe","Google Chrome",()=>{current="chrome.exe";selections++;}));}
        FrameworkElement SampleVolume()
        {
            var panel=new StackPanel();var heading=new DockPanel();var value=UI.Text("63%",11,UI.Muted);DockPanel.SetDock(value,Dock.Right);heading.Children.Add(value);heading.Children.Add(UI.Text("Volume",12));panel.Children.Add(heading);
            var row=new DockPanel{Margin=new(0,8,0,0)};var mute=Icons.Button("volume","Mute sample",()=>{},false,28);DockPanel.SetDock(mute,Dock.Left);row.Children.Add(mute);var mixer=Icons.Button("mixer","Sample mixer",()=>{},false,28);DockPanel.SetDock(mixer,Dock.Right);row.Children.Add(mixer);var slider=new Slider{Minimum=0,Maximum=100,Value=63,Height=26,Margin=new(8,0,8,0)};UI.Named(slider,"Sample volume");row.Children.Add(slider);panel.Children.Add(row);return panel;
        }
        MediaControls Inject()
        {
            var old=Field<MediaControls>("mediaControls");old.Close(false);var owner=(Grid)old.Parent;int index=owner.Children.IndexOf(old);var content=(StackPanel)old.Children[0];old.Children.Remove(content);content.Children.RemoveAt(content.Children.Count-1);owner.Children.Remove(old);
            var replacement=new MediaControls(content,true,true,()=>choices,()=>current,SampleVolume);owner.Children.Insert(index,replacement);typeof(OverlayWindow).GetField("mediaControls",flags)!.SetValue(island,replacement);replacement.Refresh(63,false,true);return replacement;
        }
        ResetChoices();controls=Inject();await Task.Delay(90);Capture("media-default");
        Click(controls.VolumeButton);await Task.Delay(60);Capture("media-volume");Click(controls.SourceButton);await Task.Delay(60);Capture("media-sources");
        check("Current media source is marked",Diagnostics.Descendants(controls.Popover).OfType<Button>().Any(button=>AutomationProperties.GetName(button)=="Use media source Spotify" && AutomationProperties.GetItemStatus(button)=="Selected"));
        Click(Diagnostics.Descendants(controls.Popover).OfType<Button>().Single(button=>AutomationProperties.GetName(button)=="Use media source Google Chrome"));controls.Refresh(63,false,true);
        check("Source selection runs exactly once and closes",current=="chrome.exe" && selections==1 && !controls.IsOpen);
        Click(controls.SourceButton);choices.RemoveAt(1);controls.Refresh(63,false,true);check("Disappearing sessions dismiss stale choices",!controls.IsOpen);
        choices.Clear();Click(controls.SourceButton);await Task.Delay(50);check("No sessions gives useful guidance",Diagnostics.Descendants(controls.Popover).OfType<TextBlock>().Any(text=>text.Text.StartsWith("No active media sessions")));controls.Close(false);
        foreach(double width in new[]{526d,300d})
        {
            s.PanelWidth=width;app.ChangeSettings();island.SelectPage("media",false);await Task.Delay(120);ResetChoices();controls=Inject();await Task.Delay(70);closedHeight=island.PaintedHeight;
            for(int n=0;n<18;n++){int id=n;choices.Add(new("session"+n,"Long media source "+n,()=>current="session"+id));}controls.Refresh(63,false,true);
            foreach(string mode in new[]{"volume","source"})
            {
                controls.Toggle(mode);await Task.Delay(60);island.UpdateLayout();var popup=controls.Popover;var bounds=popup.TransformToAncestor(controls).TransformBounds(new Rect(popup.RenderSize));
                check(mode+" fits below its footer at "+width+" DIP",bounds.Left>=-.5 && bounds.Top>=-.5 && bounds.Right<=controls.ActualWidth+.5 && bounds.Bottom<=controls.ActualHeight+.5);
                if(mode=="source")check("Long source lists scroll internally at "+width+" DIP",popup.Child is ScrollViewer viewer && viewer.ScrollableHeight>0);
                check(mode+" wheel cannot switch widgets at "+width+" DIP",!island.RoutePanelWheel(-120,false,popup) && !island.RoutePanelWheel(120,true,popup) && island.ActivePage=="media");
                Capture("media-"+mode+"-"+width);controls.Close(false);
            }
        }
        s.ReduceMotion=false;Motion.Configure(s);controls.Toggle("volume");check("Popover has a finite animated entrance",controls.Popover.RenderTransform.HasAnimatedProperties);await Task.Delay(420);check("Entrance settles without a render subscription",!controls.Popover.RenderTransform.HasAnimatedProperties);controls.Close(false);
        s.ReduceMotion=true;Motion.Configure(s);controls.Toggle("source");check("Reduce motion skips entrance animation",!controls.Popover.RenderTransform.HasAnimatedProperties);island.SelectPage("focus",false);check("Changing widgets closes the popover",!controls.IsOpen);
        island.SelectPage("media",false);controls.Toggle("volume");island.Collapse();check("Collapsing releases the popover",!controls.IsOpen);
        s.PanelWidth=526;foreach(var widget in s.Widgets)if(widget.Id=="volume")widget.Enabled=false;
        app.ChangeSettings();island.Reveal(false);island.SelectPage("media",false);await Task.Delay(80);controls=Field<MediaControls>("mediaControls");
        check("Disabling volume hides its footer action",controls.SourceButton.IsVisible && !controls.VolumeButton.IsVisible);
        foreach(var widget in s.Widgets){if(widget.Id=="volume")widget.Enabled=true;if(widget.Id=="media")widget.Enabled=false;}
        app.ChangeSettings();island.SelectPage("media",false);await Task.Delay(80);controls=Field<MediaControls>("mediaControls");Click(controls.VolumeButton);await Task.Delay(60);island.UpdateLayout();
        var volumeBounds=controls.Popover.TransformToAncestor(controls).TransformBounds(new Rect(controls.Popover.RenderSize));
        check("Volume-only mode keeps its popover within the surface",!controls.SourceButton.IsVisible && controls.VolumeButton.IsVisible && volumeBounds.Top>=0 && volumeBounds.Bottom<=controls.ActualHeight+.5);Capture("volume-only");controls.Close(false);
        foreach(var widget in s.Widgets)if(widget.Id=="media")widget.Enabled=true;s.CompactLayout=false;app.ChangeSettings();island.Reveal(false);await Task.Delay(80);
        check("Classic layout retains its inline volume slider",Field<Slider>("volume").IsVisible && Field<MediaControls?>("mediaControls")==null);
        check("Fixture leaves voice and screen sharing off",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Actual WPF renders with original artwork/title, injected Spotify/Chrome enumeration and a sample volume slider for screenshots. Actual Windows slider identity checked without modifying its value. No playback, volume/mute/device write, microphone, capture, API key or provider request.");
    }
}
