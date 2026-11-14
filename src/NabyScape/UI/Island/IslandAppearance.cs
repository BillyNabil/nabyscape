using System.Windows.Media;

namespace NabyScape;

// Surface colors per state: hover and the open panel use the expanded island color; the closed
// and tucked dock use the collapsed color at its own opacity. Quiet states still never show
// ambience or glass. Content ink follows the surface it sits on (dark ink on light surfaces).
public sealed partial class OverlayWindow
{
    private (string Open,string Closed,Color Fill,bool Light)? appearanceKey;
    private Brush openFill=Brushes.Black,closedFill=Brushes.Black;
    private Tone openTone=Tone.Dark,closedTone=Tone.Dark;
    private bool? headerQuiet;
    internal Brush ClosedFill=>closedFill;
    internal Brush OpenFill=>openFill;
    internal Tone HeaderTone=>ThemeInk.GetTone(compact);
    private void RefreshIslandAppearance()
    {
        var s=app.Settings;bool light=ThemeInk.IsLight;
        string open=IslandPalette.Background(s,light),closed=IslandPalette.Collapsed(s,light);
        surface.SetMaterial(open,s.GlassStrength);
        var key=(open,closed,IslandPalette.CollapsedColor(s,light),light);if(appearanceKey==key)return;appearanceKey=key;
        openFill=UI.Brush(open);closedFill=IslandPalette.CollapsedFill(s,light);
        var nextOpen=IslandPalette.ToneFor(open);closedTone=IslandPalette.ToneFor(closed);
        if(nextOpen!=openTone || root.ReadLocalValue(ThemeInk.ToneProperty)==System.Windows.DependencyProperty.UnsetValue){openTone=nextOpen;ThemeInk.SetTone(root,openTone);ThemeInk.Refresh(root);}
        headerQuiet=null;ApplySurfaceFill(!expanded && !peeking || IsTucked);
    }
    private void ApplySurfaceFill(bool quiet)
    {
        var fill=quiet?closedFill:openFill;
        if(!ReferenceEquals(surface.Fill,fill)){surface.Fill=fill;surface.InvalidateVisual();}
        if(headerQuiet==quiet)return;headerQuiet=quiet;
        // The header row sits on the closed surface, and on the open surface once expanded.
        var tone=quiet?closedTone:openTone;
        if(ThemeInk.GetTone(compact)!=tone || compact.ReadLocalValue(ThemeInk.ToneProperty)==System.Windows.DependencyProperty.UnsetValue){ThemeInk.SetTone(compact,tone);ThemeInk.Refresh(compact);}
    }
    // The unread-alert line under the closed dock stays visible on any collapsed color.
    private Brush ClosedAccent()
    {
        string accent=IslandPalette.Accent(app.Settings);var color=((SolidColorBrush)UI.Brush(accent)).Color;var under=((SolidColorBrush)UI.Brush(IslandPalette.Collapsed(app.Settings))).Color;
        return ThemeInk.Contrast(color,under)<1.6?UI.ContrastText(IslandPalette.Collapsed(app.Settings)):UI.Brush(accent);
    }
}
