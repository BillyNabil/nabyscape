using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

// Light/dark theme resolution, collapsed island colors and the light ink re-mapping (#5, #12).
internal static class AppearanceThemeTests
{
    internal static void Run(Action<string,Action> test)
    {
        static void Assert(bool value,string reason="Appearance theme invariant failed"){if(!value)throw new Exception(reason);}
        static Color C(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
        test("Theme: new and default profiles keep the classic dark look and black closed dock",()=>
        {
            var s=new Settings();s.Normalize();
            Assert(s.ColorTheme=="Dark" && s.CollapsedSameColor==true && s.CollapsedBackground=="#000000" && s.CollapsedOpacity==1);
            Assert(IslandPalette.Collapsed(s,false)=="#000000" && IslandPalette.Background(s,false)=="#000000" && IslandPalette.CollapsedColor(s,false)==Colors.Black);
            var factory=FactoryDefaults.Create();Assert(factory.ColorTheme=="Dark" && factory.CollapsedSameColor==true && IslandPalette.CollapsedColor(factory,false)==Colors.Black);
        });
        test("Theme: profiles saved before collapsed colors keep exactly the closed look they had",()=>
        {
            var custom=JsonSerializer.Deserialize<Settings>("{\"Background\":\"#182028\",\"Width\":300}")!;custom.Normalize();
            Assert(custom.CollapsedSameColor==false && IslandPalette.Collapsed(custom,false)=="#000000" && IslandPalette.Background(custom,false)=="#182028","Custom island color must not repaint the closed dock on upgrade");
            var black=JsonSerializer.Deserialize<Settings>("{\"Background\":\"#000000\"}")!;black.Normalize();
            Assert(black.CollapsedSameColor==true && IslandPalette.Collapsed(black,false)=="#000000");
            // Unnormalized legacy values resolve the same way.
            Assert(!IslandPalette.SameColor(new Settings{Background="#25221E"}) && IslandPalette.SameColor(new Settings()));
        });
        test("Theme: normalization repairs unknown themes and collapsed values",()=>
        {
            var s=new Settings{ColorTheme="Neon",CollapsedBackground="red",CollapsedOpacity=double.NaN};s.Normalize();
            Assert(s.ColorTheme=="Dark" && s.CollapsedBackground=="#000000" && s.CollapsedOpacity==1);
            s.CollapsedOpacity=0;s.Normalize();Assert(s.CollapsedOpacity==IslandPalette.MinimumCollapsedOpacity);
            s.CollapsedOpacity=7;s.Normalize();Assert(s.CollapsedOpacity==1);
            foreach(string choice in AppearanceTheme.Choices){s.ColorTheme=choice;s.Normalize();Assert(s.ColorTheme==choice);}
            Assert(AppearanceTheme.Label("System")=="Follow Windows" && AppearanceTheme.Label("bad")=="Dark");
        });
        test("Theme: Follow Windows is the only choice that reads the Windows app mode",()=>
        {
            Assert(!ThemeInk.Resolve("Dark",true) && !ThemeInk.Resolve("Dark",false));
            Assert(ThemeInk.Resolve("Light",false) && ThemeInk.Resolve("Light",true));
            Assert(ThemeInk.Resolve("System",true) && !ThemeInk.Resolve("System",false));
            ThemeInk.ReadSystemLight();
        });
        test("Theme: light substitutes only the default dark colors and keeps picked colors",()=>
        {
            var s=new Settings();s.Normalize();
            Assert(IslandPalette.Background(s,true)==IslandPalette.LightSurface && IslandPalette.Collapsed(s,true)==IslandPalette.LightSurface);
            Assert(IslandPalette.Accent(s,true)=="#1D1D1F" && IslandPalette.ControlFill(s,true)=="#E6E7EB" && IslandPalette.ControlBorder(s,true)=="#C9CCD3" && IslandPalette.SelectionFill(s,true)=="#D3D7DE");
            Assert(IslandPalette.Accent(s,false)=="#FFFFFF" && IslandPalette.ControlFill(s,false)=="#16171B");
            s.Background="#182028";s.Accent="#E3D2AF";s.CollapsedSameColor=false;s.CollapsedBackground="#3D4350";
            Assert(IslandPalette.Background(s,true)=="#182028" && IslandPalette.Accent(s,true)=="#E3D2AF" && IslandPalette.Collapsed(s,true)=="#3D4350");
            s.CollapsedBackground="#000000";Assert(IslandPalette.Collapsed(s,true)==IslandPalette.LightSurface && IslandPalette.Collapsed(s,false)=="#000000");
        });
        test("Theme: collapsed color and opacity paint only the closed surface",()=>
        {
            var s=new Settings{Background="#101114",CollapsedSameColor=false,CollapsedBackground="#3D4350",CollapsedOpacity=.5};s.Normalize();
            Assert(IslandPalette.CollapsedColor(s,false)==Color.FromArgb(128,0x3D,0x43,0x50) && IslandPalette.Background(s,false)=="#101114");
            Assert(IslandPalette.CollapsedFill(s,false) is SolidColorBrush fill && fill.IsFrozen && fill.Color.A==128);
            s.CollapsedSameColor=true;Assert(IslandPalette.Collapsed(s,false)=="#101114" && IslandPalette.CollapsedColor(s,false).A==128);
            s.CollapsedOpacity=1;Assert(IslandPalette.CollapsedColor(s,false)==C("#101114"));
        });
        test("Theme: content ink follows the surface it sits on",()=>
        {
            Assert(IslandPalette.ToneFor("#000000")==Tone.Dark && IslandPalette.ToneFor("#182028")==Tone.Dark && IslandPalette.ToneFor("#3D4350")==Tone.Dark);
            Assert(IslandPalette.ToneFor(IslandPalette.LightSurface)==Tone.Light && IslandPalette.ToneFor("#FFFFFF")==Tone.Light && IslandPalette.ToneFor("#E3D2AF")==Tone.Light);
        });
        test("Theme: light ink and fill re-mapping stays readable and keeps hue",()=>
        {
            var light=ThemeInk.LightBase;
            foreach(string ink in new[]{"#F2F3F5","#A6ABB4","#FFFFFF","#8B8D94","#C7FF91","#FF9F0A","#F0B8B2","#9AA394"})
            {var mapped=ThemeInk.OnLight(C(ink));Assert(ThemeInk.Contrast(Color.FromRgb(mapped.R,mapped.G,mapped.B),light)>=4.5,"Low contrast for "+ink);}
            var lime=ThemeInk.OnLight(C("#C7FF91"));Assert(lime.G>lime.R && lime.G>lime.B,"Lime keeps its hue");
            Assert(ThemeInk.OnLight(C("#111317"))==C("#111317") && ThemeInk.OnLight(C("#80FFFFFF")).A==0x80);
            Assert(ThemeInk.Luminance(ThemeInk.SurfaceOnLight(C("#16191F")))>.7 && ThemeInk.Luminance(ThemeInk.SurfaceOnLight(C("#000000")))>.9);
            Assert(ThemeInk.SurfaceOnLight(C("#12FFFFFF"))==C("#12000000") && ThemeInk.SurfaceOnLight(C("#C7FF91"))==C("#C7FF91") && ThemeInk.SurfaceOnLight(C("#00000000"))==C("#00000000"));
            Assert(ThemeInk.SurfaceOnLight(C("#E61A1D22")).A==0xE6,"Translucent dark fills keep their alpha");
            var live=new SolidColorBrush(Colors.White);Assert(ReferenceEquals(ThemeInk.Remap(live,ThemeInk.Role.Ink),live),"Live palette brushes are owner-managed");
            Assert(ReferenceEquals(ThemeInk.Remap(UI.Brush("#F2F3F5"),ThemeInk.Role.Ink),ThemeInk.Remap(UI.Brush("#F2F3F5"),ThemeInk.Role.Ink)));
        });
        test("Theme: a light tone re-maps loaded content and returning to dark restores the originals",()=>
        {
            if(Accessibility.HighContrast)return;
            var white=UI.Brush("#F2F3F5");var card=UI.Brush("#16191F");var swatch=UI.Brush("#182028");
            var text=new TextBlock{Foreground=white};var border=new Border{Background=card,BorderBrush=UI.Brush("#30343B")};var icon=new System.Windows.Shapes.Rectangle{Fill=Brushes.White};var kept=new Border{Background=swatch};
            var host=new StackPanel();host.Children.Add(text);host.Children.Add(border);host.Children.Add(icon);host.Children.Add(kept);ThemeInk.SetTone(kept,Tone.Fixed);
            ThemeInk.SetTone(host,Tone.Light);ThemeInk.Refresh(host);
            Assert(text.Foreground is SolidColorBrush ink && ThemeInk.Contrast(ink.Color,ThemeInk.LightBase)>=4.5,"Text is dark on light");
            Assert(border.Background is SolidColorBrush fill && ThemeInk.Luminance(fill.Color)>.7,"Card fill is light");
            Assert(icon.Fill is SolidColorBrush glyph && ThemeInk.Luminance(glyph.Color)<.2,"Icon ink is dark");
            Assert(ReferenceEquals(kept.Background,swatch),"Fixed content keeps its data color");
            ThemeInk.SetTone(host,Tone.Dark);ThemeInk.Refresh(host);
            Assert(ReferenceEquals(text.Foreground,white) && ReferenceEquals(border.Background,card) && ReferenceEquals(icon.Fill,Brushes.White),"Dark restores the original brushes");
            // A brush set by code after re-mapping becomes the new original.
            ThemeInk.SetTone(host,Tone.Light);ThemeInk.Refresh(host);text.Foreground=UI.Muted;ThemeInk.Reapply(text);
            Assert(text.Foreground is SolidColorBrush muted && ThemeInk.Contrast(muted.Color,ThemeInk.LightBase)>=4.5);
            ThemeInk.SetTone(host,Tone.Dark);ThemeInk.Refresh(host);Assert(ReferenceEquals(text.Foreground,UI.Muted));
        });
        test("Theme: control tokens ship dark values and light counterparts",()=>
        {
            Assert(UI.ThemeTokens.Select(t=>t.Key).Distinct().Count()==UI.ThemeTokens.Length);
            foreach(var token in UI.ThemeTokens){C(token.Dark);C(token.Light);}
            Assert(UI.ThemeTokens.Any(t=>t.Key=="HoverWash" && t.Dark=="#12FFFFFF") && UI.ThemeTokens.Any(t=>t.Key=="CheckedFill" && t.Dark=="#ECEDEF"));
        });
        test("Theme: reset, layout profiles, undo and older backups cover the new appearance keys",()=>
        {
            Assert(AppearanceTheme.Keys.All(k=>PreferenceReset.Categories["Appearance"].Contains(k)) && AppearanceTheme.Keys.All(k=>CustomizationProfile.CustomizationKeys.Contains(k)));
            var source=new Settings{ColorTheme="System",CollapsedSameColor=false,CollapsedBackground="#2E3036",CollapsedOpacity=.6};var target=new Settings();
            CustomizationProfile.Import(CustomizationProfile.Export(source),target);
            Assert(target.ColorTheme=="System" && target.CollapsedSameColor==false && target.CollapsedBackground=="#2E3036" && Math.Abs(target.CollapsedOpacity-.6)<1e-9);
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-theme-backup-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new Store(root);var current=new Settings{ColorTheme="Light"};current.Normalize();store.Save("settings",current);
                var old=JsonNode.Parse(JsonSerializer.Serialize(FactoryDefaults.Create()))!.AsObject();foreach(string key in AppearanceTheme.Keys)old.Remove(key);
                File.WriteAllText(Path.Combine(root,"preference-backup.json"),old.ToJsonString());
                PreferenceReset.Restore(store,current);Assert(current.ColorTheme=="Dark" && current.CollapsedSameColor==true,"A pre-theme backup restores the classic look");
                current.ColorTheme="Light";current.CollapsedOpacity=.4;PreferenceReset.Reset(store,current,"Appearance");Assert(current.ColorTheme=="Dark" && current.CollapsedOpacity==1);
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        });
        test("Settings: Naby's dock panels keep their own size below the widget panel limits",()=>
        {
            var small=new Settings{PanelWidth=300,PanelMaxHeight=240};var mid=new Settings{PanelWidth=700,PanelMaxHeight=600,PanelScale=1.2,PanelPadding=40};var narrow=new Settings{PanelWidth=500,PanelMaxHeight=500};
            Assert(AssistantView.PanelSize(small)==AssistantView.PanelSize(mid) && AssistantView.PanelSize(small,true)==AssistantView.PanelSize(narrow,true));
        });
    }
}
