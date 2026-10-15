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
        var box=new ComboBox();foreach(string title in new[]{"Icons + labels","Icons only","Text only"})box.Items.Add(title);var values=new[]{"IconsLabels","Icons","Text"};box.SelectedIndex=Math.Max(0,Array.IndexOf(values,selected));UI.Named(box,label);Grid.SetColumn(box,1);row.Children.Add(box);box.SelectionChanged+=(_,_)=>{if(box.SelectedIndex>=0){change(values[box.SelectedIndex]);previewMode="Panel";previewContent=label.StartsWith("Launch")?"Launch":"Navigation";Save();}};var group=new StackPanel();group.Children.Add(row);group.Children.Add(UI.Text(label.StartsWith("Launch")?"Choose how saved apps and shortcuts appear in Launch.":"Choose how the Media, Focus, Status, Launch and Tools buttons appear.",11,UI.Muted));group.Margin=new(0,0,0,14);return group;
    }
    private void VisibilityControls()
    {
        var s=app.Settings;var modifiers=new ComboBox();foreach(string label in new[]{"Ctrl+Alt","Ctrl+Shift","Alt+Shift"})modifiers.Items.Add(label);modifiers.SelectedItem=s.VisibilityHotkeyModifiers;UI.Named(modifiers,"Visibility hotkey modifiers");modifiers.SelectionChanged+=(_,_)=>{s.VisibilityHotkeyModifiers=modifiers.SelectedItem?.ToString()??"Ctrl+Alt";Save();};
        var key=new ComboBox();foreach(string label in Enumerable.Range('A',26).Select(n=>((char)n).ToString()))key.Items.Add(label);key.SelectedItem=s.VisibilityHotkeyKey;UI.Named(key,"Visibility hotkey key");key.SelectionChanged+=(_,_)=>{s.VisibilityHotkeyKey=key.SelectedItem?.ToString()??"H";Save();};
        var keys=new Grid();keys.ColumnDefinitions.Add(new());keys.ColumnDefinitions.Add(new(){Width=new GridLength(80)});keys.Children.Add(modifiers);Grid.SetColumn(key,1);keys.Children.Add(key);
        page.Children.Add(Section("Hide or show the island",Check("Enable visibility shortcut",s.VisibilityHotkeyEnabled,v=>s.VisibilityHotkeyEnabled=v),keys,UI.Text("Default: Ctrl+Alt+H. Shows the closed island even over the current maximized or fullscreen app. Switching apps restores automatic hiding.",11,UI.Muted)));
        page.Children.Add(Section("Live feedback",Check("Gentle waveform motion during quiet playback",s.WaveformIdleMotion,v=>s.WaveformIdleMotion=v),UI.Text("Real audio levels drive the bars. Quiet playback can use decorative motion when no level is available.",11,UI.Muted),Check("Closed-island volume indicator",s.VolumeFeedback,v=>s.VolumeFeedback=v),Check("Closed-island brightness indicator",s.BrightnessFeedback,v=>s.BrightnessFeedback=v),brightnessState,UI.Text("Brightness indication requires Windows display-brightness events. Unsupported displays cannot report changes; no brightness is adjusted.",11,UI.Muted)));
    }
}
