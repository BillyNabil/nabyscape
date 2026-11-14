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
            foreach(string code in LocalizationCatalog.Languages.Select(l=>l.Code))foreach(double scale in new[]{1d,1.5})
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
            // Long Russian/Ukrainian captions and Hangul fallback: every sidebar label and visible button must show its full text.
            bool showAll=s.SettingsShowAll;s.SettingsShowAll=true;
            try
            {
                foreach(string code in LocalizationCatalog.Languages.Select(l=>l.Code))foreach(double scale in new[]{1d,1.5})
                {
                    s.UiLanguage=code;s.AccessibilityTextScale=scale;s.AccessibilityHighContrast=false;Localization.Configure(s);UI.ApplyControlTheme(s);
                    var window=new SettingsWindow(app){ShowActivated=false,Width=1100,Height=760};
                    try
                    {
                        window.Show();
                        foreach(string page in new[]{"Home","Application"})
                        {
                            Diagnostics.NavigateSettings(window,page);await Task.Delay(120);window.UpdateLayout();
                            var nav=(System.Collections.Generic.Dictionary<string,Button>)typeof(SettingsWindow).GetField("navigationButtons",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                            var labels=nav.Values.Where(b=>b.IsVisible).SelectMany(b=>Diagnostics.Descendants(b).OfType<TextBlock>()).Where(t=>Clipped(t,t.Text,0)).Select(t=>t.Text).ToArray();
                            check($"Production UI Settings navigation labels fit {code}/{scale}/{page} (clipped: {string.Join(" | ",labels)})",labels.Length==0 && nav.Values.Count(b=>b.IsVisible)>=14);
                            var buttons=Diagnostics.Descendants(window).OfType<Button>().Where(b=>b.IsVisible && b.Content is string && !nav.ContainsValue(b) && Clipped(b,(string)b.Content,b.Padding.Left+b.Padding.Right+b.BorderThickness.Left+b.BorderThickness.Right)).Select(b=>(string)b.Content).Distinct().ToArray();
                            check($"Production UI Settings buttons show their full captions {code}/{scale}/{page} (clipped: {string.Join(" | ",buttons)})",buttons.Length==0);
                            Capture(window,$"production-settings-wide-{code}-{scale:0.0}-{page.ToLowerInvariant()}");
                        }
                    }
                    finally{window.Close();}
                }
            }
            finally{s.SettingsShowAll=showAll;}
        }
        finally{s.UiLanguage=language;s.AccessibilityTextScale=textScale;s.AccessibilityHighContrast=contrast;Localization.Configure(s);UI.ApplyControlTheme(s);}
        // Text wider than its element or layout slot (or, when wrapping, a single word wider than a line) is trimmed or cut off.
        static bool Clipped(FrameworkElement element,string text,double chrome)
        {
            if(text.Length==0 || !element.IsVisible || element.ActualWidth<=0)return false;
            var measured=new FormattedText(text,System.Globalization.CultureInfo.CurrentUICulture,element.FlowDirection,new Typeface(System.Windows.Documents.TextElement.GetFontFamily(element),System.Windows.Documents.TextElement.GetFontStyle(element),System.Windows.Documents.TextElement.GetFontWeight(element),System.Windows.Documents.TextElement.GetFontStretch(element)),System.Windows.Documents.TextElement.GetFontSize(element),Brushes.White,VisualTreeHelper.GetDpi(element).PixelsPerDip);
            double slot=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(element) is {Bounds:{IsEmpty:false} bounds}?bounds.Width:double.PositiveInfinity;
            double width=Math.Min(element.ActualWidth,slot)-chrome;
            if(element is TextBlock{TextWrapping:not TextWrapping.NoWrap})return measured.MinWidth>width+.5;
            return measured.WidthIncludingTrailingWhitespace>width+.5;
        }

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
