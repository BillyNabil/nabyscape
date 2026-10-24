using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NabyScape;

internal static class RemovedActivitiesDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,Action<string,bool> check)
    {
        var s=app.Settings;s.HideFullscreen=s.HideMaximized=false;s.SmartActivities=false;s.ReduceMotion=true;
        foreach(var widget in s.Widgets)widget.Enabled=true;
        s.Shortcuts=new(){new(){Name="Removal fixture shortcut",Target="https://example.com/"}};app.ChangeSettings();
        app.Audio.DiagnosticPeak=()=>.2f;app.Media.DiagnosticPlayback(true);
        app.Session.Timer.Start(3600,DateTimeOffset.UtcNow);app.Session.Focus.Start(1500,DateTimeOffset.UtcNow,"work");app.SaveSession();
        island.Collapse();await Task.Delay(250);
        check("Removal: music and countdowns do not create a secondary activity window",app.Windows.Count==1 && island.IsVisible && !island.IsExpanded && !island.IsPeeking);
        check("Removal: shared Timer and Focus sessions remain running",app.Session.Timer.Running && app.Session.Focus.Running);
        check("Removal: the closed dock still shows Focus",island.HeaderLabel.Text.StartsWith("Focus",StringComparison.Ordinal));
        island.ShowCommands(false);await Task.Delay(100);
        bool RetiredButton(Button button){string name=AutomationProperties.GetName(button);return name=="Activities" || name.StartsWith("Activity ·",StringComparison.Ordinal);}
        check("Removal: command palette has no routine entry",!Diagnostics.Descendants(island).OfType<Button>().Any(RetiredButton));
        island.OpenFeaturePage("shortcuts");await Task.Delay(100);
        check("Removal: regular shortcuts remain available",Diagnostics.Descendants(island).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Removal fixture shortcut") && !Diagnostics.Descendants(island).OfType<Button>().Any(RetiredButton));
        island.ShowTools("awake",false);await Task.Delay(100);
        check("Removal: standalone Keep awake remains available",Diagnostics.Descendants(island).OfType<Button>().Any(b=>b.IsVisible && AutomationProperties.GetName(b)=="Start keep awake") && !app.Awake.Active);
        var settings=new SettingsWindow(app);settings.Show();
        try
        {
            foreach(string page in new[]{"Controls","Launch"})
            {
                Diagnostics.NavigateSettings(settings,page);await Task.Delay(100);settings.UpdateLayout();
                var names=Diagnostics.LogicalDescendants(settings).Select(node=>AutomationProperties.GetName(node)).ToArray();
                check("Removal: Settings has no retired switches on "+page,!names.Any(name=>name is "Enable Activities" or "Show companion beside music" or "Companion beside music"));
            }
            check("Removal: obsolete Settings scenes are gone",!SettingsScene.Sections.Intersect(new[]{"Two live activities","Activity actions"}).Any());
        }
        finally{settings.Close();app.Session.Timer.Pause(DateTimeOffset.UtcNow);app.Session.Focus.Pause(DateTimeOffset.UtcNow);island.Collapse();app.Audio.DiagnosticPeak=null;}
    }
}
