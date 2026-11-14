using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;

// Settings > Layout > Closed island: notch width discoverability and the closed-dock clock position.
public sealed partial class SettingsWindow
{
    private void ClosedWidthHint(StackPanel target)
    {
        var hint=UI.Caption("This is the notch bar width. With move and resize handles on, drag a lower corner of the island to resize it on screen.",11,UI.Muted);
        hint.TextWrapping=TextWrapping.Wrap;hint.Margin=new(0,-2,0,10);target.Children.Add(hint);
    }
    private void ClosedClockSettings(StackPanel target)
    {
        var s=app.Settings;var caption=UI.Caption("Clock position",11,UI.Muted);caption.Margin=new(0,8,0,0);target.Children.Add(caption);
        // Items show translated names; the saved value stays the English key.
        var choice=new ComboBox{ItemsSource=Settings.ClosedClockPositions.Select(Localization.Text).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(Settings.ClosedClockPositions,s.ClosedClockPosition)),MinHeight=32,Margin=new(0,4,0,6)};UI.LocalizedName(choice,"Closed dock clock position");
        choice.SelectionChanged+=(_,_)=>{if(choice.SelectedIndex<0)return;string value=Settings.ClosedClockPositions[choice.SelectedIndex];if(value!=s.ClosedClockPosition){s.ClosedClockPosition=value;ConfigurePreviewForSetting("Closed dock clock position");previewMode="Closed";Save();}};
        WireSettingPreview(choice,"Closed dock clock position");target.Children.Add(choice);
        var hint=UI.Caption("Center keeps the time in the middle while the dock is idle. Music, lyrics, timers and notices take the middle while active, and the time moves right. Closed media None keeps it centered.",11,UI.Muted);
        hint.TextWrapping=TextWrapping.Wrap;hint.Margin=new(0,0,0,10);target.Children.Add(hint);
    }
}
