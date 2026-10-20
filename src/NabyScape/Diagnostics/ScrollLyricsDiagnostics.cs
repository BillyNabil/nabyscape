using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class ScrollLyricsDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,flags)!.GetValue(island)!;
        void Call(string name,params object[] args)=>typeof(OverlayWindow).GetMethod(name,flags)!.Invoke(island,args);
        void Wheel(UIElement element,int delta){element.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=Mouse.PreviewMouseWheelEvent});}
        void Capture(string name,double dpi=1)
        {
            island.UpdateLayout();var full=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth*dpi),(int)Math.Ceiling(island.ActualHeight*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);full.Render(island);
            int width=Math.Min(full.PixelWidth,(int)Math.Ceiling(island.PaintedWidth*dpi)),height=Math.Min(full.PixelHeight,(int)Math.Ceiling(island.PaintedHeight*dpi));var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(new CroppedBitmap(full,new Int32Rect(Math.Max(0,(full.PixelWidth-width)/2),0,width,height))));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        var s=app.Settings;s.CompactLayout=true;s.CategoryStyle="Dots";s.Preset="Mac";s.Top=0;s.PanelWidth=526;s.PanelMaxHeight=417;s.PanelScale=1.04;s.PanelPadding=26;s.ReduceMotion=true;s.LiveActivity=true;s.PanelSwipe=true;s.ClipboardHistory=true;s.HideFullscreen=s.HideMaximized=s.AlwaysTucked=false;s.SmartActivities=false;
        app.ChangeSettings();app.Media.DiagnosticPlayback(true);app.Audio.DiagnosticPeak=()=>.2f;
        for(int i=0;i<30;i++)app.Productivity.Clips.Add(new ClipItem{Text="Clipboard sample "+(i+1)+" · a local fixture",Timestamp=DateTimeOffset.UtcNow.AddMinutes(-i)});app.Productivity.Save();
        island.ShowTools("clips",false);await Task.Delay(200);island.UpdateLayout();var inner=Field<ScrollViewer>("clipboardScroll");var outer=Field<ScrollViewer>("scroll");var row=((StackPanel)inner.Content).Children.OfType<Button>().First();var pause=Diagnostics.Descendants(island).OfType<CheckBox>().Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)=="Pause capture");
        double headerY=pause.TransformToAncestor(island).Transform(new Point()).Y;
        check("Clipboard list has its own bounded viewport while the panel fits",inner.ScrollableHeight>100 && outer.ScrollableHeight<1);
        Capture("clipboard-before");Wheel(row,-120);await Task.Delay(100);island.UpdateLayout();
        check("Wheel inside clipboard scrolls only the history",inner.VerticalOffset>0 && outer.VerticalOffset==0 && island.ActivePage=="clips" && Math.Abs(headerY-pause.TransformToAncestor(island).Transform(new Point()).Y)<.5);
        Capture("clipboard-scrolled");double singleWheel=inner.VerticalOffset;inner.ScrollToTop();await Task.Delay(80);for(int i=0;i<4;i++)Wheel(row,-30);await Task.Delay(80);check("Fast trackpad deltas accumulate within a single layout frame",Math.Abs(inner.VerticalOffset-singleWheel)<1);
        inner.ScrollToBottom();await Task.Delay(80);Field<PeekWheelAccumulator>("panelWheel").Reset();Wheel(row,-120);await Task.Delay(80);
        check("Clipboard bottom consumes vertical wheel without moving the panel or changing page",Math.Abs(inner.VerticalOffset-inner.ScrollableHeight)<1 && outer.VerticalOffset==0 && island.ActivePage=="clips");
        Field<PeekWheelAccumulator>("panelWheel").Reset();var tabs=Field<System.Collections.Generic.Dictionary<string,Button>>("compactTabs");Wheel(tabs["clips"],-120);await Task.Delay(80);
        check("Wheel on panel navigation changes slide even with a long clipboard history",island.ActivePage!="clips");
        island.ShowTools("clips",false);await Task.Delay(150);inner=Field<ScrollViewer>("clipboardScroll");check("Returning to clipboard retains its own list position",inner.VerticalOffset>0 && outer.VerticalOffset==0);
        Field<PeekWheelAccumulator>("panelWheel").Reset();row=((StackPanel)inner.Content).Children.OfType<Button>().First();check("Horizontal gesture can still leave the clipboard list",island.RoutePanelWheel(120,true,row) && island.ActivePage!="clips");
        island.ShowTools("notes",false);await Task.Delay(60);var note=Diagnostics.Descendants(island).OfType<TextBox>().Single(text=>System.Windows.Automation.AutomationProperties.GetName(text)=="Quick notes");
        check("Notes keep their editor wheel instead of switching slides",!island.RoutePanelWheel(-120,false,note) && island.ActivePage=="notes");island.ShowTools("clips",false);await Task.Delay(80);
        foreach(double width in new[]{300d,526d})
        {
            s.PanelWidth=width;s.PanelMaxHeight=300;app.ChangeSettings();await Task.Delay(100);island.UpdateLayout();inner=Field<ScrollViewer>("clipboardScroll");
            check("Clipboard stays within its viewport at "+width+" DIP and short height",inner.ViewportHeight>30 && inner.ScrollableHeight>0 && outer.ScrollableHeight<1);Capture("clipboard-"+width+"-short");
        }
        s.PanelWidth=526;s.PanelMaxHeight=480;app.ChangeSettings();island.SelectPage("media",false);await Task.Delay(100);
        var lyrics=Field<LyricsView>("mediaLyrics");var result=new LyricResult(new[]{new LyricLine(0,"A little closer to the music"),new LyricLine(4,"One line, right on time"),new LyricLine(8,"Let the next moment arrive")},"Synced lyrics · original sample");
        lyrics.SetFixture(result,5.6,12);lyrics.Visibility=Visibility.Visible;Call("ResizeToolPanel");await Task.Delay(150);island.UpdateLayout();
        var layout=Field<Grid>("mediaLyricsLayout");var aside=Field<Border>("lyricsAside");var controls=(FrameworkElement)layout.Children[0];
        Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(island).TransformBounds(new Rect(element.RenderSize));
        check("Visible lyrics use a dedicated column to the right of music controls",Grid.GetColumn(aside)==1 && Bounds(lyrics).Left>Bounds(controls).Right && island.PaintedWidth>800 && s.PanelWidth==526);
        check("Lyrics column and player fit the expanded contour",outer.ScrollableHeight<1 && Bounds(lyrics).Right<island.ActualWidth/2+island.PaintedWidth/2);
        Capture("lyrics-right");Capture("lyrics-right-150",1.5);Capture("lyrics-right-200",2);
        lyrics.Visibility=Visibility.Collapsed;Call("ResizeToolPanel");await Task.Delay(80);check("Hiding lyrics restores the original panel width",Math.Abs(island.PaintedWidth-526)<1);
        s.PanelWidth=300;s.PanelScale=1.25;s.PanelMaxHeight=650;app.ChangeSettings();await Task.Delay(60);lyrics.Visibility=Visibility.Visible;Call("ResizeToolPanel");await Task.Delay(100);island.UpdateLayout();
        check("Small lyric layouts remain readable with a stacked fallback",island.PaintedWidth>=300 && (Grid.GetColumn(aside)==1?Bounds(controls).Width>=240:Grid.GetRow(aside)==1));Capture("lyrics-compact");
        check("Fixture checks never start microphone or screen sharing",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Synthetic clipboard and original lyric text. WPF PreviewMouseWheel routed-event checks and offscreen captures at 100/150/200% raster scale. No system clipboard read/write, provider request, microphone, screen capture or playback transport command. Physical mixed-DPI monitors and GPU presentation were not measured.");
    }
}
