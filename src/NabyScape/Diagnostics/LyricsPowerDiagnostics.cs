using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
namespace NabyScape;
internal static class LyricsPowerDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,Action<string,bool> check)
    {
        app.Settings.Widgets.Single(w=>w.Id=="power").Enabled=true;app.ChangeSettings();island.OpenFeaturePage("power");
        ComboBox? picker=null;
        for(int i=0;i<90;i++)
        {
            picker=Diagnostics.Descendants(island).OfType<ComboBox>().FirstOrDefault(c=>AutomationProperties.GetName(c)=="Available Windows power plans");
            if(picker?.Items.Count>0)break;await Task.Delay(100);
        }
        check("Power widget mounts with an accessible plan selector",picker!=null && picker.IsVisible);
        check("Power widget reads installed plans without applying changes",picker!=null && picker.Items.OfType<PowerPlan>().Any(p=>p.Active));
        check("Power widget provides explicit Apply and Refresh buttons",Diagnostics.Descendants(island).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Apply power plan") && Diagnostics.Descendants(island).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Refresh plans"));
        var window=new LyricsSearchWindow(app){Owner=island};window.Show();await Task.Delay(100);
        try
        {
            var sources=Diagnostics.Descendants(window).OfType<RadioButton>().ToArray();
            check("Lyrics picker exposes direct fetch rows for LRCLIB, BiniLyrics and custom API",sources.Length==3 && sources[0].IsChecked==true);
            sources[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(100);
            check("Custom lyrics source exposes a URL field",Diagnostics.Descendants(window).OfType<TextBox>().Count(t=>t.IsVisible)>=3);
            check("Lyrics network lookup requires an explicit Search action",Diagnostics.Descendants(window).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Search synced lyrics" && b.IsEnabled));
        }
        finally{window.Close();island.Collapse();}
    }
}
