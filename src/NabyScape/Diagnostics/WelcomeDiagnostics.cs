using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class WelcomeDiagnostics
{
    internal static async Task Run(App app,string directory,Action<string,bool> check)
    {
        var settings=app.Settings;string edge=settings.DockEdge;
        var enabled=settings.Widgets.ToDictionary(w=>w.Id,w=>w.Enabled);
        var tools=settings.Tools.ToDictionary(w=>w.Id,w=>w.Enabled);
        void Click(WelcomeWindow window,string label)=>Diagnostics.Descendants(window).OfType<Button>().Single(b=>b.Content?.ToString()==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool Pending()=>app.Store.Load<WelcomeState>("welcome") is {Pending:true,Completed:false} && WelcomeSetup.ShouldOffer(app.Store,true);
        WelcomeWindow? window=null;
        SettingsWindow? settingsWindow=null;
        try
        {
            app.Store.Save("welcome",new WelcomeState{Pending=true});
            string before=JsonSerializer.Serialize(settings);
            window=new WelcomeWindow(app){ShowActivated=false,Width=420,Height=360};window.Show();await Task.Delay(100);window.UpdateLayout();
            var next=Diagnostics.Descendants(window).OfType<Button>().Single(b=>b.Content?.ToString()=="Next");
            var point=next.TranslatePoint(new Point(),window);
            check("Welcome footer stays visible at minimum size",next.ActualHeight>0 && point.Y>=0 && point.Y+next.ActualHeight<=window.ActualHeight);
            check("Welcome offers Music and Custom purpose choices",new[]{"Music","Custom"}.All(label=>Diagnostics.Descendants(window).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==label)));
            window.Close();
            check("Welcome closing leaves new-user setup pending",Pending() && before==JsonSerializer.Serialize(settings));
            window=new WelcomeWindow(app){ShowActivated=false};window.Show();await Task.Delay(60);
            Click(window,"Skip setup");
            check("Welcome Skip leaves preferences unchanged",before==JsonSerializer.Serialize(settings) && !WelcomeSetup.ShouldOffer(app.Store,false));

            // Enabling Notes also enables its legacy Tools entry during Normalize.
            // A locked payload forces the real Store write to fail after normalization.
            settings.Widgets.Single(w=>w.Id=="notes").Enabled=false;settings.Tools.Single(w=>w.Id=="notes").Enabled=false;
            app.Store.Save("settings",settings);app.Store.Save("welcome",new WelcomeState{Pending=true});
            string settingsFile=Path.Combine(app.Store.DirectoryPath,"settings.json");
            before=JsonSerializer.Serialize(settings);string persistedBefore=File.ReadAllText(settingsFile);
            window=new WelcomeWindow(app){ShowActivated=false};window.Show();await Task.Delay(60);
            Click(window,"Next");
            Diagnostics.Descendants(window).OfType<RadioButton>().Single(b=>b.Content?.ToString()==(edge=="Bottom"?"Top":"Bottom")).IsChecked=true;
            var notes=Diagnostics.Descendants(window).OfType<CheckBox>().Single(b=>b.Content?.ToString()=="Notes");
            notes.IsChecked=true;notes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Click(window,"Next");
            using(var locked=new FileStream(settingsFile,FileMode.Open,FileAccess.Read,FileShare.Read))Click(window,"Finish setup");
            check("Welcome failed save preserves every live preference and stored payload",before==JsonSerializer.Serialize(settings) && persistedBefore==File.ReadAllText(settingsFile));
            check("Welcome failed save stays open with an actionable error",window.IsVisible && Diagnostics.Descendants(window).OfType<TextBlock>().Any(t=>t.Text=="Could not save your choices. Your previous settings are unchanged."));
            window.Close();check("Welcome closing after failed save keeps setup pending",Pending());
            foreach(var widget in settings.Widgets)widget.Enabled=enabled[widget.Id];foreach(var tool in settings.Tools)tool.Enabled=tools[tool.Id];

            window=new WelcomeWindow(app){ShowActivated=false};window.Show();await Task.Delay(60);Click(window,"Next");Click(window,"Next");
            using(var locked=new FileStream(Path.Combine(app.Store.DirectoryPath,"welcome.json"),FileMode.Open,FileAccess.Read,FileShare.Read))Click(window,"Finish setup");
            check("Welcome completion-marker failure keeps guide open",window.IsVisible && Diagnostics.Descendants(window).OfType<TextBlock>().Any(t=>t.Text=="Could not save setup. Check your settings folder and try again."));
            window.Close();check("Welcome closing after completion-marker failure keeps setup pending",Pending());

            settingsWindow=new SettingsWindow(app){ShowActivated=false};settingsWindow.Show();Diagnostics.NavigateSettings(settingsWindow,"Content");await Task.Delay(60);
            var widgetSection=Diagnostics.LogicalDescendants(settingsWindow).OfType<Border>().Single(b=>b.Tag as string=="Enabled widgets & order");
            var retainedToggle=Diagnostics.Descendants(widgetSection).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Enable Timer");
            var liveWidgets=settings.Widgets.ToDictionary(w=>w.Id);var liveTools=settings.Tools.ToDictionary(t=>t.Id);
            bool timerEnabled=settings.Widgets.Single(w=>w.Id=="timer").Enabled;

            window=new WelcomeWindow(app){ShowActivated=false};window.Show();await Task.Delay(60);
            Click(window,"Next");
            Diagnostics.Descendants(window).OfType<RadioButton>().Single(b=>b.Content?.ToString()=="Bottom").IsChecked=true;
            var media=Diagnostics.Descendants(window).OfType<CheckBox>().Single(b=>b.Content?.ToString()=="Media");
            media.IsChecked=false;media.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Click(window,"Next");window.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(directory,"welcome-ready.png")))png.Save(file);
            Click(window,"Finish setup");
            var persisted=app.Store.Load<Settings>("settings");
            check("Welcome applies dock and widget choices",persisted.DockEdge=="Bottom" && !persisted.ToolEnabled("media"));
            check("Welcome preserves unrelated widgets",persisted.Widgets.Where(w=>w.Id!="media").All(w=>enabled[w.Id]==w.Enabled));
            check("Welcome can be reopened after completion",!WelcomeSetup.ShouldOffer(app.Store,true) && !window.IsVisible);
            check("Welcome Finish preserves live widget and tool object identities",settings.Widgets.All(w=>ReferenceEquals(w,liveWidgets[w.Id])) && settings.Tools.All(t=>ReferenceEquals(t,liveTools[t.Id])));
            retainedToggle.IsChecked=!timerEnabled;retainedToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(400);
            check("Welcome Finish leaves an already-open Settings widget toggle connected and persisted",settings.Widgets.Single(w=>w.Id=="timer").Enabled==!timerEnabled && app.Store.Load<Settings>("settings").Widgets.Single(w=>w.Id=="timer").Enabled==!timerEnabled);
        }
        finally
        {
            window?.Close();settingsWindow?.Close();settings.DockEdge=edge;foreach(var widget in settings.Widgets)widget.Enabled=enabled[widget.Id];foreach(var tool in settings.Tools)tool.Enabled=tools[tool.Id];app.ChangeSettings();
        }
    }
}
