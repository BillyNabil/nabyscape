using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private bool activitiesOpen;
    private ActivityDefinition? activityDraft;
    private string activityMessage="";
    private StackPanel? activityContent;
    internal bool ActivitiesOpen=>activitiesOpen;
    private void InitializeActivities()=>app.Activities.Changed+=ActivitiesChanged;
    private void CleanupActivities()=>app.Activities.Changed-=ActivitiesChanged;
    private void ActivitiesChanged(){if(activitiesOpen && activityDraft==null)RefreshActivities();}
    internal void ShowActivities(bool activate=true)
    {
        if(!app.Settings.ActivitiesEnabled)return;
        FlushNotes();if(commandOpen)CloseCommands();activitiesOpen=true;BuildBody();scroll.ScrollToTop();Reveal(activate);ResizeToolPanel();
    }
    private void CloseActivities(){activitiesOpen=false;activityDraft=null;activityMessage="";BuildBody();ResizeToolPanel();}
    private FrameworkElement BuildActivitiesPage()
    {
        activityContent=new StackPanel{Margin=new(3,0,3,0)};RefreshActivities();return activityContent;
    }
    private void RefreshActivities()
    {
        if(activityContent==null)return;
        var panel=activityContent;panel.Children.Clear();
        var heading=new DockPanel();var back=UI.Button(activityDraft==null?"Back":"Cancel",()=>{if(activityDraft!=null){activityDraft=null;activityMessage="";RefreshActivities();}else CloseActivities();});DockPanel.SetDock(back,Dock.Right);heading.Children.Add(back);heading.Children.Add(UI.Heading(activityDraft==null?"Activities":"Edit activity",18));panel.Children.Add(heading);
        if(activityDraft!=null){BuildActivityEditor(panel);return;}
        var hint=UI.Text("Open your workspace, start Focus, and stay awake with one Start. Saved only on this device.",12,UI.Muted);hint.Margin=new(0,8,0,12);panel.Children.Add(hint);
        panel.Children.Add(UI.Row(UI.Button("New activity",()=>EditActivity(new(){Name=""})),UI.Button("Study template",()=>EditActivity(new(){Name="Study",Steps=new(){new(){Kind="open"},new(){Kind="focus",Minutes=25},new(){Kind="awake",Minutes=30}}}))));
        var service=app.Activities;
        if(service.ActiveActivity is { } active)
        {
            panel.Children.Add(UI.Text("Active · "+active.Name,13));panel.Children.Add(UI.Button("Stop activity",()=>{service.Stop();if(service.LastError!=null)app.Notify("Could not stop activity",service.LastError);},true));
            panel.Children.Add(UI.Text("Stop ends only Focus and Keep awake owned by this run. Opened windows stay open.",11,UI.Muted));
        }
        foreach(var result in service.Results)
        {
            var text=UI.Text((result.Success?"✓ ":"! ")+result.Label+" · "+result.Detail,11,result.Success?UI.Muted:UI.Brush("#FFB4A9"));text.Margin=new(0,5,0,0);panel.Children.Add(text);
        }
        foreach(var item in service.Definitions)
        {
            var definition=item;var content=new StackPanel();content.Children.Add(UI.Text(definition.Name,13));content.Children.Add(UI.Text(string.Join(" → ",definition.Steps.Select(s=>s.Label)),11,UI.Muted));
            var start=UI.Button("Start",()=>StartActivity(definition),true);start.IsEnabled=service.ActiveActivity==null;UI.Named(start,"Start activity "+definition.Name);
            content.Children.Add(UI.Row(start,UI.Button("Edit",()=>EditActivity(ActivityActionsService.Clone(definition))),UI.Button("Delete",()=>{if(service.Remove(definition.Id))RefreshActivities();else{activityMessage=service.LastError??"Could not delete activity.";RefreshActivities();}})));
            panel.Children.Add(new Border{Child=content,Background=UI.Brush("#14FFFFFF"),CornerRadius=new(12),Padding=new(12),Margin=new(0,10,0,0)});
        }
        if(service.Definitions.Count==0)panel.Children.Add(UI.Text("No activities yet. Create one or customize the Study template, then save it before starting.",12,UI.Muted));
        if(activityMessage.Length>0)panel.Children.Add(UI.Text(activityMessage,12,UI.Brush("#FFB4A9")));
        if(expanded)ResizeToolPanel();
    }
    private void StartActivity(ActivityDefinition definition)
    {
        bool replace=app.Activities.ReplacesFocus(definition);
        if(replace && MessageBox.Show(this,"Starting this activity replaces your current Focus session. Continue?","Replace Focus?",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        if(!app.Activities.Start(definition.Id,replace)){activityMessage=app.Activities.LastError??"Stop the active activity before starting another.";RefreshActivities();}
    }
    private void EditActivity(ActivityDefinition draft){activityDraft=draft;activityMessage="";RefreshActivities();scroll.ScrollToTop();}
    private void BuildActivityEditor(StackPanel panel)
    {
        var draft=activityDraft!;
        var name=UI.Input("Activity name",draft.Name,"Name this activity");name.MaxLength=80;name.Margin=new(0,10,0,10);name.TextChanged+=(_,_)=>draft.Name=name.Text;panel.Children.Add(name);
        panel.Children.Add(UI.Text("Steps run from top to bottom. Choose each launch target; no commands or arguments.",11,UI.Muted));
        for(int i=0;i<draft.Steps.Count;i++)
        {
            int index=i;var step=draft.Steps[i];var content=new StackPanel();content.Children.Add(UI.Text($"{i+1}. "+step.Label,12));
            if(step.Kind=="open")
            {
                var target=UI.Text(step.Target.AppId??step.Target.Target,10,UI.Muted);target.TextTrimming=TextTrimming.CharacterEllipsis;target.TextWrapping=TextWrapping.NoWrap;content.Children.Add(target);
                content.Children.Add(UI.Row(UI.Button("App…",()=>new AppPicker(this,shortcut=>{step.Target=shortcut;RefreshActivities();}).ShowDialog()),UI.Button("File…",()=>PickActivityFile(step)),UI.Button("Folder…",()=>PickActivityFolder(step))));
                var site=UI.Input("Website for step "+(index+1),step.Target.AppId==null && step.Target.Target.StartsWith("http",StringComparison.OrdinalIgnoreCase)?step.Target.Target:"","https://example.com");
                content.Children.Add(site);content.Children.Add(UI.Button("Use website",()=>{try{var url=CleanLink.ValidateUrl(site.Text,4096,"website");step.Target=new(){Name=new Uri(url).Host,Target=url};activityMessage="";RefreshActivities();}catch(Exception ex){activityMessage=ex.Message;RefreshActivities();}}));
            }
            else
            {
                var minutes=UI.Input("Minutes for step "+(index+1),step.Minutes.ToString(),"Duration in minutes");minutes.MaxLength=4;minutes.TextChanged+=(_,_)=>step.Minutes=int.TryParse(minutes.Text,out int value)?value:0;content.Children.Add(minutes);
                if(step.Kind=="awake"){var display=new CheckBox{Content="Keep display on",IsChecked=step.KeepDisplayOn,Margin=new(0,6,0,6)};display.Click+=(_,_)=>step.KeepDisplayOn=display.IsChecked==true;content.Children.Add(display);}
            }
            var up=UI.Button("↑",()=>{(draft.Steps[index-1],draft.Steps[index])=(draft.Steps[index],draft.Steps[index-1]);RefreshActivities();});up.IsEnabled=index>0;UI.Named(up,"Move step "+(index+1)+" up");
            var down=UI.Button("↓",()=>{(draft.Steps[index+1],draft.Steps[index])=(draft.Steps[index],draft.Steps[index+1]);RefreshActivities();});down.IsEnabled=index<draft.Steps.Count-1;UI.Named(down,"Move step "+(index+1)+" down");
            var remove=UI.Button("Remove",()=>{draft.Steps.RemoveAt(index);RefreshActivities();});UI.Named(remove,"Remove step "+(index+1));content.Children.Add(UI.Row(up,down,remove));
            panel.Children.Add(new Border{Child=content,Background=UI.Brush("#14FFFFFF"),CornerRadius=new(10),Padding=new(10),Margin=new(0,8,0,0)});
        }
        void Add(string kind){if(draft.Steps.Count>=20){activityMessage="You can add up to 20 steps.";}else{draft.Steps.Add(new(){Kind=kind,Minutes=kind=="awake"?30:25});activityMessage="";}RefreshActivities();}
        panel.Children.Add(UI.Row(UI.Button("+ Open",()=>Add("open")),UI.Button("+ Focus",()=>Add("focus")),UI.Button("+ Awake",()=>Add("awake"))));
        panel.Children.Add(UI.Button("Save activity",()=>{if(app.Activities.Save(draft)){activityDraft=null;activityMessage="";RefreshActivities();}else{activityMessage=app.Activities.LastError??"Could not save activity.";RefreshActivities();}},true));
        if(activityMessage.Length>0)panel.Children.Add(UI.Text(activityMessage,12,UI.Brush("#FFB4A9")));
        ResizeToolPanel();
    }
    private void PickActivityFile(ActivityStep step){var dialog=new Microsoft.Win32.OpenFileDialog{Title="Choose activity app or file",Filter="Applications & files|*.*"};if(dialog.ShowDialog(this)==true){step.Target=new(){Name=Path.GetFileNameWithoutExtension(dialog.FileName),Target=dialog.FileName};RefreshActivities();}}
    private void PickActivityFolder(ActivityStep step){var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Choose activity folder"};if(dialog.ShowDialog(this)==true){step.Target=new(){Name=Path.GetFileName(dialog.FolderName),Target=dialog.FolderName};RefreshActivities();}}
}
