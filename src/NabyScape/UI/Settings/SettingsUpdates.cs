using System;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private TextBlock? updateStatus;
    private Button? downloadUpdate,installUpdate;
    private void UpdateControls()=>Dispatcher.BeginInvoke(()=>
    {
        if(downloadUpdate!=null)downloadUpdate.IsEnabled=!app.Updates.Busy && app.Updates.Available!=null;
        if(installUpdate!=null)installUpdate.IsEnabled=!app.Updates.Busy && app.Updates.VerifiedSetup!=null && OfficialUpdates.IsInstalled(Environment.ProcessPath!);
        if(updateStatus!=null)updateStatus.Text=app.Updates.Status+"\nAvailable: "+(app.Updates.Available?.Version.ToString()??"None checked");
    });
    private void UpdatesSection()
    {
        var auto=new CheckBox{Content="Check automatically once per day",IsChecked=app.Settings.AutoCheckUpdates};UI.Named(auto,"Check automatically once per day");auto.Click+=(_,_)=>{app.Settings.AutoCheckUpdates=auto.IsChecked==true;Save();};auto.ToolTip="Check once at normal startup when the last attempt was at least 24 hours ago.";
        updateStatus=UI.Text(app.Updates.Status+"\nAvailable: "+(app.Updates.Available?.Version.ToString()??"None checked"),12,UI.Muted);UI.Named(updateStatus,"Update status");
        page.Children.Add(Section("Updates",UI.Text("Installed: "+OfficialUpdates.InstalledVersion+" - win-x64",13),
            UI.Text("Check, review the available version and notes, then download. Choose Install and restart after verification.",12),auto,updateStatus,
            UI.Button("Check for updates",()=>{_=app.Updates.Check(true);}),
            UI.Button("Release notes",()=>ShortcutService.Open(new(){Target=app.Updates.Available?.Notes.AbsoluteUri??OfficialUpdates.Repository+"/releases"})),
            downloadUpdate=UI.Button("Download update",()=>{_=app.Updates.Download();}),
            UI.Button("Cancel update",app.Updates.Cancel),
            installUpdate=UI.Button("Install and restart",async ()=>{try{await UpdateHandoff.Prepare(app);}catch(Exception ex){updateStatus.Text=ex.Message;app.Store.Log(ex);}}),
            UI.Text(OfficialUpdates.IsInstalled(Environment.ProcessPath!)?"Installation saves Notes, backs up preferences, quits and restarts NabyScape.":"Portable copy: use Release notes to download the portable ZIP, quit, then replace your portable files manually. Automatic installation is unavailable here.",11,UI.Muted),
            UI.Text("Downloads use official GitHub releases. SHA-256 checks integrity; unsigned checksums do not establish publisher identity. Windows security checks remain enabled.",11,UI.Muted)));
        downloadUpdate.ToolTip="Download the available official win-x64 setup and verify its size, version and SHA-256.";
        installUpdate.ToolTip="Save Notes, backup preferences, quit and run the verified setup, then reopen NabyScape.";
        UpdateControls();
    }
}
