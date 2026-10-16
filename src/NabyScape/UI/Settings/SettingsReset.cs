using System;
using System.Windows;
using System.Windows.Input;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private void FlushPreferenceChanges()
    {
        Keyboard.ClearFocus();saveTimer.Stop();app.FlushPreferenceEditors();app.ChangeSettings();
    }
    private void ResetPreferences(string? category)
    {
        if(MessageBox.Show(this,PreferenceReset.Summary(category),"Reset preferences",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
        try{FlushPreferenceChanges();PreferenceReset.Reset(app.Store,app.Settings,category);app.ChangeSettings();Navigate(current);status.Text="Preferences reset. The backup can be restored in Application.";}
        catch(Exception ex){status.Text="Reset aborted: "+ex.Message;app.Store.Log(ex);}
    }
    private void RestorePreferences()
    {
        if(MessageBox.Show(this,"Restore the last preference backup? Startup, clipboard capture/history and saved content are kept.","Restore preferences",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
        try{FlushPreferenceChanges();PreferenceReset.Restore(app.Store,app.Settings);app.ChangeSettings();Navigate(current);status.Text="Preference backup restored.";}
        catch(Exception ex){status.Text="Restore aborted: "+ex.Message;app.Store.Log(ex);}
    }
}
