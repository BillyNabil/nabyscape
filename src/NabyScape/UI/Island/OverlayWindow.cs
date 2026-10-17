using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace NabyScape;

public sealed partial class OverlayWindow : Window
{
    private readonly App app;
    private readonly IslandSurface surface=new();
    private readonly SurfaceMotion motion=new();
    private readonly Grid root=new(),visuals=new();
    private readonly Button compact;
    private readonly TextBlock summary=UI.Text("",12),compactClock=UI.Text("",12);
    private readonly StackPanel body=new();
    private readonly WrapPanel widgets=new();
    private readonly ScrollViewer scroll;
    private readonly TranslateTransform bodySlide=new(0,-8);
    private readonly TextBlock title=UI.Text("",20),artist=UI.Text("",12,UI.Muted),clock=UI.Text("",32),date=UI.Text("",11,UI.Muted);
    private readonly TextBlock timerTime=UI.Text("00:00",29),focusTime=UI.Text("25:00",29),focusPhase=UI.Text("",11,UI.Muted);
    private readonly TextBlock cpu=UI.Text("",11),ram=UI.Text("",11),battery=UI.Text(""),volumeText=UI.Text("",11,UI.Muted);
    private readonly Image artwork=new(){Width=64,Height=64,Stretch=Stretch.UniformToFill};
    private readonly ComboBox sessions=new();
    private readonly Slider volume=new(){Minimum=0,Maximum=100,Height=22};
    private readonly ProgressBar cpuBar=new(){Maximum=100,Height=4,Margin=new(0,7,0,12)},ramBar=new(){Maximum=100,Height=4,Margin=new(0,7,0,0)};
    private Button play=null!,next=null!,previous=null!,timerPause=null!,focusPause=null!,mute=null!;
    private Border? batteryCard;
    private OutsideClick? outside;
    private bool expanded,userHidden,environmentHidden,updating,positioning;
    private double expandedWidth=452,expandedHeight=634,bodyWidth=388;
    private string sessionKey="",playIcon="",muteIcon="";
    private IntPtr hwnd;
    internal double PaintedWidth=>surface.SurfaceWidth;
    internal double PaintedHeight=>surface.SurfaceHeight;
    internal bool IsAnimating=>motion.Active;
    internal bool IsExpanded=>expanded;
    internal Geometry Contour=>surface.Contour;
    internal TextBlock HeaderLabel=>summary;
    internal double PeekOpacity=>peekPanel.Opacity;
    internal bool ContentFits=>scroll.ExtentHeight<=scroll.ViewportHeight+1;
    internal bool DiagnosticRun {get;set;}
    public OverlayWindow(App app)
    {
        this.app=app;Title="NabyScape";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;Topmost=true;ShowActivated=false;
        FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI");FontSize=13;UseLayoutRounding=false;SnapsToDevicePixels=false;
        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        var header=new Grid();header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        activity.Margin=new(0,0,9,0);header.Children.Add(activity);closedCover.Child=WrapArtwork(closedCoverImage);header.Children.Add(closedCover);header.Children.Add(feedbackIcon);
        header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetColumn(feedbackLevel,3);header.Children.Add(feedbackLevel);
        summary.TextWrapping=TextWrapping.NoWrap;summary.TextTrimming=TextTrimming.CharacterEllipsis;summary.FontWeight=FontWeights.Medium;
        Grid.SetColumn(summary,1);header.Children.Add(summary);Grid.SetColumn(closedTitleWave,1);header.Children.Add(closedTitleWave);Grid.SetColumn(awakeMark,2);awakeMark.Margin=new(8,0,0,0);awakeMark.ToolTip="Keep awake is active";UI.Named(awakeMark,"Keep awake is active");awakeMark.Visibility=Visibility.Collapsed;header.Children.Add(awakeMark);Grid.SetColumn(compactClock,4);compactClock.Margin=new(12,0,0,0);compactClock.FontWeight=FontWeights.SemiBold;header.Children.Add(compactClock);
        compact=new Button{Content=header,Padding=new(14,0,14,0),Margin=new(0),Background=Brushes.Transparent,BorderThickness=new(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top};
        UI.Named(compact,"Open or close island");compact.ToolTip="Click to open · Esc to close";
        Motion.Button(compact);
        compact.Click+=(_,_)=> {if(app.Notifications.Current!=null && !expanded){OpenNotice();return;}if(expanded)Collapse();else Reveal();};
        scroll=UI.Scroll(body);scroll.VerticalAlignment=VerticalAlignment.Top;scroll.HorizontalAlignment=HorizontalAlignment.Center;scroll.Visibility=Visibility.Collapsed;scroll.Opacity=0;scroll.RenderTransform=bodySlide;
        root.RenderTransform=tuckSlide;root.MouseLeftButtonDown+=(_,e)=>{if(TuckRequested && !expanded && e.OriginalSource==surface){Reveal();e.Handled=true;}};root.Children.Add(surface);root.Children.Add(visuals);visuals.Children.Add(compact);visuals.Children.Add(scroll);Content=root;
        surface.ContourChanged+=()=>visuals.Clip=surface.Contour;
        foreach(var number in new[]{clock,compactClock,timerTime,focusTime,cpu,ram})Typography.SetNumeralAlignment(number,FontNumeralAlignment.Tabular);
        title.FontWeight=FontWeights.SemiBold;timerTime.FontWeight=focusTime.FontWeight=FontWeights.SemiBold;clock.FontWeight=compactClock.FontWeight=FontWeights.Bold;
        Typography.SetNumeralAlignment(compactClock,FontNumeralAlignment.Tabular);
        SourceInitialized+=(_,_)=> {hwnd=new WindowInteropHelper(this).Handle;Native.SetWindowLong(hwnd,-20,Native.GetWindowLong(hwnd,-20)|0x80);HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);ApplySettings();};
        SizeChanged+=(_,_)=>Position();
        IsVisibleChanged+=(_,_)=>{if(!IsVisible)InvalidateSystemMonitor();};
        PreviewKeyDown+=(_,e)=> {if(e.Key==Key.K && Keyboard.Modifiers==ModifierKeys.Control){ShowCommands();e.Handled=true;}else if(e.Key==Key.Escape){if(commandOpen)CloseCommands();else if(activitiesOpen)CloseActivities();else Collapse();e.Handled=true;}};
        Closing+=(_,e)=> {if(!app.Quitting){e.Cancel=true;Collapse();Hide();userHidden=true;app.Previews?.SetHidden(true);}};
        Deactivated+=(_,_)=> {if((expanded || peeking) && !DiagnosticRun && audioMenu?.IsOpen!=true)Collapse();};
        app.SettingsChanged+=ApplySettings;app.Updated+=Update;Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        volume.ValueChanged+=(_,_)=> {if(!updating){app.Audio.SetVolume(volume.Value/100);volumeText.Text=$"{volume.Value:0}%";}};
        sessions.SelectionChanged+=(_,_)=> {if(!updating && sessions.SelectedIndex>=0 && sessions.SelectedIndex<app.Media.Sessions.Count)app.Media.Select(app.Media.Sessions[sessions.SelectedIndex]);};
        InitializeDisplayFollow();InitializeCompactInteraction();InitializeLiveActivity();InitializeEnhancements();InitializeInteractionFeatures();InitializeActivities();ApplySettings();
    }
    private IntPtr WindowMessage(IntPtr handle,int message,IntPtr wparam,IntPtr lparam,ref bool handled)
    {if(message==0x84){var packed=lparam.ToInt64();var point=PointFromScreen(new Point((short)(packed & 0xffff),(short)((packed>>16)&0xffff)));if(!VisiblePoint(point)){handled=true;return new IntPtr(-1);}}if(message==0x20E && (peeking || expanded) && PointerInside()){int delta=(short)((wparam.ToInt64()>>16)&0xffff);if(peeking)RoutePeekWheel(delta,true,InputHitTest(Mouse.GetPosition(this)) as DependencyObject);else if(!RoutePanelWheel(delta,true,InputHitTest(Mouse.GetPosition(this)) as DependencyObject))return IntPtr.Zero;handled=true;return IntPtr.Zero;}if(message==0x312 && wparam.ToInt32()==TuckHotkeyId){if(!DiagnosticRun)ToggleTuck();handled=true;return IntPtr.Zero;}if(message==0x312 && wparam.ToInt32()==VisibilityHotkeyId){if(DiagnosticRun){handled=true;return IntPtr.Zero;}ToggleVisibility();handled=true;return IntPtr.Zero;}if(message==0x312 && wparam.ToInt32()==HotkeyId){if(DiagnosticRun){handled=true;return IntPtr.Zero;}if(expanded)Collapse();else Reveal();handled=true;}if(message==0x21 && !expanded){handled=true;return new IntPtr(3);}if(message==0x2E0)Dispatcher.BeginInvoke(ReflowDisplay);return IntPtr.Zero;}
    private void DisplayChanged(object? sender,EventArgs e)=>Dispatcher.BeginInvoke(()=> {if(DiagnosticRun)return;ApplySettings();ReflowDisplay();ConfigureDisplayFollow();CheckEnvironment();});
    private void Dimensions(bool contentChanged=false)
    {
        var screen=Native.ChooseScreen(app.Settings);var scale=Native.Scale(screen);
        expandedWidth=Math.Min(app.Settings.PanelWidth,screen.WorkingArea.Width/scale-24);
        double available=screen.WorkingArea.Height/scale-(app.Settings.Preset=="Mac"?0:app.Settings.Top)-16;
        bodyWidth=Math.Max(140,(expandedWidth-app.Settings.PanelPadding*2-18)/app.Settings.PanelScale);
        body.Width=bodyWidth;
        if(body.LayoutTransform is ScaleTransform scaling){scaling.ScaleX=scaling.ScaleY=app.Settings.PanelScale;}else body.LayoutTransform=new ScaleTransform(app.Settings.PanelScale,app.Settings.PanelScale);
        double desired=app.Settings.PanelMaxHeight;
        if(app.Settings.CompactLayout)
        {
            double contentHeight=0;var constraint=new Size(bodyWidth,double.PositiveInfinity);
            // Measure the selected page directly: the collapsed ScrollViewer and stretched
            // page host can otherwise report zero or the previous tab's arranged height.
            foreach(FrameworkElement child in body.Children)
            {
                FrameworkElement measured=child;
                if(child==compactPageHost){if(!compactPages.TryGetValue(activePage,out var page))continue;measured=page;}
                if(contentChanged)measured.InvalidateMeasure();measured.Measure(constraint);contentHeight+=measured.DesiredSize.Height;
            }
            desired=Math.Clamp(contentHeight*app.Settings.PanelScale+32,184,app.Settings.PanelMaxHeight);
        }
        expandedHeight=Math.Max(app.Settings.Height,Math.Min(desired,available));
        scroll.Width=expandedWidth-app.Settings.PanelPadding*2;scroll.Height=Math.Max(0,expandedHeight-26);scroll.Margin=new(0,10,0,0);
    }
    private void ApplySettings()
    {
        var editorState=CaptureEditorState();
        if(ApplyIncrementalSettings(OverlaySettingsState.Capture(app.Settings))){RestoreEditorState(editorState);return;}
        if(!app.Settings.ActivitiesEnabled){activitiesOpen=false;activityDraft=null;}classicMixerOpen=false;if(activePage=="mixer")activePage="media";
        FlushNotes();ConfigureHotkey();var s=app.Settings;refreshHz=Native.RefreshRate(Native.ChooseScreen(s));Motion.Configure(s);Opacity=s.Opacity;surface.Fill=UI.Brush(s.Background);surface.GlassStyle=s.GlassStyle;surface.SetMaterial(s.Background,s.GlassStrength);surface.Notch=s.Preset=="Mac";surface.Radius=s.Radius;
        hoverDelay.Interval=TimeSpan.FromMilliseconds(app.Settings.HoverDelayMs);detailDelay.Interval=TimeSpan.FromMilliseconds(app.Settings.HoverDetailDelayMs);
        HideNotice();EndPeekGesture();peeking=false;peekPanel.BeginAnimation(OpacityProperty,null);peekPanel.Opacity=0;peekPanel.Visibility=Visibility.Collapsed;compact.BeginAnimation(OpacityProperty,null);compact.Opacity=1;compact.IsHitTestVisible=true;StopHoverTimers();
        compact.Width=Math.Max(120,Math.Min(s.Width,expandedWidth)-(surface.Notch?32:12));compact.Height=s.Height;compact.MinHeight=s.Height;Dimensions();if(expanded)EnsureBody();Dimensions();motion.Dispose();Width=Math.Max(expandedWidth,s.Width);Height=Math.Max(HostHeight,s.Height);
        scroll.BeginAnimation(OpacityProperty,null);scroll.Opacity=expanded?1:0;scroll.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
        bodySlide.BeginAnimation(TranslateTransform.YProperty,null);bodySlide.Y=expanded?0:-8;
        motion.Snap(surface,IslandSurface.SurfaceWidthProperty,expanded?expandedWidth:s.Width);motion.Snap(surface,IslandSurface.SurfaceHeightProperty,expanded?expandedHeight:s.Height);
        motion.Snap(surface,IslandSurface.GlassLiftProperty,expanded?1:0);
        surface.Refresh();ApplyEnvironmentTuck();Position();Update();ConfigureLiveActivity();RestoreEditorState(editorState);
    }
    private void BuildBody()
    {bodyInitialized=true;bodyDirty=false;bodyReflow.Clear();pendingCompactPages.Clear();BodyBuilds++;BuildBodyContent();ApplyHeadingTypography(body);}
    private void BuildBodyContent()
    {
        InvalidateSystemMonitor();
        foreach(var control in new FrameworkElement[]{title,artist,clock,date,timerTime,focusTime,focusPhase,cpu,ram,battery,volumeText,artwork,sessions,volume,cpuBar,ramBar})
        {if(control.Parent is Panel p)p.Children.Remove(control);else if(control.Parent is Decorator d)d.Child=null;else if(control.Parent is ContentControl c)c.Content=null;}
        body.Children.Clear();widgets.Children.Clear();compactPages.Clear();compactTabs.Clear();compactPageHost.Children.Clear();batteryCard=null;play=null!;timerPause=null!;focusPause=null!;mute=null!;
        if(activitiesOpen){body.Children.Add(BuildActivitiesPage());return;}
        if(commandOpen){body.Children.Add(BuildCommandPage());return;}
        if(app.Settings.CompactLayout){BuildCompactBody();return;}
        if(hoverPanelWidget!=null){body.Children.Add(BuildCompactPage(PageFor(hoverPanelWidget)));return;}
        if(classicMixerOpen){var mixer=BuildMixerPage();body.Children.Add(mixer);return;}
        var heading=new DockPanel{Margin=new(2,0,4,12)};var settings=Icons.Button("settings","Settings",app.ShowSettings,false,32);settings.Margin=new(0);settings.Background=UI.Brush("#1C1C1E");
        DockPanel.SetDock(settings,Dock.Right);heading.Children.Add(settings);var name=UI.Heading("Activity",18);name.FontWeight=FontWeights.SemiBold;heading.Children.Add(name);body.Children.Add(heading);body.Children.Add(widgets);
        double full=bodyWidth,half=(full-10)/2;widgets.Width=full+10;
        foreach(var widget in app.Settings.Widgets.Where(w=>w.Enabled))
        {
            Border card;bool small=false;
            switch(widget.Id)
            {
                case "media":
                    var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;artist.TextWrapping=TextWrapping.NoWrap;artist.TextTrimming=TextTrimming.CharacterEllipsis;artist.Margin=new(0,4,0,0);info.Children.Add(WrapTitle(title));info.Children.Add(WrapTitle(artist));
                    var art=new Border{CornerRadius=new(13),Child=WrapArtwork(artwork),ClipToBounds=true,Margin=new(0,0,14,0)};art.SizeChanged+=(_,_)=>art.Clip=new RectangleGeometry(new Rect(0,0,art.ActualWidth,art.ActualHeight),13,13);
                    var mediaGrid=new Grid();mediaGrid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});mediaGrid.ColumnDefinitions.Add(new());mediaGrid.Children.Add(art);Grid.SetColumn(info,1);mediaGrid.Children.Add(info);
                    previous=Icons.Button("previous","Previous track",()=>_=app.Media.Command("previous"),false,42);play=Icons.Button("play","Play or pause",()=>_=app.Media.Command("toggle"),true,50);playIcon="";next=Icons.Button("next","Next track",()=>_=app.Media.Command("next"),false,42);previous.Background=next.Background=Brushes.Transparent;
                    var transport=UI.Row(previous,play,next);transport.HorizontalAlignment=HorizontalAlignment.Center;transport.Margin=new(0,10,0,2);UI.Named(sessions,"Media session");sessions.ToolTip="Choose a media app";sessions.Margin=new(0,6,0,0);
                    card=UI.Card("Now playing",mediaGrid,transport,sessions);break;
                case "volume":
                    UI.Named(volume,"Windows volume");volume.Margin=new(12,0,10,0);mute=Icons.Button("volume","Mute or unmute",()=> {app.Audio.ToggleMute();Update();},false,30);mute.Background=Brushes.Transparent;mute.Padding=new(4);mute.Margin=new(0);muteIcon="";
                    var audioOutput=UI.Button("",OpenAudioMenu);audioOutput.Content=volumeText;audioOutput.MinHeight=28;audioOutput.Padding=new(5);UI.Named(audioOutput,"Audio devices and microphone");var audioRow=new DockPanel();DockPanel.SetDock(mute,Dock.Left);audioRow.Children.Add(mute);DockPanel.SetDock(audioOutput,Dock.Right);audioRow.Children.Add(audioOutput);audioRow.Children.Add(volume);card=UI.Card("Volume",audioRow);break;
                case "stopwatch":card=UI.Card("Stopwatch",new StopwatchView(app));break;
                case "timer":
                    small=true;var minutes=UI.Input("Timer duration in minutes","5","0.1–1440 minutes");minutes.Width=46;minutes.Padding=new(7,3,7,3);minutes.Margin=new(0,3,6,4);
                    timerPause=Icons.Button("pause","Pause or resume timer",()=> {if(app.Session.Timer.Running)app.Session.Timer.Pause(DateTimeOffset.UtcNow);else app.Session.Timer.Resume(DateTimeOffset.UtcNow);app.SaveSession();},false,30);
                    var timerStart=Icons.Button("play","Start",()=> {if(!double.TryParse(minutes.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.CurrentCulture,out var n) || !double.IsFinite(n) || n<.1 || n>1440){app.Notify("Invalid duration","Enter a duration from 0.1 to 1440 minutes.");minutes.Focus();return;}app.Session.Timer.Start(n*60,DateTimeOffset.UtcNow);app.SaveSession();},true,30);
                    var reset=UI.Button("Reset",()=> {app.Session.Timer.Reset();app.SaveSession();});reset.Padding=new(7,3,7,3);reset.FontSize=10;reset.MinHeight=28;
                    card=UI.Card("Timer",timerTime,UI.Row(minutes,UI.Text("minutes",11,UI.Muted)),UI.Row(timerStart,timerPause,reset));break;
                case "focus":
                    small=true;focusPause=Icons.Button("pause","Pause or resume focus",()=> {if(app.Session.Focus.Running)app.Session.Focus.Pause(DateTimeOffset.UtcNow);else app.Session.Focus.Resume(DateTimeOffset.UtcNow);app.SaveSession();},false,30);
                    var focusStart=Icons.Button("play","Start focus",()=> {app.Session.Focus.Start(app.Settings.FocusMinutes*60,DateTimeOffset.UtcNow,"work");app.SaveSession();},true,30);var focusReset=UI.Button("Reset",()=> {app.Session.Focus.Reset();app.SaveSession();});focusReset.Padding=new(7,3,7,3);focusReset.FontSize=10;focusReset.MinHeight=28;focusPhase.Height=34;focusPhase.Margin=new(0,4,0,4);
                    card=UI.Card("Focus",focusTime,focusPhase,UI.Row(focusStart,focusPause,focusReset));break;
                case "clock":small=true;date.Margin=new(0,6,0,2);card=UI.Card("Local time",clock,date);break;
                case "system":small=true;cpuBar.Foreground=ramBar.Foreground=Brushes.White;cpuBar.Background=ramBar.Background=UI.Brush("#3A3A3C");card=UI.Card("System",cpu,cpuBar,ram,ramBar);break;
                case "battery":small=true;card=UI.Card("Battery",battery);batteryCard=card;break;
                case "shortcuts":
                    var shortcuts=new WrapPanel();foreach(var shortcut in app.Settings.Shortcuts){var button=LaunchButton(shortcut);shortcuts.Children.Add(button);}card=UI.Card("Quick launch",UI.Button("Search commands  ·  Ctrl+K",()=>ShowCommands()),UI.Button("Activities",()=>ShowActivities()),shortcuts);break;
                case "tools":card=UI.Card("Tools",BuildToolsPage());break;
                default:continue;
            }
            card.Width=small?half:full;card.Margin=new(0,0,small?10:0,10);
            if(small)card.MinHeight=widget.Id is "timer" or "focus"?184:120;
            widgets.Children.Add(card);bodyReflow.Add(()=>{card.Width=small?(bodyWidth-10)/2:bodyWidth;widgets.Width=bodyWidth+10;});
        }
    }
    public void Reveal(bool activate=true)
    {
        EnsureBody();Dimensions();
        HideNotice();EndPeekGesture();StopHoverTimers();automaticPreview=false;activityDismiss.Stop();previewKind=activityTitle=activityDetail="";userHidden=false;app.Previews?.SetHidden(false);RememberExternalForeground();visibilityOverride=lastExternalForeground;environmentHidden=false;SetTucked(false);Show();Position();if(expanded){if(activate)Activate();return;}expanded=true;peeking=false;peekPanel.Visibility=Visibility.Collapsed;compact.IsHitTestVisible=true;Motion.Fade(compact,1,120);Dimensions();
        // The layered HWND stays fixed across all three states; only its alpha contour changes.
        scroll.Visibility=Visibility.Visible;scroll.IsHitTestVisible=true;
        ConfigurePerformance();motion.Go(surface,IslandSurface.SurfaceWidthProperty,expandedWidth,app.Settings.AnimationMs);motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,app.Settings.AnimationMs);motion.OnComplete(()=>{});
        motion.Go(surface,IslandSurface.GlassLiftProperty,1,app.Settings.AnimationMs);WakeLiveRendering();
        Motion.Fade(scroll,1,Math.Min(260,app.Settings.AnimationMs));Motion.To(bodySlide,TranslateTransform.YProperty,0,320);
        DismissSatellite();if(activate){Activate();if(app.Settings.CompactLayout && compactTabs.TryGetValue(activePage,out var focusTab))focusTab.Focus();else scroll.Focus();}outside?.Dispose();outside=DiagnosticRun?null:new OutsideClick(this,Collapse,p=>VisiblePoint(p) || sessions.IsDropDownOpen || audioMenu?.IsOpen==true,WakeLiveRendering,()=>!liveRendering && app.Settings.GlassStyle && app.Settings.LiveActivity && Motion.Enabled);Update();
    }
    public void Collapse()
    {
        shelfDropBrowsing=false;
        if(hoverPanelWidget!=null){hoverPanelWidget=null;bodyDirty=true;}
        InvalidateSystemMonitor();
        HideNotice();EndPeekGesture();StopHoverTimers();FlushNotes();automaticPreview=false;activityDismiss.Stop();previewKind=activityTitle=activityDetail="";foreach(var label in marqueeLabels.Values)label.Reset();if(!expanded && !peeking){ApplyEnvironmentTuck();return;}expanded=false;peeking=false;UpdateClosedMediaIcon();outside?.Dispose();outside=null;peekPanel.IsHitTestVisible=false;Motion.Fade(peekPanel,0,90);compact.IsHitTestVisible=true;Motion.Fade(compact,1,120);scroll.IsHitTestVisible=false;Motion.Fade(scroll,0,100);Motion.To(bodySlide,TranslateTransform.YProperty,-8,150);
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,app.Settings.Width,Math.Max(160,app.Settings.AnimationMs*.78));motion.Go(surface,IslandSurface.SurfaceHeightProperty,app.Settings.Height,Math.Max(160,app.Settings.AnimationMs*.78));
        motion.Go(surface,IslandSurface.GlassLiftProperty,0,Math.Max(160,app.Settings.AnimationMs*.78));
        CloseMixer();
        if(TuckRequested){ApplyEnvironmentTuck();return;}
        motion.OnComplete(()=> {if(expanded || peeking)return;peekPanel.Visibility=Visibility.Collapsed;scroll.Visibility=Visibility.Collapsed;UpdateSatellite();});
    }
    public void ToggleVisibility()
    {
        FinishFileDrop(false);bool show=userHidden || !IsVisible;
        RememberExternalForeground();
        Collapse();userHidden=!show;
        visibilityOverride=show?lastExternalForeground:IntPtr.Zero;
        environmentHidden=false;SetTucked(false);
        app.Previews?.SetHidden(!show);if(show){Show();Position();}else Hide();
    }
    private IntPtr visibilityOverride,lastExternalForeground;
    private IntPtr RememberExternalForeground()
    {
        var foreground=ExternalForegroundForRules();
        lastExternalForeground=VisibilityPolicy.LatestExternalForeground(foreground,lastExternalForeground,Native.IsExternalWindow(lastExternalForeground));
        return foreground;
    }
    internal bool EnvironmentHidden=>TuckRequested;
    internal bool UserHidden=>userHidden;
    internal IntPtr VisibilityOverride=>visibilityOverride;
    private void Position()
    {
        if(hwnd==IntPtr.Zero || positioning)return;positioning=true;
        try{var s=app.Settings;var screen=Native.ChooseScreen(s);var scale=Native.Scale(screen);var b=screen.Bounds;var width=(int)Math.Ceiling(ActualWidth*scale);var height=(int)Math.Ceiling(ActualHeight*scale);if(width<1 || height<1)return;
            var point=MonitorFollowPolicy.Place(s,b,scale,width,height,TuckRequested);Native.GetWindowRect(hwnd,out var current);if(current.Left!=point.X || current.Top!=point.Y)Native.SetWindowPos(hwnd,new IntPtr(-1),point.X,point.Y,0,0,0x0010|0x0001);
        }finally{positioning=false;}
    }
    public void CheckEnvironment()
    {
        if(DiagnosticRun && !diagnosticForeground.HasValue && !manualTuck && !app.Settings.AlwaysTucked){Position();return;}
        var foreground=RememberExternalForeground();
        if(userHidden)return;
        visibilityOverride=VisibilityPolicy.RetainOverride(visibilityOverride,foreground);
        bool hide=visibilityOverride==IntPtr.Zero && Native.ShouldHideOn(Native.ChooseScreen(app.Settings),foreground,app.Settings.HideFullscreen,app.Settings.HideMaximized);
        environmentHidden=hide;
        if(TuckRequested)
        {
            bool engaged=PointerInside() || PeekInteractionActive || IsMouseCaptureWithin || (expanded && IsKeyboardFocusWithin);
            if(AutoTuckPolicy.ShouldTuck(TuckRequested,userHidden,expanded,peeking,engaged)){Collapse();ApplyEnvironmentTuck();}
        }
        else SetTucked(false);
        Position();
    }
    private void Update()
    {
        if(!IsVisible || userHidden || IsTucked)return;
        var change=app.CurrentUpdate;if(change is RuntimeChange.Productivity or RuntimeChange.Calendar){if(peeking)UpdatePeek();if(change==RuntimeChange.Calendar)UpdateSatellite();return;}var now=DateTimeOffset.Now;if((change&(RuntimeChange.Media|RuntimeChange.Countdown|RuntimeChange.Settings))!=0)CheckSmartActivities();if((change&(RuntimeChange.Media|RuntimeChange.Settings))!=0)UpdateAlbumTint();ConfigurePerformance();UpdateAudioFeedback();bool On(string id)=>app.Settings.Widgets.Any(w=>w.Id==id && w.Enabled);
        UpdateSatellite();UpdateClosedMediaIcon();Motion.Text(compactClock,now.ToString(app.Settings.Clock24Hour?"HH:mm":"h:mm tt"));UpdateRoadmapStatus(now);
        Motion.Text(summary,FeedbackVisible?feedbackText:app.Notifications.Current is InternalNotice notice?"● "+notice.Title:!ShowMediaPrimary && On("focus") && app.Session.Focus.Remaining(now)>0?(app.Session.Focus.Phase=="break"?"Break  ":"Focus  ")+app.Session.Focus.Display(now):!ShowMediaPrimary && On("timer") && app.Session.Timer.Remaining(now)>0?"Timer  "+app.Session.Timer.Display(now):On("media") && app.Media.Available?app.Media.Title:"");
        if(!liveRendering && !AudioVisualDemand){var session=ActiveCountdown();activity.Sample(0,1,session?.Progress(now)??0,session!=null,session==app.Session.Focus,false,0);}
        WakeLiveRendering();ApplyTitleTypography(!FeedbackVisible && (ShowMediaPrimary || !(On("focus") && app.Session.Focus.Remaining(now)>0) && !(On("timer") && app.Session.Timer.Remaining(now)>0)) && On("media") && app.Media.Available);UpdatePeek();UpdateMixer();UpdateSystemMonitor();if(!expanded)return;updating=true;
        try{
            if((change&(RuntimeChange.Media|RuntimeChange.Settings))!=0)
            {
            SwapText(title,app.Media.Title);SwapText(artist,app.Media.Artist);SwapArtwork(artwork,app.Media.Artwork);artwork.Visibility=app.Media.Artwork==null?Visibility.Collapsed:Visibility.Visible;
            if(play!=null){var icon=app.Media.Playing?"pause":"play";if(playIcon!=icon){playIcon=icon;play.Content=Icons.Create(icon,22,Brushes.Black);}play.IsEnabled=app.Media.CanToggle;next.IsEnabled=app.Media.CanNext;previous.IsEnabled=app.Media.CanPrevious;}
            var key=string.Join("|",app.Media.Sessions.Select(s=>s.SourceAppUserModelId));bool sessionsChanged=key!=sessionKey;if(sessionsChanged){sessionKey=key;sessions.Items.Clear();foreach(var session in app.Media.Sessions)sessions.Items.Add(MediaService.SessionName(session));}sessions.SelectedIndex=app.Media.Sessions.FindIndex(s=>s.SourceAppUserModelId==app.Media.Selected?.SourceAppUserModelId);sessions.Visibility=app.Media.Sessions.Count>1?Visibility.Visible:Visibility.Collapsed;
            if(sessionsChanged && app.Settings.CompactLayout){Dimensions();motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));motion.OnComplete(()=>{});}
            }
            clock.Text=now.ToString(app.Settings.Clock24Hour?(app.Settings.ClockSeconds?"HH:mm:ss":"HH:mm"):(app.Settings.ClockSeconds?"h:mm:ss tt":"h:mm tt"));date.Visibility=app.Settings.ShowDate?Visibility.Visible:Visibility.Collapsed;date.Text=now.ToString("ddd, dd MMM",System.Globalization.CultureInfo.GetCultureInfo("en-US"));timerTime.Text=app.Session.Timer.Display(now);focusTime.Text=app.Session.Focus.Remaining(now)>0?app.Session.Focus.Display(now):Countdown.FormatSeconds(app.Settings.FocusMinutes*60);
            if(timerPause!=null){Motion.Icon(timerPause,app.Session.Timer.Running?"pause":"play",14);timerPause.IsEnabled=app.Session.Timer.Remaining(now)>0;}if(focusPause!=null){Motion.Icon(focusPause,app.Session.Focus.Running?"pause":"play",14);focusPause.IsEnabled=app.Session.Focus.Remaining(now)>0;}
            focusPhase.Text=app.Session.Focus.Remaining(now)>0?(app.Session.Focus.Phase=="break"?"Break":"Focusing")+(app.Session.Focus.Running?"":" · paused"):$"{app.Settings.FocusMinutes} min focus\n{app.Settings.BreakMinutes} min break";
            if((change&(RuntimeChange.System|RuntimeChange.Settings))!=0){cpu.Text=$"CPU  {app.Usage.cpu:0}%";ram.Text=$"RAM  {app.Usage.used:0.0}/{app.Usage.total:0.0} GB";Motion.Progress(cpuBar,app.Usage.cpu);Motion.Progress(ramBar,app.Usage.total>0?app.Usage.used/app.Usage.total*100:0);}
            if(!volume.IsMouseCaptureWithin && !volume.IsKeyboardFocusWithin)volume.Value=app.Audio.Volume*100;volume.IsEnabled=app.Audio.Available;volumeText.Text=app.Audio.Available?$"{app.Audio.Volume*100:0}%":"—";volume.ToolTip=app.Audio.Device;
            var currentMute=app.Audio.Muted?"muted":"volume";if(mute!=null && muteIcon!=currentMute){muteIcon=currentMute;mute.Content=Icons.Create(currentMute,18);}
            var power=Forms.SystemInformation.PowerStatus;bool hasBattery=(power.BatteryChargeStatus & Forms.BatteryChargeStatus.NoSystemBattery)==0 && power.BatteryChargeStatus!=Forms.BatteryChargeStatus.Unknown;if(batteryCard!=null)batteryCard.Visibility=hasBattery?Visibility.Visible:Visibility.Collapsed;battery.Text=$"{power.BatteryLifePercent*100:0}% · "+(power.PowerLineStatus==Forms.PowerLineStatus.Online?"Charging":"Battery");
            UpdateTimeline();
        }finally{updating=false;}
    }
    public void Cleanup(){pointerCheck.Stop();app.SettingsChanged-=ConfigureDisplayFollow;shelfThumbnails.Clear();CleanupInteractionFeatures();CleanupActivities();StopFeedback();StopSystemMonitor();FlushNotes();app.Productivity.Changed-=ProductivityChanged;app.Calendar.Changed-=CalendarChanged;app.Awake.Changed-=AwakeChanged;activityDismiss.Stop();if(hotkeyRegistered)UnregisterHotKey(hwnd,HotkeyId);if(visibilityHotkeyRegistered)UnregisterHotKey(hwnd,VisibilityHotkeyId);if(tuckHotkeyRegistered)UnregisterHotKey(hwnd,TuckHotkeyId);hotkeyRegistered=visibilityHotkeyRegistered=tuckHotkeyRegistered=false;StopHoverTimers();liveTick.Stop();StopLiveRendering();seekDelay.Stop();tuckMotion.Dispose();motion.Dispose();outside?.Dispose();app.SettingsChanged-=ApplySettings;app.Updated-=Update;Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;}
}
