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

internal static class IntegrationSettingsDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();app.Media.DiagnosticFixture=false;app.Settings.SettingsMaterial="Solid";app.Settings.ReduceMotion=true;app.ChangeSettings();
        var window=new SettingsWindow(app);window.Show();await Task.Delay(150);
        var fitting=typeof(SettingsWindow).GetField("fittingBounds",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var navigate=typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!;
        // Only this isolated fixture freezes real-monitor containment while it simulates DIP viewports.
        fitting.SetValue(window,true);window.MinHeight=1;window.MinWidth=1;
        try
        {
            var closedPreview=(Border)typeof(SettingsWindow).GetField("preview",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            check("Integration idle Closed/Music preview omits brand fallback and waveform",!Diagnostics.Descendants(closedPreview).OfType<TextBlock>().Any(t=>t.Text=="NabyScape") && !Diagnostics.Descendants(closedPreview).OfType<ActivityVisual>().Any());
            foreach(var pixels in new[]{(1024,600),(1280,720),(1366,768),(1920,1080)})foreach(double dpi in new[]{1d,1.5,2})
            {
                double width=pixels.Item1/dpi,height=pixels.Item2/dpi-48;
                string tag=$"{pixels.Item1}x{pixels.Item2}-{dpi*100:0}pct";
                window.Width=width;window.Height=height;await Task.Delay(40);window.UpdateLayout();
                var form=Diagnostics.Descendants(window).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)=="Settings form viewport");
                var preview=Diagnostics.Descendants(window).OfType<Expander>().Single(e=>AutomationProperties.GetName(e)=="Settings live preview");
                var chooser=Diagnostics.Descendants(window).OfType<ComboBox>().Single(e=>AutomationProperties.GetName(e)=="Settings category");
                check($"Matrix {tag} viewport {window.ActualWidth:0.##}x{window.ActualHeight:0.##} DIP incl 48 DIP taskbar; form {form.ActualHeight:0.##}",Math.Abs(window.ActualWidth-width)<1 && Math.Abs(window.ActualHeight-height)<1 && form.ActualHeight>=64);
                check($"Matrix {tag} category chooser and preview collapse",chooser.IsVisible==(width<760) && (width>=1100 && height>=600 || !preview.IsExpanded));
                foreach(string category in new[]{"Appearance","Layout","Content","Controls","Audio","System","Launch","Application"})
                {
                    navigate.Invoke(window,new object[]{category});window.UpdateLayout();
                    foreach(var advanced in Diagnostics.Descendants(form).OfType<Expander>().ToArray())advanced.IsExpanded=true;
                    window.UpdateLayout();
                    var presenter=Diagnostics.Descendants(form).OfType<ScrollContentPresenter>().First();
                    var controls=Diagnostics.Descendants(form).OfType<FrameworkElement>().Where(e=>e.IsVisible && e.ActualWidth>0 && e is Button or Slider or TextBox or ComboBox or CheckBox or ColorField).ToArray();
                    string overflow=string.Join("; ",controls.Where(e=>{var r=e.TransformToAncestor(presenter).TransformBounds(new Rect(e.RenderSize));return r.Left<-.5 || r.Right>presenter.ActualWidth+.5;}).Select(e=>e.GetType().Name+":"+AutomationProperties.GetName(e)).Distinct());
                    check($"Matrix {tag} {category} horizontal controls fit ({overflow})",overflow.Length==0);
                    bool reachable=controls.Length>0;
                    foreach(var control in controls.Where(c=>AutomationProperties.GetName(c).Length>0))
                    {
                        if(control.Focusable && control.IsEnabled)reachable &= control.Focus();
                        control.BringIntoView();window.UpdateLayout();var r=control.TransformToAncestor(presenter).TransformBounds(new Rect(control.RenderSize));
                        reachable &= r.Bottom>0 && r.Top<presenter.ActualHeight && presenter.ActualHeight>=Math.Min(control.ActualHeight,24);
                    }
                    check($"Matrix {tag} {category} named controls scroll into view",reachable);
                    form.ScrollToBottom();window.UpdateLayout();check($"Matrix {tag} {category} scroll reaches end",Math.Abs(form.VerticalOffset-form.ScrollableHeight)<1);
                }
                navigate.Invoke(window,new object[]{"Appearance"});form.ScrollToTop();window.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width*dpi),(int)Math.Ceiling(height*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);bitmap.Render(window);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(directory,"settings-simulated-"+tag+".png")))encoder.Save(file);
                if(width==512 && height==252)
                {
                    var material=Diagnostics.Descendants(form).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Settings material");material.Focus();material.BringIntoView();window.UpdateLayout();
                    bitmap=new RenderTargetBitmap((int)Math.Ceiling(width*dpi),(int)Math.Ceiling(height*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);bitmap.Render(window);encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(directory,"settings-small-material-control-simulated.png")))encoder.Save(file);
                }
            }
            window.Width=1100;window.Height=780;navigate.Invoke(window,new object[]{"Application"});window.UpdateLayout();
            check("Integration preference reset category chooser present",Diagnostics.Descendants(window).OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="Preference reset category"));
            File.WriteAllText(Path.Combine(directory,"matrix-method.txt"),"12 synthetic logical viewports: pixels / DPI, then subtract 48 DIP taskbar. RenderTargetBitmap at target DPI. Real-monitor containment frozen through existing private fittingBounds only inside fixture. No physical DPI, hotplug, native backdrop or modal reset keyboard claim.\n");
        }
        finally{fitting.SetValue(window,false);window.Close();}
    }
}
