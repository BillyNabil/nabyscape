using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
internal sealed partial class AssistantProviderSettings
{
    private bool switchingProvider;
    internal bool HasProviderDraft=>Selected is {} saved && !loadingProvider && (key.Password.Length>0 || ReadDraftIdentity()!=ReadSavedIdentity(saved));
    internal async Task<bool> ConfirmProviderChanges()
    {
        if(!HasProviderDraft)return true;
        var dialog=new Window{Title="Unsaved provider changes",Width=420,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=Window.GetWindow(this),Background=UI.Brush("#16191F"),Foreground=System.Windows.Media.Brushes.White};
        var panel=new StackPanel{Margin=new(22)};panel.Children.Add(UI.Heading("Keep your connection changes?",18));panel.Children.Add(UI.Text("Save this provider before continuing, discard its changes, or keep editing.",12,UI.Muted));
        string choice="edit";var actions=new WrapPanel{Margin=new(0,16,0,0)};
        foreach(string label in new[]{"Save","Discard","Keep editing"}){string action=label;actions.Children.Add(AssistantWindow.Pill(label,()=>{choice=action;dialog.Close();},label=="Save"));}panel.Children.Add(actions);dialog.Content=panel;dialog.ShowDialog();
        if(choice=="Save")return await SaveProvider();
        if(choice=="Discard"){syncing=true;list.SelectedItem=editorProvider;syncing=false;LoadSelected();return true;}
        return false;
    }
    private async Task ChangeEditedProvider()
    {
        if(switchingProvider)return;switchingProvider=true;var requested=list.SelectedItem as AssistantProvider;
        try{syncing=true;list.SelectedItem=editorProvider;syncing=false;if(!await ConfirmProviderChanges())return;syncing=true;list.SelectedItem=ai.Options.Providers.Find(p=>p.Id==requested?.Id);syncing=false;LoadSelected();connectionEditor.IsExpanded=true;}
        finally{syncing=false;switchingProvider=false;}
    }
}
