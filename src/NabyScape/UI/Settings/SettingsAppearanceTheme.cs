using System;
using System.Linq;
using System.Windows.Controls;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private void ThemeControls()
    {
        var s=app.Settings;
        var choice=new ComboBox{ItemsSource=AppearanceTheme.Labels.Select(Localization.Text).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(AppearanceTheme.Choices,s.ColorTheme))};UI.LocalizedName(choice,"Theme");
        choice.SelectionChanged+=(_,_)=>{if(choice.SelectedIndex<0 || AppearanceTheme.Choices[choice.SelectedIndex]==s.ColorTheme)return;s.ColorTheme=AppearanceTheme.Choices[choice.SelectedIndex];previewMode="Panel";Save();};
        WireSettingPreview(choice,"Theme");
        page.Children.Add(Section("Theme",choice,
            UI.Caption("Dark keeps the classic black dock. Light gives the dock and Settings light surfaces with dark text. Follow Windows switches with the Windows app mode.",11,UI.Muted),
            UI.Caption("Colors you picked yourself stay as chosen. The default black island becomes light in Light.",11,UI.Muted)));
    }
    // The live preview paints the same surface as the dock state it shows, and its sample text
    // takes the ink of that surface.
    private void PreviewSurface(bool closed)
    {
        var s=app.Settings;bool light=ThemeInk.IsLight;string color=closed?IslandPalette.Collapsed(s,light):IslandPalette.Background(s,light);
        preview.Background=Accessibility.HighContrast?System.Windows.SystemColors.WindowBrush:closed?IslandPalette.CollapsedFill(s,light):UI.Brush(color);
        var tone=IslandPalette.ToneFor(color);
        if(ThemeInk.GetTone(preview)!=tone || preview.ReadLocalValue(ThemeInk.ToneProperty)==System.Windows.DependencyProperty.UnsetValue){ThemeInk.SetTone(preview,tone);ThemeInk.Refresh(preview);}
    }
    // Expanded (hover and open panel) and collapsed (closed and tucked) surfaces can differ.
    private void IslandColorControls(StackPanel colors)
    {
        var s=app.Settings;bool same=IslandPalette.SameColor(s);
        colors.Children.Add(Check("Use same color for both states",same,v=>{s.CollapsedSameColor=v;previewMode="Closed";Dispatcher.BeginInvoke(()=>Navigate(current));}));
        ColorControl(colors,same?"Island color":"Expanded island color",s.Background,v=>s.Background=v,new[]{"#000000","#101114","#182028","#25221E"});
        if(!same)ColorControl(colors,"Collapsed island color",s.CollapsedBackground,v=>{s.CollapsedBackground=v;previewMode="Closed";},new[]{"#000000","#1C1D21","#2E3036","#3D4350"});
        AddSlider(colors,"Collapsed island opacity",IslandPalette.CollapsedOpacity(s)*100,IslandPalette.MinimumCollapsedOpacity*100,100,v=>{s.CollapsedOpacity=v/100;previewMode="Closed";},"%",1);
        colors.Children.Add(UI.Caption("The collapsed color shows on the closed and tucked dock. Lower opacity lets your wallpaper show through; text stays solid. Glass stays off while the dock is closed.",11,UI.Muted));
    }
}
