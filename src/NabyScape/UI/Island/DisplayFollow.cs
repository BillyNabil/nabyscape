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
    internal static System.Drawing.Point Place(Settings s,System.Drawing.Rectangle bounds,double scale,int width,int height,bool tucked)
    {
        double compact=s.Width*scale;
        int x=bounds.Left+(int)((bounds.Width-compact)*s.Horizontal/100+compact/2-width/2d);
        int y=bounds.Top+(int)((tucked || s.Preset=="Mac"?0:s.Top)*scale);
        return new(Math.Clamp(x,bounds.Left,Math.Max(bounds.Left,bounds.Right-width)),Math.Clamp(y,bounds.Top,Math.Max(bounds.Top,bounds.Bottom-height)));
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
    private readonly MonitorFollowPolicy followPolicy=new();
    private readonly DispatcherTimer pointerCheck=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private Forms.Screen IslandScreen=>Native.ChooseScreen(app.Settings);
    private void InitializeDisplayFollow()
    {
        pointerCheck.Tick+=(_,_)=>ResolveFollow();
        IsVisibleChanged+=(_,_)=>ConfigureDisplayFollow();
        app.SettingsChanged+=ConfigureDisplayFollow;
        ConfigureDisplayFollow();
    }
    private void ConfigureDisplayFollow()
    {
        if(app.Settings.DisplayMode!="Follow" || userHidden || !IsVisible)
        {
            pointerCheck.Stop();followPolicy.Reset();
            if(app.Settings.DisplayMode!="Follow")Native.FollowMonitor=null;
            return;
        }
        if(!pointerCheck.IsEnabled){followPolicy.Reset();ResolveFollow(true);pointerCheck.Start();}
    }
    private bool FollowInteraction=>fileDragActive || PeekInteractionActive || SatellitePointerInside || IsMouseOver || IsKeyboardFocusWithin || Mouse.Captured!=null || Mouse.LeftButton==MouseButtonState.Pressed || Mouse.RightButton==MouseButtonState.Pressed || app.Windows.Cast<Window>().Any(w=>w.IsKeyboardFocusWithin && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase);
    private void ResolveFollow(bool immediate=false)
    {
        if(DiagnosticRun || app.Settings.DisplayMode!="Follow")return;
        if(userHidden || !IsVisible){followPolicy.Reset();return;}
        if(!Native.CursorPoint(out var point))return;
        var target=Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y));
        var next=followPolicy.Observe(IslandScreen.DeviceName,target.DeviceName,Environment.TickCount64,FollowInteraction,userHidden,immediate);
        if(next==null)return;
        Native.FollowMonitor=next;
        ReflowDisplay();
    }
    private void ReflowDisplay()
    {
        // Reflow existing controls: relocating never navigates or rebuilds an editor.
        Dimensions();foreach(var reflow in bodyReflow)reflow();Dimensions();
        if(peeking)ApplyPeekGeometry();
        Width=Math.Max(expandedWidth,app.Settings.Width);Height=Math.Max(HostHeight,app.Settings.Height);
        refreshHz=Native.RefreshRate(IslandScreen);
        surface.BeginUpdate();try{motion.Snap(surface,IslandSurface.SurfaceWidthProperty,expanded?expandedWidth:peeking?Math.Min(app.Settings.HoverWidth,expandedWidth):app.Settings.Width);motion.Snap(surface,IslandSurface.SurfaceHeightProperty,expanded?expandedHeight:peeking?(peekDetailed?app.Settings.HoverHeight:100):app.Settings.Height);}finally{surface.EndUpdate();}
        Position();CheckEnvironment();UpdateSatellite();
    }
}
