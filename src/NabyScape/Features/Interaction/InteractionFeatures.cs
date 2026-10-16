using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NabyScape;

internal static class SecondaryActivity
{
    internal static string? Select(Countdown timer,Countdown focus,DateTimeOffset now,bool timerOn=true,bool focusOn=true)
    {
        bool t=timerOn && timer.Running && timer.Remaining(now)>0,f=focusOn && focus.Running && focus.Remaining(now)>0;
        if(!t && !f)return null;
        return t && (!f || timer.Deadline<=focus.Deadline)?"timer":"focus";
    }
    internal static string? SelectSource(string mode,string? countdown,bool upcoming,bool systemOn,bool toolsOn,bool clockOn)
    {
        return mode switch
        {
            "Countdown"=>countdown,
            "CPU"=>systemOn?"cpu":null,
            "RAM"=>systemOn?"ram":null,
            "Calendar"=>toolsOn && upcoming?"calendar":null,
            "Clock"=>clockOn?"clock":null,
            _=>countdown??(toolsOn && upcoming?"calendar":systemOn?"cpu":clockOn?"clock":null)
        };
    }
}

internal sealed class CoverGesture
{
    internal bool Pressed {get;private set;}
    internal bool Held {get;private set;}
    internal void Begin(){Pressed=true;Held=false;}
    internal bool Hold(){if(!Pressed)return false;Held=true;return true;}
    internal bool End(){bool click=Pressed && !Held;Cancel();return click;}
    internal void Cancel(){Pressed=false;Held=false;}
}

