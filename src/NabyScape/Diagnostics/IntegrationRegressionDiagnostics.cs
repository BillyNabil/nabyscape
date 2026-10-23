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
            app.Session.Timer.Reset();app.Session.Focus.Reset();app.SaveSession();island.UpdateLayout();
            var starts=Diagnostics.Descendants(island).OfType<Button>().Where(b=>b.IsVisible && AutomationProperties.GetAutomationId(b) is "timer-primary" or "focus-primary").ToArray();check("Focused countdown primary controls exist at "+width,starts.Length==2 && starts.Select(AutomationProperties.GetAutomationId).Distinct().Count()==2 && starts.Select(AutomationProperties.GetName).Distinct().Count()==2);
            foreach(var button in starts)
            {
                var icon=(FrameworkElement)button.Content;var iconBounds=icon.TransformToAncestor(button).TransformBounds(new Rect(icon.RenderSize));
                check($"Countdown icon fits and remains accessible at {width}: {AutomationProperties.GetName(button)}; button={button.RenderSize}; icon={icon.RenderSize}; bounds={iconBounds}; enabled={button.IsEnabled}; focus={button.Focusable}/{button.IsTabStop}; state={AutomationProperties.GetItemStatus(button)}",button.ActualWidth>=35.9 && button.ActualHeight>=35.9 && button.IsEnabled && button.Focusable && button.IsTabStop && AutomationProperties.GetItemStatus(button)=="Start" && icon.ActualWidth>=10 && icon.ActualHeight>=16.9 && iconBounds.Left>=0 && iconBounds.Top>=0 && iconBounds.Right<=button.ActualWidth+.1 && iconBounds.Bottom<=button.ActualHeight+.1);
                button.BringIntoView();await Task.Delay(30);island.UpdateLayout();var viewport=Diagnostics.Descendants(island).OfType<ScrollViewer>().First(v=>Diagnostics.Descendants(v).Contains(button));var actionBounds=button.TransformToAncestor(viewport).TransformBounds(new Rect(button.RenderSize));
                check("Countdown primary scrolls fully into viewport at "+width,actionBounds.Top>=-.5 && actionBounds.Bottom<=viewport.ActualHeight+.5);
            }
        }
    }
}
