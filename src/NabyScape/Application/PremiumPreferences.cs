using System;
using System.Collections.Generic;
using System.Linq;
namespace NabyScape;

public sealed partial class Settings
{
    public bool CoverAmbient {get;set;}=true;
    public double AmbientStrength {get;set;}=.5;
    public double AmbientSpread {get;set;}=.75;
    public string AmbientMotion {get;set;}="Drift";
    public string AmbientScope {get;set;}="All widgets";
    public List<string> AmbientWidgets {get;set;}=Names.Keys.Where(id=>id!="tools").Concat(new[]{"closed","ai"}).ToList();
    public string AmbientColorSource {get;set;}="Album cover";
    public string AmbientCustomColor {get;set;}="#7AA8DC";
    public string AmbientAudioResponse {get;set;}="Responsive";
    public double AmbientSensitivity {get;set;}=1.4;
    public string InteractionMotion {get;set;}="Subtle";
    public double NotificationSeconds {get;set;}=12;
    public string NotificationAnimation {get;set;}="Lift";
    public bool QuietHours {get;set;}
    public int QuietStart {get;set;}=22;
    public int QuietEnd {get;set;}=7;
    public DateTimeOffset? NotificationsSnoozedUntil {get;set;}
    private void NormalizePremium()
    {
        AmbientStrength=double.IsFinite(AmbientStrength)?Math.Clamp(AmbientStrength,0,1):.5;
        AmbientSpread=double.IsFinite(AmbientSpread)?Math.Clamp(AmbientSpread,.25,1):.75;
        NotificationSeconds=double.IsFinite(NotificationSeconds)?Math.Clamp(NotificationSeconds,4,30):12;
        QuietStart=Math.Clamp(QuietStart,0,23);QuietEnd=Math.Clamp(QuietEnd,0,23);
        if(AmbientMotion is not ("Still" or "Breathe" or "Drift"))AmbientMotion="Drift";
        if(AmbientScope is not ("All widgets" or "Music only" or "Selected widgets"))AmbientScope="All widgets";
        AmbientWidgets=(AmbientWidgets??new()).Where(id=>id!=null && (id is "closed" or "ai" || Names.ContainsKey(id) && id!="tools")).Distinct().ToList();
        if(AmbientColorSource is not ("Album cover" or "Accent color" or "Custom color"))AmbientColorSource="Album cover";
        if(AmbientCustomColor==null || AmbientCustomColor.Length!=7 || AmbientCustomColor[0]!='#' || !uint.TryParse(AmbientCustomColor.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out _))AmbientCustomColor="#7AA8DC";
        if(AmbientAudioResponse is not ("Off" or "Soft" or "Responsive" or "Punchy"))AmbientAudioResponse="Responsive";
        AmbientSensitivity=double.IsFinite(AmbientSensitivity)?Math.Clamp(AmbientSensitivity,.5,3):1.4;
        if(InteractionMotion is not ("Off" or "Subtle" or "Expressive"))InteractionMotion="Subtle";
        if(NotificationAnimation is not ("Lift" or "Fade" or "Soft scale"))NotificationAnimation="Lift";
    }
}
internal static class PremiumPreferences
{
    internal static readonly string[] Keys="CoverAmbient AmbientStrength AmbientSpread AmbientMotion AmbientScope AmbientWidgets AmbientColorSource AmbientCustomColor AmbientAudioResponse AmbientSensitivity InteractionMotion NotificationSeconds NotificationAnimation QuietHours QuietStart QuietEnd NotificationsSnoozedUntil".Split(' ');
}

internal static class NotificationPolicy
{
    internal static bool Quiet(Settings s,DateTimeOffset now)
    {
        if(s.NotificationsSnoozedUntil is {} until && until>now)return true;
        if(!s.QuietHours || s.QuietStart==s.QuietEnd)return false;
        int hour=now.ToLocalTime().Hour;
        return s.QuietStart<s.QuietEnd?hour>=s.QuietStart && hour<s.QuietEnd:hour>=s.QuietStart || hour<s.QuietEnd;
    }
}
