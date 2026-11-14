using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class Settings
{
    // "Dark" is the classic look. "Light" and "System" (follow the Windows app mode) are opt-in.
    public string ColorTheme {get;set;}="Dark";
    // Closed (collapsed) island surface. null marks a profile saved before this option existed;
    // normalization resolves it so the closed dock keeps the look it had before (always black).
    public bool? CollapsedSameColor {get;set;}
    public string CollapsedBackground {get;set;}="#000000";
    // Fill opacity of the closed surface only; content and the window-wide opacity are separate.
    public double CollapsedOpacity {get;set;}=1;
    internal void NormalizeAppearanceTheme()
    {
        if(Array.IndexOf(AppearanceTheme.Choices,ColorTheme)<0)ColorTheme="Dark";
        if(!ValidColor(CollapsedBackground))CollapsedBackground=IslandPalette.DarkSurface;
        CollapsedSameColor??=IslandPalette.LegacySameColor(Background);
        CollapsedOpacity=double.IsFinite(CollapsedOpacity)?Math.Clamp(CollapsedOpacity,IslandPalette.MinimumCollapsedOpacity,1):1;
    }
}

internal static class AppearanceTheme
{
    internal static readonly string[] Keys={nameof(Settings.ColorTheme),nameof(Settings.CollapsedSameColor),nameof(Settings.CollapsedBackground),nameof(Settings.CollapsedOpacity)};
    internal static readonly string[] Choices={"Dark","Light","System"};
    internal static readonly string[] Labels={"Dark","Light","Follow Windows"};
    internal static string Label(string choice)=>Labels[Math.Max(0,Array.IndexOf(Choices,choice))];
}

// Effective island and control colors for the active theme. The classic dark defaults stand for
// "the theme's color": in Light they resolve to light counterparts, while any color a person
// picked themselves is used exactly as chosen.
internal static class IslandPalette
{
    internal const string DarkSurface="#000000",LightSurface="#F5F5F7";
    internal const double MinimumCollapsedOpacity=.2;
    private static readonly Dictionary<string,string> LightDefaults=new(StringComparer.OrdinalIgnoreCase)
    {
        [DarkSurface]=LightSurface,      // island
        ["#FFFFFF"]="#1D1D1F",           // accent
        ["#16171B"]="#E6E7EB",           // control fill
        ["#303238"]="#C9CCD3",           // control outline
        ["#34373E"]="#D3D7DE"            // selected control
    };
    private static string Themed(string value,string darkDefault,bool light)
        =>light && string.Equals(value,darkDefault,StringComparison.OrdinalIgnoreCase)?LightDefaults[darkDefault]:value;
    internal static bool LegacySameColor(string background)=>string.Equals(background,DarkSurface,StringComparison.OrdinalIgnoreCase);
    internal static bool SameColor(Settings s)=>s.CollapsedSameColor??LegacySameColor(s.Background);
    internal static string Background(Settings s,bool light)=>Themed(s.Background,DarkSurface,light);
    internal static string Collapsed(Settings s,bool light)=>SameColor(s)?Background(s,light):Themed(s.CollapsedBackground,DarkSurface,light);
    internal static double CollapsedOpacity(Settings s)=>double.IsFinite(s.CollapsedOpacity)?Math.Clamp(s.CollapsedOpacity,MinimumCollapsedOpacity,1):1;
    internal static string Accent(Settings s,bool light)=>Themed(s.Accent,"#FFFFFF",light);
    internal static string ControlFill(Settings s,bool light)=>Themed(s.ControlFill,"#16171B",light);
    internal static string ControlBorder(Settings s,bool light)=>Themed(s.ControlBorder,"#303238",light);
    internal static string SelectionFill(Settings s,bool light)=>Themed(s.SelectionFill,"#34373E",light);
    internal static string Background(Settings s)=>Background(s,ThemeInk.IsLight);
    internal static string Collapsed(Settings s)=>Collapsed(s,ThemeInk.IsLight);
    internal static string Accent(Settings s)=>Accent(s,ThemeInk.IsLight);
    internal static string SelectionFill(Settings s)=>SelectionFill(s,ThemeInk.IsLight);
    internal static string ControlFill(Settings s)=>ControlFill(s,ThemeInk.IsLight);
    internal static string ControlBorder(Settings s)=>ControlBorder(s,ThemeInk.IsLight);
    // The closed surface paints its color at the collapsed opacity; text and icons stay opaque.
    internal static Color CollapsedColor(Settings s,bool light)
    {var c=((SolidColorBrush)UI.Brush(Collapsed(s,light))).Color;return Color.FromArgb((byte)Math.Round(255*CollapsedOpacity(s)),c.R,c.G,c.B);}
    private static readonly Dictionary<Color,Brush> fills=new();
    internal static Brush CollapsedFill(Settings s,bool light)
    {
        var color=CollapsedColor(s,light);
        lock(fills){if(fills.TryGetValue(color,out var cached))return cached;var brush=new SolidColorBrush(color);brush.Freeze();if(fills.Count>32)fills.Clear();fills[color]=brush;return brush;}
    }
    // Content on a surface uses dark ink when that surface is light (same rule as UI.ContrastText).
    internal static Tone ToneFor(string surface)=>ThemeInk.IsLightSurface(surface)?Tone.Light:Tone.Dark;
}
