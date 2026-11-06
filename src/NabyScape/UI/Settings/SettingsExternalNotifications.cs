using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;

public sealed partial class SettingsWindow
{
    private void ExternalNotificationSettings()
    {
        var s=app.Settings;
        // Live state, not help text: a distinct brush keeps it visible in the essentials view.
        var status=UI.Text("",11,SettingsTheme.Quiet);
        var apps=new StackPanel{Margin=new(0,4,0,0)};
        var appsLabel=UI.Caption("Apps",12,UI.Muted);appsLabel.Margin=new(0,10,0,2);
        var privacy=UI.CaptionButton("Open Windows notification access",()=>ShortcutService.Open(new(){Target="ms-settings:privacy-notifications"}));
        var options=new StackPanel();
        void Refresh()
        {
            string watcher=app.ExternalNotifications?.Status??"Off";
            status.Text=!s.ExternalNotifications?Localization.Text("Off · nothing is read from Windows."):watcher switch
            {
                "On"=>Localization.Text("On · new alerts from other apps appear in the notification center."),
                "Blocked by Windows"=>Localization.Text("Windows is blocking access. Allow notification access for apps, then turn this on again."),
                "Unavailable"=>Localization.Text("Windows notifications could not be read. Try turning this off and on."),
                _=>Localization.Text("Starting…")
            };
            privacy.Visibility=watcher=="Blocked by Windows" && s.ExternalNotifications?Visibility.Visible:Visibility.Collapsed;
            options.Visibility=s.ExternalNotifications?Visibility.Visible:Visibility.Collapsed;
        }
        async void LoadApps()
        {
            apps.Children.Clear();
            string[] seen=Array.Empty<string>();
            try{seen=await ExternalNotificationWatcher.CurrentAppsAsync();}catch(Exception ex){app.Store.Log(ex);}
            var names=seen.Concat(s.ExternalNotificationMuted).Concat(app.Notifications.History.Where(e=>e.Notice.Kind=="external").Select(e=>e.Notice.Source??"")).Where(n=>n.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n=>n,StringComparer.CurrentCultureIgnoreCase).ToArray();
            if(names.Length==0){apps.Children.Add(UI.Caption("Apps appear here after they show a Windows notification.",11,UI.Muted));return;}
            foreach(string name in names)
            {
                var appName=name;
                var toggle=new CheckBox{Content=UI.Text(appName,12),IsChecked=ExternalNotificationPolicy.Allowed(s,appName)};UI.Named(toggle,appName);
                toggle.Click+=(_,_)=>{s.ExternalNotificationMuted.RemoveAll(n=>string.Equals(n,appName,StringComparison.OrdinalIgnoreCase));if(toggle.IsChecked!=true)s.ExternalNotificationMuted.Add(appName);Save();};
                apps.Children.Add(toggle);
            }
        }
        var enable=Check("Show notifications from other apps",s.ExternalNotifications,v=>{s.ExternalNotifications=v;Dispatcher.BeginInvoke(()=>{Refresh();if(v)LoadApps();});});
        options.Children.Add(Check("Pop up on the island",s.ExternalNotificationPopup,v=>s.ExternalNotificationPopup=v));
        options.Children.Add(Check("Show message text in pop-ups",s.ExternalNotificationText,v=>s.ExternalNotificationText=v));
        options.Children.Add(appsLabel);options.Children.Add(apps);
        page.Children.Add(Section("Other apps",enable,status,privacy,options,
            UI.Text("NabyScape reads the alerts Windows already shows from apps such as Claude, ChatGPT or Telegram, checks for new ones every 1.5 seconds while this is on, and keeps them only for this session. Reply buttons from the original notification stay in Windows; use Open app to answer.",11,UI.Muted)));
        void Changed()=>Dispatcher.BeginInvoke(Refresh);
        // Follow the watcher only while this section is on screen.
        if(app.ExternalNotifications is {} watcher){watcher.StatusChanged+=Changed;status.Unloaded+=(_,_)=>watcher.StatusChanged-=Changed;}
        Refresh();if(s.ExternalNotifications)LoadApps();
    }
}
