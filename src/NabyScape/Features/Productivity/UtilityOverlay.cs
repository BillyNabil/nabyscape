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
    private bool weatherLayoutPending;
    private double WeatherContentBudget
    {
        get
        {
            var screen=Native.ChooseScreen(app.Settings);double available=Math.Min(app.Settings.PanelMaxHeight,screen.WorkingArea.Height/Native.Scale(screen)-16);
            return (available-PanelFramePadding)/app.Settings.PanelScale-48-app.Settings.SectionGap;
        }
    }
    private void SizeWeatherViewport()
    {
        if(toolsMode=="weather")foreach(var weather in toolsContent.Children.OfType<WeatherView>())weather.FitTo(bodyWidth,WeatherContentBudget);
    }
    private FrameworkElement BuildWeatherPage()
    {
        var weather=new WeatherView(app);
        weather.FitTo(bodyWidth,WeatherContentBudget);
        weather.ContentSizeChanged+=()=>QueueWeatherPanelResize(weather);
        weather.Loaded+=(_,_)=>QueueWeatherPanelResize(weather);
        return weather;
    }
    private void QueueWeatherPanelResize(WeatherView weather)
    {
        if(weatherLayoutPending || !expanded || toolsMode!="weather" || !toolsContent.Children.Contains(weather))return;
        weatherLayoutPending=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>
        {
            try
            {
                // A second forecast can replace the view while a previous resize is
                // queued. Resize the current mounted view, rather than dropping its
                // only layout notification because the old instance was unloaded.
                if(expanded && toolsMode=="weather" && toolsContent.Children.OfType<WeatherView>().Any(current=>current.IsLoaded && current.IsVisible) && !assistantOpen && !commandOpen && !activitiesOpen)
                    ResizeToolPanel();
            }
            finally{weatherLayoutPending=false;}
        });
    }
    private string toolsMode="home";
    private string taskDraft="";
    private bool utilityMutating;
    private static readonly (string mode,string title)[] toolChoices={ ("files","File shelf"),("notes","Notes"),("tasks","Tasks"),("clips","Clipboard"),("calendar","Calendar"),("awake","Keep awake"),("links","Clean links"),("weather","Weather") };
    private bool ToolOn(string id)=>app.Settings.ToolEnabled(id) || app.Settings.HoverViews.Any(view=>view.Id==id && view.Enabled && HoverPagePolicy.Available(app.Settings,id));
    private readonly DispatcherTimer noteSave=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private ContextMenu? audioMenu;
    private FrameworkElement BuildToolsPage()
    {
        if(toolsContent.Parent is Panel old)old.Children.Remove(toolsContent);
        var panel=new StackPanel();panel.Children.Add(toolsContent);RefreshTools();return panel;
    }
    private void NavigateTool(string mode)
    {
        if(mode!="home" && !ToolOn(mode))return;
        activitiesOpen=false;FlushNotes();bool back=mode=="home";toolsMode=mode;calendarEditing=false;scroll.ScrollToTop();RefreshTools();
        if(Motion.Enabled){var move=new TranslateTransform(back?-14:14,0);toolsContent.RenderTransform=move;toolsContent.Opacity=0;Motion.Fade(toolsContent,1,180);Motion.To(move,TranslateTransform.XProperty,0,280);}
    }
    private void BuildToolLauncher()
    {
        var list=new StackPanel{Margin=new(0,3,0,4)};
        for(int i=0;i<toolChoices.Length;i++)
        {
            
            var choice=toolChoices[i];if(!ToolOn(choice.mode))continue;var content=new Grid();content.ColumnDefinitions.Add(new(){Width=new GridLength(28)});content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new(){Width=new GridLength(18)});
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
        if(list.Children.Count==0){list.Children.Add(UI.Text("All tools are hidden. Enable them in Settings → Widgets.",12,UI.Muted));list.Children.Add(UI.Button("Open Settings",app.ShowSettings));}toolsContent.Children.Add(list);
    }

    private static FrameworkElement ToolIcon(string mode)
    {
        string data=mode switch{
            "files"=>"M3,7 L9,7 11,9 21,9 21,20 3,20 Z M3,7 L3,4 10,4 12,6 21,6 21,9",
            "notes" or "tasks"=>"M5,3 L19,3 19,21 5,21 Z M9,8 L15,8 M9,12 L15,12 M9,16 L13,16",
            "clips"=>"M9,5 L5,5 5,21 19,21 19,5 15,5 M9,3 L15,3 15,7 9,7 Z",
            "calendar"=>"M3,6 L21,6 21,21 3,21 Z M3,11 L21,11 M8,3 L8,8 M16,3 L16,8 M8,15 L10,15 M14,15 L16,15",
            "awake"=>"M4,8 L17,8 17,16 Q17,20 13,20 L8,20 Q4,20 4,16 Z M17,9 L20,9 Q24,13 17,15 M8,3 L8,5 M13,2 L13,5",
            _=>"M10,14 L14,10 M9,16 L7,18 A4,4 0 0 1 2,12 L7,7 A4,4 0 0 1 13,8 M11,16 A4,4 0 0 0 17,17 L22,12 A4,4 0 0 0 17,6 L15,8"};
        return new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Stroke=UI.Brush("#C6C8CE"),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=20,Height=20,Stretch=Stretch.Uniform};
    }
    private void BuildNotes()
    {
        if(toolsMode=="tasks"){BuildVisualTasks();return;}var p=app.Productivity;var heading=UI.Heading(toolsMode=="tasks"?"Tasks":"Notes",19);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0,0,0,12);toolsContent.Children.Add(heading);
        var write=new StackPanel();var tasks=new StackPanel();toolsContent.Children.Add(toolsMode=="notes"?write:tasks);
        mainNoteEditor=null;
        if(toolsMode=="notes")
        {
            var note=UI.Input("Quick notes",p.Note);mainNoteEditor=note;note.AcceptsReturn=true;note.TextWrapping=TextWrapping.Wrap;note.Height=136;note.VerticalContentAlignment=VerticalAlignment.Top;note.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;note.MaxLength=50000;note.Padding=new(13);note.Margin=new(0);
            var editor=new Grid();editor.Children.Add(note);var placeholder=UI.Text("Capture a thought…",13,UI.Muted);placeholder.IsHitTestVisible=false;placeholder.VerticalAlignment=VerticalAlignment.Top;placeholder.Margin=new(14);editor.Children.Add(placeholder);write.Children.Add(editor);
            var meta=UI.Text("Saved on this device",10,UI.Muted);meta.Margin=new(2,9,0,2);write.Children.Add(meta);
            void Placeholder()=>placeholder.Visibility=note.Text.Length==0?Visibility.Visible:Visibility.Collapsed;Placeholder();
            note.TextChanged+=(_,_)=>{if(!syncingNote)p.Note=note.Text;Placeholder();noteSave.Stop();noteSave.Start();};
            return;
        }
    }

    private void InitializeUtilities()
    {
        noteSave.Tick+=(_,_)=>FlushNotes();app.Productivity.Changed+=ProductivityChanged;
        app.Calendar.Changed+=CalendarChanged;app.Awake.Changed+=AwakeChanged;
        InitializeFileDrop();
    }

    private void ProductivityChanged()
    {
        SyncNoteEditors();UpdateToolPeeks();if(toolsMode=="notes" && mainNoteEditor?.IsLoaded==true)return;
        if(utilityMutating || toolsMode is not ("files" or "notes" or "tasks" or "clips") || (toolsMode=="notes" && Keyboard.FocusedElement is TextBox))return;
        RefreshTools();
    }
    private void FlushNotes(){noteSave.Stop();if(app.Productivity!=null){utilityMutating=true;try{app.Productivity.Save();}finally{utilityMutating=false;}}}
    private void MutateUtility(Action action){utilityMutating=true;try{action();}catch(Exception ex){app.Store.Log(ex);app.Notify("Could not complete the action",ex.Message);}finally{utilityMutating=false;}RefreshTools();}
    internal void ShowTools(string mode,bool activate=true)
    {if(mode!="home" && !ToolOn(mode))return;if(activitiesOpen){activitiesOpen=false;activityDraft=null;BuildBody();}if(commandOpen)CloseCommands();FlushNotes();toolsMode=mode;calendarEditing=false;scroll.ScrollToTop();if(mode=="home")mode=app.Settings.Widgets.FirstOrDefault(w=>w.Enabled && Settings.ToolNames.ContainsKey(w.Id))?.Id??"notes";toolsMode=mode;OpenHoverPanel(mode,activate);RefreshTools();}
    private void RefreshTools()
    {

        if(toolsMode!="home" && !ToolOn(toolsMode))toolsMode="home";
        toolsContent.Children.Clear();var p=app.Productivity;if(p==null)return;
        if(toolsMode!="home" && !Settings.ToolNames.ContainsKey(activePage))
        {
            var back=UI.Button("All tools",()=>NavigateTool("home"));var label=UI.Text("‹   All tools",11,UI.Muted);back.Content=label;back.HorizontalAlignment=HorizontalAlignment.Left;back.Background=Brushes.Transparent;back.BorderThickness=new(0);back.Padding=new(1,0,8,0);back.MinHeight=24;back.Margin=new(0,0,0,10);toolsContent.Children.Add(back);
        }
        if(toolsMode=="home")BuildToolLauncher();
        else if(toolsMode=="files")
        {
            BuildFileShelf();
        }
        else if(toolsMode is "notes" or "tasks")
        {
            BuildNotes();
        }
        else if(toolsMode=="weather")toolsContent.Children.Add(BuildWeatherPage());
        else if(toolsMode=="calendar")BuildCalendar(toolsContent);
        else if(toolsMode=="awake")BuildAwake(toolsContent);
        else if(toolsMode=="links")BuildCleanLinks(toolsContent);
        else
        {
            BuildSmartClipboard(toolsContent);
        }
        ApplyHeadingTypography(toolsContent);
        if(expanded && (activePage=="tools" || Settings.ToolNames.ContainsKey(activePage))){Dimensions(true);motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));}
    }
    internal ContextMenu? AudioPopup=>audioMenu;
    internal void OpenAudioMenu()
    {
        audioMenu=new ContextMenu{Background=UI.Brush("#191A1E"),Foreground=Brushes.White,BorderBrush=UI.Brush("#42434B"),Padding=new(5),HasDropShadow=false};
        if(mediaControls?.IsVisible==true){audioMenu.PlacementTarget=mediaControls.VolumeButton;audioMenu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;}
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
