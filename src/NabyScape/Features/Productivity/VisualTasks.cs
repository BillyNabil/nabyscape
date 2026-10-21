using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class OverlayWindow
{
    private void BuildVisualTasks()
    {
        var p=app.Productivity;
        var count=UI.Text("",11,UI.Muted);count.HorizontalAlignment=HorizontalAlignment.Right;
        void Summary(){int done=p.Tasks.Count(t=>t.Done);count.Text=p.Tasks.Count==0?"":$"{p.Tasks.Count-done} open";UI.Named(count,$"{done} of {p.Tasks.Count} tasks completed");}
        Summary();var heading=new DockPanel{Margin=new(0,0,0,16)};DockPanel.SetDock(count,Dock.Right);heading.Children.Add(count);heading.Children.Add(UI.Heading("Tasks",18));toolsContent.Children.Add(heading);
        var list=new StackPanel();var viewport=UI.Scroll(list);viewport.Tag="panel-content-scroll";viewport.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;viewport.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;viewport.MaxHeight=Math.Clamp((app.Settings.PanelMaxHeight-180)/app.Settings.PanelScale,72,360);UI.Named(viewport,"Task list");toolsContent.Children.Add(viewport);
        foreach(var item in p.Tasks.ToArray())
        {
            var row=new DockPanel{MinHeight=46,LastChildFill=true};var frame=new Border{Child=row,CornerRadius=new(8),Background=Brushes.Transparent,Padding=new(4,0,4,0)};
            var remove=Icons.Button("x","Remove task "+item.Text,()=>MutateUtility(()=>p.RemoveTask(item.Id)),false,28);remove.Background=Brushes.Transparent;remove.Margin=new(8,0,0,0);DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);MinimalWidgets.Reveal(remove,frame);
            var label=UI.Text(item.Text,12,item.Done?UI.Muted:Brushes.White);label.TextWrapping=TextWrapping.Wrap;if(item.Done)label.TextDecorations=TextDecorations.Strikethrough;
            var check=new CheckBox{Style=(Style)FindResource("TaskToggle"),IsChecked=item.Done,Content=label,Margin=new(0,9,0,9)};UI.Named(check,item.Text);row.Children.Add(check);
            row.SizeChanged+=(_,_)=>label.MaxWidth=Math.Max(80,row.ActualWidth-66);
            void Hover()=>Motion.Fade(frame,1,100);
            frame.MouseEnter+=(_,_)=>{frame.Background=UI.Brush("#0BFFFFFF");Hover();};frame.MouseLeave+=(_,_)=>frame.Background=Brushes.Transparent;
            check.Click+=(_,_)=>{item.Done=check.IsChecked==true;label.TextDecorations=item.Done?TextDecorations.Strikethrough:null;label.Foreground=item.Done?UI.Muted:Brushes.White;Summary();utilityMutating=true;try{p.Save();}finally{utilityMutating=false;}};list.Children.Add(frame);
        }
        if(p.Tasks.Count==0){var empty=UI.Text("Add your first task below.",12,UI.Muted);empty.Margin=new(4,16,0,18);list.Children.Add(empty);}
        var input=UI.Input("New task",taskDraft);input.MaxLength=240;input.BorderThickness=new(0);input.Background=Brushes.Transparent;input.Margin=new(0);input.Padding=new(10,8,0,8);input.MinHeight=36;input.TextChanged+=(_,_)=>taskDraft=input.Text;
        void Add(){if(string.IsNullOrWhiteSpace(input.Text))return;string text=input.Text;MutateUtility(()=>{if(p.AddTask(text))taskDraft="";else app.Notify("Task limit reached","Remove a task before adding another. Your draft is still here.");});}
        var composer=new Grid{Margin=new(0,18,0,0)};composer.ColumnDefinitions.Add(new());composer.ColumnDefinitions.Add(new(){Width=new GridLength(38)});composer.Children.Add(input);
        var placeholder=UI.Text("Add a task…",12,UI.Muted);placeholder.IsHitTestVisible=false;placeholder.Margin=new(10,0,0,0);composer.Children.Add(placeholder);void Hint()=>placeholder.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;Hint();input.TextChanged+=(_,_)=>Hint();
        var add=UI.Button("Add task",Add);add.Content=Icons.Create("plus",15,UI.Muted);add.FontSize=22;add.Background=Brushes.Transparent;add.BorderThickness=new(0);add.Width=34;add.MinHeight=34;add.Padding=new(0);add.Margin=new(4,0,0,0);Grid.SetColumn(add,1);composer.Children.Add(add);toolsContent.Children.Add(new Border{Child=composer,Background=UI.Brush("#14161A"),BorderBrush=UI.Brush("#30343B"),BorderThickness=new(.7),CornerRadius=new(9),Margin=new(0,14,0,0)});composer.Margin=new(0);add.IsEnabled=!string.IsNullOrWhiteSpace(input.Text);input.TextChanged+=(_,_)=>add.IsEnabled=!string.IsNullOrWhiteSpace(input.Text);
        input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Add();e.Handled=true;}};
    }
}
