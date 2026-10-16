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
    private readonly TextBlock peekTitle=UI.Text("",13),peekSubtitle=UI.Text("",11,UI.Muted);
    private readonly Image peekArt=new(){Width=56,Height=56,Stretch=Stretch.UniformToFill};
    private readonly ProgressBar peekProgress=new(){Minimum=0,Maximum=1,Height=3,Background=UI.Brush("#34363D"),Foreground=Brushes.White,BorderThickness=new(0)};
    private readonly TextBlock peekElapsed=UI.Text("",11,UI.Muted),peekDuration=UI.Text("",11,UI.Muted);
    private readonly TranslateTransform peekArtSlide=new();
    private readonly System.Windows.Controls.Grid peekCoverContent=new();
    private FrameworkElement peekFallback=null!;
    private readonly ActivityVisual peekWave=new(){Width=34,Height=12,WaveOnly=true};
    private Button? peekPlay;
    private readonly Slider seekSlider=new(){Minimum=0,Maximum=1,TickFrequency=.001,SmallChange=.01,LargeChange=.05,IsMoveToPointEnabled=true,Height=24,Margin=new(0)};
    private readonly TextBlock elapsed=UI.Text("0:00",10,UI.Muted),duration=UI.Text("0:00",10,UI.Muted);
    private readonly DispatcherTimer hoverDelay=new(){Interval=TimeSpan.FromMilliseconds(180)},leaveDelay=new(){Interval=TimeSpan.FromMilliseconds(300)},seekDelay=new(){Interval=TimeSpan.FromMilliseconds(140)};
    private double HostHeight
    {
        get
        {
            if(!app.Settings.CompactLayout)return expandedHeight;
            var screen=Native.ChooseScreen(app.Settings);return Math.Min(app.Settings.PanelMaxHeight,screen.WorkingArea.Height/Native.Scale(screen)-(app.Settings.Preset=="Mac"?0:app.Settings.Top)-16);
        }
    }
    internal bool IsPeeking=>peeking;
    internal string ActivePage=>activePage;
    private static string PageFor(string widget)=>widget switch{"media" or "volume"=>"media","timer" or "focus" or "stopwatch"=>"focus","shortcuts"=>"access","tools"=>"tools",_=>"status"};
    private static string PageName(string page)=>page switch{"media"=>"Media","focus"=>"Focus","status"=>"Status","tools"=>"Tools",_=>"Launch"};
    private bool WidgetOn(string id)=>app.Settings.Widgets.Any(w=>w.Enabled && w.Id==id);
    private void InitializeCompactInteraction()
    {
        root.MouseEnter+=(_,_)=>{if(DiagnosticRun)return;EnsurePeekDeck();if(IsTucked){RevealTuckedPreview();return;}leaveDelay.Stop();if(peeking){automaticPreview=false;activityDismiss.Stop();}else{previewKind=activityTitle=activityDetail="";}if(!coverGesture.Pressed && !expanded && !peeking && app.Settings.HoverPeek && app.Settings.CompactLayout && !userHidden && !TuckRequested)hoverDelay.Start();};
        root.MouseLeave+=(_,_)=>{if(DiagnosticRun)return;hoverDelay.Stop();if(peeking || TuckRequested)leaveDelay.Start();};
        hoverDelay.Tick+=(_,_)=>{hoverDelay.Stop();if(PointerInside() && !expanded && !userHidden && !TuckRequested)PreviewActivity();};
        leaveDelay.Tick+=(_,_)=>{leaveDelay.Stop();if((peeking || TuckRequested) && !PointerInside() && !SatellitePointerInside && !PeekInteractionActive && !fileDragActive)Collapse();};
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
        Grid.SetRow(seekSlider,1);timelinePanel.Children.Add(seekSlider);UI.Named(seekSlider,"Playback position");
        seekSlider.ValueChanged+=(_,_)=>{if(!updating && (seekSlider.IsMouseCaptureWithin || seekSlider.IsKeyboardFocusWithin)){seekDelay.Stop();seekDelay.Start();}};
        seekDelay.Tick+=async(_,_)=>{seekDelay.Stop();await app.Media.Seek(seekSlider.Value);};
    }
    private void StopHoverTimers(){hoverDelay.Stop();leaveDelay.Stop();detailDelay.Stop();}
    internal void PreviewActivity()
    {
        if(app.Notifications.Current!=null && !expanded && !peeking && !fileDragActive && !userHidden){OpenNotice();return;}
        if(PeekPages().Length==0 || (!app.Settings.CompactLayout && !tuckPreview && !fileDragActive) || (!app.Settings.HoverPeek && !automaticPreview && !tuckPreview && !fileDragActive) || expanded || peeking || userHidden || (TuckRequested && !tuckPreview && !fileDragActive))return;
        peeking=true;UpdateSatellite();Show();Dimensions();ConfigurePerformance();peekDetailed=!app.Settings.TwoStageHover || automaticPreview;detailDelay.Stop();if(!peekDetailed)detailDelay.Start();
        scroll.Visibility=Visibility.Collapsed;compact.IsHitTestVisible=false;Motion.Fade(compact,0,80);
        PreparePeekDeck();
        peekPanel.Visibility=Visibility.Visible;peekPanel.IsHitTestVisible=true;Motion.Fade(peekPanel,1,160);
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,Math.Min(app.Settings.HoverWidth,expandedWidth),app.Settings.AnimationMs);
        motion.Go(surface,IslandSurface.SurfaceHeightProperty,peekDetailed?PeekExpandedHeight:100,app.Settings.AnimationMs);motion.Go(surface,IslandSurface.GlassLiftProperty,1,app.Settings.AnimationMs);motion.OnComplete(()=>{});UpdatePeek();WakeLiveRendering();
    }
    private void UpdatePeek()
    {
        if(!peeking || noticeShowing)return;
        UpdatePeekDeck();
        bool media=WidgetOn("media") && app.Media.Available;ApplyMediaTypography(peekTitle,media && app.Settings.BoldMediaTitle);
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
        ApplyPeekGeometry();
    }
    private void BuildCompactBody()
    {
        if(timelinePanel.Parent is Panel old)old.Children.Remove(timelinePanel);
        title.FontSize=18;artist.FontSize=12;artwork.Width=artwork.Height=56;timerTime.FontSize=focusTime.FontSize=27;clock.FontSize=26;
        var navigation=new Grid{Margin=new(0,0,0,app.Settings.SectionGap)};
        navigation.ColumnDefinitions.Add(new(){Width=new GridLength(28)});navigation.ColumnDefinitions.Add(new());navigation.ColumnDefinitions.Add(new(){Width=new GridLength(28)});
        var settings=Icons.Button("settings","Settings",app.ShowSettings,false,28);settings.Margin=new(0);settings.Background=Brushes.Transparent;Grid.SetColumn(settings,2);navigation.Children.Add(settings);
        if(bodyWidth<250){navigation.RowDefinitions.Add(new(){Height=GridLength.Auto});navigation.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetRow(settings,0);}
        int categoryCount=Math.Max(1,app.Settings.Widgets.Where(w=>w.Enabled).Select(w=>PageFor(w.Id)).Distinct().Count());
        var tabRow=new System.Windows.Controls.Primitives.UniformGrid{Columns=categoryCount,Rows=1,Width=Math.Min(bodyWidth-(bodyWidth<250?0:56),categoryCount*(app.Settings.CategoryStyle=="Icons"?40:48)),HorizontalAlignment=HorizontalAlignment.Center};Grid.SetColumn(tabRow,bodyWidth<250?0:1);if(bodyWidth<250){Grid.SetColumnSpan(tabRow,3);Grid.SetRow(tabRow,1);}KeyboardNavigation.SetDirectionalNavigation(tabRow,KeyboardNavigationMode.Cycle);navigation.Children.Add(tabRow);body.Children.Add(navigation);body.Children.Add(compactPageHost);
        bodyReflow.Add(()=>
        {
            bool narrow=bodyWidth<250;navigation.Margin=new(0,0,0,app.Settings.SectionGap);navigation.RowDefinitions.Clear();if(narrow){navigation.RowDefinitions.Add(new(){Height=GridLength.Auto});navigation.RowDefinitions.Add(new(){Height=GridLength.Auto});}
            Grid.SetRow(settings,0);Grid.SetColumn(tabRow,narrow?0:1);Grid.SetColumnSpan(tabRow,narrow?3:1);Grid.SetRow(tabRow,narrow?1:0);tabRow.Width=Math.Min(bodyWidth-(narrow?0:56),categoryCount*(app.Settings.CategoryStyle=="Icons"?40:48));
        });
        foreach(string page in app.Settings.Widgets.Where(w=>w.Enabled).Select(w=>PageFor(w.Id)).Distinct())
        {
            FrameworkElement view;if(page==activePage)view=BuildCompactPage(page);else{view=new Border();pendingCompactPages.Add(page);}view.Visibility=Visibility.Collapsed;compactPages[page]=view;compactPageHost.Children.Add(view);
            var tab=UI.Button(PageName(page),()=>SelectPage(page));tab.Padding=new(8,6,8,6);tab.FontSize=10.5;tab.MinHeight=30;tab.Margin=new(0,0,4,0);tab.Tag=new CornerRadius(15);StyleCategory(tab,page);compactTabs[page]=tab;tabRow.Children.Add(tab);
        }
        if(compactPages.Count==0){var empty=UI.Text("Enable widgets in Settings.",12,UI.Muted);compactPages["media"]=empty;compactPageHost.Children.Add(empty);}
        var mixer=new Border{Visibility=Visibility.Collapsed};pendingCompactPages.Add("mixer");compactPages["mixer"]=mixer;compactPageHost.Children.Add(mixer);
        if(!compactPages.ContainsKey(activePage))activePage=compactPages.Keys.FirstOrDefault()??"media";
        SelectPage(activePage,false);
    }
    internal void SelectPage(string page,bool animate=true)
    {
        EnsureBody();
        if(activitiesOpen){activitiesOpen=false;activityDraft=null;BuildBody();}
        if(!app.Settings.CompactLayout || commandOpen || !compactPages.ContainsKey(page))return;
        if(animate && activePage==page && compactPages[page].Visibility==Visibility.Visible)return;
        EnsureCompactPage(page);InvalidateSystemMonitor();activePage=page;
        scroll.VerticalScrollBarVisibility=page=="status"?ScrollBarVisibility.Hidden:ScrollBarVisibility.Auto;
        foreach(var entry in compactPages){entry.Value.Visibility=entry.Key==page?Visibility.Visible:Visibility.Collapsed;}
        UpdateSystemMonitor();
        foreach(var entry in compactTabs){entry.Value.Background=entry.Key==(page=="mixer"?"media":page)?UI.Brush(app.Settings.SelectionFill):Brushes.Transparent;entry.Value.Foreground=entry.Key==(page=="mixer"?"media":page)?UI.ContrastText(app.Settings.SelectionFill):UI.Muted;StyleCategory(entry.Value,entry.Key,entry.Key==(page=="mixer"?"media":page));}
        if(animate)Motion.Enter(compactPages[page],4);
        Dimensions();
        if(expanded){motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));motion.OnComplete(()=>{});}
    }
    private FrameworkElement BuildCompactPage(string page)
    {
        if(page=="tools")return BuildToolsPage();
        if(page=="media")
        {
            var panel=new StackPanel();
            if(WidgetOn("media"))
            {
                var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;artist.TextWrapping=TextWrapping.NoWrap;artist.TextTrimming=TextTrimming.CharacterEllipsis;artist.Margin=new(0,4,0,0);info.Children.Add(WrapTitle(title));info.Children.Add(WrapTitle(artist));
                var cover=new Border{Child=WrapArtwork(artwork),Width=56,Height=56,Margin=new(0,0,16,0),Clip=new RectangleGeometry(new Rect(0,0,56,56),14,14)};
                var row=new DockPanel{Margin=new(0,0,0,app.Settings.SectionGap)};DockPanel.SetDock(cover,Dock.Left);row.Children.Add(cover);row.Children.Add(info);panel.Children.Add(row);bodyReflow.Add(()=>row.Margin=new(0,0,0,app.Settings.SectionGap));
                previous=Icons.Button("previous","Previous track",()=>_=app.Media.Command("previous"),false,38);play=Icons.Button("play","Play or pause",()=>_=app.Media.Command("toggle"),true,46);playIcon="";next=Icons.Button("next","Next track",()=>_=app.Media.Command("next"),false,38);previous.Background=next.Background=Brushes.Transparent;
                previous.Margin=new(0,0,18,0);play.Margin=new(0);next.Margin=new(18,0,0,0);
                var buttons=UI.Row(previous,play,next);buttons.HorizontalAlignment=HorizontalAlignment.Center;buttons.Margin=new(0,0,0,12);panel.Children.Add(buttons);
                if(app.Settings.LiveActivity){if(musicWave.Parent is Panel oldWave)oldWave.Children.Remove(musicWave);musicWave.Height=12;musicWave.Margin=new(0,0,0,8);panel.Children.Add(musicWave);}timelinePanel.Margin=new(0,0,0,10);panel.Children.Add(timelinePanel);
                sessions.Margin=new(0,0,0,14);sessions.MinHeight=30;UI.Named(sessions,"Media session");panel.Children.Add(sessions);
            }
            if(WidgetOn("volume"))
            {
                mute=Icons.Button("volume","Mute or unmute",()=>{app.Audio.ToggleMute();Update();},false,26);mute.Background=Brushes.Transparent;mute.Margin=new(0);muteIcon="";volume.Margin=new(9,0,8,0);
                var output=UI.Button("",OpenAudioMenu);var chevron=Icons.Create("chevron",9,UI.Muted);chevron.Margin=new(5,0,0,0);output.Content=UI.Row(volumeText,chevron);output.MinHeight=26;output.Padding=new(5,3,5,3);output.Margin=new(0);output.Background=Brushes.Transparent;output.ToolTip="Output devices & microphone";UI.Named(output,"Audio devices and microphone");
                volume.Margin=new(12,0,12,0);var row=new DockPanel{Margin=new(0,2,0,2)};var mixerButton=Icons.Button("mixer","Open volume mixer",OpenMixer,false,26);mixerButton.Background=Brushes.Transparent;mixerButton.Margin=new(6,0,0,0);DockPanel.SetDock(mixerButton,Dock.Right);row.Children.Add(mixerButton);DockPanel.SetDock(mute,Dock.Left);row.Children.Add(mute);DockPanel.SetDock(output,Dock.Right);row.Children.Add(output);row.Children.Add(volume);panel.Children.Add(row);UI.Named(volume,"Windows volume");
            }
            return panel;
        }
        if(page=="focus")
        {
            var row=new Grid();bool stacked=bodyWidth<310 || app.Settings.Widgets.Count(w=>w.Enabled && PageFor(w.Id)=="focus")>2;int count=app.Settings.Widgets.Count(w=>w.Enabled && PageFor(w.Id)=="focus");for(int i=0;i<count;i++){if(stacked)row.RowDefinitions.Add(new(){Height=GridLength.Auto});else row.ColumnDefinitions.Add(new());}int column=0;
            foreach(var widget in app.Settings.Widgets.Where(w=>w.Enabled && PageFor(w.Id)=="focus"))
            {
                if(widget.Id=="stopwatch"){var watchModule=new Border{Child=new StopwatchView(app),Margin=new(0,12,0,4)};if(stacked)Grid.SetRow(watchModule,column++);else Grid.SetColumn(watchModule,column++);row.Children.Add(watchModule);continue;}
                bool timer=widget.Id=="timer";var contents=new StackPanel();var heading=UI.Heading(timer?"Timer":"Focus",13);heading.FontWeight=FontWeights.SemiBold;contents.Children.Add(heading);
                var number=timer?timerTime:focusTime;number.Margin=new(0,10,0,8);contents.Children.Add(number);
                if(timer)
                {
                    var minutes=UI.Input("Timer duration in minutes","5","0.1–1440 minutes");minutes.Width=58;minutes.Height=30;minutes.FontSize=12;minutes.Padding=new(8,0,8,0);minutes.Margin=new(0,0,7,0);var input=UI.Row(minutes,UI.Text("minutes",11,UI.Muted));input.Height=38;contents.Children.Add(input);
                    var start=Icons.Button("play","Start",()=>{if(!double.TryParse(minutes.Text,out var n) || !double.IsFinite(n) || n<.1 || n>1440){app.Notify("Invalid duration","Enter a duration from 0.1 to 1440 minutes.");return;}app.Session.Timer.Start(n*60,DateTimeOffset.UtcNow);app.SaveSession();},true,26);
                    timerPause=Icons.Button("pause","Pause or resume timer",()=>{if(app.Session.Timer.Running)app.Session.Timer.Pause(DateTimeOffset.UtcNow);else app.Session.Timer.Resume(DateTimeOffset.UtcNow);app.SaveSession();},false,26);
                    var reset=UI.Button("Reset",()=>{app.Session.Timer.Reset();app.SaveSession();});contents.Children.Add(CountdownActions(start,timerPause,reset));
                }
                else
                {
                    focusPhase.Height=38;focusPhase.FontSize=11;focusPhase.Margin=new(0);contents.Children.Add(focusPhase);
                    var start=Icons.Button("play","Start focus",()=>{app.Session.Focus.Start(app.Settings.FocusMinutes*60,DateTimeOffset.UtcNow,"work");app.SaveSession();},true,26);
                    focusPause=Icons.Button("pause","Pause or resume focus",()=>{if(app.Session.Focus.Running)app.Session.Focus.Pause(DateTimeOffset.UtcNow);else app.Session.Focus.Resume(DateTimeOffset.UtcNow);app.SaveSession();},false,26);
                    var reset=UI.Button("Reset",()=>{app.Session.Focus.Reset();app.SaveSession();});contents.Children.Add(CountdownActions(start,focusPause,reset));
                }
                var module=new Border{BorderBrush=UI.Brush(app.Settings.ControlBorder),BorderThickness=stacked?new Thickness(0,column==0?0:1,0,0):new Thickness(column==0?0:1,0,0,0),Padding=stacked?new Thickness(0,column==0?4:16,0,4):new Thickness(column==0?0:8,4,0,4),Margin=stacked?new Thickness(0,column==0?0:16,0,0):new Thickness(column==0?0:8,0,0,0),Child=contents};if(stacked)Grid.SetRow(module,column++);else Grid.SetColumn(module,column++);row.Children.Add(module);module.SetResourceReference(Border.BorderBrushProperty,"ControlBorder");
            }
            bodyReflow.Add(()=>
            {
                bool vertical=bodyWidth<310 || count>2;row.RowDefinitions.Clear();row.ColumnDefinitions.Clear();int index=0;
                foreach(Border module in row.Children){if(vertical)row.RowDefinitions.Add(new(){Height=GridLength.Auto});else row.ColumnDefinitions.Add(new());Grid.SetRow(module,vertical?index:0);Grid.SetColumn(module,vertical?0:index);module.BorderThickness=vertical?new Thickness(0,index==0?0:1,0,0):new Thickness(index==0?0:1,0,0,0);module.Padding=vertical?new Thickness(0,index==0?4:16,0,4):new Thickness(index==0?0:8,4,0,4);module.Margin=vertical?new Thickness(0,index==0?0:16,0,0):new Thickness(index==0?0:8,0,0,0);index++;}
            });
            return row;
        }
        if(page=="status")
        {
            var panel=new StackPanel();foreach(var widget in app.Settings.Widgets.Where(w=>w.Enabled && PageFor(w.Id)=="status"))
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
        var launch=new StackPanel();launch.Children.Add(UI.Button("Search commands  ·  Ctrl+K",()=>ShowCommands()));
        var shortcuts=new WrapPanel();KeyboardNavigation.SetDirectionalNavigation(shortcuts,KeyboardNavigationMode.Cycle);launch.Children.Add(shortcuts);foreach(var shortcut in app.Settings.Shortcuts)
        {
            shortcuts.Children.Add(LaunchButton(shortcut));
        }
        if(app.Settings.ActivitiesEnabled)launch.Children.Add(UI.Button("Activities",()=>ShowActivities()));
        if(app.Settings.Shortcuts.Count==0)shortcuts.Children.Add(UI.Text("Add shortcuts in Settings.",12,UI.Muted));return launch;
    }
    private static FrameworkElement CountdownActions(Button start,Button pause,Button reset)
    {
        var row=new Grid{Margin=new(0,10,0,0),MaxWidth=180,HorizontalAlignment=HorizontalAlignment.Stretch};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(32)});row.ColumnDefinitions.Add(new(){Width=new GridLength(40)});
        start.Content="Start";start.Width=double.NaN;start.Height=32;start.MinHeight=32;start.Padding=new(5,0,5,0);start.FontSize=11;start.FontWeight=FontWeights.SemiBold;start.Tag=new CornerRadius(7);start.Margin=new(0,0,5,0);row.Children.Add(start);
        pause.Width=pause.Height=pause.MinHeight=32;pause.Padding=new(7);pause.Margin=new(0);pause.Tag=new CornerRadius(7);Grid.SetColumn(pause,1);row.Children.Add(pause);
        reset.MinHeight=32;reset.Padding=new(2);reset.FontSize=10;reset.Background=Brushes.Transparent;reset.Margin=new(0);Grid.SetColumn(reset,2);row.Children.Add(reset);return row;
    }
    private void UpdateTimeline()
    {
        if(!app.Settings.CompactLayout || !WidgetOn("media"))return;
        var position=app.Media.Timeline();elapsed.Text=TimeSpan.FromSeconds(position.elapsed).ToString(position.duration>=3600?@"h\:mm\:ss":@"m\:ss");duration.Text=TimeSpan.FromSeconds(position.duration).ToString(position.duration>=3600?@"h\:mm\:ss":@"m\:ss");
        timelineAt=DateTimeOffset.UtcNow;timelineElapsed=position.elapsed;timelineDuration=position.duration;
        seekSlider.IsEnabled=position.duration>0 && app.Media.CanSeek;
        if(!seekSlider.IsMouseCaptureWithin && !seekSlider.IsKeyboardFocusWithin && !seekDelay.IsEnabled)seekSlider.Value=position.duration>0?position.elapsed/position.duration:0;
    }
}
