using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private MediaControls? mediaControls;
    private long mediaExternalDismissedAt;
    private FrameworkElement BuildMediaControls(StackPanel panel)
    {
        mediaControls=new MediaControls(panel,WidgetOn("media"),WidgetOn("volume"),
            ()=>app.Media.Sessions.Select(session=>new MediaSourceChoice(session.SourceAppUserModelId,MediaService.SessionName(session),()=>
            {
                // A session may disappear after its menu was rendered.
                if(app.Media.Sessions.Contains(session))app.Media.Select(session);
            })).ToArray(),()=>app.Media.Selected?.SourceAppUserModelId,BuildVolumePopover);
        RefreshMediaControls(true);return mediaControls;
    }
    private FrameworkElement BuildVolumePopover()
    {
        DetachMediaControl(volume);DetachMediaControl(volumeText);
        var panel=new StackPanel();var header=new DockPanel{Margin=new(0,0,0,7)};
        var devices=Icons.Button("chevron","Audio devices and microphone",()=>{mediaControls?.Close(false);OpenAudioMenu();},false,22);devices.Background=Brushes.Transparent;devices.Margin=new(6,0,0,0);devices.ToolTip="Output devices & microphone";DockPanel.SetDock(devices,Dock.Right);header.Children.Add(devices);DockPanel.SetDock(volumeText,Dock.Right);header.Children.Add(volumeText);header.Children.Add(UI.Text("Volume",12));panel.Children.Add(header);
        mute=Icons.Button("volume","Mute or unmute",()=>{app.Audio.ToggleMute();Update();},false,28);mute.Background=Brushes.Transparent;mute.Margin=new(0);muteIcon="";
        var row=new DockPanel();var mixer=Icons.Button("mixer","Open volume mixer",OpenMixer,false,28);mixer.Margin=new(7,0,0,0);mixer.Background=Brushes.Transparent;DockPanel.SetDock(mixer,Dock.Right);row.Children.Add(mixer);DockPanel.SetDock(mute,Dock.Left);row.Children.Add(mute);volume.Margin=new(8,0,5,0);UI.Named(volume,"Windows volume");row.Children.Add(volume);panel.Children.Add(row);
        Update();return panel;
    }
    private static void DetachMediaControl(FrameworkElement control)
    {
        if(control.Parent is Panel panel)panel.Children.Remove(control);else if(control.Parent is Decorator decorator)decorator.Child=null;else if(control.Parent is ContentControl content)content.Content=null;
    }
    private void RefreshMediaControls(bool sources=false)=>mediaControls?.Refresh(volume.IsMouseCaptureWithin || volume.IsKeyboardFocusWithin?volume.Value:app.Audio.Volume*100,app.Audio.Muted,app.Audio.Available,sources);
    private void DismissMediaOrCollapse()
    {
        // Deactivation and the outside-click hook can describe the same click.
        long now=Environment.TickCount64;
        if(mediaControls?.Close(false)==true){mediaExternalDismissedAt=now;return;}
        if(now-mediaExternalDismissedAt<200)return;
        Collapse();
    }
}
