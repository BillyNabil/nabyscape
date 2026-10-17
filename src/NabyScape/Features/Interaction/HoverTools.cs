using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class OverlayWindow
{
    private readonly StackPanel peekClipboard=new(),peekTasks=new();
    private string clipboardPeekKey="",tasksPeekKey="";
    private FrameworkElement BuildToolPeek(string id)
    {var page=new StackPanel();page.Children.Add(UI.Heading(id=="clips"?"Clipboard":"Tasks",13));page.Children.Add(id=="clips"?peekClipboard:peekTasks);var open=PlainPeekButton("Open all "+id,()=>{ShowTools(id);});open.Content=id=="clips"?"Open clipboard":"All tasks";open.FontSize=11;open.Margin=new(0,5,0,0);page.Children.Add(open);return page;}
    private void UpdateToolPeeks()
    {
        if(!peekInitialized)return;
        var p=app.Productivity;
        string clips=p.ClipboardEnabled?string.Join("|",p.Clips.Take(2).Select(c=>c.Id))+"|"+p.ClipboardPaused:"off";
        if(clips!=clipboardPeekKey)
        {
            clipboardPeekKey=clips;peekClipboard.Children.Clear();
            if(!p.ClipboardEnabled)peekClipboard.Children.Add(UI.Text("History is off. Enable it in Settings → Controls.",11,UI.Muted));
            else if(p.Clips.Count==0)peekClipboard.Children.Add(UI.Text("Nothing captured yet",11,UI.Muted));
            else foreach(var clip in p.Clips.Take(2))
            {
                var row=new DockPanel{Height=28};
                if(clip.Kind=="image"){var image=new Image{Source=p.LoadClipImage(clip,(int)Math.Ceiling(24*VisualTreeHelper.GetDpi(this).DpiScaleX)),Width=24,Height=24,Stretch=Stretch.Uniform,Margin=new(0,0,8,0)};row.Children.Add(image);}
                var line=UI.Text(clip.Label,12);line.TextWrapping=TextWrapping.NoWrap;line.TextTrimming=TextTrimming.CharacterEllipsis;line.ToolTip=clip.Kind=="image"?"Image":clip.Label;row.Children.Add(line);peekClipboard.Children.Add(row);
            }
        }
        string tasks=string.Join("|",p.Tasks.Take(2).Select(t=>t.Id+":"+t.Text));
        if(tasks!=tasksPeekKey || peekTasks.Children.Count==0)
        {
            tasksPeekKey=tasks;peekTasks.Children.Clear();
            foreach(var task in p.Tasks.Take(2))
            {
                var label=UI.Text(task.Text,12);label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;
                var toggle=new CheckBox{Content=label,Tag=task,IsChecked=task.Done,Style=(Style)FindResource("TaskToggle"),Height=28,Margin=new(0)};UI.Named(toggle,"Preview task "+task.Id);
                toggle.Click+=(_,_)=>{task.Done=toggle.IsChecked==true;p.Save();};peekTasks.Children.Add(toggle);
            }
            if(p.Tasks.Count==0)peekTasks.Children.Add(UI.Text("No tasks yet. Add one in Tools → Tasks.",11,UI.Muted));
        }
        foreach(var toggle in peekTasks.Children.OfType<CheckBox>())if(toggle.Tag is TaskItem task){toggle.IsChecked=task.Done;((TextBlock)toggle.Content).TextDecorations=task.Done?TextDecorations.Strikethrough:null;}
        foreach(var list in new[]{peekTasks,peekClipboard})for(int i=0;i<list.Children.Count;i++)list.Children[i].Visibility=i==0 || peekDetailed?Visibility.Visible:Visibility.Collapsed;
        bool detach=app.Settings.DetachablePreviews && peekId is not ("clips" or "tasks");
        peekFooter.ColumnDefinitions[3].Width=new(detach?74:0);foreach(FrameworkElement child in peekFooter.Children)if(child.Tag as string=="detach")child.Visibility=detach?Visibility.Visible:Visibility.Collapsed;
    }
}