public sealed partial class OverlayWindow
{
    private readonly CoverGesture coverGesture=new();
    private readonly DispatcherTimer coverHold=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private Point coverDown;
    private Window? satelliteWindow;
    private readonly TextBlock satelliteTime=UI.Text("",11);
    private readonly ActivityVisual satelliteRing=new(){Width=19,Height=19};
    private string? satelliteKind;
    private Border? satelliteIcon;
    private Button? satelliteButton;
    private string? satelliteIconKind;
    private IslandSurface? satelliteSurface;
    private Grid? satelliteRoot;
    private readonly TranslateTransform satelliteSlide=new();
    private readonly DispatcherTimer satelliteDismiss=new();
    private bool satellitePreview;
    internal bool DiagnosticSatelliteMedia {get;set;}
    internal bool DiagnosticSatellitePointerPresent {get;set;}
    private bool SatellitePointerInside=>DiagnosticRun?DiagnosticSatellitePointerPresent:satelliteWindow?.IsMouseOver==true;
    private bool SatelliteVisible=>satelliteWindow?.IsVisible==true;
    private readonly TextBlock peekSystem=UI.Text("",17);
    private TextBox? mainNoteEditor;
    private bool syncingNote;
    private void InitializeInteractionFeatures()
    {
        satelliteDismiss.Tick+=(_,_)=>{satelliteDismiss.Stop();if(!ShowMediaPrimary){satelliteWindow?.Hide();if(!peeking)satelliteKind=null;}};
        closedCover.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            if(!app.Settings.CoverGestures || expanded || peeking || !ClosedCoverVisible)return;
            StopHoverTimers();coverDown=e.GetPosition(closedCover);coverGesture.Begin();closedCover.CaptureMouse();coverHold.Start();e.Handled=true;
        };
        closedCover.PreviewMouseMove+=(_,e)=>
        {
            if(!coverGesture.Pressed)return;var delta=e.GetPosition(closedCover)-coverDown;
            if(Math.Abs(delta.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y)>SystemParameters.MinimumVerticalDragDistance)CancelCoverGesture();
            e.Handled=true;
        };
        closedCover.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            if(!closedCover.IsMouseCaptured)return;coverHold.Stop();bool click=coverGesture.End();closedCover.ReleaseMouseCapture();if(click)_=app.Media.Command("toggle");e.Handled=true;
        };
        closedCover.LostMouseCapture+=(_,_)=>{coverHold.Stop();coverGesture.Cancel();};
        coverHold.Tick+=(_,_)=>{coverHold.Stop();if(coverGesture.Hold())OpenCoverSessions();};
        compact.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space && Keyboard.Modifiers==ModifierKeys.Control && app.Settings.CoverGestures){_=app.Media.Command("toggle");e.Handled=true;}else if(e.Key==Key.M && Keyboard.Modifiers==ModifierKeys.Control && app.Settings.CoverGestures){OpenCoverSessions();e.Handled=true;}};
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){CancelCoverGesture();satelliteWindow?.Hide();}};
    }
    private void CancelCoverGesture(){coverHold.Stop();coverGesture.Cancel();if(closedCover.IsMouseCaptured)closedCover.ReleaseMouseCapture();}
    private void OpenCoverSessions()
    {
        if(userHidden || !app.Media.Available)return;
        audioMenu?.SetCurrentValue(ContextMenu.IsOpenProperty,false);
        var menu=new ContextMenu{PlacementTarget=closedCover};audioMenu=menu;
        foreach(var session in app.Media.Sessions.ToArray())
        {var selected=session;var item=new MenuItem{Header=MediaService.SessionName(session),IsCheckable=true,IsChecked=session==app.Media.Selected};item.Click+=(_,_)=>app.Media.Select(selected);menu.Items.Add(item);}
        if(menu.Items.Count==0)menu.Items.Add(new MenuItem{Header="No media sessions",IsEnabled=false});
        menu.Closed+=(_,_)=>{if(audioMenu==menu)audioMenu=null;};menu.IsOpen=true;
    }
    internal bool SystemSummaryVisible=>IsVisible && (peeking && satellitePreview && satelliteKind=="clock" && peekId=="system" || WidgetOn("system") && (SatelliteNeedsSystemSamples || peeking && peekId=="system" || expanded && !activitiesOpen && !commandOpen && (app.Settings.CompactLayout?activePage=="status":!classicMixerOpen)));
    private bool SatelliteEligible=>app.Settings.ActivitySatellite && WidgetOn("media") && (app.Media.Available || DiagnosticRun && DiagnosticSatelliteMedia) && !expanded && !peeking && surface.SurfaceHeight<=app.Settings.Height+.5 && surface.SurfaceWidth<=app.Settings.Width+.5 && !FeedbackVisible && !TuckRequested && !userHidden && IsVisible;
    private CalendarEvent? SatelliteEvent=>app.Calendar.Events.Where(e=>e.End>DateTimeOffset.UtcNow && e.Start<=DateTimeOffset.UtcNow.AddHours(24)).OrderBy(e=>e.Start).FirstOrDefault();
    private string? SelectedSatellite=>SecondaryActivity.SelectSource(app.Settings.SatelliteSource,SecondaryActivity.Select(app.Session.Timer,app.Session.Focus,DateTimeOffset.UtcNow,WidgetOn("timer"),WidgetOn("focus")),SatelliteEvent!=null,WidgetOn("system"),WidgetOn("tools"),WidgetOn("clock"));
    private bool ShowMediaPrimary=>SatelliteEligible && SelectedSatellite!=null && SatelliteHasRoom();
    private bool SatelliteNeedsSystemSamples=>SatelliteEligible && SatelliteHasRoom() && SelectedSatellite is "cpu" or "ram";
    private string? SatellitePeekKind=>satelliteKind is "cpu" or "ram" or "clock"?"system":satelliteKind;
    private double SatelliteShoulder=>surface.Notch?Math.Min(16,app.Settings.Height*.36):0;
    private double SatelliteWidth=>76+2*SatelliteShoulder;
    private Point SatelliteAnchor=>PointToScreen(new Point((ActualWidth+app.Settings.Width)/2-2*SatelliteShoulder+8,0));
    private bool SatelliteHasRoom()
    {
        if(!IsLoaded)return false;var screen=Native.ChooseScreen(app.Settings);double scale=Native.Scale(screen);var p=SatelliteAnchor;
        return p.X+SatelliteWidth*scale<=screen.Bounds.Right-8*scale;
    }
    private void UpdateSatellite()
    {
        if(!peeking)satellitePreview=false;
        var selectedNext=ShowMediaPrimary?SelectedSatellite:null;
        if(selectedNext==null){DismissSatellite(userHidden || TuckRequested || !IsVisible);if(!peeking && satelliteWindow?.IsVisible!=true)satelliteKind=null;return;}
        satelliteKind=selectedNext;satelliteDismiss.Stop();
        if(satelliteWindow==null)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};satelliteRing.Width=satelliteRing.Height=16;satelliteRing.Margin=new(0,0,5,0);row.Children.Add(satelliteRing);satelliteIcon=new Border{Width=14,Height=14,Margin=new(0,0,5,0)};row.Children.Add(satelliteIcon);row.Children.Add(satelliteTime);
            var button=UI.Button("Open companion",()=>OpenSatellite(true));satelliteButton=button;button.Content=row;button.Padding=new(8,0,8,0);button.Margin=new(0);button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.MinHeight=0;button.ToolTip="Hover to preview · click to open";
            satelliteSurface=new IslandSurface();satelliteRoot=new Grid{RenderTransform=satelliteSlide};satelliteRoot.Children.Add(satelliteSurface);satelliteRoot.Children.Add(button);
            satelliteWindow=new Window{Width=SatelliteWidth,Height=app.Settings.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Content=satelliteRoot,Owner=this};
            satelliteWindow.SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(satelliteWindow).Handle)?.AddHook((IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled)=>{if(message==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});
            satelliteWindow.MouseEnter+=(_,_)=>{leaveDelay.Stop();OpenSatellite(false);};
            satelliteWindow.MouseLeave+=(_,_)=>{if(satellitePreview && peeking)leaveDelay.Start();};
            satelliteWindow.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Collapse();e.Handled=true;}};
        }
        var now=DateTimeOffset.UtcNow;bool countdownKind=satelliteKind is "timer" or "focus";
        satelliteRing.Visibility=countdownKind?Visibility.Visible:Visibility.Collapsed;
        satelliteIcon!.Visibility=countdownKind || satelliteKind is "cpu" or "ram"?Visibility.Collapsed:Visibility.Visible;
        if(satelliteIconKind!=satelliteKind){satelliteIcon.Child=Icons.Create(satelliteKind=="calendar"?"calendar":satelliteKind=="clock"?"clock":"nav-status",16);satelliteIconKind=satelliteKind;}
        string label;
        if(countdownKind){var countdown=satelliteKind=="timer"?app.Session.Timer:app.Session.Focus;satelliteTime.Text=countdown.Display(now);satelliteRing.Sample(0,1,countdown.Progress(now),true,satelliteKind=="focus",false,0);label=satelliteKind=="timer"?"Timer":"Focus";}
        else if(satelliteKind=="cpu"){satelliteTime.Text=$"CPU {app.Usage.cpu:0}%";label="CPU usage";}
        else if(satelliteKind=="ram"){satelliteTime.Text=app.Usage.total>0?$"RAM {app.Usage.used/app.Usage.total*100:0}%":"—";label="Memory usage";}
        else if(satelliteKind=="calendar"){var next=SatelliteEvent;satelliteTime.Text=next?.Start<=now?"Now":next?.Start.ToLocalTime().ToString("HH:mm")??"—";label="Calendar · "+next?.Title;}
        else{satelliteTime.Text=DateTime.Now.ToString(app.Settings.Clock24Hour?"HH:mm":"h:mm tt");label="Clock";}
        UI.Named(satelliteButton!,"Open "+label);satelliteButton!.ToolTip=label+" · Hover to preview · click to open";
        var settings=app.Settings;double width=SatelliteWidth,height=settings.Height;
        satelliteWindow.Width=width;satelliteWindow.Height=height;
        satelliteSurface!.SurfaceWidth=width;satelliteSurface.SurfaceHeight=height;satelliteSurface.Radius=settings.Radius;satelliteSurface.Notch=surface.Notch;satelliteSurface.Fill=surface.Fill;satelliteSurface.GlassStyle=surface.GlassStyle;satelliteSurface.SetMaterial(settings.Background,settings.GlassStrength);satelliteSurface.AlbumTint=surface.AlbumTint;satelliteSurface.Refresh();
        satelliteRoot!.Clip=satelliteSurface.Contour;
        satelliteButton.Padding=new Thickness(SatelliteShoulder+6,0,SatelliteShoulder+6,0);
        var p=SatelliteAnchor;var dpi=VisualTreeHelper.GetDpi(this);satelliteWindow.Left=p.X/dpi.DpiScaleX;satelliteWindow.Top=p.Y/dpi.DpiScaleY;
        if(!satelliteWindow.IsVisible){satelliteWindow.Opacity=0;satelliteSlide.Y=-6;satelliteWindow.Show();}
        Motion.Fade(satelliteWindow,Opacity,SatelliteTransitionMs);Motion.To(satelliteSlide,TranslateTransform.YProperty,0,SatelliteTransitionMs);
    }
    private double SatelliteTransitionMs=>Math.Clamp(app.Settings.AnimationMs*.55,120,240);
    private void DismissSatellite(bool immediate=false)
    {
        if(satelliteWindow?.IsVisible!=true)return;
        if(immediate || !Motion.Enabled){satelliteDismiss.Stop();satelliteWindow.Hide();satelliteWindow.Opacity=0;return;}
        if(satelliteDismiss.IsEnabled)return;
        Motion.Fade(satelliteWindow,0,SatelliteTransitionMs);Motion.To(satelliteSlide,TranslateTransform.YProperty,-6,SatelliteTransitionMs);
        satelliteDismiss.Interval=TimeSpan.FromMilliseconds(SatelliteTransitionMs+10);satelliteDismiss.Start();
    }
    private void OpenSatellite(bool panel)
    {
        var kind=SatellitePeekKind;if(kind==null || userHidden || TuckRequested || !IsVisible)return;
        if(panel){OpenFeaturePage(kind);return;}
        satellitePreview=true;previewKind=kind;automaticPreview=true;PreviewActivity();SelectPeek(kind);automaticPreview=false;
        satellitePreview=peeking && peekId==kind;
    }
    private void SyncNoteEditors()
    {
        if(syncingNote)return;syncingNote=true;
        try
        {
            foreach(var editor in new[]{mainNoteEditor,peekNote})
            {
                if(editor==null || editor.Text==app.Productivity.Note)continue;
                int start=editor.SelectionStart,length=editor.SelectionLength;double offset=editor.VerticalOffset;
                editor.Text=app.Productivity.Note;editor.Select(Math.Min(start,editor.Text.Length),Math.Min(length,Math.Max(0,editor.Text.Length-start)));editor.ScrollToVerticalOffset(offset);
            }
        }finally{syncingNote=false;}
    }
    internal void OpenFeaturePage(string kind)
    {
        if(kind is "notes" or "calendar" or "files")ShowTools(kind);
        else{Reveal();SelectPage(kind=="system"?"status":kind is "timer" or "focus"?"focus":"media");}
    }
    private void CleanupInteractionFeatures(){satelliteDismiss.Stop();CancelCoverGesture();satelliteWindow?.Close();satelliteWindow=null;}
}

