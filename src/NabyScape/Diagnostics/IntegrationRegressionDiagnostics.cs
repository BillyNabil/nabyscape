using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace NabyScape;
internal static class IntegrationRegressionDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,Action<string,bool> check)
    {
        app.Settings.HideFullscreen=app.Settings.HideMaximized=false;app.Settings.ReduceMotion=false;app.Settings.SmartActivities=false;
        app.Settings.PanelWidth=340;app.Settings.PanelScale=1.25;foreach(var widget in app.Settings.Widgets)widget.Enabled=true;
        var shortcut=new Shortcut{Name="Diagnostic long shortcut",Target="https://example.com/"+new string('w',240)};app.Settings.Shortcuts.Add(shortcut);app.ChangeSettings();island.ShowCommands(false);await Task.Delay(250);island.UpdateLayout();
        var last=Diagnostics.Descendants(island).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==shortcut.Name);
        var scroll=Diagnostics.Descendants(island).OfType<ScrollViewer>().First(v=>Diagnostics.Descendants(v).Contains(last));
        scroll.ScrollToEnd();await Task.Delay(150);island.UpdateLayout();var bounds=last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
        check($"Focused command scroll: row {bounds}; viewport {scroll.ActualHeight}; offset {scroll.VerticalOffset}/{scroll.ScrollableHeight}",bounds.Top>=-.5 && bounds.Bottom<=scroll.ActualHeight+.5);
        app.Settings.Shortcuts.Remove(shortcut);app.Settings.PanelScale=1;app.ChangeSettings();typeof(OverlayWindow).GetMethod("CloseCommands",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(island,null);
        var window=new SettingsWindow(app);window.Show();await Task.Delay(150);
        try
        {
            Diagnostics.Descendants(window).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Island color color picker").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();
            foreach(var expander in Diagnostics.Descendants(window).OfType<Expander>().Where(e=>e.Header?.ToString()=="Color values · HEX / RGB").ToArray())expander.IsExpanded=true;window.UpdateLayout();
            var hex=Diagnostics.Descendants(window).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Island color hex");hex.Focus();hex.Text="#182028";
            for(int i=0;i<60 && app.Store.Load<Settings>("settings").Background!="#182028";i++)await Task.Delay(50);
            check($"Focused valid HEX persists: live {app.Settings.Background}, saved {app.Store.Load<Settings>("settings").Background}",app.Settings.Background=="#182028" && app.Store.Load<Settings>("settings").Background=="#182028");
        }
        finally{window.Close();}
        app.Settings.DisplayMode="Selected";
        foreach(var screen in System.Windows.Forms.Screen.AllScreens)
        {
            app.Settings.Monitor=screen.DeviceName;app.ChangeSettings();await Task.Delay(300);Native.GetWindowRect(new WindowInteropHelper(island).Handle,out var rect);
            check($"Focused selected-monitor containment {screen.DeviceName} mode {app.Settings.DisplayMode}",rect.Left>=screen.Bounds.Left && rect.Right<=screen.Bounds.Right && rect.Top>=screen.Bounds.Top && rect.Bottom<=screen.Bounds.Bottom);
        }
        foreach(double width in new[]{514d,340d})
        {
            app.Settings.ReduceMotion=true;app.Settings.PanelWidth=width;app.ChangeSettings();island.Reveal(false);island.SelectPage("focus",false);await Task.Delay(100);island.UpdateLayout();
            var starts=Diagnostics.Descendants(island).OfType<Button>().Where(b=>b.IsVisible && b.Content?.ToString()=="Start").ToArray();check("Focused Start controls exist at "+width,starts.Length>=2);
            foreach(var button in starts)
            {
                var text=new FormattedText("Start",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(button.FontFamily,button.FontStyle,button.FontWeight,button.FontStretch),button.FontSize,Brushes.White,VisualTreeHelper.GetDpi(button).PixelsPerDip);
                double required=text.WidthIncludingTrailingWhitespace+button.Padding.Left+button.Padding.Right;
                check($"Focused Start text fits at {width}: {AutomationProperties.GetName(button)} width {button.ActualWidth:0.##}, required {required:0.##}",button.ActualWidth+.5>=required);
            }
        }
    }
}
