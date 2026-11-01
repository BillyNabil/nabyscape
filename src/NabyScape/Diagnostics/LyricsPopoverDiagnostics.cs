using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NabyScape;
internal static class LyricsPopoverDiagnostics
{
    internal static async Task RunLanguageLive(App app,string directory)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var match=await app.Lyrics.FindAutomatic("Nanairo Symphony - TV Size","COALAMODE.",92,System.Threading.CancellationToken.None);
        File.WriteAllText(Path.Combine(directory,"language-live.json"),System.Text.Json.JsonSerializer.Serialize(new{Matched=match!=null,Source=match?.Source,Language=match==null?"und":LyricsLanguage.Detect(match.Value.Choice.Lrc),ElapsedMs=watch.ElapsedMilliseconds}));
    }
    internal static async Task RunAutomaticLive(App app,string directory)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var match=await app.Lyrics.FindAutomatic("Rick Astley - Never Gonna Give You Up [Official Video]","Rick Astley - Topic",214,System.Threading.CancellationToken.None);
        File.WriteAllText(Path.Combine(directory,"automatic-live.json"),System.Text.Json.JsonSerializer.Serialize(new{Matched=match!=null,Source=match?.Source,ElapsedMs=watch.ElapsedMilliseconds,Lines=match==null?0:LyricTiming.Parse(match.Value.Choice.Lrc).Length}));
    }
    internal static async Task RunLive(App app,string directory)
    {
        var results=new System.Collections.Generic.List<object>();
        foreach(string source in LyricsProviders.Names.Where(s=>!LyricsProviders.NeedsKey(s) && !LyricsProviders.NeedsVideo(s)))
        {
            using var deadline=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(22));
            try
            {
                var choices=await app.Lyrics.FetchProvider(source,"Never Gonna Give You Up","Rick Astley",214,deadline.Token);
                results.Add(new{Source=source,Status="Responded",Candidates=choices.Length,Error=""});
            }
            catch(Exception ex){results.Add(new{Source=source,Status="Unavailable",Candidates=0,Error=ex.GetType().Name+": "+ex.Message});}
            File.WriteAllText(Path.Combine(directory,"live-provider-results.json"),System.Text.Json.JsonSerializer.Serialize(results,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }
    }
    internal static async Task Run(App app,string directory,Action<string,bool> check)
    {
        var view=new LyricsView(app){Background=UI.Brush("#151618")};view.SetFixture(new LyricResult(Array.Empty<LyricLine>(),"No synced lyrics for this track."),0,180);
        var host=new Window{Width=300,Height=250,Content=view,Background=UI.Brush("#151618"),ShowActivated=false};host.Show();await Task.Delay(100);
        void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout();if(element.ActualWidth<1 || element.ActualHeight<1)return;
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
        try
        {
            var trigger=Diagnostics.Descendants(view).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Choose lyrics provider");
            check("Lyrics footer has one compact disclosure instead of three competing actions",Diagnostics.Descendants(view).OfType<Button>().Count(b=>b.IsVisible)==1 && trigger.ActualHeight>=28);
            var credit=(TextBlock)typeof(LyricsView).GetField("credit",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
            check("Lyrics status stays readable and inside the panel",credit.FontSize>=11 && credit.TransformToAncestor(view).Transform(new Point()).Y+credit.ActualHeight<=view.ActualHeight+1);
            Capture(view,"lyrics-empty");trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
            var control=Diagnostics.Descendants(view).OfType<LyricsSourcesControl>().Single();var popup=(Popup)typeof(LyricsSourcesControl).GetField("popup",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(control)!;
            var border=(Border)popup.Child;var viewport=(ScrollViewer)border.Child;
            var names=Diagnostics.Descendants(border).OfType<Button>().Select(b=>AutomationProperties.GetName(b)).ToArray();
            check("Lyrics popover contains all fourteen requested provider choices",LyricsProviders.Names.All(names.Contains));
            check("Automatic lyrics is available alongside all manual sources",names.Contains("Automatic"));
            check("Lyrics writing system is available directly in the compact menu",names.Contains("Lyric text"));
            check("Lyrics popover preserves Retry and Local LRC",names.Contains("Retry lyrics") && names.Contains("Local LRC…"));
            check("Lyrics popover escapes the clipped panel and remains bounded",popup.IsOpen && border.ActualWidth<=280 && border.ActualHeight<=350 && viewport.ScrollableHeight>0);
            check("Lyrics popover holds the dock interaction open",LyricsSourcesControl.MenuOpen);
            Capture(border,"lyrics-provider-top");viewport.ScrollToEnd();await Task.Delay(80);Capture(border,"lyrics-provider-bottom");
            check("Provider list scrolls to its final actions",viewport.VerticalOffset>0);
            Diagnostics.Descendants(border).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Lyric text").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);viewport.ScrollToTop();await Task.Delay(30);
            var modes=Diagnostics.Descendants(border).OfType<Button>().Select(b=>AutomationProperties.GetName(b)).ToArray();
            check("Original and Japanese Romaji have explicit selectable labels",modes.Contains("Original") && modes.Contains("Japanese Romaji"));Capture(border,"lyrics-text-modes");
            popup.IsOpen=false;await Task.Delay(50);check("Closing lyrics menu releases the dock interaction guard",!LyricsSourcesControl.MenuOpen);
        }
        finally{host.Close();}
    }
}
