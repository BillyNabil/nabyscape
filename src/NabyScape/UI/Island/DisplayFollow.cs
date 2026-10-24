using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace NabyScape;

internal sealed class MonitorFollowPolicy
{
    private string candidate="";
    private long since;
    internal void Reset(){candidate="";since=0;}
    internal static string Target(string[] available,string primary,string mode,string stored,string? followed)
    {string preferred=mode=="Follow"?followed??stored:mode=="Primary"?primary:stored;return Array.Exists(available,name=>name==preferred)?preferred:primary;}
    internal static System.Drawing.Point Place(Settings s,System.Drawing.Rectangle bounds,double scale,int width,int height,bool tucked,int paintedWidth=0)
    {
        double compact=s.Width*scale;int painted=paintedWidth>0?paintedWidth:width;
        int inset=Math.Max(0,(width-painted)/2),gap=(int)Math.Round(s.DockInset*scale);
        int x=bounds.Left+(int)((bounds.Width-compact)*s.Horizontal/100+compact/2-width/2d);
        int y=bounds.Top+(int)((tucked || s.Preset=="Mac" && s.DockEdge=="Top"?0:s.Top)*scale);
        if(s.DockEdge=="Bottom")y=bounds.Bottom-height-(tucked?0:gap);
        else if(s.DockEdge is "Left" or "Right")
        {
            x=s.DockEdge=="Left"?bounds.Left-inset+(tucked?0:gap):bounds.Right-painted-inset-(tucked?0:gap);
            y=bounds.Top+(int)((bounds.Height-height)*s.DockVertical/100);
        }
        return new(Math.Clamp(x,bounds.Left-inset,Math.Max(bounds.Left-inset,bounds.Right-width+inset)),Math.Clamp(y,bounds.Top,Math.Max(bounds.Top,bounds.Bottom-height)));
    }
    internal string? Observe(string current,string pointer,long now,bool blocked,bool hidden,bool immediate=false)
    {
        if(hidden || blocked || pointer==current){Reset();return null;}
        if(immediate){Reset();return pointer;}
        if(candidate!=pointer){candidate=pointer;since=now;return null;}
        if(now-since<250)return null;
        Reset();return pointer;
    }
}
public sealed partial class OverlayWindow
{
    private readonly DispatcherTimer followSlide=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly System.Diagnostics.Stopwatch followClock=new();
    private bool followMoving;
    private System.Drawing.Point followStart,followTarget;
    private double followDuration;
    private readonly MonitorFollowPolicy followPolicy=new();
    private readonly DispatcherTimer pointerCheck=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private Forms.Screen IslandScreen=>Native.ChooseScreen(app.Settings);
    private void InitializeDisplayFollow()
    {
        followSlide.Tick+=(_,_)=>
        {
            if(userHidden || !IsVisible || !Motion.Enabled){StopFollowSlide();Position();return;}
            double t=Math.Clamp(followClock.Elapsed.TotalMilliseconds/followDuration,0,1),ease=1-Math.Pow(1-t,3);
            int x=(int)Math.Round(followStart.X+(followTarget.X-followStart.X)*ease),y=(int)Math.Round(followStart.Y+(followTarget.Y-followStart.Y)*ease);
            Native.SetWindowPos(hwnd,IntPtr.Zero,x,y,0,0,0x0010|0x0001|0x0004);
            if(t>=1){StopFollowSlide();Position();}
        };
        Closed+=(_,_)=>{followSlide.Stop();pointerCheck.Stop();};
        pointerCheck.Tick+=(_,_)=>{ResolveFollow();TrackAssistantPointer();CheckHiddenTuckEdge();};
        IsVisibleChanged+=(_,_)=>ConfigureDisplayFollow();
        app.SettingsChanged+=ConfigureDisplayFollow;
        ConfigureDisplayFollow();
    }
    private void ConfigureDisplayFollow()
    {
        if(app.Settings.DisplayMode!="Follow"){StopFollowSlide();Native.FollowMonitor=null;}
        if(userHidden || !IsVisible || (app.Settings.DisplayMode!="Follow" && !AssistantPointerTracking && !HiddenTuckHover))
        {
            pointerCheck.Stop();followPolicy.Reset();
            if(app.Settings.DisplayMode!="Follow"){StopFollowSlide();Native.FollowMonitor=null;}
            return;
        }
        if(!pointerCheck.IsEnabled){followPolicy.Reset();ResolveFollow(true);pointerCheck.Start();}
    }
    private bool HiddenTuckHover=>IsTucked && app.Settings.TuckEdge==0 && !userHidden;
    private void CheckHiddenTuckEdge()
    {
        if(!HiddenTuckHover || !Native.CursorPoint(out var point))return;
        var screen=IslandScreen;double scale=Native.Scale(screen);var b=app.Settings.DockEdge=="Top"?screen.Bounds:screen.WorkingArea;
        double center=b.Left+(b.Width-app.Settings.Width*scale)*app.Settings.Horizontal/100+app.Settings.Width*scale/2;
        double cy=b.Top+(b.Height-app.Settings.Height*scale)*app.Settings.DockVertical/100+app.Settings.Height*scale/2;
        bool hit=app.Settings.DockEdge switch
        {
            "Left"=>Math.Abs(point.X-b.Left)<3 && Math.Abs(point.Y-cy)<app.Settings.Height*scale/2,
            "Right"=>Math.Abs(point.X-(b.Right-1))<3 && Math.Abs(point.Y-cy)<app.Settings.Height*scale/2,
            "Bottom"=>Math.Abs(point.Y-(b.Bottom-1))<3 && Math.Abs(point.X-center)<app.Settings.Width*scale/2,
            _=>Math.Abs(point.Y-b.Top)<3 && Math.Abs(point.X-center)<app.Settings.Width*scale/2
        };
        if(hit)RevealTuckedPreview();
    }
    private void StopFollowSlide(){followSlide.Stop();followClock.Stop();followMoving=false;}
    private bool FollowInteraction=>followMoving || mediaControls?.IsOpen==true || MinimalWidgets.MenuOpen(this) || fileDragActive || PeekInteractionActive || IsMouseOver || IsKeyboardFocusWithin || Mouse.Captured!=null || Mouse.LeftButton==MouseButtonState.Pressed || Mouse.RightButton==MouseButtonState.Pressed || app.Windows.Cast<Window>().Any(w=>w.IsKeyboardFocusWithin && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase);
    private void ResolveFollow(bool immediate=false)
    {
        if(DiagnosticRun || app.Settings.DisplayMode!="Follow")return;
        if(userHidden || !IsVisible){followPolicy.Reset();return;}
        if(!Native.CursorPoint(out var point))return;
        var target=Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y));
        var next=followPolicy.Observe(IslandScreen.DeviceName,target.DeviceName,Environment.TickCount64,FollowInteraction,userHidden,immediate);
        if(next==null)return;
        Native.GetWindowRect(hwnd,out var previous);
        followStart=new(previous.Left,previous.Top);followTarget=followStart;
        followMoving=!immediate && app.Settings.FollowMonitorAnimation && Motion.Enabled && !IsTucked;
        Native.FollowMonitor=next;ReflowDisplay();
        if(followMoving){followDuration=app.Settings.FollowMonitorDuration;followClock.Restart();followSlide.Start();}
    }
    private void ReflowDisplay()
    {
        // Reflow existing controls: relocating never navigates or rebuilds an editor.
        Dimensions();foreach(var reflow in bodyReflow)reflow();Dimensions();
        if(peeking)ApplyPeekGeometry();
        Width=WindowHostWidth;Height=Math.Max(HostHeight,app.Settings.Height);
        refreshHz=Native.RefreshRate(IslandScreen);
        surface.BeginUpdate();try{motion.Snap(surface,IslandSurface.SurfaceWidthProperty,expanded?expandedWidth:peeking?PeekSurfaceWidth:ClosedSurfaceWidth);motion.Snap(surface,IslandSurface.SurfaceHeightProperty,expanded?expandedHeight:peeking?(assistantPeeking?AssistantPeekHeight:peekDetailed?app.Settings.HoverHeight:100):app.Settings.Height);}finally{surface.EndUpdate();}
        Position();CheckEnvironment();SyncAssistantVisibility();
    }
}
