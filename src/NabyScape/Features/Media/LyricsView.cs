using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NabyScape;

internal sealed class LyricsView : Grid
{
    private readonly App app;
    private readonly TextBlock previous=UI.Text("",11,UI.Muted),current=UI.Text("",18),next=UI.Text("",12,UI.Muted),credit=UI.Text("",9,UI.Muted),highlight=UI.Text("",18);
    private readonly Grid line=new(){MinHeight=42,ClipToBounds=true};
    private readonly RectangleGeometry sweep=new();
    private readonly DispatcherTimer tick=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private readonly Button retry;
    private int index=int.MinValue;
    private LyricResult? last;
    private bool attached;
    private ScrollViewer? viewport;
    private Window? host;
    private string style="";
    private (LyricResult Result,double At,double Duration)? fixture;
    internal void SetFixture(LyricResult result,double seconds,double duration){fixture=(result,seconds,duration);tick.Stop();Paint();}
    internal LyricsView(App app)
    {
        this.app=app;Height=130;Margin=new(0,6,0,12);ClipToBounds=true;
        for(int i=0;i<4;i++)RowDefinitions.Add(new(){Height=i==1?new GridLength(1,GridUnitType.Star):GridLength.Auto});
        previous.TextTrimming=next.TextTrimming=TextTrimming.CharacterEllipsis;previous.TextWrapping=next.TextWrapping=TextWrapping.NoWrap;previous.Opacity=.4;next.Opacity=.6;previous.Margin=new(0,0,0,5);next.Margin=new(0,5,0,8);
        foreach(var text in new[]{current,highlight}){text.FontWeight=FontWeights.SemiBold;text.VerticalAlignment=VerticalAlignment.Center;text.MaxHeight=52;text.TextTrimming=TextTrimming.CharacterEllipsis;}
        highlight.IsHitTestVisible=false;highlight.Clip=sweep;line.Children.Add(current);line.Children.Add(highlight);
        Children.Add(previous);SetRow(line,1);Children.Add(line);SetRow(next,2);Children.Add(next);
        var footer=new DockPanel();retry=UI.Button("Retry lyrics",()=>{app.Lyrics.Retry();Refresh();});retry.Content="Retry";retry.MinHeight=22;retry.FontSize=9;retry.Padding=new(4,0,4,0);retry.Margin=new(6,0,0,0);retry.Background=Brushes.Transparent;DockPanel.SetDock(retry,Dock.Right);footer.Children.Add(retry);
        var local=UI.Button("Load local timed lyrics",()=>{credit.Text=LyricsFiles.Import(app,Window.GetWindow(this));});local.Content="Local LRC…";local.MinHeight=22;local.FontSize=9;local.Padding=new(4,0,4,0);local.Margin=new(6,0,0,0);local.Background=Brushes.Transparent;DockPanel.SetDock(local,Dock.Right);footer.Children.Add(local);footer.Children.Add(credit);SetRow(footer,3);Children.Add(footer);
        Loaded+=(_,_)=>Attach();Unloaded+=(_,_)=>Detach();IsVisibleChanged+=(_,_)=>{if(IsVisible)Attach();else Detach();};
        tick.Tick+=(_,_)=>Paint();
    }
    private void Attach()
    {
        if(attached || !IsLoaded || !IsVisible)return;attached=true;app.Updated+=Updated;app.Lyrics.Changed+=Paint;app.Lyrics.Acquire(this);
        for(var parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))if(parent is ScrollViewer viewer){viewport=viewer;viewport.ScrollChanged+=ViewportChanged;break;}
        host=Window.GetWindow(this);if(host!=null)host.StateChanged+=HostChanged;Refresh();
    }
    private void Detach(){if(!attached)return;attached=false;app.Updated-=Updated;app.Lyrics.Changed-=Paint;if(viewport!=null)viewport.ScrollChanged-=ViewportChanged;if(host!=null)host.StateChanged-=HostChanged;viewport=null;host=null;tick.Stop();if(fixture==null)app.Lyrics.Release(this);}
    private void ViewportChanged(object sender,ScrollChangedEventArgs e)=>Refresh();
    private void HostChanged(object? sender,EventArgs e)=>Refresh();
    private void Updated(){if((app.CurrentUpdate&(RuntimeChange.Media|RuntimeChange.Settings))!=0)Refresh();}
    private void Refresh()
    {
        if(!attached)return;if(!ElementVisibility.InViewport(this)){tick.Stop();if(fixture==null)app.Lyrics.Release(this);return;}
        if(fixture!=null){Paint();return;}
        app.Lyrics.Acquire(this);_=app.Lyrics.Load(app.Media.Title,app.Media.Artist,app.Media.Timeline().duration,app.Settings.LyricsEnabled,LyricTiming.CanUse(app.Media));
        Paint();if(app.Media.Playing && app.Settings.LyricsEnabled && LyricTiming.CanUse(app.Media))tick.Start();else tick.Stop();
    }
    private void Paint()
    {
        if(!attached || !ElementVisibility.InViewport(this)){tick.Stop();return;}var result=fixture?.Result??app.Lyrics.Result;var lines=result.Lines;var position=app.Media.Timeline();double at=(fixture?.At??position.elapsed)+app.Settings.LyricsOffset;
        int now=LyricTiming.Index(lines,at);bool change=now!=index || !ReferenceEquals(last,result) || style!=app.Settings.LyricsAnimation;index=now;last=result;style=app.Settings.LyricsAnimation;
        credit.Text=result.Status;retry.Visibility=lines.Length==0 && !result.Status.StartsWith("Finding")?Visibility.Visible:Visibility.Collapsed;
        if(change)
        {
            previous.Text=now>0?lines[now-1].Text:"";current.Text=highlight.Text=now>=0?lines[now].Text:lines.Length>0?"♪":"Lyrics";next.Text=now+1<lines.Length?lines[now+1].Text:"";
            current.Foreground=style=="Karaoke"?UI.Muted:Brushes.White;highlight.Visibility=style=="Karaoke" && now>=0?Visibility.Visible:Visibility.Collapsed;
            if(Motion.Enabled && !app.Settings.ReduceMotion)
            {
                if(style=="Slide"){var move=new TranslateTransform(0,10);line.RenderTransform=move;Motion.To(move,TranslateTransform.YProperty,0,260);Motion.Fade(line,1,200);}
                else if(style=="Focus"){var scale=new ScaleTransform(.97,.97);line.RenderTransformOrigin=new(0,.5);line.RenderTransform=scale;Motion.To(scale,ScaleTransform.ScaleXProperty,1,300);Motion.To(scale,ScaleTransform.ScaleYProperty,1,300);line.Opacity=.5;Motion.Fade(line,1,260);}
                else{line.RenderTransform=Transform.Identity;line.Opacity=1;}
            }
            else{line.RenderTransform=Transform.Identity;line.Opacity=1;}
        }
        if(style=="Karaoke" && now>=0)
        {
            double end=now+1<lines.Length?lines[now+1].At:fixture?.Duration??position.duration;double progress=Math.Clamp((at-lines[now].At)/Math.Max(.1,end-lines[now].At),0,1);
            // LRC supplies line timing. This is a line sweep, not fabricated word timestamps.
            var target=new Rect(0,0,Math.Max(0,line.ActualWidth*(app.Settings.ReduceMotion || !Motion.Enabled?1:progress)),Math.Max(0,line.ActualHeight));var before=sweep.Rect;
            sweep.BeginAnimation(RectangleGeometry.RectProperty,null);sweep.Rect=target;
            if(!change && Motion.Enabled && !app.Settings.ReduceMotion && target.Width>=before.Width && target.Width-before.Width<line.ActualWidth*.2)
            {
                var animation=new RectAnimation(before,target,TimeSpan.FromMilliseconds(100)){FillBehavior=FillBehavior.Stop};Timeline.SetDesiredFrameRate(animation,Motion.FrameLimit);sweep.BeginAnimation(RectangleGeometry.RectProperty,animation);
            }
        }
    }
}

