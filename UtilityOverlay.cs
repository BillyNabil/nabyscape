using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly StackPanel toolsContent=new();
    private string toolsMode="home";
    private string notesMode="write",taskDraft="";
    private bool utilityMutating;
    private static readonly (string mode,string title)[] toolChoices={ ("files","File shelf"),("notes","Notes"),("clips","Clipboard"),("calendar","Calendar"),("awake","Keep awake"),("links","Clean links") };
    private readonly DispatcherTimer noteSave=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private ContextMenu? audioMenu;
    private FrameworkElement BuildToolsPage()
    {
        if(toolsContent.Parent is Panel old)old.Children.Remove(toolsContent);
        var panel=new StackPanel();panel.Children.Add(toolsContent);RefreshTools();return panel;
    }
    private void NavigateTool(string mode)
    {
        FlushNotes();bool back=mode=="home";toolsMode=mode;calendarEditing=false;scroll.ScrollToTop();RefreshTools();
        if(Motion.Enabled){var move=new TranslateTransform(back?-14:14,0);toolsContent.RenderTransform=move;toolsContent.Opacity=0;Motion.Fade(toolsContent,1,180);Motion.To(move,TranslateTransform.XProperty,0,280);}
    }
    private void BuildToolLauncher()
    {
        var list=new StackPanel{Margin=new(0,3,0,4)};
        for(int i=0;i<toolChoices.Length;i++)
        {
            
            var choice=toolChoices[i];var content=new Grid();content.ColumnDefinitions.Add(new(){Width=new GridLength(28)});content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new(){Width=new GridLength(18)});
            var icon=ToolIcon(choice.mode);icon.Width=18;icon.Height=18;icon.HorizontalAlignment=HorizontalAlignment.Left;content.Children.Add(icon);
            var label=UI.Text(choice.title,12);Grid.SetColumn(label,1);content.Children.Add(label);
            var arrow=new System.Windows.Shapes.Path{Data=Geometry.Parse("M1,1 L5,5 1,9"),Stroke=UI.Brush("#ADB0B7"),StrokeThickness=1.3,Width=5,Height=9,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right,Opacity=0};Grid.SetColumn(arrow,2);content.Children.Add(arrow);
            var row=new Button{Content=content,Template=(ControlTemplate)FindResource("ToolRowTemplate"),Height=32,MinHeight=32,Margin=new(0,0,0,0),Padding=new(10,0,13,0),HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=Brushes.Transparent,Cursor=Cursors.Hand};UI.Named(row,choice.title);row.Click+=(_,_)=>NavigateTool(choice.mode);
            var arrowMove=new TranslateTransform(-3,0);arrow.RenderTransform=arrowMove;
            void Feedback(bool pressed=false)
            {
                bool active=row.IsMouseOver || row.IsKeyboardFocusWithin;
                if(row.Template.FindName("RowLight",row) is Border light)Motion.Fade(light,pressed?.12:active?.065:0,160);
                Motion.Fade(arrow,active?1:0,160);Motion.To(arrowMove,TranslateTransform.XProperty,active?0:-3,220);
            }
            row.MouseEnter+=(_,_)=>Feedback();row.MouseLeave+=(_,_)=>Feedback();row.GotKeyboardFocus+=(_,_)=>Feedback();row.LostKeyboardFocus+=(_,_)=>Feedback();row.PreviewMouseLeftButtonDown+=(_,_)=>Feedback(true);row.PreviewMouseLeftButtonUp+=(_,_)=>Feedback();row.LostMouseCapture+=(_,_)=>Feedback();
            row.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Space or Key.Enter)Feedback(true);};row.PreviewKeyUp+=(_,_)=>Feedback();list.Children.Add(row);
        }
        toolsContent.Children.Add(list);
    }

    private static FrameworkElement ToolIcon(string mode)
    {
        string data=mode switch{
            "files"=>"M3,7 L9,7 11,9 21,9 21,20 3,20 Z M3,7 L3,4 10,4 12,6 21,6 21,9",
            "notes"=>"M5,3 L19,3 19,21 5,21 Z M9,8 L15,8 M9,12 L15,12 M9,16 L13,16",
            "clips"=>"M9,5 L5,5 5,21 19,21 19,5 15,5 M9,3 L15,3 15,7 9,7 Z",
            "calendar"=>"M3,6 L21,6 21,21 3,21 Z M3,11 L21,11 M8,3 L8,8 M16,3 L16,8 M8,15 L10,15 M14,15 L16,15",
            "awake"=>"M4,8 L17,8 17,16 Q17,20 13,20 L8,20 Q4,20 4,16 Z M17,9 L20,9 Q24,13 17,15 M8,3 L8,5 M13,2 L13,5",
            _=>"M10,14 L14,10 M9,16 L7,18 A4,4 0 0 1 2,12 L7,7 A4,4 0 0 1 13,8 M11,16 A4,4 0 0 0 17,17 L22,12 A4,4 0 0 0 17,6 L15,8"};
        return new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Stroke=UI.Brush("#C6C8CE"),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=20,Height=20,Stretch=Stretch.Uniform};
    }
    private void BuildNotes()
    {
        var p=app.Productivity;var heading=UI.Heading("Notes",19);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0,0,0,12);toolsContent.Children.Add(heading);
        var tabs=new Grid{Height=32};tabs.ColumnDefinitions.Add(new());tabs.ColumnDefinitions.Add(new());
        var capsule=new Border{Background=UI.Brush("#34363C"),CornerRadius=new(11),Margin=new(3)};Grid.SetColumnSpan(capsule,2);capsule.HorizontalAlignment=HorizontalAlignment.Left;var slide=new TranslateTransform();capsule.RenderTransform=slide;tabs.Children.Add(capsule);
        var track=new Border{Background=UI.Brush("#14161A"),CornerRadius=new(13),Margin=new(0,0,0,15),Child=tabs};toolsContent.Children.Add(track);
        var write=new StackPanel();var tasks=new StackPanel();toolsContent.Children.Add(write);toolsContent.Children.Add(tasks);
        var note=UI.Input("Quick notes",p.Note);note.AcceptsReturn=true;note.TextWrapping=TextWrapping.Wrap;note.Height=136;note.VerticalContentAlignment=VerticalAlignment.Top;note.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;note.MaxLength=50000;note.Padding=new(13);note.Margin=new(0);
        var editor=new Grid();editor.Children.Add(note);var placeholder=UI.Text("Capture a thought…",13,UI.Muted);placeholder.IsHitTestVisible=false;placeholder.VerticalAlignment=VerticalAlignment.Top;placeholder.Margin=new(14);editor.Children.Add(placeholder);write.Children.Add(editor);
        var meta=UI.Text("Saved on this device",10,UI.Muted);meta.Margin=new(2,9,0,2);write.Children.Add(meta);
        void Placeholder()=>placeholder.Visibility=note.Text.Length==0?Visibility.Visible:Visibility.Collapsed;Placeholder();
        note.TextChanged+=(_,_)=>{p.Note=note.Text;Placeholder();noteSave.Stop();noteSave.Start();};
        var addRow=new DockPanel{Margin=new(0,0,0,9)};var input=UI.Input("New task",taskDraft);input.MaxLength=240;input.MinHeight=34;input.Margin=new(0);input.Padding=new(11,8,11,8);input.TextChanged+=(_,_)=>taskDraft=input.Text;
        void Add(){if(string.IsNullOrWhiteSpace(input.Text))return;string text=input.Text;MutateUtility(()=>{if(p.AddTask(text))taskDraft="";else app.Notify("Task limit reached","Remove a task before adding another. Your draft is still here.");});}
        var add=UI.Button("Add task",Add);add.Content="+";add.FontSize=18;add.Width=34;add.Height=34;add.MinHeight=34;add.Padding=new(0);add.Margin=new(7,0,0,0);DockPanel.SetDock(add,Dock.Right);addRow.Children.Add(add);addRow.Children.Add(input);tasks.Children.Add(addRow);
        input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Add();e.Handled=true;}};
        foreach(var item in p.Tasks.ToArray())
        {
            var row=new DockPanel{MinHeight=38,Margin=new(1,1,3,1)};var remove=UI.Button("×",()=>MutateUtility(()=>p.RemoveTask(item.Id)));remove.Padding=new(0);remove.Width=26;remove.Height=26;remove.MinHeight=26;remove.Background=Brushes.Transparent;remove.BorderThickness=new(0);remove.Margin=new(5,0,0,0);remove.Opacity=.25;UI.Named(remove,"Remove task "+item.Text);DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
            row.MouseEnter+=(_,_)=>Motion.Fade(remove,.85);row.MouseLeave+=(_,_)=>Motion.Fade(remove,.25);remove.GotKeyboardFocus+=(_,_)=>Motion.Fade(remove,1);
            var label=UI.Text(item.Text,12,item.Done?UI.Muted:Brushes.White);label.MaxWidth=240;if(item.Done)label.TextDecorations=TextDecorations.Strikethrough;
            var check=new CheckBox{Style=(Style)FindResource("TaskToggle"),IsChecked=item.Done,Content=label,Margin=new(0,6,0,6)};UI.Named(check,item.Text);
            check.Click+=(_,_)=>{item.Done=check.IsChecked==true;label.TextDecorations=item.Done?TextDecorations.Strikethrough:null;label.Foreground=item.Done?UI.Muted:Brushes.White;utilityMutating=true;try{p.Save();}finally{utilityMutating=false;}};row.Children.Add(check);tasks.Children.Add(row);
        }
        if(p.Tasks.Count==0){var empty=UI.Text("Make room for what matters.\nAdd your first task above.",12,UI.Muted);empty.Margin=new(2,16,0,18);tasks.Children.Add(empty);}
        var buttons=new Button[2];
        void Select(bool animate){write.Visibility=notesMode=="write"?Visibility.Visible:Visibility.Collapsed;tasks.Visibility=notesMode=="tasks"?Visibility.Visible:Visibility.Collapsed;capsule.Width=Math.Max(0,tabs.ActualWidth/2-6);double x=notesMode=="write"?0:tabs.ActualWidth/2;if(animate)Motion.To(slide,TranslateTransform.XProperty,x,240);else slide.X=x;for(int i=0;i<2;i++)buttons[i].Foreground=(notesMode=="write")==(i==0)?Brushes.White:UI.Muted;if(animate)Motion.Enter(notesMode=="write"?write:tasks,5);ResizeToolPanel();}
        for(int i=0;i<2;i++){int index=i;var button=UI.Button(i==0?"Write":"Tasks",()=>{FlushNotes();notesMode=index==0?"write":"tasks";scroll.ScrollToTop();Select(true);});button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.MinHeight=26;button.Margin=new(0);button.Padding=new(0);button.FontSize=11;Grid.SetColumn(button,i);tabs.Children.Add(button);buttons[i]=button;}
        tabs.SizeChanged+=(_,_)=>Select(false);Select(false);
    }

    private void InitializeUtilities()
    {
        noteSave.Tick+=(_,_)=>FlushNotes();app.Productivity.Changed+=ProductivityChanged;
        app.Calendar.Changed+=CalendarChanged;app.Awake.Changed+=AwakeChanged;
        AllowDrop=true;
        DragEnter+=(_,e)=>{if(WidgetOn("tools") && e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=DragDropEffects.Copy;if(!expanded){if(!peeking)PreviewActivity();SelectPeek("files");}else if(activePage!="tools" || toolsMode!="files")ShowTools("files",false);e.Handled=true;}};
        DragOver+=(_,e)=>{if(WidgetOn("tools") && e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=DragDropEffects.Copy;e.Handled=true;}};
        Drop+=(_,e)=>
        {
            if(!WidgetOn("tools") || !e.Data.GetDataPresent(DataFormats.FileDrop))return;
            if(e.Data.GetData(DataFormats.FileDrop) is string[] paths){utilityMutating=true;try{app.Productivity.AddFiles(paths);}finally{utilityMutating=false;}if(peeking){SelectPeek("files");UpdatePeekDeck();}else ShowTools("files");}e.Handled=true;
        };
    }
    private void ProductivityChanged()
    {
        if(utilityMutating || toolsMode is not ("files" or "notes" or "clips") || (toolsMode=="notes" && Keyboard.FocusedElement is TextBox))return;
        RefreshTools();
    }
    private void FlushNotes(){noteSave.Stop();if(app.Productivity!=null){utilityMutating=true;try{app.Productivity.Save();}finally{utilityMutating=false;}}}
    private void MutateUtility(Action action){utilityMutating=true;try{action();}catch(Exception ex){app.Store.Log(ex);app.Notify("Could not complete the action",ex.Message);}finally{utilityMutating=false;}RefreshTools();}
    internal void ShowTools(string mode,bool activate=true)
    {if(!WidgetOn("tools"))return;if(commandOpen)CloseCommands();FlushNotes();toolsMode=mode;calendarEditing=false;scroll.ScrollToTop();Reveal(activate);if(app.Settings.CompactLayout)SelectPage("tools");RefreshTools();}
    private void RefreshTools()
    {

        toolsContent.Children.Clear();var p=app.Productivity;if(p==null)return;
        if(toolsMode!="home")
        {
            var back=UI.Button("All tools",()=>NavigateTool("home"));var label=UI.Text("‹   All tools",11,UI.Muted);back.Content=label;back.HorizontalAlignment=HorizontalAlignment.Left;back.Background=Brushes.Transparent;back.BorderThickness=new(0);back.Padding=new(1,0,8,0);back.MinHeight=24;back.Margin=new(0,0,0,10);toolsContent.Children.Add(back);
        }
        if(toolsMode=="home")BuildToolLauncher();
        else if(toolsMode=="files")
        {
            toolsContent.Children.Add(UI.Heading("File shelf",18));var hint=UI.Text("Drop files here, then drag them into another app.",11,UI.Muted);hint.Margin=new(0,6,0,12);toolsContent.Children.Add(hint);
            var add=UI.Button($"Add files…  {p.Shelf.Count}/40",()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Multiselect=true,Title="Add to file shelf"};if(dialog.ShowDialog(this)==true)MutateUtility(()=>p.AddFiles(dialog.FileNames));});add.IsEnabled=p.Shelf.Count<40;toolsContent.Children.Add(add);
            foreach(var item in p.Shelf.ToArray())
            {
                var row=new DockPanel{Margin=new(0,9,0,0)};var remove=UI.Button("×",()=>MutateUtility(()=>p.RemoveFile(item.Path)));remove.FontSize=17;remove.Padding=new(9,2,9,2);remove.MinHeight=29;remove.Margin=new(6,0,0,0);UI.Named(remove,"Unstage "+item.Name);DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
                var label=new StackPanel();var name=UI.Text(item.Name,12,item.Exists?Brushes.White:UI.Muted);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;label.Children.Add(name);label.Children.Add(UI.Text(item.Exists?"Drag out · double-click to open":"File not found",10,UI.Muted));
                var tile=new Border{Background=UI.Brush("#16FFFFFF"),CornerRadius=new(11),Padding=new(10,8,10,8),Child=label,ToolTip=item.Path};Point start=default;
                tile.PreviewMouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(tile);if(e.ClickCount==2){try{ShortcutService.Open(new(){Target=item.Path});}catch(Exception ex){app.Notify("Could not open file",ex.Message);}e.Handled=true;}};
                tile.PreviewMouseMove+=(_,e)=>{if(e.LeftButton!=MouseButtonState.Pressed || !item.Exists)return;var now=e.GetPosition(tile);if(Math.Abs(now.X-start.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;DragDrop.DoDragDrop(tile,new DataObject(DataFormats.FileDrop,new[]{item.Path}),DragDropEffects.Copy|DragDropEffects.Link);};row.Children.Add(tile);toolsContent.Children.Add(row);
            }
            if(p.Shelf.Count==0)toolsContent.Children.Add(UI.Text("Your shelf is ready.",12,UI.Muted));
        }
        else if(toolsMode=="notes")
        {
            BuildNotes();
        }
        else if(toolsMode=="calendar")BuildCalendar(toolsContent);
        else if(toolsMode=="awake")BuildAwake(toolsContent);
        else if(toolsMode=="links")BuildCleanLinks(toolsContent);
        else
        {
            toolsContent.Children.Add(UI.Heading("Clipboard history",18));
            if(!p.ClipboardEnabled)
            {
                var intro=UI.Text("History is off. Enable it to save the text and images you copy next, only on this device.",12,UI.Muted);intro.Margin=new(0,10,0,12);toolsContent.Children.Add(intro);
                toolsContent.Children.Add(UI.Button("Enable history",()=>{app.Settings.ClipboardHistory=true;app.ChangeSettings();},true));
            }
            else
            {
                var pause=new CheckBox{Content="Pause capture",IsChecked=p.ClipboardPaused};pause.Click+=(_,_)=>p.ClipboardPaused=pause.IsChecked==true;toolsContent.Children.Add(pause);
                toolsContent.Children.Add(UI.Row(UI.Button("Clear history",()=>MutateUtility(p.ClearClips)),UI.Button("Turn off",()=>{app.Settings.ClipboardHistory=false;app.ChangeSettings();})));
                foreach(var item in p.Clips.OrderByDescending(c=>c.Pinned).ThenByDescending(c=>c.Timestamp).ToArray())
                {
                    var content=new StackPanel();var text=UI.Text(item.Label,12);text.MaxHeight=38;content.Children.Add(text);
                    if(item.Kind=="image" && File.Exists(item.ImagePath)){try{var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(item.ImagePath);bitmap.DecodePixelWidth=80;bitmap.EndInit();bitmap.Freeze();content.Children.Add(new Image{Source=bitmap,Height=48,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,5,0,0)});}catch(Exception ex){app.Store.Log(ex);}}
                    var copy=UI.Button("Copy",()=>{try{p.CopyClip(item);}catch(Exception ex){app.Notify("Could not copy item",ex.Message);}});var pin=UI.Button(item.Pinned?"Unpin":"Pin",()=>MutateUtility(()=>p.ToggleClipPin(item.Id)));var remove=UI.Button("Remove",()=>MutateUtility(()=>p.RemoveClip(item.Id)));foreach(var b in new[]{copy,pin,remove}){b.FontSize=10;b.Padding=new(9,5,9,5);b.MinHeight=27;}content.Children.Add(UI.Row(copy,pin,remove));toolsContent.Children.Add(new Border{Background=UI.Brush("#14FFFFFF"),CornerRadius=new(12),Padding=new(10),Margin=new(0,8,0,0),Child=content});
                }
                if(p.Clips.Count==0)toolsContent.Children.Add(UI.Text("Your next copied item will appear here.",12,UI.Muted));
            }
        }
        ApplyHeadingTypography(toolsContent);
        if(expanded && activePage=="tools"){Dimensions();motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));}
    }
    internal ContextMenu? AudioPopup=>audioMenu;
    internal void OpenAudioMenu()
    {
        audioMenu=new ContextMenu{Background=UI.Brush("#191A1E"),Foreground=Brushes.White,BorderBrush=UI.Brush("#42434B"),Padding=new(5),HasDropShadow=false};
        var mixerItem=new MenuItem{Header="Volume mixer",Foreground=Brushes.White};mixerItem.Click+=(_,_)=>OpenMixer();audioMenu.Items.Add(mixerItem);audioMenu.Items.Add(new Separator{Style=(Style)FindResource(typeof(Separator))});
        foreach(var device in app.Audio.Outputs())
        {
            var item=new MenuItem{Header=device.Name,IsCheckable=true,IsChecked=device.Id==app.Audio.DefaultOutputId,Foreground=Brushes.White};
            item.Click+=(_,_)=>{if(!app.Audio.SetOutput(device.Id))app.Notify("Could not switch output",app.Audio.LastError??"Device unavailable.");Update();};audioMenu.Items.Add(item);
        }
        if(audioMenu.Items.Count==0)audioMenu.Items.Add(new MenuItem{Header="No active output devices",IsEnabled=false});audioMenu.Items.Add(new Separator{Style=(Style)FindResource(typeof(Separator))});
        var mic=new MenuItem{Header=(app.Audio.MicrophoneMuted?"Unmute":"Mute")+" microphone · "+app.Audio.MicrophoneName,IsEnabled=app.Audio.MicrophoneAvailable,Foreground=Brushes.White};mic.Click+=(_,_)=>{if(!app.Audio.ToggleMicrophoneMute())app.Notify("Could not change microphone",app.Audio.LastError??"Device unavailable.");};audioMenu.Items.Add(mic);audioMenu.Closed+=(_,_)=>{if(peeking && !DiagnosticRun && !PointerInside())leaveDelay.Start();};audioMenu.IsOpen=true;
    }
}
