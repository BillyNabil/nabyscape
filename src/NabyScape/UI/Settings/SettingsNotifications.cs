using System;
using System.Windows.Controls;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private void NotificationSettings()
    {
        var s=app.Settings;CompletionSoundSettings();
        var delivery=new ComboBox{ItemsSource=new[]{"Notch","Windows","Both","Off"},SelectedItem=s.NotificationDelivery};UI.Named(delivery,"Notification delivery");delivery.SelectionChanged+=(_,_)=>{s.NotificationDelivery=delivery.SelectedItem?.ToString()??"Notch";Save();};
        page.Children.Add(Section("Notifications",
            delivery,
            Check("Timer finished",s.NotifyTimer,v=>s.NotifyTimer=v),Check("Focus finished",s.NotifyFocus,v=>s.NotifyFocus=v),Check("Downloads ready",s.NotifyDownloads,v=>s.NotifyDownloads=v),Check("Update available",s.NotifyUpdates,v=>s.NotifyUpdates=v),Check("Important app failures",s.NotifyFailures,v=>s.NotifyFailures=v),
            UI.Text("Notch shows a compact indicator. Hover or click for details. Manually hidden islands use Windows instead. Volume and brightness take priority; panels and editors stay undisturbed. The notification center keeps this session's last 50 alerts; nothing is saved to disk.",11,UI.Muted),
            UI.CaptionButton("Open notification center",app.OpenNotifications),UI.Text("Open it from the bell in the island panel, the tray menu, or Ctrl+K → Notifications. Windows delivery follows your Windows notification preferences.",11,UI.Muted)));
        ExternalNotificationSettings();
        NotificationPresentationSettings();
        var folder=UI.Text(s.DownloadsFolder.Length==0?"Windows Downloads known folder (including relocated folders)":s.DownloadsFolder,11,UI.Muted);
        page.Children.Add(Section("Downloads",Check("Infer completed arrivals in a download folder",s.DownloadsEnabled,v=>s.DownloadsEnabled=v),folder,
            UI.Button("Choose folder…",()=>{var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Monitor folder arrivals"};if(dialog.ShowDialog(this)==true){s.DownloadsFolder=dialog.FolderName;folder.Text=dialog.FolderName;Save();}}),
            UI.Button("Use Windows Downloads",()=>{s.DownloadsFolder="";folder.Text="Windows Downloads known folder";Save();}),
            UI.Text("Off by default. Watches this folder only, never subfolders. Existing files are ignored. A final name, readable file and stable size for two seconds suggest an arrival; this cannot always prove a browser finished downloading. Originals stay in place. Drag a burst of files together, Show in folder, or Add to shelf (40-item limit).",11,UI.Muted),
            UI.Text("Monitoring stops when disabled. If a folder disconnects or becomes inaccessible, toggle monitoring off and on to restart. Folder paths are excluded from layout exports.",11,UI.Muted)));
    }
}
