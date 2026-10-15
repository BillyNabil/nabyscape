using System;
using System.Windows;

namespace NabyScape;

internal static class DetachedPreviewTests
{
    // Pure fixtures only: no Application, clipboard, playback, window or native poller is created.
    internal static void Run(Action<string,bool> check)
    {
        var state=new DetachedPreviewState();
        check("Detached previews start closed",!state.Visible("media") && !state.Visible("notes"));
        check("Detached preview opens one instance",state.Open("media") && !state.Open("media") && state.Visible("media"));
        state.Open("notes");state.SetHidden(true);
        check("Manual hide hides all open previews",!state.Visible("media") && !state.Visible("notes"));
        state.Close("notes");state.SetHidden(false);
        check("Manual show restores only previews still open",state.Visible("media") && !state.Visible("notes") && !state.Visible("timer"));
        state.SetHidden(true);state.Open("focus");
        check("Open while manually hidden respects hide",!state.Visible("focus"));
        state.SetHidden(false);
        check("Explicitly opened hidden preview restores on show",state.Visible("focus"));
        state.Close("media");
        check("Close removes only its UI state",!state.Visible("media") && state.Visible("focus"));
        check("Preview aliases use canonical singleton keys",DetachedPreviewManager.NormalizeKind("Music")=="media" && DetachedPreviewManager.NormalizeKind("Shelf")=="files" && DetachedPreviewManager.NormalizeKind("note")=="notes");
        var work=new Rect(-1600,40,1600,860);
        var offscreen=DetachedPreviewGeometry.Clamp(new Rect(5000,-800,350,330),work);
        check("Disconnected monitor position clamps to available work area",work.Contains(offscreen) && offscreen.Right==work.Right && offscreen.Top==work.Top);
        var huge=DetachedPreviewGeometry.Clamp(new Rect(-1600,40,12000,6000),work);
        check("Oversized preview fits monitor work area",huge==work);
        var small=DetachedPreviewGeometry.Clamp(new Rect(-1400,100,1,1),work);
        check("Preview minimum dimensions are enforced",small.Width==280 && small.Height==220);
        var tinyWork=new Rect(0,0,200,150);
        check("Small DPI work area stays reachable",DetachedPreviewGeometry.Clamp(new Rect(10,10,350,330),tinyWork)==tinyWork);
        var nonfinite=DetachedPreviewGeometry.Clamp(new Rect(double.NaN,double.PositiveInfinity,double.NaN,double.PositiveInfinity),work);
        check("Corrupt nonfinite geometry has bounded defaults",double.IsFinite(nonfinite.X) && double.IsFinite(nonfinite.Y) && work.Contains(nonfinite));
        var unchanged=new Rect(-1000,130,400,400);
        check("Valid negative-monitor coordinates remain unchanged",DetachedPreviewGeometry.Clamp(unchanged,work)==unchanged);
        var dpiWork=new Rect(0,0,1280,680);
        check("Scaled work area clamps bottom and right",DetachedPreviewGeometry.Clamp(new Rect(1200,650,350,330),dpiWork)==new Rect(930,350,350,330));
    }
}
