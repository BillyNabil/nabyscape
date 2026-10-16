using System;
using System.Diagnostics;

namespace NabyScape;

[Flags]
internal enum RuntimeChange
{
    Clock=1,Media=2,Audio=4,Countdown=8,Productivity=16,Calendar=32,System=64,Settings=128,All=255
}

// All consumers use the same monotonic sample lease; rendering still follows each display frame.
internal sealed class MediaSampleGate
{
    private long sampledAt;
    private double nextAt;
    private string? source;
    private bool valid;
    internal long Sequence {get;private set;}
    internal bool Accept(string? nextSource,long stamp)
    {
        double interval=Stopwatch.Frequency/30d;
        bool same=valid && source==nextSource && stamp>=sampledAt;
        if(same && stamp<nextAt)return false;
        nextAt=same && stamp-nextAt<interval*2?nextAt+(Math.Floor((stamp-nextAt)/interval)+1)*interval:stamp+interval;
        valid=true;source=nextSource;sampledAt=stamp;Sequence++;return true;
    }
    internal void Reset(){valid=false;sampledAt=0;nextAt=0;source=null;}
}

internal readonly record struct MediaLevelSnapshot(long Sequence,long Timestamp,float Peak,bool Muted,int Sessions,int AudibleSessions);
