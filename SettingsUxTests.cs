using System;
using System.Windows.Media;

namespace NabyScape;

internal static class SettingsUxTests
{
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool value){if(!value)throw new Exception("Settings UX invariant failed");}
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
