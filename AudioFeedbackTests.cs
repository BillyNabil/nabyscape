using System;

namespace NabyScape;

internal static class AudioFeedbackTests
{
    internal static void Run(Action<string,bool> check)
    {
        int enumerations=0,releases=0,errors=0;bool broken=false;float value=.7f;
        using var cache=new PeakCache<int>(()=>{enumerations++;return new[]{0,1};},id=>id==0?.05f:broken?throw new InvalidOperationException("fixture"):value,_=>releases++,_=>errors++);
        var now=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);
        check("Meter sees louder non-default output",Math.Abs(cache.Read(now)-.7)<.001);
        for(int i=0;i<59;i++)cache.Read(now.AddMilliseconds(i*30));
        check("Meter caches native discovery across frames",enumerations==1);
        broken=true;cache.Read(now.AddSeconds(1.9));
        check("Broken meter removed without losing healthy endpoint",Math.Abs(cache.Read(now.AddSeconds(1.95))-.05)<.001 && errors==1);
        broken=false;value=.9f;
        check("Meter retries failed device on next bounded lease",Math.Abs(cache.Read(now.AddSeconds(2.1))-.9)<.001 && enumerations==2 && releases==2);
        var wave=new ActivityVisual();wave.Sample(0,.1,0,false,false,true,1,true,true);
        check("Playing decorative waveform has subtle motion at silence",wave.Envelope>0 && wave.Envelope<.15);
        wave.Sample(0,.1,0,false,false,false,2,true,true);
        check("Stopped waveform returns to quiet baseline",wave.Envelope==0);
        wave.Sample(0,.1,0,false,false,true,3,true,false);
        check("Real-only waveform stays quiet without a peak",wave.Envelope==0);
        wave.Sample(.64,.1,0,false,false,true,4,true,false);
        check("Real waveform responds to measured output",wave.Envelope>.4);
    }
}
