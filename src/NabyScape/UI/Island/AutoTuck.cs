using System;
using System.Windows;
using System.Windows.Media;

namespace NabyScape;

internal static class AutoTuckPolicy
{
    internal const double EdgeHeight=6;
    internal static double Offset(double compactHeight,double edge=EdgeHeight)=>-Math.Max(0,compactHeight-Math.Clamp(edge,3,12));
    internal static bool ShouldTuck(bool requested,bool manuallyHidden,bool expanded,bool peeking,bool engaged)=>requested && !manuallyHidden && !engaged;
    internal static bool CanHover(bool manuallyHidden,bool requested,bool tucked)=>!manuallyHidden && requested && tucked;
}

public sealed partial class OverlayWindow
{
    private readonly TranslateTransform tuckSlide=new();
    private bool tucked,tuckPreview,manualTuck;
    private bool TuckRequested=>manualTuck || app.Settings.AlwaysTucked || environmentHidden;
    internal bool ManualTuck=>manualTuck;
    internal void FlushPreferenceEditors()
    {noteSave.Stop();utilityMutating=true;try{if(!app.Productivity.Save())throw new System.IO.IOException("Pending editor changes could not be saved.");}finally{utilityMutating=false;}}
    public void ToggleTuck()
    {
        manualTuck=!manualTuck;
        if(userHidden)return;
        Collapse();if(manualTuck){SetTucked(true);Position();}else{SetTucked(false);CheckEnvironment();}
    }
    internal bool IsTucked=>tucked;
    internal double TuckOffset=>tuckSlide.Y;
    // Coordinates are local to the HWND, including the content translation and its clip.
    internal Geometry VisibleContour
    {
        get
        {
            var translated=surface.Contour.Clone();translated.Transform=new TranslateTransform(0,tuckSlide.Y);
            return Geometry.Combine(translated,new RectangleGeometry(new Rect(0,0,Math.Max(0,ActualWidth),Math.Max(0,ActualHeight))),GeometryCombineMode.Intersect,null);
        }
    }
    internal Rect VisibleBounds=>VisibleContour.Bounds;
    internal bool VisiblePoint(Point point)=>point.X>=0 && point.Y>=0 && point.X<ActualWidth && point.Y<ActualHeight && surface.Contour.FillContains(new Point(point.X,point.Y-tuckSlide.Y));
    private void ApplyEnvironmentTuck(){if(TuckRequested && !userHidden && !expanded && !peeking)SetTucked(true);else if(!TuckRequested)SetTucked(false);}
    private void SetTucked(bool value)
    {
        bool changed=tucked!=value;tucked=value;RefreshNoticeEdge();
        if(value)
        {
            // Snap the compact silhouette before translating. No render loop remains behind the edge.
            motion.Dispose();motion.Snap(surface,IslandSurface.SurfaceWidthProperty,app.Settings.Width);motion.Snap(surface,IslandSurface.SurfaceHeightProperty,app.Settings.Height);motion.Snap(surface,IslandSurface.GlassLiftProperty,0);
            scroll.Visibility=peekPanel.Visibility=Visibility.Collapsed;
            compact.BeginAnimation(OpacityProperty,null);compact.Opacity=1;compact.IsHitTestVisible=true;
            InvalidateSystemMonitor();StopLiveRendering();liveTick.Stop();ClearFeedback();
        }
        tuckSlide.Y=value?AutoTuckPolicy.Offset(app.Settings.Height,app.Settings.TuckEdge):0;
        if(changed && !value)ConfigureLiveActivity();
    }
    internal void RevealTuckedPreview()
    {
        if(!AutoTuckPolicy.CanHover(userHidden,TuckRequested,tucked))return;
        SetTucked(false);Position();tuckPreview=true;
        try{PreviewActivity();}finally{tuckPreview=false;}
    }
}

internal static class AutoTuckTests
{
    internal static void Run(Action<string,bool> test)
    {
        test("Automatic tuck leaves six DIP at the display edge",AutoTuckPolicy.Offset(44)==-38 && AutoTuckPolicy.Offset(28)==-22 && AutoTuckPolicy.Offset(72)==-66);
        test("Short surfaces are never moved below the edge",AutoTuckPolicy.Offset(2)==0);
        foreach(bool expanded in new[]{false,true})foreach(bool preview in new[]{false,true})
        {
            test($"Inactive automatic state tucks {expanded}/{preview}",AutoTuckPolicy.ShouldTuck(true,false,expanded,preview,false));
            test($"Pointer or interaction retains revealed state {expanded}/{preview}",!AutoTuckPolicy.ShouldTuck(true,false,expanded,preview,true));
        }
        test("Manual hide cannot become an automatic tuck",!AutoTuckPolicy.ShouldTuck(true,true,false,false,false) && !AutoTuckPolicy.CanHover(true,true,true));
        test("Ending automatic suppression restores the normal state",!AutoTuckPolicy.ShouldTuck(false,false,false,false,false) && !AutoTuckPolicy.CanHover(false,false,true));
        test("Only a tucked automatic edge can reveal a hover",AutoTuckPolicy.CanHover(false,true,true) && !AutoTuckPolicy.CanHover(false,true,false));
        foreach(bool notch in new[]{false,true})
        {
            var contour=IslandGeometry.Create(300,44,22,notch);contour=contour.Clone();contour.Transform=new TranslateTransform(0,AutoTuckPolicy.Offset(44));
            var visible=Geometry.Combine(contour,new RectangleGeometry(new Rect(0,0,300,400)),GeometryCombineMode.Intersect,null);
            test($"Translated contour retains a narrow clickable center {notch}",visible.FillContains(new Point(150,2)) && !visible.FillContains(new Point(150,8)) && !visible.FillContains(new Point(1,2)) && Math.Abs(visible.Bounds.Height-6)<.01);
        }
    }
}
