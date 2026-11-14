using System;
using System.Linq;
namespace NabyScape;

public sealed partial class Settings
{
    // Closed dock ("notch bar") clock. Automatic keeps the original layout: left with the centered date
    // while idle, right beside music, timers and notices. Left/Right pin it; Center centers it on the dock.
    public string ClosedClockPosition {get;set;}="Automatic";
    internal static readonly string[] ClosedClockPositions={"Automatic","Left","Center","Right"};
    // Closed width bounds, shared by Normalize, the Settings slider and the on-dock resize grips.
    // New installs start at 400 px, so the maximum leaves room to widen the notch.
    internal const double ClosedWidthMin=180,ClosedWidthMax=600,ClosedWidthFallback=228;
    internal static double ClampClosedWidth(double value)=>double.IsFinite(value)?Math.Clamp(value,ClosedWidthMin,ClosedWidthMax):ClosedWidthFallback;
    internal void NormalizeDockClock()
    {
        string value=(ClosedClockPosition??"").Trim();if(value.Equals("Centre",StringComparison.OrdinalIgnoreCase))value="Center";
        ClosedClockPosition=ClosedClockPositions.FirstOrDefault(p=>p.Equals(value,StringComparison.OrdinalIgnoreCase))??"Automatic";
    }
}
