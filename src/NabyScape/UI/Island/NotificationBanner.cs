using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NabyScape;

// A new alert grows out of the closed dock as a compact banner (app, title, one line of text),
// then folds back on its own. Hovering keeps it; clicking opens the sender or the full card.
public sealed partial class OverlayWindow
{
    private readonly DispatcherTimer bannerTimer=new(){Interval=TimeSpan.FromSeconds(6)};
    private bool bannerShowing,bannerTimerReady;
    private Border? bannerProgress;
    internal bool NoticeBannerOpen=>bannerShowing && noticeShowing;
    internal static double BannerSeconds(Settings s)=>Math.Clamp(s.NotificationSeconds,4,12);
    private bool BannerBlocked=>userHidden || expanded || peeking || fileDragActive || dockEditing || PeekInteractionActive || FeedbackVisible || assistantOpen || commandOpen;
    private void QueueNoticeBanner()
    {
        if(DiagnosticRun && !DiagnosticBanners)return;
        if(app.Notifications.Current==null || app.Notifications.CurrentDisplayed || BannerBlocked)return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background,ShowNoticeBanner);
    }
    internal bool DiagnosticBanners { get; set; }
    internal void ShowNoticeBanner()
    {
        var notice=app.Notifications.Current;
        if(notice==null || app.Notifications.CurrentDisplayed || BannerBlocked)return;
        if(!bannerTimerReady){bannerTimerReady=true;bannerTimer.Tick+=(_,_)=>BannerTimeout();}
        if(noticeCard!=null)visuals.Children.Remove(noticeCard);
        app.Notifications.MarkDisplayed();
        StopHoverTimers();SetTucked(false);peeking=true;noticeShowing=true;bannerShowing=true;peekPanel.Visibility=Visibility.Collapsed;SyncAssistantVisibility();RefreshNoticeEdge();
        bool hideText=notice.Kind=="external" && !app.Settings.ExternalNotificationText;
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var glyph=NoticeAvatar.Create(notice,40);glyph.Margin=new(0,0,12,0);glyph.VerticalAlignment=VerticalAlignment.Center;
        grid.Children.Add(glyph);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(text,1);
        var source=UI.Text(NotificationCenterText.Kind(notice)+"  ·  "+Localization.Text("now"),11,UI.Muted);source.TextWrapping=TextWrapping.NoWrap;source.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(source);
        var title=UI.Text(notice.Title,14);title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.Margin=new(0,2,0,0);text.Children.Add(title);
        string line=hideText?Localization.Text("New notification"):notice.Detail;
        if(line.Length>0){var body=UI.Text(line,12,UI.Muted);body.TextWrapping=TextWrapping.NoWrap;body.TextTrimming=TextTrimming.CharacterEllipsis;body.Margin=new(0,1,0,0);text.Children.Add(body);}
        grid.Children.Add(text);
        var more=Icons.Button("chevron-right","View all notifications",()=>{CloseBanner(false);OpenNotificationCenter();},false,26);more.Background=Brushes.Transparent;more.Margin=new(8,0,0,0);more.Opacity=.6;more.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(more,2);grid.Children.Add(more);
        bannerProgress=new Border{Height=2,CornerRadius=new(1),Background=UI.Brush(IslandPalette.Accent(app.Settings)),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(52,10,0,0),Opacity=.85,RenderTransformOrigin=new(0,.5),RenderTransform=new ScaleTransform(1,1)};
        var content=new StackPanel();content.Children.Add(grid);content.Children.Add(bannerProgress);
        var hit=new Border{Child=content,Background=Brushes.Transparent,Cursor=Cursors.Hand,Padding=new(2,0,2,0)};
        System.Windows.Automation.AutomationProperties.SetName(hit,NotificationCenterText.Kind(notice)+": "+notice.Title);
        hit.ToolTip=Localization.Text(notice.Kind=="external"?"Click to open the app":"Click for details");
        hit.MouseLeftButtonUp+=(_,e)=>{if(e.OriginalSource is DependencyObject source && IsInside(source,more))return;e.Handled=true;ActivateBanner(notice);};
        hit.MouseEnter+=(_,_)=>PauseBanner(true);hit.MouseLeave+=(_,_)=>PauseBanner(false);
        var screen=Native.ChooseScreen(app.Settings);double scale=Native.Scale(screen);
        // Banners use the hover width from Position & size.
        double width=Math.Max(260,PeekSurfaceWidth);
        double inset=surface.Notch?30:18;
        noticeCard=new(){Child=hit,Width=width-inset*2,Margin=new(0,surface.Notch?10:12,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
        visuals.Children.Add(noticeCard);compact.IsHitTestVisible=false;compact.Opacity=0;Show();Dimensions();
        hit.Measure(new Size(noticeCard.Width,double.PositiveInfinity));
        bannerProgress.Width=Math.Max(0,noticeCard.Width-52);
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,width,app.Settings.AnimationMs);
        motion.Go(surface,IslandSurface.SurfaceHeightProperty,hit.DesiredSize.Height+(surface.Notch?24:26),app.Settings.AnimationMs);
        ControlFeedback.Notice(noticeCard,app.Settings.NotificationAnimation);
        bannerTimer.Interval=TimeSpan.FromSeconds(BannerSeconds(app.Settings));StartBannerCountdown();
    }
    private static bool IsInside(DependencyObject source,DependencyObject ancestor)
    {for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node==ancestor)return true;return false;}
    private void StartBannerCountdown()
    {
        bannerTimer.Stop();bannerTimer.Start();
        if(bannerProgress?.RenderTransform is ScaleTransform scale)
        {
            if(Motion.Enabled)scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,0,bannerTimer.Interval));
            else{scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.ScaleX=1;}
        }
    }
    private void PauseBanner(bool hovering)
    {
        if(!NoticeBannerOpen)return;
        if(hovering){bannerTimer.Stop();if(bannerProgress?.RenderTransform is ScaleTransform scale){scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.ScaleX=1;}}
        else StartBannerCountdown();
    }
    private void BannerTimeout()
    {
        bannerTimer.Stop();if(!NoticeBannerOpen)return;
        if(PointerInside()){StartBannerCountdown();return;}
        CloseBanner(true);
    }
    // The alert stays in the notification center; the queue moves on to the next banner.
    private void CloseBanner(bool showNext)
    {
        bannerTimer.Stop();
        if(NoticeBannerOpen){Collapse();app.Notifications.Dismiss();}
        if(showNext)Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{if(!noticeShowing)QueueNoticeBanner();}));
    }
    private void ActivateBanner(InternalNotice notice)
    {
        if(notice.Kind=="external" && ExternalAppLauncher.Open(notice.AppId)){CloseBanner(true);return;}
        bannerTimer.Stop();bannerShowing=false;Collapse();OpenNotice(true);
    }
}
