using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private TextBlock? featureUsageSummary;
    private void RefreshFeatureUsage(){if(featureUsageSummary!=null)featureUsageSummary.Text=app.Settings.Widgets.Count(w=>w.Enabled)+" widgets enabled · choose only what you use";}
    private void FeatureResourceSettings()
    {
        var s=app.Settings;var body=new StackPanel();
        var summary=UI.Text(s.Widgets.Count(w=>w.Enabled)+" widgets enabled · choose only what you use",14);featureUsageSummary=summary;body.Children.Add(summary);
        var actions=new WrapPanel{Margin=new(0,10,0,8)};
        actions.Children.Add(UI.CaptionButton("Use essentials",()=>
        {
            string[] keep={"media","clock","timer","focus"};
            foreach(var widget in s.Widgets)widget.Enabled=keep.Contains(widget.Id);
            foreach(var view in s.HoverViews)view.Enabled=keep.Contains(view.Id);
            s.HoverFollowsWidgets=true;Save();Navigate("Content");SetSaveFeedback("Essentials selected · music, clock, timer and focus. Saved content and running sessions are preserved.");
        }));
        actions.Children.Add(UI.CaptionButton("Customize",()=>
        {
            var target=page.Children.OfType<Border>().FirstOrDefault(b=>b.Tag as string=="Enabled widgets & order");target?.BringIntoView();
        }));body.Children.Add(actions);
        body.Children.Add(UI.Text("Essentials shows music, clock, timer and focus. Other widgets stay off until you enable them. Animation quality stays the same.",12,UI.Muted));
        body.Children.Add(Check("Connect to music apps",s.MediaIntegrationEnabled,v=>s.MediaIntegrationEnabled=v));
        body.Children.Add(UI.Text("Off disconnects Windows media sessions and releases track artwork. It leaves playback in your music app untouched.",12,UI.Muted));
        body.Children.Add(Check("Watch for completed downloads",s.DownloadsEnabled,v=>s.DownloadsEnabled=v));
        var links=new WrapPanel{Margin=new(0,8,0,0)};links.Children.Add(UI.CaptionButton("Clipboard capture",()=>Navigate("Clipboard")));links.Children.Add(UI.CaptionButton("Voice & wake word",()=>Navigate("AI")));body.Children.Add(links);
        body.Children.Add(UI.Text("Clipboard capture and voice listening have separate switches. Turning a widget off preserves your notes, files and timers. Hidden weather and system pages stop their work; audio capture is released when no visual needs it.",12,UI.Muted));
        page.Children.Add(Section("Feature usage",body));
    }
}