internal static class LyricsFiles
{
    internal static string Import(App app,Window? owner)
    {
        if(!LyricTiming.CanUse(app.Media))return "Select a music session with track details and a playback timeline first.";
        var dialog=new Microsoft.Win32.OpenFileDialog{Title="Timed lyrics for the current track",Filter="Timed lyrics (*.lrc)|*.lrc"};if(dialog.ShowDialog(owner)!=true)return "No file chosen.";
        try
        {
            if(new System.IO.FileInfo(dialog.FileName).Length>512*1024)return "Choose an LRC file under 512 KB.";
            app.Lyrics.Import(app.Media.Title,app.Media.Artist,app.Media.Timeline().duration,System.IO.File.ReadAllText(dialog.FileName));app.Settings.LyricsEnabled=true;app.ChangeSettings();return "Local timed lyrics loaded for this session.";
        }
        catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException or System.Text.RegularExpressions.RegexMatchTimeoutException){return "Could not load this timed lyric file.";}
    }
}

public sealed partial class OverlayWindow
{
    private LyricsView? mediaLyrics;
    private Button? lyricsButton;
    private void ToggleLyrics(){app.Settings.LyricsEnabled=!app.Settings.LyricsEnabled;app.ChangeSettings();}
    private void SyncLyrics()
    {
        bool compatible=LyricTiming.CanUse(app.Media);
        bool changed=false;
        if(mediaLyrics!=null){var visible=app.Settings.LyricsEnabled && compatible?Visibility.Visible:Visibility.Collapsed;changed=mediaLyrics.Visibility!=visible;mediaLyrics.Visibility=visible;}
        if(lyricsButton!=null){lyricsButton.Visibility=compatible?Visibility.Visible:Visibility.Collapsed;lyricsButton.Background=app.Settings.LyricsEnabled?UI.Brush(app.Settings.SelectionFill):Brushes.Transparent;lyricsButton.ToolTip=app.Settings.LyricsEnabled?"Hide lyrics":"Show synced lyrics · looks up track details with LRCLIB";System.Windows.Automation.AutomationProperties.SetItemStatus(lyricsButton,app.Settings.LyricsEnabled?"On":"Off");}
        if(changed && mediaLyricsLayout?.IsLoaded==true)ResizeToolPanel();
    }
}
