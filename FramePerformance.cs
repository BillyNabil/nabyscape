using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace NabyScape;

internal sealed class FramePacer
{
    private double next=-1;private int previousCap;
    public bool Accept(TimeSpan time,int cap)
    {
        double now=time.TotalSeconds,interval=1d/Math.Clamp(cap,15,360);
        if(next<0 || previousCap!=cap || now>next+1){previousCap=cap;next=now+interval;return true;}
        if(now+.0007<next)return false;
        do{next+=interval;}while(next<=now+.0007);return true;
    }
    public void Reset(){next=-1;}
}
internal static class FrameTelemetry
{
    private static readonly Queue<double> samples=new();private static TimeSpan? last;
    internal static void Sample(TimeSpan time){if(last.HasValue){double ms=(time-last.Value).TotalMilliseconds;if(ms>0 && ms<250){samples.Enqueue(ms);while(samples.Count>240)samples.Dequeue();}}last=time;}
    internal static string Status
    {
        get{if(samples.Count<2)return "Waiting for animation activity…";var s=samples.OrderBy(x=>x).ToArray();double mean=s.Average(),p95=s[Math.Min(s.Length-1,(int)(s.Length*.95))];int late=s.Count(x=>x>1000d/Motion.FrameLimit*1.8);return $"{1000/mean:0} updates/s · p95 {p95:0.0} ms\n{late}/{s.Length} late intervals · cap {Motion.FrameLimit} FPS\nMeasured WPF render callbacks, not GPU presentation.";}
    }
}
public sealed partial class OverlayWindow
{
    private uint refreshHz=60;
    private readonly FramePacer livePacer=new();
    private void ConfigurePerformance()
    {
        if(refreshHz<2)refreshHz=60;
        bool battery=SystemInformation.PowerStatus.PowerLineStatus==PowerLineStatus.Offline;
        Motion.FrameLimit=FrameLimitFor(app.Settings.Performance,(int)refreshHz,expanded || peeking || motion.Active,battery,app.Usage.cpu);
    }
    internal static int FrameLimitFor(string profile,int hz,bool interactive,bool battery,double cpu)
    {
        hz=Math.Clamp(hz,30,240);
        return profile switch{"Hemat"=>Math.Min(hz,interactive?60:30),"Maksimal"=>hz,_=>Math.Min(hz,battery || cpu>85?60:interactive?hz:60)};
    }
}
