using System;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private TextBlock? updateStatus;
    private Button? downloadUpdate,installUpdate,restoreUpdate;
    private void UpdateControls()=>Dispatcher.BeginInvoke(()=>
    {
        if(downloadUpdate!=null)downloadUpdate.IsEnabled=!app.Updates.Busy && app.Updates.Available!=null;
        if(installUpdate!=null)installUpdate.IsEnabled=!app.Updates.Busy && app.Updates.VerifiedSetup!=null && OfficialUpdates.IsAutomaticInstallPath(Environment.ProcessPath!);
        if(restoreUpdate!=null)restoreUpdate.IsEnabled=!app.Updates.Busy && UpdateRecovery.PreviousId()!=null && OfficialUpdates.IsAutomaticInstallPath(Environment.ProcessPath!);
        if(updateStatus!=null){string status=Localization.Text(app.Updates.Status)+"\n"+Localization.Text("Available:")+" "+(app.Updates.Available?.Version.ToString()??Localization.Text("None checked"));if(updateStatus.Text!=status)Accessibility.Announce(updateStatus,status);}
    });
    private void UpdatesSection()
    {
        var auto=new CheckBox{Content=Localization.Text("Check automatically once per day"),IsChecked=app.Settings.AutoCheckUpdates};UI.Named(auto,Localization.Text("Check automatically once per day"));auto.Click+=(_,_)=>{app.Settings.AutoCheckUpdates=auto.IsChecked==true;Save();};auto.ToolTip=Localization.Text("Check once at normal startup when the last attempt was at least 24 hours ago.");
        var download=new CheckBox{Content=Localization.Text("Download updates automatically"),IsChecked=app.Settings.AutoDownloadUpdates};UI.Named(download,Localization.Text("Download updates automatically"));download.Click+=(_,_)=>{app.Settings.AutoDownloadUpdates=download.IsChecked==true;Save();};
        updateStatus=UI.Text(Localization.Text(app.Updates.Status),12,UI.Muted);UI.Named(updateStatus,Localization.Text("Update status"));
        page.Children.Add(Section("Updates",UI.Text(Localization.Text("Installed:")+" "+OfficialUpdates.InstalledVersion+" · "+RuntimePlatform.Rid,13),
            UI.Caption("Check for a release, download it, then choose when to install and restart.",12),auto,download,
            UI.Caption("Automatic downloads are optional and may use a metered connection. They follow automatic startup checks; installation always waits for you.",11,UI.Muted),updateStatus,
            UI.CaptionButton("Check for updates",()=>{_=app.Updates.Check(true);}),
            UI.CaptionButton("Release notes",()=>ShortcutService.Open(new(){Target=app.Updates.Available?.Notes.AbsoluteUri??OfficialUpdates.Repository+"/releases"})),
            downloadUpdate=UI.CaptionButton("Download update",()=>{_=app.Updates.Download();}),
            UI.CaptionButton("Cancel update",app.Updates.Cancel),
            installUpdate=UI.CaptionButton("Install and restart",async ()=>{try{await UpdateHandoff.Prepare(app);}catch(Exception ex){updateStatus.Text=ex.Message;app.Store.Log(ex);}}),
            UI.Caption(OfficialUpdates.IsAutomaticInstallPath(Environment.ProcessPath!)?"Before installation, the updater saves a local recovery copy of the app and JSON profiles. If installation or initial startup fails, it restores that copy.":OfficialUpdates.IsInstalled(Environment.ProcessPath!)?"Legacy installation: quit NabyScape and run the latest official setup, then open NabyScape.exe. Automatic installation and recovery require the current executable name.":"Portable copy: use Release notes to download the portable ZIP, quit, then replace your portable files manually. Automatic installation is unavailable here.",11,UI.Muted),
            UI.Text(UpdateRecovery.Description(),11,UI.Muted),
            restoreUpdate=UI.CaptionButton("Restore previous version",async ()=>
            {
                if(MessageBox.Show(this,Localization.Text("Restore the previous app and its saved settings? Changes to settings and JSON profile data since that backup will be replaced. Local media files are kept."),Localization.Text("Restore previous version"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
                try{await UpdateHandoff.Prepare(app,true);}catch(Exception ex){updateStatus.Text=ex.Message;app.Store.Log(ex);}
            }),
            UI.Caption("Downloads use official GitHub releases. SHA-256 checks integrity; unsigned checksums do not establish publisher identity. Windows security checks remain enabled.",11,UI.Muted)));
        downloadUpdate.ToolTip=Localization.Text("Download the official setup for this app's architecture and verify its size, version and SHA-256.");
        installUpdate.ToolTip=Localization.Text("Save, create a recovery copy, install the verified update and reopen NabyScape.");
        UpdateControls();
    }
}
