using System;
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
internal static class MinimalWidgetsDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,flags)!.GetValue(island)!;
        var s=app.Settings;s.GlassStyle=false;s.Background="#000000";app.Assistant.Options.Enabled=true;app.Assistant.SaveOptions();s.CompactLayout=true;s.CategoryStyle="Dots";s.LaunchStyle="Icons";s.PanelWidth=526;s.PanelMaxHeight=500;s.PanelPadding=34;s.PanelScale=1;s.PanelAutoHeight=true;s.HideMaximized=s.HideFullscreen=s.AlwaysTucked=false;s.SmartActivities=false;s.ReduceMotion=true;s.LiveActivity=true;s.AmbientScope="All widgets";s.AmbientMotion="Still";s.AmbientAudioResponse="Off";s.AmbientStrength=.18;s.CountdownSound="Off";s.NotificationDelivery="Off";s.ClipboardHistory=true;
        foreach(var w in s.Widgets)w.Enabled=w.Id!="stopwatch";
        s.Shortcuts=new(){new(){Name="Explorer",Target="explorer.exe"},new(){Name="Settings",Target="ms-settings:"},new(){Name="Notepad",Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"notepad.exe")}};
        app.ChangeSettings();app.Media.DiagnosticPlayback(true);app.Audio.DiagnosticPeak=()=>.3f;
        BitmapSource Artwork(int variant,bool portrait=false)
        {
            int width=portrait?240:400,height=portrait?320:240;var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb((byte)(42+variant*13),87,137),Color.FromRgb(176,195,210),90),null,new Rect(0,0,width,height));
                dc.DrawEllipse(UI.Brush("#E5CCAA"),null,new Point(width*.72,height*.25),height*.13,height*.13);
                var terrain=new StreamGeometry();using(var path=terrain.Open()){path.BeginFigure(new Point(0,height),true,true);path.LineTo(new(width*.26,height*.44),true,false);path.LineTo(new(width*.6,height*.78),true,false);path.LineTo(new(width*.8,height*.49),true,false);path.LineTo(new(width,height*.7),true,false);path.LineTo(new(width,height),true,false);}terrain.Freeze();dc.DrawGeometry(UI.Brush("#334B61"),null,terrain);
                if(portrait){dc.DrawRoundedRectangle(UI.Brush("#EDE6D6"),null,new Rect(24,24,width-48,height-48),3,3);var title=new FormattedText("Design\nnotes",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),24,UI.Brush("#30353D"),1);dc.DrawText(title,new Point(44,48));for(int i=0;i<7;i++)dc.DrawRectangle(UI.Brush("#B2AEA3"),null,new Rect(44,160+i*13,110-(i%3)*17,2));}
            }
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
        }
        byte[] Png(BitmapSource source){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var memory=new MemoryStream();encoder.Save(memory);return memory.ToArray();}
        string first=Path.Combine(directory,"Design notes.png"),second=Path.Combine(directory,"Wallpaper.png");File.WriteAllBytes(first,Png(Artwork(1,true)));File.WriteAllBytes(second,Png(Artwork(2)));
        app.Productivity.AddFiles(new[]{first,second});app.Productivity.AddTask("Review visuals");app.Productivity.AddTask("Polish widget interactions");app.Productivity.AddTask("Prepare release notes");app.Productivity.Tasks[0].Done=true;app.Productivity.Save();
        app.Productivity.IngestImage(Png(Artwork(3)));app.Productivity.IngestImage(Png(Artwork(4)));app.Productivity.IngestText("Ideas for the next release\nRefine the widget interactions.\nKeep the visual language calm.");app.Productivity.IngestText("#8DA8C1");
        var day=DateTime.Today;app.Calendar.Add("Design review",new DateTimeOffset(day.AddHours(14)),new DateTimeOffset(day.AddHours(14.5)),"");app.Calendar.Add("Quiet focus",new DateTimeOffset(day.AddHours(16)),new DateTimeOffset(day.AddHours(17)),"");
        typeof(MediaService).GetProperty(nameof(MediaService.Artwork))!.SetValue(app.Media,Artwork(0));typeof(MediaService).GetProperty(nameof(MediaService.Title))!.SetValue(app.Media,"Evening light");typeof(MediaService).GetProperty(nameof(MediaService.Artist))!.SetValue(app.Media,"Original artwork · local demo");app.PublishUpdate(RuntimeChange.Media);
        void Capture(string name,double scale=1)
        {
            island.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth*scale),(int)Math.Ceiling(island.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(island);
            int width=Math.Min(bitmap.PixelWidth,(int)Math.Ceiling(island.PaintedWidth*scale)),height=Math.Min(bitmap.PixelHeight,(int)Math.Ceiling(island.PaintedHeight*scale));var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap,new Int32Rect((bitmap.PixelWidth-width)/2,0,width,height))));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        island.Reveal(false);
        foreach(string page in new[]{"media","focus","access","tasks","files","clips","calendar"})
        {
            island.SelectPage(page,false);await Task.Delay(page is "files" or "clips"?300:100);island.UpdateLayout();Capture(page);
            var body=Field<StackPanel>("body");var outer=Field<ScrollViewer>("scroll");File.AppendAllText(Path.Combine(directory,"layout.txt"),$"{page}: painted {island.PaintedHeight}, bodyDesired {body.DesiredSize}, bodyActual {body.RenderSize}, scrollExtent {outer.ExtentHeight}, viewport {outer.ViewportHeight}\n");
            check(page+" remains within its horizontal content bounds",Diagnostics.Descendants(body).OfType<Button>().Where(b=>b.IsVisible && b.ActualWidth>0 && b.ContextMenu?.IsOpen!=true).All(b=>{try{var r=b.TransformToAncestor(body).TransformBounds(new Rect(b.RenderSize));return r.Left>=-1 && r.Right<=body.ActualWidth+1;}catch{return false;}}));
            check(page+" keeps a real bottom inset",outer.Margin.Top>=18 && Math.Abs(outer.Height-(island.PaintedHeight-44))<2);
        }
        island.SelectPage("media",false);await Task.Delay(80);check("Album artwork is a larger visual anchor",Field<Image>("artwork").Width>=80);var footer=Field<MediaControls>("mediaControls").VolumeButton;var volumeBounds=footer.TransformToAncestor(island).TransformBounds(new Rect(footer.RenderSize));check("Media removes the empty session-selector gap",island.PaintedHeight-volumeBounds.Bottom is >=18 and <=48);Capture("media-150",1.5);Capture("media-200",2);
        var transport=Field<Button>("play");var wave=Field<ActivityVisual>("musicWave");
        double Center(FrameworkElement e)=>e.TransformToAncestor(island).Transform(new Point(e.ActualWidth/2,0)).X;
        check("Playback and waveform share the same horizontal center",Math.Abs(Center(transport)-Center(wave))<1);
        island.SelectPage("focus",false);await Task.Delay(80);check("Countdowns use two thin progress instruments",Diagnostics.Descendants(island).OfType<ProgressRing>().Count(r=>r.IsVisible)==2);
        var start=Diagnostics.Descendants(island).OfType<Button>().Single(b=>AutomationProperties.GetAutomationId(b)=="timer-primary");Click(start);check("Ring countdown retains state-driven primary action",app.Session.Timer.Running && AutomationProperties.GetItemStatus(start)=="Pause");Click(start);check("The same action pauses the original session",!app.Session.Timer.Running && AutomationProperties.GetItemStatus(start)=="Resume");
        app.Session.Timer.Reset();app.Session.Focus.Reset();app.SaveSession();
        island.SelectPage("tasks",false);await Task.Delay(60);var task=Diagnostics.Descendants(island).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Polish widget interactions");task.IsChecked=true;task.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));check("Task summary reflects saved completion without rebuilding rows",app.Productivity.Tasks.Count(t=>t.Done)==2 && Diagnostics.Descendants(island).OfType<TextBlock>().Any(t=>t.Text=="1 open"));
        var draft=Diagnostics.Descendants(island).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="New task");draft.Text="A draft stays here";draft.Select(2,5);app.PublishUpdate(RuntimeChange.Clock|RuntimeChange.Media);check("Task draft and selection survive unrelated updates",draft.Text=="A draft stays here" && draft.SelectionStart==2 && draft.SelectionLength==5);
        island.SelectPage("files",false);await Task.Delay(150);var more=Diagnostics.Descendants(island).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="File shelf actions");Click(more);check("Shelf secondary actions remain keyboard-accessible",more.ContextMenu?.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,"Select all"))==true);check("Open tool menus retain ownership of the island",MinimalWidgets.MenuOpen(island));more.ContextMenu!.IsOpen=false;check("Closing tool menus releases their registry",!MinimalWidgets.MenuOpen(island));
        var selected=Diagnostics.Descendants(island).OfType<CheckBox>().First(c=>AutomationProperties.GetName(c).StartsWith("Select "));selected.IsChecked=true;check("Preview selection keeps the original file reference",Field<System.Collections.Generic.HashSet<string>>("shelfSelection").Count==1 && File.Exists(first));
        island.SelectPage("clips",false);await Task.Delay(100);check("Clipboard uses a two-column visual gallery",Field<ScrollViewer>("clipboardScroll").Content is TileFlow {Columns:2});
        var clipMenu=Diagnostics.Descendants(island).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Clipboard actions");Click(clipMenu);check("Clipboard clear and disable actions remain available",clipMenu.ContextMenu?.Items.OfType<MenuItem>().Select(i=>i.Header).SequenceEqual(new object[]{"Clear history","Turn off"})==true);clipMenu.ContextMenu!.IsOpen=false;
        var imageItem=Diagnostics.Descendants(island).OfType<Button>().First(b=>AutomationProperties.GetName(b)=="View Image");Click(imageItem);await Task.Delay(100);check("Clipboard originals and pin/remove actions remain in details",Diagnostics.Descendants(island).OfType<Button>().Any(b=>Equals(b.Content,"Save as PNG…")) && Diagnostics.Descendants(island).OfType<Button>().Any(b=>Equals(b.Content,"Pin")));
        for(int i=0;i<12;i++)app.Productivity.IngestImage(Png(Artwork(i+5)));await Task.Delay(180);var thumbDriver=Field<ViewportThumbnails>("clipboardThumbnails");check("Large clipboard textures are limited to the nearby viewport",thumbDriver.ActiveCount<app.Productivity.Clips.Count(c=>c.Kind=="image"));island.SelectPage("notes",false);await Task.Delay(80);check("Leaving the gallery releases its image textures",thumbDriver.ActiveCount==0);
        foreach(double width in new[]{300d,526d})
        {
            s.PanelWidth=width;s.PanelMaxHeight=417;s.PanelPadding=width<400?24:34;app.ChangeSettings();
            foreach(string page in new[]{"media","focus","access","tasks","files","clips","calendar"})
            {
                island.SelectPage(page,false);await Task.Delay(60);island.UpdateLayout();var body=Field<StackPanel>("body");
                check(page+" fits "+width+" DIP",body.ActualWidth<=island.PaintedWidth-40 && Diagnostics.Descendants(body).OfType<TextBox>().Where(t=>t.IsVisible).All(t=>{var bounds=t.TransformToAncestor(body).TransformBounds(new Rect(t.RenderSize));return bounds.Left>=-1 && bounds.Right<=body.ActualWidth+1;}));
                if(width==300 && page=="clips")check("Narrow clipboard becomes one readable column",Field<ScrollViewer>("clipboardScroll").Content is TileFlow {Columns:1});
                Capture(page+"-"+width);
            }
        }
        island.SelectPage("calendar",false);var week=Diagnostics.Descendants(island).OfType<System.Windows.Controls.Primitives.UniformGrid>().Single(g=>AutomationProperties.GetName(g)=="Calendar week");check("Calendar provides seven accessible dates",week.Children.Count==7 && week.Children.OfType<Button>().All(b=>AutomationProperties.GetName(b).Length>10));
        Capture("calendar-150",1.5);Capture("calendar-200",2);
        check("Fixtures do not start microphone or screen sharing",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Actual WPF renders. Original drawn landscape/document images, local fixture files, tasks, events and clipboard items only. No system clipboard copy, real launch command, microphone, screen capture or provider request. 150/200% images are raster exports, not physical monitor DPI checks.");
    }
}