internal static class InteractionFeatureTests
{
    internal static void Run(Action<string,bool> test)
    {
        var now=DateTimeOffset.UtcNow;var t=new Countdown();var f=new Countdown();
        test("Satellite excludes empty countdowns",SecondaryActivity.Select(t,f,now)==null);
        t.Start(120,now);f.Start(60,now);test("Satellite selects earliest running deadline",SecondaryActivity.Select(t,f,now)=="focus");
        t.Start(60,now);test("Satellite timer wins deadline tie",SecondaryActivity.Select(t,f,now)=="timer");
        t.Pause(now);test("Satellite ignores paused timer",SecondaryActivity.Select(t,f,now)=="focus");
        test("Satellite respects disabled countdown widgets",SecondaryActivity.Select(t,f,now,true,false)==null);
        test("Satellite excludes overdue deadlines",SecondaryActivity.Select(t,f,now.AddHours(1))==null);
        test("Automatic companion uses CPU without countdowns",SecondaryActivity.SelectSource("Auto",null,false,true,true,true)=="cpu");
        test("Automatic companion prefers countdown over calendar",SecondaryActivity.SelectSource("Auto","timer",true,true,true,true)=="timer");
        test("Automatic companion prefers nearby calendar over CPU",SecondaryActivity.SelectSource("Auto",null,true,true,true,true)=="calendar");
        test("Fixed RAM companion ignores active countdown",SecondaryActivity.SelectSource("RAM","timer",true,true,true,true)=="ram");
        test("Fixed CPU companion respects disabled system",SecondaryActivity.SelectSource("CPU",null,false,false,true,true)==null);
        test("Fixed calendar hides without nearby event",SecondaryActivity.SelectSource("Calendar",null,false,true,true,true)==null);
        test("Fixed calendar respects disabled tools",SecondaryActivity.SelectSource("Calendar",null,true,true,false,true)==null);
        test("Automatic companion falls back to clock",SecondaryActivity.SelectSource("Auto",null,false,false,true,true)=="clock");
        test("Companion hides when all eligible widgets are disabled",SecondaryActivity.SelectSource("Auto",null,true,false,false,false)==null);
        test("Countdown-only mode keeps original behavior",SecondaryActivity.SelectSource("Countdown",null,true,true,true,true)==null);
        var invalid=new Settings{SatelliteSource="unknown"};invalid.Normalize();test("Unknown companion source migrates to Automatic",invalid.SatelliteSource=="Auto");
        var profile=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(new Settings{SatelliteSource="RAM"}),profile);test("Companion source survives layout profile round trip",profile.SatelliteSource=="RAM");
        var g=new CoverGesture();g.Begin();test("Cover short release is click",g.End());g.Begin();test("Cover hold fires once state",g.Hold());test("Held cover does not also click",!g.End());g.Begin();g.Cancel();test("Cancelled drag does not click or hold",!g.End() && !g.Hold());
        var id=t.RunId;t.Start(1,now);test("New countdown start changes ownership",id!=t.RunId);id=t.RunId;t.Pause(now);t.Resume(now);test("Countdown pause preserves ownership",id==t.RunId);
        var legacy=new Settings{HoverViews=new(){new(){Id="media"}}};legacy.Normalize();test("System hover migrates disabled",legacy.HoverViews.Single(x=>x.Id=="system").Enabled==false);
        var clone=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(new Settings{CoverGestures=false,ActivitySatellite=false,DropReveal=false,DetachablePreviews=false}),clone);
        test("Interaction visual preferences round trip",!clone.CoverGestures && !clone.ActivitySatellite && !clone.DropReveal && !clone.DetachablePreviews);
    }
}
