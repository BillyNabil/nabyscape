using System;
using System.Windows;
using System.Windows.Media;
namespace NabyScape;

// Settings uses the owner-selected NabyScape identity: near-black, bone text and a lime
// signal reserved for "on", "selected" and the one primary action on screen.
// These resources are scoped to the Settings chrome so the live island preview keeps the
// user's own island colors.
internal static class SettingsTheme
{
    internal static readonly Brush Void=Frozen("#070A08");
    internal static readonly Brush Sidebar=Frozen("#0B0F0C");
    internal static readonly Brush Card=Frozen("#111612");
    internal static readonly Brush CardRaised=Frozen("#161C17");
    internal static readonly Brush Line=Frozen("#212922");
    internal static readonly Brush LineSoft=Frozen("#1A211B");
    internal static readonly Brush Bone=Frozen("#EFF2E9");
    internal static readonly Brush Quiet=Frozen("#9AA394");
    internal static readonly Brush Lime=Frozen("#C7FF91");
    internal static readonly Brush LimeInk=Frozen("#0B1306");
    internal static readonly Brush LimeWash=Frozen("#1C2716");
    internal static readonly Brush LimeGlow=Frozen("#33C7FF91");
    internal static readonly FontFamily Display=new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Manrope, Segoe UI Variable Display, Segoe UI");
    internal static readonly FontFamily Body=new("Segoe UI Variable Text, Segoe UI");
    internal static readonly FontFamily Mono=new("Cascadia Mono, Consolas");
    private static Brush Frozen(string hex){var brush=UI.Brush(hex);if(brush.CanFreeze && !brush.IsFrozen)brush=brush.CloneCurrentValue();brush.Freeze();return brush;}
    // Control template resources (see Theme.xaml) re-pointed for Settings only.
    internal static ResourceDictionary ChromeResources()
    {
        var d=new ResourceDictionary();
        if(Accessibility.HighContrast)return d;
        d["Accent"]=Lime;d["AccentText"]=LimeInk;
        d["CheckedFill"]=Lime;d["CheckedText"]=LimeInk;d["SwitchKnob"]=Frozen("#C9D0C3");
        d["TrackFill"]=Frozen("#28302A");d["ProgressFill"]=Lime;d["ProgressTrack"]=Frozen("#28302A");
        d["ControlFill"]=Frozen("#161C17");d["ControlBorder"]=Frozen("#2A332B");d["ControlText"]=Bone;
        d["SelectionFill"]=LimeWash;d["SelectionText"]=Bone;
        return d;
    }
}
