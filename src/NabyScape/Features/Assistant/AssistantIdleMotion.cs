using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace NabyScape;

// One retained clock composes the same four idle channels. No render callback,
// frequency reduction or change to the breathing, floating and swaying periods.
internal sealed class AssistantIdleMotion : MatrixAnimationBase
{
    internal static bool SampleForDiagnostics;
    internal static long DiagnosticEvaluations;
    private readonly bool music;
    private static readonly AssistantIdleMotion resting=new(false),playing=new(true);
    private AssistantIdleMotion(bool music){this.music=music;Duration=Duration.Forever;}
    internal static AssistantIdleMotion For(bool music)
    {var motion=music?playing:resting;if(!motion.IsFrozen)motion.Freeze();return motion;}
    protected override Freezable CreateInstanceCore()=>new AssistantIdleMotion(music);
    protected override Matrix GetCurrentValueCore(Matrix from,Matrix to,AnimationClock clock)
    {if(SampleForDiagnostics)DiagnosticEvaluations++;return Pose(clock.CurrentTime?.TotalSeconds??0,music);}
    internal static double Sine(double seconds,double halfPeriod)=>(1-Math.Cos(Math.PI*(seconds%(halfPeriod*2))/halfPeriod))/2;
    internal static double Float(double seconds,bool music)=>-3.5*Sine(seconds,music?.65:2.1);
    internal static Matrix Pose(double seconds,bool music)
    {
        double scale=1+.035*Sine(seconds,music?.7:2.4),angle=(music?-5:-3)*Sine(seconds,music?1.1:3.4);
        var matrix=Matrix.Identity;matrix.ScaleAt(scale,scale,50,50);matrix.RotateAt(angle,50,50);matrix.Translate(0,Float(seconds,music));return matrix;
    }
}
