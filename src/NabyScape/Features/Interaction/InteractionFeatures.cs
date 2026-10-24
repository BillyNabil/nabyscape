using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NabyScape;

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
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){CancelCoverGesture();}};
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
    internal bool SystemSummaryVisible=>IsVisible && (peeking && peekId=="system" || WidgetOn("system") && expanded && !commandOpen && (app.Settings.CompactLayout?activePage=="status":!classicMixerOpen));
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
        else OpenHoverPanel(kind);
    }
    private void CleanupInteractionFeatures()=>CancelCoverGesture();
}

internal static class InteractionFeatureTests
{
    internal static void Run(Action<string,bool> test)
    {
        var now=DateTimeOffset.UtcNow;var t=new Countdown();
        var g=new CoverGesture();g.Begin();test("Cover short release is click",g.End());g.Begin();test("Cover hold fires once state",g.Hold());test("Held cover does not also click",!g.End());g.Begin();g.Cancel();test("Cancelled drag does not click or hold",!g.End() && !g.Hold());
        var id=t.RunId;t.Start(1,now);test("New countdown start changes ownership",id!=t.RunId);id=t.RunId;t.Pause(now);t.Resume(now);test("Countdown pause preserves ownership",id==t.RunId);
        var legacy=new Settings{HoverViews=new(){new(){Id="media"}}};legacy.Normalize();test("System hover migrates disabled",legacy.HoverViews.Single(x=>x.Id=="system").Enabled==false);
        var clone=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(new Settings{CoverGestures=false,DropReveal=false,DetachablePreviews=false}),clone);
        test("Interaction visual preferences round trip",!clone.CoverGestures && !clone.DropReveal && !clone.DetachablePreviews);
    }
}
