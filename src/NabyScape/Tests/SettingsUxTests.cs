using System;
using System.Linq;
using System.Windows.Media;

namespace NabyScape;

internal static class SettingsUxTests
{
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool value){if(!value)throw new Exception("Settings UX invariant failed");}
        test("Settings backdrop gates and native errors are explicit and fail closed",()=>
        {
            var ready=new SettingsBackdrop.Conditions(true,false,true,false,false);int calls=0;
            int Apply(int type){calls++;return 0;}
            foreach(var blocked in new[]{ready with{Supported=false},ready with{HighContrast=true},ready with{Transparency=false},ready with{EnergySaver=true},ready with{Remote=true}})
            {var result=SettingsBackdrop.Resolve("Native Acrylic",blocked,Apply);Assert(!result.Native && result.Actual=="Solid" && result.Reason.Length>0);}
            Assert(calls==0);
            foreach(string name in new[]{"Native Mica","Native Acrylic"})
            {
                int seen=0;var ok=SettingsBackdrop.Resolve(name,ready,type=>{seen=type;return 0;});Assert(ok.Native && seen==(name=="Native Mica"?2:3));
                var failure=SettingsBackdrop.Resolve(name,ready,_=>unchecked((int)0x80070057));Assert(!failure.Native && failure.Actual=="Solid" && failure.Status.Contains("80070057"));
            }
            Assert(SettingsBackdrop.Resolve("Solid",ready,Apply).Actual=="Solid" && calls==0);
            Assert(SettingsBackdrop.Resolve("Simulated glass",ready,Apply).Actual=="Simulated glass" && calls==0);
            Assert(SettingsBackdrop.Requested(new Settings())=="Solid" && SettingsBackdrop.Requested(new Settings{GlassStyle=false})=="Solid");
            Assert(SettingsBackdrop.Resolve("unknown",ready,Apply).Actual=="Solid" && calls==0);
            Assert(CustomizationProfile.CustomizationKeys.Contains("SettingsMaterial") && PreferenceReset.Categories["Appearance"].Contains("SettingsMaterial"));
        });
        test("Settings material persists independently and old layouts preserve the selection",()=>
        {
            var settings=new Settings{SettingsMaterial="Native Acrylic",GlassStyle=false};
            var restored=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
            restored.Normalize();Assert(restored.SettingsMaterial=="Native Acrylic" && !restored.GlassStyle);
            var imported=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(settings),imported);
            Assert(imported.SettingsMaterial=="Native Acrylic" && !imported.GlassStyle);
            CustomizationProfile.Import("{\"format\":\"luma.layout.v1\",\"values\":{\"GlassStyle\":true}}",imported);
            Assert(imported.SettingsMaterial=="Native Acrylic" && imported.GlassStyle);
            imported.SettingsMaterial="invalid";imported.Normalize();Assert(SettingsBackdrop.Requested(imported)=="Solid");
        });
        test("Every registered Settings example is unique and renders without a running app",()=>
        {
            Assert(SettingsScene.Sections.Contains("Updates") && SettingsScene.Sections.Contains("Reset preferences") && SettingsScene.Sections.Distinct().Count()==SettingsScene.Sections.Length);
            foreach(string kind in SettingsScene.Sections)
            {
                var scene=new SettingsScene(kind,new Settings());scene.Measure(new System.Windows.Size(420,200));scene.Arrange(new System.Windows.Rect(0,0,420,200));scene.UpdateLayout();
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(420,200,96,96,PixelFormats.Pbgra32);bitmap.Render(scene);
            }
        });
        test("Settings bounds fit small, detached and scaled work areas",()=>
        {
            foreach(double scale in new[]{1d,1.5,2})
            foreach(var area in new[]{new System.Windows.Rect(0,0,480*scale,300*scale),new System.Windows.Rect(-1920,80,1280,720)})
            {
                var result=SettingsWindow.FitWorkingArea(new System.Windows.Rect(2800,-800,1700,1200),area,scale);
                Assert(area.Contains(result) && result.Width>=Math.Min(480*scale,area.Width) && result.Height>=Math.Min(300*scale,area.Height));
            }
        });
        test("Interactive Settings movement crosses adjacent displays before containment",()=>
        {
            var left=new System.Windows.Rect(0,0,1920,1080);var right=new System.Windows.Rect(1920,0,1920,1080);
            foreach(int x in new[]{1000,1300,1500,1700,1900,2100})
            {
                var moving=new System.Windows.Rect(x,100,900,700);
                var area=x+moving.Width/2<1920?left:right;
                var result=SettingsWindow.ShouldFitSettingsBounds(true,false)?SettingsWindow.FitWorkingArea(moving,area,1):moving;
                Assert(result==moving);
            }
            var destination=new System.Windows.Rect(2100,100,900,700);
            Assert(SettingsWindow.ShouldFitSettingsBounds(false,false) && SettingsWindow.FitWorkingArea(destination,right,1)==destination);
            var detached=new System.Windows.Rect(2600,-100,900,700);
            Assert(SettingsWindow.ShouldFitSettingsBounds(true,true) && left.Contains(SettingsWindow.FitWorkingArea(detached,left,1)));
            var scaled=new System.Windows.Rect(1700,900,1100,780);
            Assert(right.Contains(SettingsWindow.FitWorkingArea(scaled,right,1.5)));
        });
        test("Widget labels and caption have separate measured tile space",()=>
        {
            var settings=new Settings();foreach(var widget in settings.Widgets)widget.Enabled=true;
            foreach(double dpi in new[]{1d,1.5,2})
            {
                var tiles=SettingsScene.WidgetTiles(settings,dpi);Assert(tiles.Length==settings.Widgets.Count);
                foreach(var tile in tiles)Assert(tile.Label.Width<=tile.Bounds.Width-12+.01 && tile.Label.Height<=tile.Bounds.Height-12+.01);
                Assert(tiles.Any(tile=>tile.Bounds.Height>28));
                var scene=new SettingsScene("Enabled widgets & order",settings);scene.Measure(new System.Windows.Size(420,double.PositiveInfinity));Assert(scene.DesiredSize.Height>=tiles.Last().Bounds.Bottom+42);
            }
        });
        test("Tool preferences migrate without deleting old data or enabling new hover pages",()=>
        {
            var s=new Settings{Widgets=new(){new(){Id="tools"}},Tools=new(){new(){Id="files",Enabled=false},new(){Id="files"},new(){Id="bad"}}};s.Normalize();
            Assert(s.Tools.Count==Settings.ToolNames.Count && !s.ToolEnabled("files") && s.ToolEnabled("notes") && s.ActivitiesEnabled && s.HoverViews.Count==HoverPagePolicy.Ids.Length && !s.HoverViews.Single(v=>v.Id=="clips").Enabled && !s.HoverViews.Single(v=>v.Id=="tasks").Enabled);
            s.ActivitiesEnabled=false;var target=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(s),target);Assert(!target.ActivitiesEnabled && !target.ToolEnabled("files") && target.ToolEnabled("tasks"));
        });
        test("Leaving a scrolling media title restores its bounded ellipsis layout",()=>
        {
            var text=new System.Windows.Controls.TextBlock{Text=new string('W',180)};var label=new MarqueeLabel(text);
            label.Measure(new System.Windows.Size(90,24));label.Arrange(new System.Windows.Rect(0,0,90,24));
            text.Width=2000;text.TextTrimming=System.Windows.TextTrimming.None;
            label.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0){RoutedEvent=System.Windows.Input.Mouse.MouseLeaveEvent});
            Assert(double.IsNaN(text.Width) && text.TextTrimming==System.Windows.TextTrimming.CharacterEllipsis && label.ClipToBounds);
        });
        test("Selected media meters match Spotify desktop and packaged identities without including Discord",()=>
        {
            Assert(AudioService.MediaProcessMatches("Spotify.exe","Spotify") && AudioService.MediaProcessMatches("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify","Spotify") && !AudioService.MediaProcessMatches("Spotify.exe","Discord") && !AudioService.MediaProcessMatches("Discord.exe","Spotify"));
        });
        test("Layout and visibility settings have concrete explanations",()=>
        {
            foreach(string name in new[]{"Content size","Content inset","Two-stage reveal","Tuck above maximized apps","Reduce motion","Glass intensity"})Assert(SettingsWindow.SettingDescription(name).Length>30);
        });
        test("Color picker constructs and applies its hue template under the application theme",()=>
        {
            var application=System.Windows.Application.Current ?? new System.Windows.Application();
            application.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary{Source=new Uri("/NabyScape;component/Theme.xaml",UriKind.Relative)});
            var picker=new ColorPicker("Test color","#182028",new[]{"#FFFFFF"},Array.Empty<string>(),_=>{},_=>{});
            var hue=(System.Windows.Controls.Slider)typeof(ColorPicker).GetField("hue",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(picker)!;
            var editor=(System.Windows.Controls.StackPanel)picker.Children[1];Assert(editor.Visibility==System.Windows.Visibility.Collapsed);
            ((System.Windows.Controls.Button)picker.Children[0]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Assert(editor.Visibility==System.Windows.Visibility.Visible);
            hue.ApplyTemplate();
            Assert(hue.Template.FindName("PART_Track",hue) is System.Windows.Controls.Primitives.Track && picker.Value=="#182028");
        });
        test("Monitor rows reattach after repeated scroll-owner and legacy-panel rebuilds",()=>
        {
            var rows=new System.Windows.Controls.StackPanel();
            var original=UI.Scroll(rows);
            OverlayWindow.DetachMonitorRows(rows);
            var rebuilt=UI.Scroll(rows);
            Assert(original.Content==null && ReferenceEquals(rows.Parent,rebuilt));
            OverlayWindow.DetachMonitorRows(rows);
            var classic=new System.Windows.Controls.StackPanel();classic.Children.Add(rows);
            OverlayWindow.DetachMonitorRows(rows);
            var compact=UI.Scroll(rows);
            Assert(rebuilt.Content==null && classic.Children.Count==0 && ReferenceEquals(rows.Parent,compact));
        });
        test("Color picker HSV preserves RGB colors and bounded channels",()=>
        {
            foreach(var c in new[]{Colors.Black,Colors.White,Colors.Red,Colors.Lime,Colors.Blue,Color.FromRgb(24,32,40),Color.FromRgb(211,41,187)})
            {var hsv=ColorPicker.ToHsv(c);Assert(ColorPicker.FromHsv(hsv.hue,hsv.saturation,hsv.value)==c);Assert(ColorPicker.Format(c).Length==7);}
            Assert(ColorPicker.FromHsv(360,2,-1)==Colors.Black && ColorPicker.FromHsv(-60,1,1)==Colors.Magenta);
        });
        test("Presentation settings normalize safely and retain independent choices",()=>
        {
            var s=new Settings{ClosedMediaContent="invalid",ClosedMediaIcon="invalid",CategoryStyle="invalid",LaunchStyle="Icons",VisibilityHotkeyKey="?",VisibilityHotkeyModifiers="bad",RecentColors=new(){"bad","#182028","#182028"},BoldMediaTitle=false,BoldHeadings=true};s.Normalize();
            Assert(s.ClosedMediaContent=="Title" && s.ClosedMediaIcon=="Waveform" && s.CategoryStyle=="IconsLabels" && s.LaunchStyle=="Icons" && s.VisibilityHotkeyKey=="H" && s.VisibilityHotkeyModifiers=="Ctrl+Alt" && s.RecentColors.Count==1 && !s.BoldMediaTitle && s.BoldHeadings && !s.HideMaximized);
        });
        test("Control colors validate and profile preferences preserve navigation",()=>
        {
            var s=new Settings{ControlFill="bad",ControlBorder="bad",SelectionFill="bad"};s.Normalize();Assert(s.ControlFill=="#16171B" && s.ControlBorder=="#303238" && s.SelectionFill=="#34373E");
            s.ControlFill="#151922";s.ControlBorder="#414958";s.SelectionFill="#283B54";s.PanelSwipe=false;var target=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(s),target);Assert(target.ControlFill==s.ControlFill && target.ControlBorder==s.ControlBorder && target.SelectionFill==s.SelectionFill && !target.PanelSwipe);
        });
        test("Control contrast follows light and dark custom surfaces",()=>
        {Assert(((SolidColorBrush)UI.ContrastText("#FFFFFF")).Color==Color.FromRgb(17,19,23) && ((SolidColorBrush)UI.ContrastText("#101114")).Color==Colors.White);});
        test("Presentation profiles exclude personal application identities and machine hotkeys",()=>
        {
            var s=new Settings{ClosedMediaContent="Waveform",ClosedMediaIcon="Artwork",CategoryStyle="Icons",LaunchStyle="Text",VisibilityHotkeyKey="Q",HotkeyKey="Z",Shortcuts=new(){new(){Name="Private",Target="explorer.exe",AppId="PrivateAppId"}}};var encoded=CustomizationProfile.Export(s);
            Assert(!encoded.Contains("Private") && !encoded.Contains("Hotkey"));var target=new Settings();CustomizationProfile.Import(encoded,target);Assert(target.ClosedMediaContent=="Waveform" && target.ClosedMediaIcon=="Artwork" && target.CategoryStyle=="Icons" && target.LaunchStyle=="Text" && target.VisibilityHotkeyKey=="H" && target.HotkeyKey=="I");
        });
    }
}
