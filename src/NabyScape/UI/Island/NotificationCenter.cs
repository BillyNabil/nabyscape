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
    private Grid? centerFrame;
    private HashSet<NoticeEntry> centerUnseen=new();
    private readonly List<WeakReference<FrameworkElement>> bellBadges=new();
    internal bool NotificationCenterOpen=>centerShowing && noticeShowing;

    internal void OpenNotificationCenter()
    {
        if(userHidden)SetVisibility(true);
        if(expanded)Collapse();
        if(fileDragActive || dockEditing)return;
        if(noticeCard!=null)visuals.Children.Remove(noticeCard);
        StopHoverTimers();SetTucked(false);peeking=true;noticeShowing=true;centerShowing=true;RefreshNoticeEdge();peekPanel.Visibility=Visibility.Collapsed;SyncAssistantVisibility();
        centerUnseen=app.Notifications.History.Where(e=>!e.Read).ToHashSet();
        // Header and actions stay put; only the list scrolls.
        var content=new Grid();for(int row=0;row<4;row++)content.RowDefinitions.Add(new(){Height=row==2?new GridLength(1,GridUnitType.Star):GridLength.Auto});
        var header=new DockPanel{Margin=new(0,0,0,4)};
        centerClear=UI.CaptionButton("Clear all",()=>app.Notifications.ClearHistory());centerClear.FontSize=11;centerClear.MinHeight=28;centerClear.Padding=new(10,3,10,3);centerClear.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(centerClear,Dock.Right);header.Children.Add(centerClear);
        var badge=new Border{Width=30,Height=30,CornerRadius=new(15),Background=UI.Brush("#19FFFFFF"),Margin=new(0,0,10,0),Child=Icons.Create("bell",15)};header.Children.Add(badge);
        var heading=UI.Heading(Localization.Text("Notifications"),17);heading.FontWeight=FontWeights.SemiBold;heading.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(heading);
        content.Children.Add(header);
        centerSummary=UI.Text("",11,UI.Muted);centerSummary.Margin=new(40,0,0,12);Grid.SetRow(centerSummary,1);content.Children.Add(centerSummary);
        centerList=new StackPanel();
        var footer=new WrapPanel{Margin=new(0,8,0,0)};
        foreach(var action in new[]{UI.CaptionButton("Send test",()=>{Collapse();app.SendTestNotification();}),UI.CaptionButton("Notification settings",()=>{Collapse();app.ShowSettings("Notifications");}),UI.CaptionButton("Close",Collapse)})
        {action.FontSize=11;action.MinHeight=30;action.Padding=new(10,4,10,4);action.Margin=new(0,0,6,0);footer.Children.Add(action);}
        Grid.SetRow(footer,3);content.Children.Add(footer);
        var screen=Native.ChooseScreen(app.Settings);double scale=Native.Scale(screen);
        // Panel width and Maximum panel height from Position & size; the host window is sized to them.
        Dimensions();if(Width<WindowHostWidth)Width=WindowHostWidth;if(Height<HostHeight)Height=HostHeight;
        double width=Math.Clamp(app.Settings.PanelWidth,300,Math.Max(300,WindowHostWidth));
        centerViewport=new ScrollViewer{Content=centerList,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        UI.LocalizedName(centerViewport,"Notification center");Grid.SetRow(centerViewport,2);content.Children.Add(centerViewport);
        content.MaxHeight=Math.Max(160,HostHeight-40);centerFrame=content;
        noticeCard=new(){Child=content,Width=width-(surface.Notch?64:32),Margin=new(0,16,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
        visuals.Children.Add(noticeCard);compact.IsHitTestVisible=false;compact.Opacity=0;Show();Dimensions();
        FillNotificationCenter();
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,width,app.Settings.AnimationMs);
        ResizeNotificationCenter();
        // Text wrapping settles after the first layout pass; size again once it has.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>{if(NotificationCenterOpen)ResizeNotificationCenter();}));
        ControlFeedback.Notice(noticeCard,app.Settings.NotificationAnimation);
        // Opening from the tray or a shortcut: take focus so Esc and clicking elsewhere close it.
        if(!DiagnosticRun)Activate();
        app.Notifications.MarkAllRead();
    }
    private void ResizeNotificationCenter()
    {
        if(centerFrame==null || noticeCard==null)return;
        centerFrame.InvalidateMeasure();centerFrame.Measure(new Size(noticeCard.Width,double.PositiveInfinity));
        motion.Go(surface,IslandSurface.SurfaceHeightProperty,Math.Min(centerFrame.MaxHeight,centerFrame.DesiredSize.Height)+36,app.Settings.AnimationMs);
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
    // One alert: sender and time on top, title, two lines of text. The whole row opens the
    // sender (or its folder / update page); the remove button appears on hover.
    private Border NotificationRow(NoticeEntry entry,bool fresh,DateTimeOffset now)
    {
        var notice=entry.Notice;
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var icon=NoticeAvatar.Create(notice,38);icon.Margin=new(0,0,12,0);
        grid.Children.Add(icon);
        var text=new StackPanel();Grid.SetColumn(text,1);
        var top=new DockPanel();
        var when=UI.Text(NotificationCenterText.Ago(now,entry.At),10,UI.Muted);when.Opacity=.85;when.VerticalAlignment=VerticalAlignment.Center;when.Margin=new(8,0,0,0);DockPanel.SetDock(when,Dock.Right);top.Children.Add(when);
        if(fresh){var dot=new Border{Width=6,Height=6,CornerRadius=new(3),Background=UI.Brush(app.Settings.Accent),Margin=new(0,0,6,0),VerticalAlignment=VerticalAlignment.Center};top.Children.Add(dot);}
        var sender=UI.Text(NotificationCenterText.Kind(notice),11,UI.Muted);sender.FontWeight=FontWeights.SemiBold;sender.TextWrapping=TextWrapping.NoWrap;sender.TextTrimming=TextTrimming.CharacterEllipsis;sender.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(sender);
        text.Children.Add(top);
        var title=UI.Text(notice.Title,13);title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.Margin=new(0,3,0,0);text.Children.Add(title);
        if(notice.Detail.Length>0){var detail=UI.Text(notice.Detail,12,UI.Muted);detail.MaxHeight=34;detail.LineHeight=17;detail.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;detail.TextTrimming=TextTrimming.CharacterEllipsis;detail.Margin=new(0,2,0,0);detail.ToolTip=notice.Detail;text.Children.Add(detail);}
        grid.Children.Add(text);
        var remove=Icons.Button("x","Remove notification",()=>app.Notifications.Remove(entry),false,24);remove.Background=Brushes.Transparent;remove.Margin=new(6,-2,-2,0);remove.VerticalAlignment=VerticalAlignment.Top;remove.Opacity=0;Grid.SetColumn(remove,2);grid.Children.Add(remove);
        string[] files=(notice.Files??Array.Empty<string>()).Where(File.Exists).ToArray();
        Action? open=null;string hint="";
        if(notice.Kind=="external" && !string.IsNullOrEmpty(notice.AppId)){open=()=>{Collapse();ExternalAppLauncher.Open(notice.AppId);};hint="Click to open the app";}
        else if(files.Length>0){open=()=>{try{var info=new System.Diagnostics.ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add("/select,");info.ArgumentList.Add(files[0]);System.Diagnostics.Process.Start(info);}catch(Exception ex){app.Store.Log(ex);}};hint="Click to show in folder";}
        else if(notice.Kind=="updates"){open=()=>{Collapse();app.ShowSettings("Application");};hint="Click to open updates";}
        var idle=UI.Brush(fresh?"#17FFFFFF":"#0DFFFFFF");var hover=UI.Brush("#24FFFFFF");
        var row=new Border{Child=grid,Background=idle,CornerRadius=new(14),Padding=new(12,11,8,11),Margin=new(0,0,0,6)};
        row.MouseEnter+=(_,_)=>{row.Background=hover;remove.Opacity=.8;};row.MouseLeave+=(_,_)=>{row.Background=idle;remove.Opacity=0;};
        remove.GotKeyboardFocus+=(_,_)=>remove.Opacity=.8;remove.LostKeyboardFocus+=(_,_)=>remove.Opacity=0;
        if(open!=null)
        {
            row.Cursor=System.Windows.Input.Cursors.Hand;row.ToolTip=Localization.Text(hint);
            row.MouseLeftButtonUp+=(_,e)=>{if(e.OriginalSource is DependencyObject source && IsInside(source,remove))return;e.Handled=true;open();};
            // Keyboard: the row's action is also reachable as a button for screen readers.
            var action=UI.CaptionButton(notice.Kind=="external"?"Open app":files.Length>0?"Show in folder":"Open updates",open);action.Width=1;action.Height=1;action.MinHeight=1;action.Padding=new(0);action.Margin=new(0);action.Opacity=0;action.HorizontalAlignment=HorizontalAlignment.Left;action.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(action,1);grid.Children.Add(action);
        }
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
