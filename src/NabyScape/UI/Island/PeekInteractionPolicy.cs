using System;
using System.Windows;
namespace NabyScape;

// Each wheel gesture advances one page; a pause begins the next gesture.
internal sealed class PeekWheelAccumulator
{
    private double total;
    private long last;
    private bool gated;
    internal int Push(int delta,long now)
    {
        if(now-last>=300){total=0;gated=false;}
        last=now;if(gated || delta==0)return 0;
        if(total!=0 && Math.Sign(total)!=Math.Sign(delta))total=0;
        total+=delta;if(Math.Abs(total)<100)return 0;
        gated=true;return Math.Sign(total);
    }
    internal void Reset(){total=0;last=0;gated=false;}
}
internal static class VisibilityPolicy
{
    // Own/menu and shell foregrounds must not replace the external window sampled before them.
    internal static IntPtr LatestExternalForeground(IntPtr externalForeground,IntPtr previous,bool previousEligible)=>externalForeground!=IntPtr.Zero?externalForeground:previousEligible?previous:IntPtr.Zero;
    internal static IntPtr RetainOverride(IntPtr current,IntPtr externalForeground)=>current!=IntPtr.Zero && externalForeground!=IntPtr.Zero && current!=externalForeground?IntPtr.Zero:current;
    internal static bool CarouselOwnsWheel(bool peeking,bool horizontal,bool focusedNote,bool overNote)=>peeking && (horizontal || !focusedNote || !overNote);
}
internal static class PeekLayout
{
    internal static Rect SafeBounds(double width,double height,double radius,bool notch)
    {
        var geometry=IslandGeometry.Create(width,height,radius,notch);
        double top=16,bottom=height-20,inset=32;
        while(inset<width/2-1 && (!geometry.FillContains(new Point(inset,top)) || !geometry.FillContains(new Point(inset,bottom))))inset++;
        return new Rect(inset,top,Math.Max(1,width-2*inset),Math.Max(1,bottom-top));
    }
}
internal static class PeekInteractionTests
{
    internal static void Run(Action<string,bool> test)
    {
        var wheel=new PeekWheelAccumulator();
        test("Small wheel deltas accumulate",wheel.Push(30,1000)==0 && wheel.Push(30,1010)==0 && wheel.Push(40,1020)==1);
        test("A continuous wheel gesture advances once",wheel.Push(120,1040)==0 && wheel.Push(120,1100)==0);
        test("A new wheel gesture can reverse",wheel.Push(-120,1400)==-1);
        wheel.Reset();test("Direction changes discard stale deltas",wheel.Push(60,2000)==0 && wheel.Push(-60,2010)==0 && wheel.Push(-40,2020)==-1);
        var appA=new IntPtr(101);var appB=new IntPtr(202);
        test("Tray show uses the last eligible external foreground",VisibilityPolicy.LatestExternalForeground(IntPtr.Zero,appA,true)==appA);
        test("External foreground tracking replaces the previous app",VisibilityPolicy.LatestExternalForeground(appB,appA,true)==appB);
        test("Stale retained foreground falls back to no override",VisibilityPolicy.LatestExternalForeground(IntPtr.Zero,appA,false)==IntPtr.Zero);
        test("Missing retained foreground falls back without an exception",VisibilityPolicy.LatestExternalForeground(IntPtr.Zero,IntPtr.Zero,false)==IntPtr.Zero);
        test("New external foreground wins over a stale retained window",VisibilityPolicy.LatestExternalForeground(appB,appA,false)==appB);
        test("Own windows retain foreground override",VisibilityPolicy.RetainOverride(appA,IntPtr.Zero)==appA);
        test("Same app retains foreground override",VisibilityPolicy.RetainOverride(appA,appA)==appA);
        test("Another external app clears foreground override",VisibilityPolicy.RetainOverride(appA,appB)==IntPtr.Zero);
        test("Focused note keeps vertical wheel only over editor",!VisibilityPolicy.CarouselOwnsWheel(true,false,true,true) && VisibilityPolicy.CarouselOwnsWheel(true,false,true,false) && VisibilityPolicy.CarouselOwnsWheel(true,true,true,true));
        test("Expanded and closed bypass preview wheel",!VisibilityPolicy.CarouselOwnsWheel(false,false,false,false));
        foreach(bool notch in new[]{false,true})foreach(double radius in new[]{0d,16d,40d})foreach(double height in new[]{100d,184d,280d})
        {
            var bounds=PeekLayout.SafeBounds(300,height,radius,notch);var contour=IslandGeometry.Create(300,height,radius,notch);
            test($"Safe hover rectangle {notch}/{radius}/{height}",bounds.Width>=216 && contour.FillContains(bounds.TopLeft) && contour.FillContains(bounds.BottomRight) && (height!=184 || bounds.Height>=148));
        }
    }
}
