using System;
using System.Linq;
namespace NabyScape;
internal static class WaveformMotionTests
{
    internal static void Run(Action<string,bool> test)
    {
        var wave=new ActivityVisual();
        for(int i=0;i<100;i++)wave.Sample(i%13<4?.85:.015,1/60d,0,false,false,true,i/60d,i%2==0);
        var levels=Enumerable.Range(0,25).Select(wave.WaveLevel).ToArray();
        test("Waveform levels stay bounded with varied audio",levels.All(v=>double.IsFinite(v) && v>=0 && v<=1));
        test("Waveform bars have independent asymmetric responses",Enumerable.Range(0,12).Count(i=>Math.Abs(levels[i]-levels[24-i])>.025)>=6);
        wave.Sample(0,.016,0,false,false,false,2,true,true);
        test("Waveform pause and reduced motion clear every bar",Enumerable.Range(0,25).All(i=>wave.WaveLevel(i)==0) && wave.Envelope==0);
        for(int i=0;i<60;i++)wave.Sample(0,1/60d,0,false,false,true,i/60d,i%2==0,false);
        test("Waveform silence stays quiet without decorative fallback",Enumerable.Range(0,25).All(i=>wave.WaveLevel(i)==0));
        var slow=new ActivityVisual();var fast=new ActivityVisual();
        for(int i=0;i<90;i++)slow.Sample(.5,1/30d,0,false,false,true,i/30d,true);
        for(int i=0;i<360;i++)fast.Sample(.5,1/120d,0,false,false,true,i/120d,i%4==0);
        test("Waveform response converges consistently across render rates",Enumerable.Range(0,25).All(i=>Math.Abs(slow.WaveLevel(i)-fast.WaveLevel(i))<.015));
        var release=new ActivityVisual();
        for(int i=0;i<90;i++)release.Sample(.9,1/30d,0,false,false,true,i/30d);
        double before=release.WaveLevel(0);release.Sample(0,1/60d,0,false,false,true,3);
        bool smooth=release.WaveLevel(0)>0 && release.WaveLevel(0)<before;
        for(int i=0;i<240;i++)release.Sample(0,1/60d,0,false,false,true,3+i/60d,i%2==0);
        test("Waveform releases smoothly and settles after audio stops",smooth && Enumerable.Range(0,25).All(i=>release.WaveLevel(i)<.001));
    }
}
