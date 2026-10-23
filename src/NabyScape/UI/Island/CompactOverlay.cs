using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private bool peeking;
    private string activePage="media";
    private readonly Dictionary<string,FrameworkElement> compactPages=new();
    private readonly Dictionary<string,Button> compactTabs=new();
    private readonly Grid compactPageHost=new(),peekPanel=new(),timelinePanel=new();
    private readonly TextBlock peekTitle=UI.Caption("",13),peekSubtitle=UI.Caption("",11,UI.Muted);
    private readonly Image peekArt=new(){Width=56,Height=56,Stretch=Stretch.UniformToFill};
    private readonly ProgressBar peekProgress=new(){Minimum=0,Maximum=1,Height=3,Background=UI.Brush("#34363D"),Foreground=Brushes.White,BorderThickness=new(0)};
    private readonly TextBlock peekElapsed=UI.Caption("",11,UI.Muted),peekDuration=UI.Caption("",11,UI.Muted);
    private readonly TranslateTransform peekArtSlide=new();
    private readonly System.Windows.Controls.Grid peekCoverContent=new();
    private FrameworkElement peekFallback=null!;
    private readonly ActivityVisual peekWave=new(){Width=58,Height=16,WaveOnly=true};
    private Button? peekPlay;
    private readonly Slider seekSlider=new(){Minimum=0,Maximum=1,TickFrequency=.001,SmallChange=.01,LargeChange=.05,IsMoveToPointEnabled=true,Height=24,Margin=new(0)};
    private readonly TextBlock elapsed=UI.Caption("0:00",10,UI.Muted),duration=UI.Caption("0:00",10,UI.Muted);
    private readonly DispatcherTimer hoverDelay=new(){Interval=TimeSpan.FromMilliseconds(180)},leaveDelay=new(){Interval=TimeSpan.FromMilliseconds(300)},seekDelay=new(){Interval=TimeSpan.FromMilliseconds(140)};
    private double HostHeight
    {
        get
        {
            if(assistantOpen || VoiceRoomOpen)return AssistantView.PanelSize(app.Settings,VoiceRoomOpen).Height;
            if(!app.Settings.CompactLayout)return expandedHeight;
            var screen=Native.ChooseScreen(app.Settings);return Math.Min(app.Settings.PanelMaxHeight,screen.WorkingArea.Height/Native.Scale(screen)-16);
        }
    }
    internal bool IsPeeking=>peeking;
    internal string ActivePage=>activePage;
    private static string PageFor(string widget)=>Settings.ToolNames.ContainsKey(widget)?widget:widget switch{"media" or "volume"=>"media","timer" or "focus"=>"focus","stopwatch"=>"stopwatch","voice"=>"voice","shortcuts"=>"access","tools"=>"tools",_=>"status"};
    private static string PageName(string page)=>Localization.Text(Settings.ToolNames.TryGetValue(page,out var tool)?tool:page switch{"media"=>"Media","focus"=>"Focus","stopwatch"=>"Stopwatch","voice"=>"Naby voice","status"=>"Status","tools"=>"Tools",_=>"Launch"});
    private string? hoverPanelWidget;
    private bool WidgetOn(string id)=>id==hoverPanelWidget || app.Settings.Widgets.Any(w=>w.Enabled && w.Id==id);
    private IEnumerable<WidgetSetting> PanelWidgets=>app.Settings.Widgets.Where(w=>w.Enabled || w.Id==hoverPanelWidget);
    private void OpenHoverPanel(string widget,bool activate=true)
    {
        LeaveAssistantPanel();
        string page=PageFor(widget);
        bool hidden=!app.Settings.Widgets.Any(w=>w.Enabled && w.Id==widget) || !app.Settings.CompactLayout && Settings.ToolNames.ContainsKey(widget);
        bool leavingMixer=classicMixerOpen;classicMixerOpen=false;
        if(leavingMixer || hoverPanelWidget!=(hidden?widget:null))
        {FlushNotes();hoverPanelWidget=hidden?widget:null;activePage=page;BuildBody();}
        Reveal(activate);
        if(app.Settings.CompactLayout)SelectPage(page,false);
        scroll.ScrollToTop();UpdateSystemMonitor();
    }
    private void InitializeCompactInteraction()
    {
        root.MouseEnter+=(_,_)=>{if(DiagnosticRun)return;EnsurePeekDeck();if(IsTucked){RevealTuckedPreview();return;}leaveDelay.Stop();if(peeking){automaticPreview=false;activityDismiss.Stop();}else{previewKind=activityTitle=activityDetail="";}if(!coverGesture.Pressed && !expanded && !peeking && app.Settings.HoverPeek && app.Settings.CompactLayout && !userHidden && !TuckRequested)hoverDelay.Start();};
        root.MouseLeave+=(_,_)=>{if(DiagnosticRun)return;hoverDelay.Stop();if(peeking || TuckRequested)leaveDelay.Start();};
        hoverDelay.Tick+=(_,_)=>{hoverDelay.Stop();if(assistantCharacterButton.IsMouseOver){PreviewAssistant();return;}if(PointerInside() && !expanded && !userHidden && !TuckRequested)PreviewActivity();};
        leaveDelay.Tick+=(_,_)=>{leaveDelay.Stop();if((peeking || TuckRequested) && !AssistantReplyVisible && !PointerInside() && !SatellitePointerInside && !PeekInteractionActive && !fileDragActive)Collapse();};
        peekMedia.HorizontalAlignment=HorizontalAlignment.Center;peekMedia.VerticalAlignment=VerticalAlignment.Top;peekMedia.Width=286;peekMedia.Height=100;peekMedia.Margin=new(0);
        peekMedia.RowDefinitions.Add(new(){Height=new GridLength(56)});peekMedia.RowDefinitions.Add(new(){Height=new GridLength(18)});peekMedia.RowDefinitions.Add(new(){Height=new GridLength(18)});
        peekMedia.ColumnDefinitions.Add(new(){Width=GridLength.Auto});peekMedia.ColumnDefinitions.Add(new());peekMedia.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        peekArt.RenderTransform=peekArtSlide;
        peekCoverContent.Children.Add(WrapArtwork(peekArt));peekFallback=Icons.Create("activity",28);peekCoverContent.Children.Add(peekFallback);
        var cover=new Border{CornerRadius=new(14),Child=peekCoverContent,Background=UI.Brush("#16FFFFFF"),Width=56,Height=56,Margin=new(0,0,16,0)};
        cover.Clip=new RectangleGeometry(new Rect(0,0,56,56),14,14);peekMedia.Children.Add(cover);
        peekTitle.FontSize=15;peekSubtitle.FontSize=12;peekSubtitle.Margin=new(0,6,0,0);
        peekTitle.TextWrapping=TextWrapping.NoWrap;peekTitle.TextTrimming=TextTrimming.CharacterEllipsis;peekTitle.FontWeight=FontWeights.SemiBold;
        peekSubtitle.TextWrapping=TextWrapping.NoWrap;peekSubtitle.TextTrimming=TextTrimming.CharacterEllipsis;
        var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};info.Children.Add(WrapTitle(peekTitle));info.Children.Add(WrapTitle(peekSubtitle));Grid.SetColumn(info,1);peekMedia.Children.Add(info);
        peekPlay=Icons.Button("play","Play or pause from preview",()=>_=app.Media.Command("toggle"),true,38);peekPlay.Margin=new(16,0,0,0);Grid.SetColumn(peekPlay,2);peekMedia.Children.Add(peekPlay);
        Grid.SetRow(peekProgress,1);Grid.SetColumnSpan(peekProgress,3);peekProgress.Margin=new(0,12,0,0);peekMedia.Children.Add(peekProgress);
        var peekTimes=new DockPanel{Margin=new(0,6,0,0)};DockPanel.SetDock(peekDuration,Dock.Right);peekTimes.Children.Add(peekDuration);peekTimes.Children.Add(peekElapsed);Grid.SetRow(peekTimes,2);Grid.SetColumnSpan(peekTimes,3);peekMedia.Children.Add(peekTimes);
        Grid.SetRow(peekWave,2);Grid.SetColumn(peekWave,1);peekWave.HorizontalAlignment=HorizontalAlignment.Center;peekWave.Margin=new(0,6,0,0);peekMedia.Children.Add(peekWave);

        timelinePanel.RowDefinitions.Add(new(){Height=GridLength.Auto});timelinePanel.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var labels=new DockPanel();DockPanel.SetDock(duration,Dock.Right);labels.Children.Add(duration);labels.Children.Add(elapsed);timelinePanel.Children.Add(labels);
        Grid.SetRow(seekSlider,1);timelinePanel.Children.Add(seekSlider);UI.LocalizedName(seekSlider,"Playback position");
        seekSlider.ValueChanged+=(_,_)=>{if(!updating && (seekSlider.IsMouseCaptureWithin || seekSlider.IsKeyboardFocusWithin)){seekDelay.Stop();seekDelay.Start();}};
        seekDelay.Tick+=async(_,_)=>{seekDelay.Stop();await app.Media.Seek(seekSlider.Value);};
    }
    private void StopHoverTimers(){hoverDelay.Stop();leaveDelay.Stop();detailDelay.Stop();assistantHover.Stop();}
    internal void PreviewActivity()
    {
        if(app.Notifications.Current!=null && !expanded && !peeking && !fileDragActive && !userHidden){OpenNotice();return;}
        if(PeekPages().Length==0 || (!app.Settings.CompactLayout && !tuckPreview && !fileDragActive) || (!app.Settings.HoverPeek && !automaticPreview && !tuckPreview && !fileDragActive) || expanded || peeking || userHidden || (TuckRequested && !tuckPreview && !fileDragActive))return;
        peeking=true;UpdateSatellite();Show();Dimensions();ConfigurePerformance();peekDetailed=!app.Settings.TwoStageHover || automaticPreview;detailDelay.Stop();if(!peekDetailed)detailDelay.Start();
        scroll.Visibility=Visibility.Collapsed;compact.IsHitTestVisible=false;Motion.Fade(compact,0,80);
        PreparePeekDeck();
        peekPanel.Visibility=Visibility.Visible;peekPanel.IsHitTestVisible=true;Motion.Fade(peekPanel,1,160);
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,PeekSurfaceWidth,app.Settings.AnimationMs);
        motion.Go(surface,IslandSurface.SurfaceHeightProperty,peekDetailed?PeekExpandedHeight:100,app.Settings.AnimationMs);motion.Go(surface,IslandSurface.GlassLiftProperty,1,app.Settings.AnimationMs);motion.OnComplete(()=>{});UpdatePeek();WakeLiveRendering();
    }
    private void UpdatePeek()
    {
        if(!peeking || noticeShowing || assistantPeeking)return;
        UpdatePeekDeck();
        bool media=app.Media.Available;ApplyMediaTypography(peekTitle,media && app.Settings.BoldMediaTitle);
        peekArt.Visibility=media && app.Media.Artwork!=null?Visibility.Visible:Visibility.Collapsed;SwapArtwork(peekArt,media?app.Media.Artwork:null);
        peekFallback.Visibility=peekArt.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
        var now=DateTimeOffset.Now;
        string caption=media?app.Media.Title:WidgetOn("focus") && app.Session.Focus.Remaining(now)>0?"Focus · "+app.Session.Focus.Display(now):WidgetOn("timer") && app.Session.Timer.Remaining(now)>0?"Timer · "+app.Session.Timer.Display(now):now.ToString("HH:mm");
        if(activityDetail=="Ending soon")caption=(previewKind=="timer"?"Timer · "+app.Session.Timer.Display(now):"Focus · "+app.Session.Focus.Display(now));
        if(media)SwapText(peekTitle,caption);else Motion.Text(peekTitle,caption);
        string detail=media?app.Media.Artist:ActiveCountdown()!=null?"Session running · click for controls":now.ToString("dddd, d MMMM",new System.Globalization.CultureInfo("en-US"));
        if(media)SwapText(peekSubtitle,detail);else Motion.Text(peekSubtitle,detail);
        if(peekPlay!=null){peekPlay.Visibility=media?Visibility.Visible:Visibility.Collapsed;peekPlay.IsEnabled=app.Media.CanToggle;Motion.Icon(peekPlay,app.Media.Playing?"pause":"play",18,Brushes.Black);}
        var position=media?app.Media.Timeline():(ActiveCountdown() is Countdown c?(c.DurationSeconds-c.Remaining(now),c.DurationSeconds):(0d,0d));
        peekProgress.Visibility=position.Item2>0?Visibility.Visible:Visibility.Collapsed;
        peekElapsed.Visibility=peekDuration.Visibility=Visibility.Visible;
        peekProgress.Value=position.Item2>0?Math.Clamp(position.Item1/position.Item2,0,1):0;
        var format=position.Item2>=3600?@"h\:mm\:ss":@"m\:ss";
        peekElapsed.Text=TimeSpan.FromSeconds(Math.Max(0,position.Item1)).ToString(format);peekDuration.Text=TimeSpan.FromSeconds(Math.Max(0,position.Item2)).ToString(format);
        if(position.Item2<=0){peekElapsed.Text=media?"Windows media controls":"No active session";peekDuration.Text=media?"":"Open →";}
        if(media){timelineAt=DateTimeOffset.UtcNow;timelineElapsed=position.Item1;timelineDuration=position.Item2;}
        peekWave.Visibility=peekId=="media" && media && app.Settings.LiveActivity?Visibility.Visible:Visibility.Collapsed;
        UpdateClosedLyricsDemand();ApplyPeekGeometry();
    }
    private void BuildCompactBody()
    {
        if(timelinePanel.Parent is Panel old)old.Children.Remove(timelinePanel);
        title.FontSize=18;artist.FontSize=12;artwork.Width=artwork.Height=88;clock.FontSize=26;
        var navigation=new Grid{Tag="panel-navigation",Margin=new(0,0,0,app.Settings.SectionGap)};
        navigation.ColumnDefinitions.Add(new(){Width=new GridLength(28)});navigation.ColumnDefinitions.Add(new());navigation.ColumnDefinitions.Add(new(){Width=new GridLength(28)});
        navigation.Children.Add(AssistantLauncher());
        var settings=Icons.Button("settings","Settings",app.ShowSettings,false,28);settings.Margin=new(0);settings.Background=Brushes.Transparent;Grid.SetColumn(settings,2);navigation.Children.Add(settings);
        int categoryCount=Math.Max(1,PanelWidgets.Select(w=>PageFor(w.Id)).Distinct().Count());
        var tabRow=new System.Windows.Controls.Primitives.UniformGrid{Columns=categoryCount,Rows=1,Width=categoryCount*18,HorizontalAlignment=HorizontalAlignment.Center};var navScroll=new ScrollViewer{Content=tabRow,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Center};Grid.SetColumn(navScroll,1);KeyboardNavigation.SetDirectionalNavigation(tabRow,KeyboardNavigationMode.Cycle);navScroll.PanningMode=PanningMode.HorizontalOnly;navScroll.ToolTip=Localization.Text("Panel navigation · scroll for more panels");navScroll.PreviewMouseWheel+=(_,e)=>{if(navScroll.ScrollableWidth>0){navScroll.ScrollToHorizontalOffset(navScroll.HorizontalOffset-e.Delta*.5);e.Handled=true;}};navigation.Children.Add(navScroll);body.Children.Add(navigation);body.Children.Add(compactPageHost);
        bodyReflow.Add(()=>
        {
            navigation.Margin=new(0,0,0,app.Settings.SectionGap);tabRow.Width=categoryCount*18;
        });
        foreach(string page in PanelWidgets.Select(w=>PageFor(w.Id)).Distinct())
        {
            FrameworkElement view;if(page==activePage)view=BuildCompactPage(page);else{view=new Border();pendingCompactPages.Add(page);}view.Visibility=Visibility.Collapsed;compactPages[page]=view;compactPageHost.Children.Add(view);
            var tab=UI.Button(PageName(page),()=>SelectPage(page));tab.Padding=new(8,6,8,6);tab.FontSize=10.5;tab.MinHeight=30;tab.Margin=new(0,0,4,0);tab.Tag=new CornerRadius(15);StyleCategory(tab,page);compactTabs[page]=tab;tabRow.Children.Add(tab);
        }
        if(compactPages.Count==0){var empty=UI.Caption("Enable widgets in Settings.",12,UI.Muted);compactPages["media"]=empty;compactPageHost.Children.Add(empty);}
        var mixer=new Border{Visibility=Visibility.Collapsed};pendingCompactPages.Add("mixer");compactPages["mixer"]=mixer;compactPageHost.Children.Add(mixer);
        if(!compactPages.ContainsKey(activePage))activePage=compactPages.Keys.FirstOrDefault()??"media";
        SelectPage(activePage,false);
    }
    internal void SelectPage(string page,bool animate=true)
    {
        LeaveAssistantPanel();
        mediaControls?.Close(false);EnsureBody();
        if(activitiesOpen){activitiesOpen=false;activityDraft=null;BuildBody();}
        if(!app.Settings.CompactLayout || commandOpen || !compactPages.ContainsKey(page))return;
        if(animate && activePage==page && compactPages[page].Visibility==Visibility.Visible)return;
        FlushNotes();EnsureCompactPage(page);InvalidateSystemMonitor();activePage=page;
        if(Settings.ToolNames.ContainsKey(page))
        {
            toolsMode=page;calendarEditing=false;
            if(toolsContent.Parent is Panel owner)owner.Children.Remove(toolsContent);
            ((Panel)compactPages[page]).Children.Add(toolsContent);RefreshTools();
        }
        scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;
        foreach(var entry in compactPages){entry.Value.Visibility=entry.Key==page?Visibility.Visible:Visibility.Collapsed;}
        UpdateSystemMonitor();
        foreach(var entry in compactTabs){entry.Value.Background=entry.Key==(page=="mixer"?"media":page)?UI.Brush(app.Settings.SelectionFill):Brushes.Transparent;entry.Value.Foreground=entry.Key==(page=="mixer"?"media":page)?UI.ContrastText(app.Settings.SelectionFill):UI.Muted;StyleCategory(entry.Value,entry.Key,entry.Key==(page=="mixer"?"media":page));}
        if(compactTabs.TryGetValue(page,out var selectedTab))selectedTab.BringIntoView();
        if(animate)Motion.Enter(compactPages[page],4);
        Dimensions();
        if(expanded){motion.Go(surface,IslandSurface.SurfaceWidthProperty,expandedWidth,Math.Min(300,app.Settings.AnimationMs));motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));motion.OnComplete(()=>{});}
    }
    private FrameworkElement BuildCompactPage(string page)
    {
        if(Settings.ToolNames.ContainsKey(page))return new StackPanel();
        if(page=="tools")return BuildToolsPage();
        if(page=="media")
        {
            mediaLyricsLayout=null;lyricsAside=null;mediaLyrics=null;lyricsButton=null;
            var panel=new StackPanel();
            if(WidgetOn("media"))
            {
                var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;artist.TextWrapping=TextWrapping.NoWrap;artist.TextTrimming=TextTrimming.CharacterEllipsis;artist.Margin=new(0,4,0,0);info.Children.Add(WrapTitle(title));info.Children.Add(WrapTitle(artist));
                var cover=new Border{Child=WrapArtwork(artwork),Width=88,Height=88,Margin=new(0,0,20,0),Clip=new RectangleGeometry(new Rect(0,0,88,88),15,15)};
                var row=new Grid{Margin=new(0,0,0,6)};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.Children.Add(cover);
                previous=Icons.Button("previous","Previous track",()=>_=app.Media.Command("previous"),false,38);play=Icons.Button("play","Play or pause",()=>_=app.Media.Command("toggle"),true,46);playIcon="";next=Icons.Button("next","Next track",()=>_=app.Media.Command("next"),false,38);previous.Background=next.Background=Brushes.Transparent;
                previous.Margin=new(0,0,8,0);play.Margin=new(0);next.Margin=new(8,0,0,0);
                lyricsButton=Icons.Button("lyrics","Show or hide synced lyrics",ToggleLyrics,false,28);lyricsButton.Margin=new(16,0,0,0);lyricsButton.Background=Brushes.Transparent;
                var buttons=UI.Row(previous,play,next);buttons.HorizontalAlignment=HorizontalAlignment.Center;var playbackRow=new Grid{Margin=new(0)};playbackRow.Children.Add(buttons);lyricsButton.HorizontalAlignment=HorizontalAlignment.Right;lyricsButton.VerticalAlignment=VerticalAlignment.Center;lyricsButton.Margin=new(0);playbackRow.Children.Add(lyricsButton);
                Grid.SetColumn(info,1);row.Children.Add(info);panel.Children.Add(row);panel.Children.Add(playbackRow);row.Loaded+=(_,_)=>QueueMediaLayout();row.IsVisibleChanged+=(_,_)=>{if(row.IsVisible)QueueMediaLayout();};
                row.SizeChanged+=(_,_)=>{bool narrow=row.ActualWidth<310;double size=narrow?64:88;cover.Width=cover.Height=artwork.Width=artwork.Height=size;cover.Margin=new(0,0,narrow?14:20,0);if(previousArtwork.TryGetValue(artwork,out var outgoing))outgoing.Width=outgoing.Height=size;musicWave.Width=narrow?112:164;cover.Clip=new RectangleGeometry(new Rect(0,0,size,size),narrow?11:15,narrow?11:15);previous.Width=previous.Height=next.Width=next.Height=narrow?28:38;play.Width=play.Height=narrow?38:46;title.FontSize=narrow?15:18;previous.Margin=new(0,0,narrow?2:8,0);next.Margin=new(narrow?2:8,0,0,0);};
                if(app.Settings.LiveActivity){if(musicWave.Parent is Panel oldWave)oldWave.Children.Remove(musicWave);musicWave.Height=22;musicWave.Margin=new(0,0,0,8);var waveHost=new Grid{Margin=new(0,3,0,0)};waveHost.Children.Add(musicWave);panel.Children.Add(waveHost);}mediaLyrics=new LyricsView(app);SyncLyrics();timelinePanel.Margin=new(0,0,0,10);panel.Children.Add(timelinePanel);
            }
            var controls=BuildMediaControls(panel);
            return WidgetOn("media")?WrapMediaLyrics(controls):controls;
        }
        if(page=="voice")return new AssistantVoiceView(app);
        if(page=="stopwatch")return new StopwatchView(app);
        if(page=="focus")
        {
            var row=new Grid();bool stacked=bodyWidth<310;int count=PanelWidgets.Count(w=>PageFor(w.Id)=="focus");for(int i=0;i<count;i++){if(stacked)row.RowDefinitions.Add(new(){Height=GridLength.Auto});else row.ColumnDefinitions.Add(new());}int column=0;
            foreach(var widget in PanelWidgets.Where(w=>PageFor(w.Id)=="focus"))
            {
                var contents=new CountdownView(app,widget.Id=="focus");
                var module=new Border{BorderBrush=UI.Brush(app.Settings.ControlBorder),BorderThickness=new Thickness(0),Padding=stacked?new Thickness(0,4,0,4):new Thickness(column==0?0:14,4,column==0?14:0,4),Margin=stacked?new Thickness(0,column==0?0:16,0,0):new Thickness(column==0?0:8,0,0,0),Child=contents};if(stacked)Grid.SetRow(module,column++);else Grid.SetColumn(module,column++);row.Children.Add(module);module.SetResourceReference(Border.BorderBrushProperty,"ControlBorder");
            }
            bodyReflow.Add(()=>
            {
                bool vertical=bodyWidth<310;row.RowDefinitions.Clear();row.ColumnDefinitions.Clear();int index=0;
                foreach(Border module in row.Children){if(vertical)row.RowDefinitions.Add(new(){Height=GridLength.Auto});else row.ColumnDefinitions.Add(new());Grid.SetRow(module,vertical?index:0);Grid.SetColumn(module,vertical?0:index);module.BorderThickness=new Thickness(0);module.Padding=vertical?new Thickness(0,4,0,4):new Thickness(index==0?0:14,4,index==0?14:0,4);module.Margin=vertical?new Thickness(0,index==0?0:16,0,0):new Thickness(index==0?0:8,0,0,0);index++;}
            });
            return row;
        }
        if(page=="status")
        {
            var panel=new StackPanel();foreach(var widget in PanelWidgets.Where(w=>PageFor(w.Id)=="status"))
            {
                switch(widget.Id)
                {
                    case "clock":date.Margin=new(0,3,0,12);panel.Children.Add(clock);panel.Children.Add(date);break;
                    case "system":panel.Children.Add(BuildSystemMonitor());break;
                    case "battery":batteryCard=new Border{Child=battery,Margin=new(0,10,0,0)};panel.Children.Add(batteryCard);break;
                }
            }
            return panel;
        }
        var launch=new StackPanel{Margin=new(0,2,0,8)};var search=UI.CaptionButton("Search commands  ·  Ctrl+K",()=>ShowCommands());search.Background=UI.Brush("#14161A");search.BorderThickness=new(1);search.BorderBrush=UI.Brush("#30343C");search.Padding=new(12,8,12,8);search.Margin=new(0,0,0,16);search.HorizontalContentAlignment=HorizontalAlignment.Stretch;var searchRow=new DockPanel();var keys=UI.Caption("Ctrl+K",10,UI.Muted);DockPanel.SetDock(keys,Dock.Right);searchRow.Children.Add(keys);var searchIcon=Icons.Create("search",16,UI.Muted);searchIcon.Margin=new(0,0,12,0);DockPanel.SetDock(searchIcon,Dock.Left);searchRow.Children.Add(searchIcon);searchRow.Children.Add(UI.Caption("Search commands",12,UI.Muted));search.Content=searchRow;launch.Children.Add(search);
        var shortcuts=new WrapPanel();KeyboardNavigation.SetDirectionalNavigation(shortcuts,KeyboardNavigationMode.Cycle);launch.Children.Add(shortcuts);foreach(var shortcut in app.Settings.Shortcuts)
        {
            shortcuts.Children.Add(LaunchButton(shortcut));
        }
        if(app.Settings.ActivitiesEnabled)launch.Children.Add(UI.CaptionButton("Activities",()=>ShowActivities()));
        if(app.Settings.Shortcuts.Count==0)shortcuts.Children.Add(UI.Caption("Add shortcuts in Settings.",12,UI.Muted));return launch;
    }
    private void UpdateTimeline()
    {
        if(!app.Settings.CompactLayout || !WidgetOn("media"))return;
        var position=app.Media.Timeline();elapsed.Text=TimeSpan.FromSeconds(position.elapsed).ToString(position.duration>=3600?@"h\:mm\:ss":@"m\:ss");duration.Text=TimeSpan.FromSeconds(position.duration).ToString(position.duration>=3600?@"h\:mm\:ss":@"m\:ss");
        timelineAt=DateTimeOffset.UtcNow;timelineElapsed=position.elapsed;timelineDuration=position.duration;
        seekSlider.IsEnabled=position.duration>0 && app.Media.CanSeek;
        if(!seekSlider.IsMouseCaptureWithin && !seekSlider.IsKeyboardFocusWithin && !seekDelay.IsEnabled)seekSlider.Value=position.duration>0?position.elapsed/position.duration:0;
    }
    private bool mediaLayoutPending;
    private void QueueMediaLayout()
    {
        if(mediaLayoutPending)return;mediaLayoutPending=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>{mediaLayoutPending=false;if(expanded && app.Settings.CompactLayout && activePage=="media" && !assistantOpen && !commandOpen)ResizeToolPanel();});
    }
}
