using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private readonly TextBlock brightnessState=UI.Text("",11,UI.Muted);
    private FrameworkElement StyleChoice(string label,string selected,Action<string> change)
    {
        var row=new Grid{Margin=new(0,4,0,10)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(152)});row.Children.Add(UI.Text(label,12));
        var box=new ComboBox();bool navigation=!label.StartsWith("Launch");foreach(string title in navigation?new[]{"Icons + labels","Icons only","Text only","Dots"}:new[]{"Icons + labels","Icons only","Text only"})box.Items.Add(title);var values=navigation?new[]{"IconsLabels","Icons","Text","Dots"}:new[]{"IconsLabels","Icons","Text"};box.SelectedIndex=Math.Max(0,Array.IndexOf(values,selected));UI.Named(box,label);Grid.SetColumn(box,1);row.Children.Add(box);box.SelectionChanged+=(_,_)=>{if(box.SelectedIndex>=0){change(values[box.SelectedIndex]);previewMode="Panel";previewContent=label.StartsWith("Launch")?"Launch":"Navigation";Save();}};var group=new StackPanel();group.Children.Add(row);group.Children.Add(UI.Text(label.StartsWith("Launch")?"Choose how saved apps and shortcuts appear in Launch.":"Dots keep navigation minimal. Hover or focus a dot for its panel name; use Ctrl+Tab to switch. More panels scroll sideways.",11,UI.Muted));group.Margin=new(0,0,0,14);return group;
    }
    private void VisibilityControls()
    {
        var s=app.Settings;var modifiers=new ComboBox();foreach(string label in new[]{"Ctrl+Alt","Ctrl+Shift","Alt+Shift"})modifiers.Items.Add(label);modifiers.SelectedItem=s.VisibilityHotkeyModifiers;UI.Named(modifiers,"Visibility hotkey modifiers");modifiers.SelectionChanged+=(_,_)=>{s.VisibilityHotkeyModifiers=modifiers.SelectedItem?.ToString()??"Ctrl+Alt";Save();};
        var key=new ComboBox();foreach(string label in Enumerable.Range('A',26).Select(n=>((char)n).ToString()))key.Items.Add(label);key.SelectedItem=s.VisibilityHotkeyKey;UI.Named(key,"Visibility hotkey key");key.SelectionChanged+=(_,_)=>{s.VisibilityHotkeyKey=key.SelectedItem?.ToString()??"H";Save();};
        var keys=new Grid();keys.ColumnDefinitions.Add(new());keys.ColumnDefinitions.Add(new(){Width=new GridLength(80)});keys.Children.Add(modifiers);Grid.SetColumn(key,1);keys.Children.Add(key);
        page.Children.Add(Section("Hide or show the island",Check("Enable visibility shortcut",s.VisibilityHotkeyEnabled,v=>s.VisibilityHotkeyEnabled=v),keys,UI.Text("Default: Ctrl+Alt+H. Shows the closed island even over the current maximized or fullscreen app. Switching apps restores automatic hiding.",11,UI.Muted)));
        var tuckModifiers=new ComboBox{ItemsSource=new[]{"Ctrl+Alt","Ctrl+Shift","Alt+Shift"},SelectedItem=s.TuckHotkeyModifiers};UI.Named(tuckModifiers,"Tuck hotkey modifiers");tuckModifiers.SelectionChanged+=(_,_)=>{s.TuckHotkeyModifiers=tuckModifiers.SelectedItem?.ToString()??"Ctrl+Alt";Save();};
        var tuckKey=new ComboBox{ItemsSource=Enumerable.Range('A',26).Select(n=>((char)n).ToString()).ToArray(),SelectedItem=s.TuckHotkeyKey};UI.Named(tuckKey,"Tuck hotkey key");tuckKey.SelectionChanged+=(_,_)=>{s.TuckHotkeyKey=tuckKey.SelectedItem?.ToString()??"T";Save();};
        page.Children.Add(Section("Manual tuck shortcut",Check("Enable tuck shortcut",s.TuckHotkeyEnabled,v=>s.TuckHotkeyEnabled=v),tuckModifiers,tuckKey,UI.Text("Default: Ctrl+Alt+T. First press tucks to the edge; second returns to automatic rules. Manual hide stays hidden. Choose distinct combinations for panel, visibility and tuck; registration errors appear above.",11,UI.Muted)));

    }
    private void AudioFeedbackSettings()
    {
        var s=app.Settings;
        page.Children.Add(Section("Live feedback",Check("Live activity · audio, progress & focus breathing",s.LiveActivity,v=>s.LiveActivity=v),Check("Feedback for volume & audio device changes",s.ReactiveFeedback,v=>s.ReactiveFeedback=v),Check("Gentle waveform motion during quiet playback",s.WaveformIdleMotion,v=>s.WaveformIdleMotion=v),UI.Text("Real audio levels drive the bars. Quiet playback can use decorative motion when no level is available.",11,UI.Muted),Check("Closed-island volume indicator",s.VolumeFeedback,v=>s.VolumeFeedback=v),Check("Closed-island brightness indicator",s.BrightnessFeedback,v=>s.BrightnessFeedback=v),brightnessState,UI.Text("Brightness indication requires Windows display-brightness events. Unsupported displays cannot report changes; no brightness is adjusted.",11,UI.Muted)));
    }
}
