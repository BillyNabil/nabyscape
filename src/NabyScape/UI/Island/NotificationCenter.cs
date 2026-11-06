using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

internal static class NotificationCenterText
{
    internal static string Ago(DateTimeOffset now,DateTimeOffset at)
    {
        var age=now-at;
        if(age<TimeSpan.FromMinutes(1))return Localization.Text("Just now");
        if(age<TimeSpan.FromHours(1))return Localization.Format("{0} min ago",(int)age.TotalMinutes);
        if(age<TimeSpan.FromDays(1))return Localization.Format("{0} h ago",(int)age.TotalHours);
        return at.ToLocalTime().ToString("dd MMM HH:mm",System.Globalization.CultureInfo.CurrentCulture);
    }
    internal static string Kind(InternalNotice notice)=>notice.Kind=="external"?notice.Source??"App":Localization.Text(notice.Kind switch{"timer"=>"Timer","focus"=>"Focus","downloads"=>"Downloads","updates"=>"Update",_=>"NabyScape"});
    internal static string Icon(string kind)=>kind switch{"timer"=>"timer","focus"=>"focus","downloads"=>"folder","updates"=>"settings",_=>"bell"};
}

// The notification center opens as an island popup listing this session's alerts, newest first.
public sealed partial class OverlayWindow
{
    private bool centerShowing;
    private StackPanel? centerList;
    private TextBlock? centerSummary;
    private Button? centerClear;
    private ScrollViewer? centerViewport;
    private HashSet<NoticeEntry> centerUnseen=new();
    private readonly List<WeakReference<FrameworkElement>> bellBadges=new();
    internal bool NotificationCenterOpen=>centerShowing && noticeShowing;

