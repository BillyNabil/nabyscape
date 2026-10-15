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
    internal bool SystemSummaryVisible=>IsVisible && WidgetOn("system") && (peeking && peekId=="system" || expanded && !activitiesOpen && !commandOpen && (app.Settings.CompactLayout?activePage=="status":!classicMixerOpen));
    private bool SatelliteEligible=>app.Settings.ActivitySatellite && WidgetOn("media") && (app.Media.Available || DiagnosticRun && DiagnosticSatelliteMedia) && !expanded && (!peeking || satellitePreview) && !FeedbackVisible && !environmentHidden && !userHidden && IsVisible;
    private bool ShowMediaPrimary=>SatelliteEligible && SecondaryActivity.Select(app.Session.Timer,app.Session.Focus,DateTimeOffset.UtcNow,WidgetOn("timer"),WidgetOn("focus"))!=null && SatelliteHasRoom();
    private bool SatelliteHasRoom()
    {
        if(!IsLoaded)return false;var screen=Native.ChooseScreen(app.Settings);double scale=Native.Scale(screen);var p=PointToScreen(new Point((ActualWidth+app.Settings.Width)/2+8,0));
        return p.X+82*scale<=screen.Bounds.Right-8*scale;
    }
    private void UpdateSatellite()
    {
        if(!peeking)satellitePreview=false;
        satelliteKind=ShowMediaPrimary?SecondaryActivity.Select(app.Session.Timer,app.Session.Focus,DateTimeOffset.UtcNow,WidgetOn("timer"),WidgetOn("focus")):null;
        if(satelliteKind==null){satelliteWindow?.Hide();return;}
        if(satelliteWindow==null)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};satelliteRing.Margin=new(0,0,6,0);row.Children.Add(satelliteRing);row.Children.Add(satelliteTime);
            var button=UI.Button("Open countdown",()=>OpenSatellite(true));button.Content=row;button.Padding=new(8,0,8,0);button.Margin=new(0);button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.MinHeight=36;button.ToolTip="Hover to preview · click to open";
            var border=new Border{CornerRadius=new(18),Background=Brushes.Black,Child=button};
            satelliteWindow=new Window{Width=82,Height=36,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Content=border,Owner=this};
            satelliteWindow.SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(satelliteWindow).Handle)?.AddHook((IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled)=>{if(message==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});
            satelliteWindow.MouseEnter+=(_,_)=>{leaveDelay.Stop();OpenSatellite(false);};
            satelliteWindow.MouseLeave+=(_,_)=>{if(satellitePreview && peeking)leaveDelay.Start();};
            satelliteWindow.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Collapse();e.Handled=true;}};
        }
        var countdown=satelliteKind=="timer"?app.Session.Timer:app.Session.Focus;var now=DateTimeOffset.UtcNow;satelliteTime.Text=countdown.Display(now);satelliteRing.Sample(0,1,countdown.Progress(now),true,satelliteKind=="focus",false,0);
        var p=PointToScreen(new Point((ActualWidth+app.Settings.Width)/2+8,0));var dpi=VisualTreeHelper.GetDpi(this);satelliteWindow.Left=p.X/dpi.DpiScaleX;satelliteWindow.Top=p.Y/dpi.DpiScaleY;
        if(!satelliteWindow.IsVisible)satelliteWindow.Show();
    }
    private void OpenSatellite(bool panel)
    {
        var kind=satelliteKind;if(kind==null)return;
        if(panel){Reveal();SelectPage("focus");return;}
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
    private void CleanupInteractionFeatures(){CancelCoverGesture();satelliteWindow?.Close();satelliteWindow=null;}
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
        var g=new CoverGesture();g.Begin();test("Cover short release is click",g.End());g.Begin();test("Cover hold fires once state",g.Hold());test("Held cover does not also click",!g.End());g.Begin();g.Cancel();test("Cancelled drag does not click or hold",!g.End() && !g.Hold());
        var id=t.RunId;t.Start(1,now);test("New countdown start changes ownership",id!=t.RunId);id=t.RunId;t.Pause(now);t.Resume(now);test("Countdown pause preserves ownership",id==t.RunId);
        var legacy=new Settings{HoverViews=new(){new(){Id="media"}}};legacy.Normalize();test("System hover migrates disabled",legacy.HoverViews.Single(x=>x.Id=="system").Enabled==false);
        var clone=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(new Settings{CoverGestures=false,ActivitySatellite=false,DropReveal=false,DetachablePreviews=false}),clone);
        test("Interaction visual preferences round trip",!clone.CoverGestures && !clone.ActivitySatellite && !clone.DropReveal && !clone.DetachablePreviews);
    }
}
