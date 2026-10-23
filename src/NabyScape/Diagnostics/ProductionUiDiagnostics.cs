using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

// Runs only in an isolated diagnostic profile. No device, location or provider request.
internal static class ProductionUiDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var s=app.Settings;island.Hide();
        string language=s.UiLanguage;double textScale=s.AccessibilityTextScale;bool contrast=s.AccessibilityHighContrast;
        try
        {
            foreach(string code in new[]{"en","es","id"})foreach(double scale in new[]{1d,1.5})
            {
                s.UiLanguage=code;s.AccessibilityTextScale=scale;s.AccessibilityHighContrast=scale==1.5;Localization.Configure(s);UI.ApplyControlTheme(s);
                var window=new SettingsWindow(app){ShowActivated=false,Width=600,Height=650};
                try
                {
                    window.Show();Diagnostics.NavigateSettings(window,"Application");await Task.Delay(70);window.UpdateLayout();
                    check($"Production UI localized Settings {code}/{scale}",window.Title==Localization.Text("NabyScape · Settings") && Diagnostics.CurrentSettingsCategory(window)=="Application");
                    var form=Diagnostics.Descendants(window).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)==Localization.Text("Settings form viewport"));
                    check($"Production UI General usable viewport {code}/{scale}",form.ActualWidth>=300 && form.ActualHeight>=80);
                    var lang=Diagnostics.Descendants(window).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)==Localization.Text("Interface language"));
                    lang.BringIntoView();await Task.Delay(20);window.UpdateLayout();
                    var bounds=lang.TransformToAncestor(form).TransformBounds(new Rect(lang.RenderSize));
                    check($"Production UI language control is reachable and keyboard accessible {code}/{scale}",lang.IsEnabled && lang.Focusable && lang.IsTabStop && bounds.Left>=-.5 && bounds.Right<=form.ActualWidth+.5 && bounds.Top>=-.5 && bounds.Bottom<=form.ActualHeight+.5);
                    Capture(window,$"production-settings-{code}-{scale:0.0}");
                    var sample=UI.Caption("Scale once",12);var host=new Window{Content=sample,Width=240,Height=100,ShowActivated=false};
                    try
                    {
                        host.Show();await Task.Delay(10);double once=sample.FontSize;host.Content=null;host.Content=sample;await Task.Delay(10);
                        check($"Production UI text is scaled exactly once on reload {code}/{scale} (once={once}, current={sample.FontSize}, configured={Accessibility.TextScale}, loaded={sample.IsLoaded}, source={DependencyPropertyHelper.GetValueSource(sample,TextBlock.FontSizeProperty).BaseValueSource})",Math.Abs(once-12*scale)<.01 && Math.Abs(sample.FontSize-once)<.01);
                        check($"Production UI high contrast follows preference {code}/{scale} (foreground={sample.Foreground}, expected={SystemColors.WindowTextBrush}, enabled={Accessibility.HighContrast})",!s.AccessibilityHighContrast || Accessibility.HighContrast && sample.Foreground==SystemColors.WindowTextBrush);
                    }
                    finally{host.Close();}
                }
                finally{window.Close();}
            }
        }
        finally{s.UiLanguage=language;s.AccessibilityTextScale=textScale;s.AccessibilityHighContrast=contrast;Localization.Configure(s);UI.ApplyControlTheme(s);}

        s.ReduceMotion=false;s.PanelScale=1;s.PanelPadding=26;s.PanelMaxHeight=417;s.HideFullscreen=s.HideMaximized=s.AlwaysTucked=false;s.SmartActivities=false;
        s.WeatherCity="Synthetic forecast";s.WeatherLatitude=0;s.WeatherLongitude=0;s.Widgets.Single(w=>w.Id=="weather").Enabled=true;
        foreach(var state in new[]{(Name:"day",Day:true,Code:0),(Name:"night",Day:false,Code:0),(Name:"rain",Day:true,Code:63)})
        {
            var weather=new WeatherSnapshot{Key=WeatherService.Key(s),Updated=DateTimeOffset.UtcNow,Temperature=26,FeelsLike=28,High=30,Low=22,Humidity=65,Wind=8,RainChance=state.Code==63?80:10,Day=state.Day,Code=state.Code,Hours=Enumerable.Range(0,6).Select(i=>new WeatherHour{Time=$"{12+i}:00",Temperature=25+i%3,Day=state.Day,Code=state.Code}).ToList()};
            typeof(WeatherService).GetProperty(nameof(WeatherService.Cached),BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(app.Weather,weather);
            foreach(double width in new[]{526d,300d})
            {
                s.PanelWidth=width;app.ChangeSettings();island.Show();island.Reveal(false);island.ShowTools("weather",false);await Task.Delay(300);island.UpdateLayout();
                var view=Diagnostics.Descendants(island).OfType<WeatherView>().Single(v=>v.IsVisible);
                var scroll=(ScrollViewer)typeof(OverlayWindow).GetField("scroll",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(island)!;
                check($"Production UI forecast fits without outer scrolling {state.Name}/{width} (view={view.RenderSize}, scroll={scroll.RenderSize}, overflow={scroll.ScrollableWidth}/{scroll.ScrollableHeight}, painted={island.PaintedHeight})",scroll.ScrollableHeight<1 && scroll.ScrollableWidth<1 && view.ActualHeight>120);
                var art=Diagnostics.Descendants(view).OfType<WeatherArtwork>().Single(a=>a.IsVisible && a.DecorativeMotion);
                check($"Production UI weather motion remains active {state.Name}/{width}",!Motion.Enabled || art.RenderTransform.HasAnimatedProperties);
                Capture(island,$"production-weather-{state.Name}-{width}");
                island.Hide();await Task.Delay(20);check($"Production UI hidden weather releases animation {state.Name}/{width}",!art.RenderTransform.HasAnimatedProperties);
            }
        }
        check("Production UI weather uses only synthetic cached data",app.Weather.LastAttempt==default);
        check("Production UI never starts voice or desktop sharing",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);

        void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(element);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
    }
}
