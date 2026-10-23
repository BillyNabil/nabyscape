using System;
using System.Linq;
using System.Windows.Media;
namespace NabyScape;
internal static class ReactiveAmbientTests
{
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool condition){if(!condition)throw new Exception("Reactive ambient fixture failed.");}
        test("Ambient-only sampling avoids FFT and a waveform can request bands from the same lease",()=>
        {
            using var audio=new AudioService(_=>{});audio.DiagnosticPeak=()=>.4f;
            audio.MediaPeak("fixture",includeSpectrum:false);
            Assert(audio.MediaLevel.Peak==.4f && audio.DiagnosticMeterReads==1 && audio.DiagnosticSpectrumSamples==0);
            audio.MediaPeak("fixture");audio.MediaPeak("fixture",includeSpectrum:false);
            Assert(audio.DiagnosticMeterReads==1 && audio.DiagnosticSpectrumSamples==1);
        });
        test("Ambient normalizes malformed colors, scope, selection and nonfinite sensitivity",()=>
        {
            var s=new Settings{AmbientScope="other",AmbientWidgets=new(){"notes","notes","unknown","ai"},AmbientColorSource="other",AmbientCustomColor="#GGGGGG",AmbientAudioResponse="other",AmbientSensitivity=double.NaN};s.Normalize();
            Assert(s.AmbientScope=="All widgets" && s.AmbientWidgets.SequenceEqual(new[]{"notes","ai"}) && s.AmbientColorSource=="Album cover" && s.AmbientCustomColor=="#7AA8DC" && s.AmbientAudioResponse=="Responsive" && s.AmbientSensitivity==1.4);
        });
        test("Ambient scopes include only chosen content and respect the master switch",()=>
        {
            var s=new Settings();Assert(AmbientPolicy.Includes(s,"notes") && AmbientPolicy.Includes(s,"focus"));
            s.AmbientScope="Music only";Assert(AmbientPolicy.Includes(s,"media") && !AmbientPolicy.Includes(s,"tasks"));
            s.AmbientScope="Selected widgets";s.AmbientWidgets=new(){"notes"};Assert(AmbientPolicy.Includes(s,"notes") && !AmbientPolicy.Includes(s,"media"));
            s.CoverAmbient=false;Assert(!AmbientPolicy.Includes(s,"notes"));s.CoverAmbient=true;s.AmbientStrength=0;Assert(!AmbientPolicy.Includes(s,"notes"));
        });
        test("Media None suppresses every live ambience scope without changing preferences",()=>
        {
            var s=new Settings{CoverAmbient=true,ClosedAmbient=true,AmbientStrength=.8,AmbientWidgets=new(){"media","focus","notes","closed"}};
            foreach(string scope in new[]{"All widgets","Music only","Selected widgets"})
            {
                s.AmbientScope=scope;
                foreach(string widget in new[]{"media","focus","notes","closed","ai"})Assert(!AmbientPolicy.Includes(s,widget,true));
            }
            s.AmbientScope="All widgets";Assert(AmbientPolicy.Includes(s,"focus") && s.CoverAmbient && s.ClosedAmbient && s.AmbientStrength==.8);
        });
        test("Ambient color source falls back without artwork and custom color stays independent",()=>
        {
            var s=new Settings{Accent="#123456",AmbientCustomColor="#654321"};
            var cover=new CoverPalette(Colors.Red,Colors.Blue);
            Assert(AmbientPolicy.ColorsFor(s,cover)==cover && AmbientPolicy.ColorsFor(s,default).Primary==Color.FromRgb(0x12,0x34,0x56));
            s.AmbientColorSource="Accent color";Assert(AmbientPolicy.ColorsFor(s,cover).Primary==Color.FromRgb(0x12,0x34,0x56));
            s.AmbientColorSource="Custom color";Assert(AmbientPolicy.ColorsFor(s,cover).Primary==Color.FromRgb(0x65,0x43,0x21));
        });
        test("Measured ambient responds to attack then decays to silence without fake beats",()=>
        {
            var e=new AmbientEnvelope();Assert(e.Sample(0,1d/30,"Responsive",1)==.32);
            double attack=e.Sample(.8,1d/30,"Responsive",1);Assert(attack>.65 && attack<=1);
            double release=e.Sample(0,1d/30,"Responsive",1);Assert(release<attack && release>.32);
            for(int i=0;i<90;i++)e.Sample(0,1d/30,"Responsive",1);Assert(e.Level==0);
        });
        test("Audio response profiles and sensitivity alter measured transients within bounds",()=>
        {
            var soft=new AmbientEnvelope();var punch=new AmbientEnvelope();var high=new AmbientEnvelope();var low=new AmbientEnvelope();
            Assert(punch.Sample(.9,1d/30,"Punchy",1)>soft.Sample(.9,1d/30,"Soft",1));
            Assert(high.Sample(.1,1d/30,"Responsive",3)>low.Sample(.1,1d/30,"Responsive",.5));
            Assert(double.IsFinite(high.Sample(double.NaN,double.NaN,"Responsive",double.NaN)));high.Reset();Assert(high.Level==0);
        });
        test("Ambient configuration round trips in layouts and resets without losing saved content",()=>
        {
            var s=new Settings{AmbientScope="Selected widgets",AmbientWidgets=new(){"notes","tasks"},AmbientColorSource="Custom color",AmbientCustomColor="#ABCDEF",AmbientAudioResponse="Punchy",AmbientSensitivity=2.2};
            var target=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(s),target);
            Assert(target.AmbientScope==s.AmbientScope && target.AmbientWidgets.SequenceEqual(s.AmbientWidgets) && target.AmbientCustomColor==s.AmbientCustomColor && target.AmbientAudioResponse==s.AmbientAudioResponse && target.AmbientSensitivity==2.2);
            Assert(PreferenceReset.Categories["Ambience"].Contains(nameof(Settings.AmbientWidgets)) && PremiumPreferences.Keys.Contains(nameof(Settings.AmbientSensitivity)));
        });
    }
}