    internal void OpenNotificationCenter()
    {
        if(userHidden)SetVisibility(true);
        if(expanded)Collapse();
        if(fileDragActive || dockEditing)return;
        if(noticeCard!=null)visuals.Children.Remove(noticeCard);
        StopHoverTimers();SetTucked(false);peeking=true;noticeShowing=true;centerShowing=true;peekPanel.Visibility=Visibility.Collapsed;SyncAssistantVisibility();
        centerUnseen=app.Notifications.History.Where(e=>!e.Read).ToHashSet();
        var content=new StackPanel();
        var header=new DockPanel{Margin=new(0,0,0,4)};
        centerClear=UI.CaptionButton("Clear all",()=>app.Notifications.ClearHistory());centerClear.FontSize=11;centerClear.MinHeight=28;centerClear.Padding=new(10,3,10,3);centerClear.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(centerClear,Dock.Right);header.Children.Add(centerClear);
        var badge=new Border{Width=30,Height=30,CornerRadius=new(10),Background=UI.Brush("#19FFFFFF"),Margin=new(0,0,10,0),Child=Icons.Create("bell",15)};header.Children.Add(badge);
        var heading=UI.Heading(Localization.Text("Notifications"),17);heading.FontWeight=FontWeights.SemiBold;heading.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(heading);
        content.Children.Add(header);
        centerSummary=UI.Text("",11,UI.Muted);centerSummary.Margin=new(40,0,0,12);content.Children.Add(centerSummary);
        centerList=new StackPanel();content.Children.Add(centerList);
        var footer=new WrapPanel{Margin=new(0,8,0,0)};
        foreach(var action in new[]{UI.CaptionButton("Notification settings",()=>{Collapse();app.ShowSettings("Notifications");}),UI.CaptionButton("Close",Collapse)})
        {action.FontSize=11;action.MinHeight=30;action.Padding=new(10,4,10,4);action.Margin=new(0,0,6,0);footer.Children.Add(action);}
        content.Children.Add(footer);
        var screen=Native.ChooseScreen(app.Settings);double scale=Native.Scale(screen);
        double width=Math.Min(Math.Clamp(app.Settings.HoverWidth,340,400),Math.Max(220,screen.WorkingArea.Width/scale-24));
        centerViewport=new ScrollViewer{Content=content,MaxHeight=Math.Max(140,Math.Min(440,screen.WorkingArea.Height/scale-48)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        UI.LocalizedName(centerViewport,"Notification center");
        noticeCard=new(){Child=centerViewport,Width=width-(surface.Notch?64:32),Margin=new(0,16,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
        visuals.Children.Add(noticeCard);compact.IsHitTestVisible=false;compact.Opacity=0;Show();Dimensions();
        FillNotificationCenter();
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,width,app.Settings.AnimationMs);
        ResizeNotificationCenter();
        ControlFeedback.Notice(noticeCard,app.Settings.NotificationAnimation);
        // Opening from the tray or a shortcut: take focus so Esc and clicking elsewhere close it.
        if(!DiagnosticRun)Activate();
        app.Notifications.MarkAllRead();
    }
    private void ResizeNotificationCenter()
    {
        if(centerViewport==null || noticeCard==null)return;
        centerViewport.Measure(new Size(noticeCard.Width,double.PositiveInfinity));
        motion.Go(surface,IslandSurface.SurfaceHeightProperty,Math.Min(centerViewport.MaxHeight,centerViewport.DesiredSize.Height)+32,app.Settings.AnimationMs);
    }
    private void RefreshNotificationCenter(){if(!NotificationCenterOpen)return;FillNotificationCenter();ResizeNotificationCenter();}
    private void FillNotificationCenter()
    {
        if(centerList==null)return;
        var history=app.Notifications.History;centerList.Children.Clear();
        int fresh=history.Count(e=>centerUnseen.Contains(e));
        if(centerSummary!=null)centerSummary.Text=history.Count==0?"":fresh>0?Localization.Format(fresh==1?"{0} new notification":"{0} new notifications",fresh):Localization.Text("You're all caught up");
        if(centerClear!=null)centerClear.Visibility=history.Count>0?Visibility.Visible:Visibility.Collapsed;
        if(history.Count==0)
        {
            var empty=new StackPanel{Margin=new(0,18,0,22),HorizontalAlignment=HorizontalAlignment.Center};
            var glyph=Icons.Create("bell",26,UI.Muted);glyph.HorizontalAlignment=HorizontalAlignment.Center;glyph.Margin=new(0,0,0,10);empty.Children.Add(glyph);
            var title=UI.Text(Localization.Text("No notifications yet"),14);title.HorizontalAlignment=HorizontalAlignment.Center;title.TextAlignment=TextAlignment.Center;empty.Children.Add(title);
            var hint=UI.Text(Localization.Text(app.Settings.ExternalNotifications?"Alerts from NabyScape and your other apps will show up here.":"Timer, focus, download and update alerts will show up here."),11,UI.Muted);hint.TextAlignment=TextAlignment.Center;hint.Margin=new(0,5,0,0);empty.Children.Add(hint);
            centerList.Children.Add(empty);return;
        }
        var now=DateTimeOffset.UtcNow;
        foreach(var entry in history)centerList.Children.Add(NotificationRow(entry,centerUnseen.Contains(entry),now));
    }
    private Border NotificationRow(NoticeEntry entry,bool fresh,DateTimeOffset now)
    {
        var notice=entry.Notice;
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(38)});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var icon=new Grid{Width=28,Height=28,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left};
        icon.Children.Add(new Border{CornerRadius=new(9),Background=UI.Brush("#19FFFFFF"),Child=OverlayWindow.NoticeGlyph(notice,13)});
        if(fresh)icon.Children.Add(new Border{Width=8,Height=8,CornerRadius=new(4),Background=UI.Brush(app.Settings.Accent),BorderBrush=UI.Brush("#101114"),BorderThickness=new(1.5),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,-2,-2,0)});
        grid.Children.Add(icon);
        var text=new StackPanel();Grid.SetColumn(text,1);
        var title=UI.Text(notice.Title,13);title.FontWeight=fresh?FontWeights.SemiBold:FontWeights.Normal;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(title);
        if(notice.Detail.Length>0){var detail=UI.Text(notice.Detail,11,UI.Muted);detail.MaxHeight=30;detail.TextTrimming=TextTrimming.CharacterEllipsis;detail.Margin=new(0,2,0,0);detail.ToolTip=notice.Detail;text.Children.Add(detail);}
        var meta=UI.Text(NotificationCenterText.Kind(notice)+" · "+NotificationCenterText.Ago(now,entry.At),10,UI.Muted);meta.Margin=new(0,4,0,0);meta.Opacity=.8;text.Children.Add(meta);
        string[] files=(notice.Files??Array.Empty<string>()).Where(File.Exists).ToArray();
        Button? action=null;
        if(files.Length>0)action=UI.CaptionButton("Show in folder",()=>
        {
            try{var info=new System.Diagnostics.ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add("/select,");info.ArgumentList.Add(files[0]);System.Diagnostics.Process.Start(info);}
            catch(Exception ex){app.Store.Log(ex);}
        });
        else if(notice.Kind=="updates")action=UI.CaptionButton("Open updates",()=>{Collapse();app.ShowSettings("Application");});
        else if(notice.Kind=="external" && !string.IsNullOrEmpty(notice.AppId))action=UI.CaptionButton("Open app",()=>{Collapse();ExternalAppLauncher.Open(notice.AppId);});
        if(action!=null){action.FontSize=11;action.MinHeight=26;action.Padding=new(9,2,9,2);action.Margin=new(0,8,0,0);action.HorizontalAlignment=HorizontalAlignment.Left;text.Children.Add(action);}
        grid.Children.Add(text);
        var remove=Icons.Button("x","Remove notification",()=>app.Notifications.Remove(entry),false,24);remove.Background=Brushes.Transparent;remove.Opacity=.55;remove.Margin=new(6,0,0,0);remove.MouseEnter+=(_,_)=>remove.Opacity=1;remove.MouseLeave+=(_,_)=>remove.Opacity=.55;remove.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(remove,2);grid.Children.Add(remove);
        var row=new Border{Child=grid,Background=UI.Brush(fresh?"#1CFFFFFF":"#0EFFFFFF"),CornerRadius=new(12),Padding=new(10,10,6,10),Margin=new(0,0,0,6)};
        System.Windows.Automation.AutomationProperties.SetName(row,notice.Title);
        return row;
    }
    // A bell with an unread dot, used in panel headers.
    private FrameworkElement NotificationBell(double size)
    {
        var host=new Grid{Width=size,Height=size};
        var bell=Icons.Button("bell","Notifications",OpenNotificationCenter,false,size);bell.Margin=new(0);bell.Background=Brushes.Transparent;host.Children.Add(bell);
        var dot=new Border{Width=7,Height=7,CornerRadius=new(3.5),Background=UI.Brush(app.Settings.Accent),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,3,3,0),IsHitTestVisible=false};
        host.Children.Add(dot);bellBadges.Add(new(dot));UpdateBellBadges();
        return host;
    }
    private void UpdateBellBadges()
    {
        bool unread=app.Notifications.Unread>0;
        bellBadges.RemoveAll(reference=>!reference.TryGetTarget(out _));
        foreach(var reference in bellBadges)if(reference.TryGetTarget(out var dot))dot.Visibility=unread?Visibility.Visible:Visibility.Collapsed;
    }
}
