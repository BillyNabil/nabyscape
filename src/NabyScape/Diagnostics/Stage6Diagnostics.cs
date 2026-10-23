using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace NabyScape;
internal static class Stage6Diagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();var window=new SettingsWindow(app);window.Show();await Task.Delay(300);
        try
        {
            check("Stage6 legacy Settings surface stays solid and island glass remains enabled",window.MaterialResult.Actual=="Solid" && window.Background is SolidColorBrush brush && brush.Color==Color.FromRgb(17,18,20) && app.Settings.GlassStyle);
            Diagnostics.ExpandSettingsExamples(window,"Settings material","Color & material");
            check("Stage6 distinct Settings material scene",Diagnostics.SettingsExamples(window).Count(s=>s.Section=="Settings material")==1);
            var choice=Diagnostics.Descendants(window).OfType<ComboBox>().Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Settings material");
            // Exercise normal category navigation and the existing section-index scroll path.
            foreach(string category in new[]{"Layout","Appearance"})
                Diagnostics.NavigateSettings(window,category);
            choice=Diagnostics.Descendants(window).OfType<ComboBox>().Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Settings material");
            check("Stage6 Appearance re-entry retains the material selector",choice.SelectedItem?.ToString()=="Solid");
            choice.BringIntoView();Diagnostics.ExpandSettingsExamples(window,"Settings material","Color & material");await Task.Delay(300);
            bool capture=Environment.GetCommandLineArgs().Contains("--stage6-capture");
            for(int cycle=0;cycle<2;cycle++)foreach(string material in SettingsBackdrop.Choices)
            {
                if(cycle==1 && material==SettingsBackdrop.Choices[0])
                {
                    foreach(string category in new[]{"Layout","Appearance"})Diagnostics.NavigateSettings(window,category);
                    choice=Diagnostics.Descendants(window).OfType<ComboBox>().Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Settings material");
                    choice.BringIntoView();Diagnostics.ExpandSettingsExamples(window,"Settings material","Color & material");await Task.Delay(300);
                    check("Stage6 repeated cycle follows Appearance re-entry",choice.SelectedItem?.ToString()==SettingsBackdrop.Requested(app.Settings));
                }
                choice.SelectedItem=material;await Task.Delay(800);window.UpdateLayout();var result=window.MaterialResult;
                string tag=material.Replace(' ','-').ToLowerInvariant()+(cycle==0?"":"-repeat");
                check("Stage6 "+tag+" "+result.Status,result.Requested==material && result.Actual.Length>0 && choice.SelectedItem?.ToString()==material && SettingsBackdrop.Requested(app.Settings)==material);
                check("Stage6 client and frame follow actual material "+material,result.Native?window.Background is SolidColorBrush tint && tint.Color.A==176 && HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.CompositionTarget.BackgroundColor==Colors.Transparent && WindowChrome.GetWindowChrome(window).GlassFrameThickness.Left==-1:window.Background!=Brushes.Transparent && WindowChrome.GetWindowChrome(window).GlassFrameThickness.Left==0);
                check("Stage6 Settings stays ordinary HWND and island stays layered "+material,!window.AllowsTransparency && island.AllowsTransparency);
                if(capture)
                {
                    File.WriteAllText(Path.Combine(directory,"capture-ready.txt"),tag+Environment.NewLine+result.Status);
                    string proceed=Path.Combine(directory,"continue-"+tag);var deadline=DateTime.UtcNow.AddSeconds(150);
                    string artifact=Path.Combine(directory,tag+"-wgc.jpg");
                    while((!File.Exists(proceed) || !File.Exists(artifact)) && DateTime.UtcNow<deadline)await Task.Delay(200);
                    check("Stage6 composed capture acknowledged "+material,File.Exists(proceed) && File.Exists(artifact) && new FileInfo(artifact).Length>0);
                }
                if(capture)continue; // Capture production transitions without intrusive bitmap rendering between them.
                // These are separate WPF-only checks, excluded from composed capture.
                // acknowledgment so rendering them cannot mask a composed defect.
                var scenes=Diagnostics.SettingsExamples(window).Where(s=>s.Section is "Settings material" or "Color & material").ToArray();
                check("Stage6 both material examples rendered "+tag,scenes.Length==2 && scenes.All(s=>s.ActualWidth>0 && s.ActualHeight>0));
                foreach(var scene in scenes)
                {
                    int width=(int)Math.Ceiling(scene.ActualWidth),height=(int)Math.Ceiling(scene.ActualHeight);var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(scene);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(directory,tag+"-"+(scene.Section=="Settings material"?"material":"island")+"-scene-wpf.png")))encoder.Save(file);
                    var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);double scale=Math.Min(1,scene.ActualWidth/420);int ink=0;
                    var area=scene.Section=="Settings material"?new Rect(34,31,124,18):new Rect(82,34,120,18);
                    // RenderTargetBitmap.Render retains this live visual's layout offset.
                    // Probe its painted label, rather than the transparent space above it.
                    var offset=VisualTreeHelper.GetOffset(scene);
                    for(int y=Math.Max(0,(int)(offset.Y+area.Top*scale));y<Math.Min(height,(int)(offset.Y+area.Bottom*scale));y++)for(int x=Math.Max(0,(int)(offset.X+area.Left*scale));x<Math.Min(width,(int)(offset.X+area.Right*scale));x++){int at=(y*width+x)*4;if(pixels[at]>160 && pixels[at+1]>160 && pixels[at+2]>160)ink++;}
                    check("Stage6 WPF scene static label survives "+tag+" / "+scene.Section+" (ink "+ink+")",ink>20);
                }
            }
            foreach(double width in new[]{480d,760d,1100d})
            {
                window.Width=width;window.Height=600;window.UpdateLayout();await Task.Delay(80);
                check("Stage6 caption controls and material selector survive resize "+width,Diagnostics.Descendants(window).OfType<Button>().Count(b=>System.Windows.Automation.AutomationProperties.GetName(b) is "Minimize Settings" or "Maximize Settings" or "Close Settings")==3 && choice.ActualWidth>0);
            }
            var imported=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(app.Settings),imported);check("Stage6 layout round-trips Settings material",imported.SettingsMaterial==app.Settings.SettingsMaterial);
            CustomizationProfile.Import("{\"format\":\"luma.layout.v1\",\"values\":{\"GlassStyle\":false}}",imported);check("Stage6 legacy layout imports without replacing independent Settings material",!imported.GlassStyle && imported.SettingsMaterial==app.Settings.SettingsMaterial);
            PreferenceReset.Reset(app.Store,app.Settings,"Appearance");app.ChangeSettings();check("Stage6 Appearance reset restores legacy solid Settings and island glass",window.MaterialResult.Actual=="Solid" && app.Settings.GlassStyle);
            File.WriteAllText(Path.Combine(directory,"native-environment.txt"),$"OS {Environment.OSVersion}\nConditions {SettingsBackdrop.ReadConditions()}. Native Settings use DWM with a dark translucent client foundation.\nIsland native backdrop not shipped: layered custom contour; notch transparency, hit testing, animated resize, DPI and performance gates are unproven.\n");
        }
        finally{window.Close();}
    }
}
