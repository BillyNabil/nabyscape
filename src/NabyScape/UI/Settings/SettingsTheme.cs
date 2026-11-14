using System;
using System.Windows;
using System.Windows.Media;
namespace NabyScape;

// Settings uses the owner-selected NabyScape identity: near-black, bone text and a lime
// signal reserved for "on", "selected" and the one primary action on screen.
// These resources are scoped to the Settings chrome so the live island preview keeps the
// user's own island colors.
// The surface and text brushes are shared live instances: Apply switches them between the dark
// and light palettes, and makes the layers translucent when a see-through Settings material is
// active, so every Settings element updates in place without being rebuilt.
internal static class SettingsTheme
{
    private sealed record Swatch(SolidColorBrush Brush,Color Dark,Color Light,double Translucent);
    private static Swatch Live(string dark,string light,double translucent=1)
    {var d=Parse(dark);return new(new SolidColorBrush(d),d,Parse(light),translucent);}
    private static Color Parse(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
    private static readonly Swatch voidSwatch=Live("#070A08","#F3F5F0",0),sidebarSwatch=Live("#0B0F0C","#EAEEE6",.42),cardSwatch=Live("#111612","#FFFFFF",.5),cardRaisedSwatch=Live("#161C17","#FBFCF9",.86);
    private static readonly Swatch lineSwatch=Live("#212922","#D3DACD"),lineSoftSwatch=Live("#1A211B","#E1E6DC"),boneSwatch=Live("#EFF2E9","#141912"),quietSwatch=Live("#9AA394","#586252");
    private static readonly Swatch limeWashSwatch=Live("#1C2716","#E2F2CF"),stageSwatch=Live("#0A0E0B","#ECF0E8",.55),windowSwatch=Live("#111214","#F3F5F0",.6);
    private static readonly Swatch controlFillSwatch=Live("#161C17","#F4F7F1"),controlBorderSwatch=Live("#2A332B","#C9D2C2"),trackSwatch=Live("#28302A","#D3DBCC"),knobSwatch=Live("#C9D0C3","#7E8877");
    private static readonly Swatch[] swatches={voidSwatch,sidebarSwatch,cardSwatch,cardRaisedSwatch,lineSwatch,lineSoftSwatch,boneSwatch,quietSwatch,limeWashSwatch,stageSwatch,windowSwatch,controlFillSwatch,controlBorderSwatch,trackSwatch,knobSwatch};
    internal static Brush Void=>voidSwatch.Brush;
    internal static Brush Sidebar=>sidebarSwatch.Brush;
    internal static Brush Card=>cardSwatch.Brush;
    internal static Brush CardRaised=>cardRaisedSwatch.Brush;
    internal static Brush Line=>lineSwatch.Brush;
    internal static Brush LineSoft=>lineSoftSwatch.Brush;
    internal static Brush Bone=>boneSwatch.Brush;
    internal static Brush Quiet=>quietSwatch.Brush;
    internal static Brush LimeWash=>limeWashSwatch.Brush;
    // Preview stage behind the live dock, and the window fill beneath the Settings layers.
    internal static Brush Stage=>stageSwatch.Brush;
    internal static Brush WindowFill=>windowSwatch.Brush;
    internal static readonly Brush Lime=Frozen("#C7FF91");
    internal static readonly Brush LimeInk=Frozen("#0B1306");
    internal static readonly Brush LimeGlow=Frozen("#33C7FF91");
    internal static readonly FontFamily Display=new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Manrope, Segoe UI Variable Display, Segoe UI, Malgun Gothic");
    internal static readonly FontFamily Body=new("Segoe UI Variable Text, Segoe UI, Malgun Gothic");
    internal static readonly FontFamily Mono=new("Cascadia Mono, Consolas");
    internal static bool Light {get;private set;}
    internal static bool Translucent {get;private set;}
    private static Brush Frozen(string hex){var brush=UI.Brush(hex);if(brush.CanFreeze && !brush.IsFrozen)brush=brush.CloneCurrentValue();brush.Freeze();return brush;}
    // Translucent layers let a native Mica/Acrylic backdrop or the simulated glass show through;
    // opaque cards would otherwise cover nearly the whole window.
    internal static void Apply(bool light,bool translucent)
    {
        Light=light;Translucent=translucent;
        foreach(var swatch in swatches){var color=light?swatch.Light:swatch.Dark;if(swatch.Brush.Color!=color)swatch.Brush.Color=color;double opacity=translucent?swatch.Translucent:1;if(swatch.Brush.Opacity!=opacity)swatch.Brush.Opacity=opacity;}
    }
    // Control template resources (see Theme.xaml) re-pointed for Settings only.
    internal static ResourceDictionary ChromeResources()
    {
        var d=new ResourceDictionary();
        if(Accessibility.HighContrast)return d;
        d["Accent"]=Lime;d["AccentText"]=LimeInk;
        d["CheckedFill"]=Lime;d["CheckedText"]=LimeInk;d["SwitchKnob"]=knobSwatch.Brush;
        d["TrackFill"]=trackSwatch.Brush;d["ProgressFill"]=Lime;d["ProgressTrack"]=trackSwatch.Brush;
        d["ControlFill"]=controlFillSwatch.Brush;d["ControlBorder"]=controlBorderSwatch.Brush;d["ControlText"]=Bone;
        d["SelectionFill"]=LimeWash;d["SelectionText"]=Bone;
        return d;
    }
}
