using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NabyScape;

internal sealed class DetachedPreviewView : IDisposable
{
    private readonly App app;
    private readonly string kind;
    private readonly Action openMain;
    private readonly StackPanel panel=new(){Margin=new(8)};
    private readonly DispatcherTimer noteSave=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private TextBlock? title,detail,time,status;
    private Image? artwork;
    private ComboBox? sessions;
    private Slider? volume,seek;
    private Button? toggle,previous,next;
    private TextBox? notes,minutes;
    private ProgressBar? progress,memory;
    private ListBox? shelf;
    private Calendar? calendar;
    private StackPanel? eventList;
    private string sessionKey="",shelfKey="",calendarKey="";
    private bool visible,updating,dirty;
    private Point? dragStart;
    internal FrameworkElement Content {get;}
    internal DetachedPreviewView(App app,string kind,Action openMain)
    {
        this.app=app;this.kind=kind;this.openMain=openMain;
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        switch(kind){case "media":BuildMusic();break;case "timer":case "focus":BuildClock();break;case "notes":BuildNotes();break;case "files":BuildShelf();break;case "calendar":BuildCalendar();break;case "system":BuildSystem();break;}
        var open=UI.Button("Open "+DetachedPreviewWindow.NameFor(kind),openMain);open.Margin=new(0,12,0,0);panel.Children.Add(open);
        noteSave.Tick+=(_,_)=>Flush();
    }
    internal void SetVisible(bool value)
    {
        if(visible==value)return;visible=value;
        if(value){if(Motion.Enabled)Motion.Enter(panel,4);app.Updated+=Refresh;app.Productivity.Changed+=DataChanged;app.Calendar.Changed+=DataChanged;Refresh();}
        else{app.Updated-=Refresh;app.Productivity.Changed-=DataChanged;app.Calendar.Changed-=DataChanged;Flush();}
    }
    internal void ApplySettings(){if(title!=null)title.FontWeight=kind=="media" && app.Settings.BoldMediaTitle?FontWeights.SemiBold:FontWeights.Normal;if(visible)Refresh();}
    private void DataChanged(){if(!visible)return;Refresh();}
    internal void Flush(){noteSave.Stop();if(dirty){dirty=!app.Productivity.Save();if(status!=null)status.Text=dirty?"Could not save. Try Save again.":"Saved locally";}}
    public void Dispose(){SetVisible(false);Flush();noteSave.Stop();}
    private void Add(FrameworkElement element){element.Margin=new(0,0,0,9);panel.Children.Add(element);}
    private Button Action(string label,Action action){var button=UI.Button(label,action);button.Margin=new(0,0,6,0);return button;}
    private void BuildMusic()
    {
        var metadata=new Grid();metadata.ColumnDefinitions.Add(new(){Width=new GridLength(54)});metadata.ColumnDefinitions.Add(new());
        artwork=new Image{Width=42,Height=42,Stretch=Stretch.UniformToFill,HorizontalAlignment=HorizontalAlignment.Left,Clip=new RectangleGeometry(new Rect(0,0,42,42),6,6)};UI.Named(artwork,"Current album artwork");metadata.Children.Add(artwork);
        var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};title=UI.Text("Nothing playing",15);title.FontWeight=app.Settings.BoldMediaTitle?FontWeights.SemiBold:FontWeights.Normal;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(title);detail=UI.Text("",11,UI.Muted);detail.TextWrapping=TextWrapping.NoWrap;detail.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(detail);Grid.SetColumn(labels,1);metadata.Children.Add(labels);Add(metadata);
        sessions=new ComboBox{MinHeight=28};UI.Named(sessions,"Media session");sessions.SelectionChanged+=(_,_)=>{if(!updating && sessions.SelectedIndex>=0 && sessions.SelectedIndex<app.Media.Sessions.Count)app.Media.Select(app.Media.Sessions[sessions.SelectedIndex]);};Add(sessions);
        previous=Action("Previous",()=>_=app.Media.Command("previous"));toggle=Action("Play",()=>_=app.Media.Command("toggle"));next=Action("Next",()=>_=app.Media.Command("next"));Add(UI.Row(previous,toggle,next));
        seek=new Slider{Minimum=0,Maximum=1};UI.Named(seek,"Track position");seek.ValueChanged+=(_,_)=>{if(!updating)_=app.Media.Seek(seek.Value);};Add(seek);
        volume=new Slider{Minimum=0,Maximum=100};UI.Named(volume,"Output volume");volume.ValueChanged+=(_,_)=>{if(!updating)app.Audio.SetVolume(volume.Value/100);};var volumeRow=new Grid();volumeRow.ColumnDefinitions.Add(new(){Width=new GridLength(52)});volumeRow.ColumnDefinitions.Add(new());volumeRow.Children.Add(UI.Text("Volume",11,UI.Muted));Grid.SetColumn(volume,1);volumeRow.Children.Add(volume);Add(volumeRow);
        var mute=Action("Mute / unmute",()=>app.Audio.ToggleMute());mute.FontSize=11;mute.Padding=new(8,4,8,4);mute.MinHeight=28;Add(mute);
    }
    private Countdown Clock=>kind=="timer"?app.Session.Timer:app.Session.Focus;
    private void BuildClock()
    {
        detail=UI.Text(kind=="focus"?"Focus session":"Countdown",12,UI.Muted);Add(detail);
        time=UI.Text("00:00",38);Typography.SetNumeralAlignment(time,FontNumeralAlignment.Tabular);Add(time);
        progress=new ProgressBar{Minimum=0,Maximum=1,Height=4,Background=UI.Brush("#34363C"),Foreground=Brushes.White};Add(progress);
        minutes=UI.Input(kind=="timer"?"Timer minutes":"Focus minutes",(kind=="timer"?5:app.Settings.FocusMinutes).ToString());minutes.MaxLength=4;Add(UI.Row(UI.Text("Minutes  ",12,UI.Muted),minutes));minutes.Width=80;
        toggle=Action("Start",ToggleClock);Add(UI.Row(toggle,Action("Reset",()=>{Clock.Reset();app.SaveSession();})));
        status=UI.Text("",11,UI.Muted);Add(status);
    }
    private void ToggleClock()
    {
        var now=DateTimeOffset.UtcNow;
        if(Clock.Running)Clock.Pause(now);
        else if(Clock.Remaining(now)>0)Clock.Resume(now);
        else
        {
            if(!int.TryParse(minutes!.Text,out var value) || value<1 || value>1440){status!.Text="Enter 1–1440 minutes.";return;}
            status!.Text="";Clock.Start(value*60,now,kind=="focus"?"work":"");
        }
        app.SaveSession();Refresh();
    }
    private void BuildNotes()
    {
        notes=UI.Input("Shared notes document",app.Productivity.Note);notes.AcceptsReturn=true;notes.TextWrapping=TextWrapping.Wrap;notes.VerticalContentAlignment=VerticalAlignment.Top;notes.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;notes.MaxLength=50000;notes.MinHeight=138;Add(notes);
        notes.TextChanged+=(_,_)=>{if(updating)return;app.Productivity.Note=notes.Text;dirty=true;status!.Text="Saving…";noteSave.Stop();noteSave.Start();};
        status=UI.Text("Saved locally",11,UI.Muted);Add(status);Add(Action("Save",Flush));
    }
    private void SyncNotes()
    {
        if(notes==null || notes.Text==app.Productivity.Note)return;
        int start=notes.SelectionStart,length=notes.SelectionLength;double offset=notes.VerticalOffset;
        notes.Text=app.Productivity.Note;notes.Select(Math.Min(start,notes.Text.Length),Math.Min(length,Math.Max(0,notes.Text.Length-start)));notes.ScrollToVerticalOffset(offset);
    }
    private void BuildShelf()
    {
        Add(UI.Text("Drop files here. Select files to open or drag out.",11,UI.Muted));
        shelf=new ListBox{SelectionMode=SelectionMode.Extended,MinHeight=110,MaxHeight=250,AllowDrop=true};UI.Named(shelf,"File shelf; use Control or Shift to select multiple files");Add(shelf);
        shelf.DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        shelf.Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)app.Productivity.AddFiles(paths);e.Handled=true;};
        shelf.PreviewMouseLeftButtonDown+=(_,e)=>dragStart=e.GetPosition(shelf);
        shelf.PreviewMouseMove+=(_,e)=>{if(dragStart is not Point start || e.LeftButton!=MouseButtonState.Pressed)return;var delta=e.GetPosition(shelf)-start;if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;dragStart=null;DragFiles();};
        shelf.PreviewMouseLeftButtonUp+=(_,_)=>dragStart=null;shelf.MouseDoubleClick+=(_,_)=>OpenFiles();
        shelf.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){OpenFiles();e.Handled=true;}else if(e.Key==Key.Delete){RemoveFiles();e.Handled=true;}};
        Add(UI.Row(Action("Add files",()=>{var dialog=new OpenFileDialog{Multiselect=true,Title="Add files to shelf"};if(dialog.ShowDialog()==true)app.Productivity.AddFiles(dialog.FileNames);}),Action("Open",OpenFiles),Action("Remove",RemoveFiles)));
        Add(Action("Drag selected files",DragFiles));status=UI.Text("",11,UI.Muted);Add(status);
    }
    private ShelfItem[] SelectedFiles()=>shelf!.SelectedItems.Cast<ListBoxItem>().Select(i=>(ShelfItem)i.Tag).ToArray();
    private void OpenFiles(){foreach(var item in SelectedFiles()){if(!item.Exists){status!.Text="Missing file: "+item.Name;continue;}try{ShortcutService.Open(new(){Target=item.Path});}catch(Exception ex){app.Store.Log(ex);status!.Text="Could not open "+item.Name;}}}
    private void RemoveFiles(){foreach(var item in SelectedFiles())app.Productivity.RemoveFile(item.Path);}
    private void DragFiles()
    {
        var files=SelectedFiles().Where(i=>i.Exists).Select(i=>i.Path).ToArray();if(files.Length==0){status!.Text="Select an existing file first.";return;}
        var paths=new StringCollection();paths.AddRange(files);var data=new DataObject();data.SetFileDropList(paths);DragDrop.DoDragDrop(shelf!,data,DragDropEffects.Copy|DragDropEffects.Link);
    }
    private void RefreshShelf()
    {
        var key=string.Join("\n",app.Productivity.Shelf.Select(f=>f.Path+"|"+f.Exists));if(key==shelfKey && shelf!.Items.Count>0)return;shelfKey=key;
        var selected=SelectedFiles().Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);shelf!.Items.Clear();
        foreach(var file in app.Productivity.Shelf){var item=new ListBoxItem{Content=file.Name+(file.Exists?"":" · Missing"),Tag=file,ToolTip=file.Path,Foreground=file.Exists?Brushes.White:UI.Muted};UI.Named(item,file.Name+(file.Exists?"":" missing"));shelf.Items.Add(item);item.IsSelected=selected.Contains(file.Path);}
        status!.Text=shelf.Items.Count==0?"Your shelf is empty.":"Drag copies references; Remove only removes the shelf entry.";
    }
    private void BuildCalendar()
    {
        calendar=new Calendar{SelectedDate=DateTime.Today,DisplayDate=DateTime.Today};
        calendar.SelectedDatesChanged+=(_,_)=>{calendarKey="";RenderMiniMonth(true);RenderEvents();};
        miniMonth=new StackPanel();Add(miniMonth);RenderMiniMonth();eventList=new StackPanel();Add(eventList);
    }
    private StackPanel? miniMonth;
    private DateTime shownMonth=new(DateTime.Today.Year,DateTime.Today.Month,1);
    private void RenderMiniMonth(bool focusSelectedDate=false)
    {
        if(miniMonth==null || calendar==null)return;
        string? focusName=miniMonth.IsKeyboardFocusWithin && Keyboard.FocusedElement is Button focused?AutomationProperties.GetName(focused):null;
        if(focusSelectedDate && focusName?.StartsWith("Select date ",StringComparison.Ordinal)==true && calendar.SelectedDate is DateTime selectedDate)focusName="Select date "+selectedDate.ToString("yyyy-MM-dd");
        miniMonth.Children.Clear();
        void ChangeMonth(int delta){if(delta<0 && shownMonth.Year==1 && shownMonth.Month==1 || delta>0 && shownMonth.Year==9999 && shownMonth.Month==12)return;shownMonth=shownMonth.AddMonths(delta);RenderMiniMonth();}
        var heading=new DockPanel{Margin=new(0,0,0,8)};var next=Action("Next month",()=>ChangeMonth(1));next.Content="›";next.Width=28;next.Padding=new(0);next.MinHeight=28;DockPanel.SetDock(next,Dock.Right);heading.Children.Add(next);
        var previous=Action("Previous month",()=>ChangeMonth(-1));previous.Content="‹";previous.Width=28;previous.Padding=new(0);previous.MinHeight=28;DockPanel.SetDock(previous,Dock.Left);heading.Children.Add(previous);var label=UI.Text(shownMonth.ToString("MMMM yyyy"),13);label.HorizontalAlignment=HorizontalAlignment.Center;heading.Children.Add(label);miniMonth.Children.Add(heading);
        int offset=((int)shownMonth.DayOfWeek+6)%7;int weeks=(offset+DateTime.DaysInMonth(shownMonth.Year,shownMonth.Month)+6)/7;
        var days=new Grid();for(int c=0;c<7;c++)days.ColumnDefinitions.Add(new());for(int r=0;r<=weeks;r++)days.RowDefinitions.Add(new(){Height=new GridLength(r==0?20:27)});
        for(int c=0;c<7;c++){var day=UI.Text(new[]{"M","T","W","T","F","S","S"}[c],10,UI.Muted);day.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetColumn(day,c);days.Children.Add(day);}
        for(int d=1;d<=DateTime.DaysInMonth(shownMonth.Year,shownMonth.Month);d++)
        {
            var date=shownMonth.AddDays(d-1);var button=Action("Select date "+date.ToString("yyyy-MM-dd"),()=>calendar.SelectedDate=date);button.Content=d.ToString();button.Padding=new(0);button.Margin=new(2);button.MinHeight=23;button.FontSize=11;button.Background=calendar.SelectedDate?.Date==date?UI.Brush("#373B45"):Brushes.Transparent;button.BorderThickness=new(date==DateTime.Today?1:0);button.BorderBrush=UI.Muted;button.IsTabStop=calendar.SelectedDate?.Date==date || d==1 && (calendar.SelectedDate?.Year!=shownMonth.Year || calendar.SelectedDate?.Month!=shownMonth.Month);
            button.KeyDown+=(_,e)=>{int delta=e.Key switch{Key.Left=>-1,Key.Right=>1,Key.Up=>-7,Key.Down=>7,_=>0};if(delta==0)return;try{var selected=date.AddDays(delta);shownMonth=new(selected.Year,selected.Month,1);calendar.SelectedDate=selected;}catch(ArgumentOutOfRangeException){}e.Handled=true;};
            int cell=offset+d-1;Grid.SetColumn(button,cell%7);Grid.SetRow(button,cell/7+1);days.Children.Add(button);
        }
        miniMonth.Children.Add(days);
        if(focusName!=null)heading.Children.OfType<Button>().Concat(days.Children.OfType<Button>()).FirstOrDefault(b=>AutomationProperties.GetName(b)==focusName)?.Focus();
    }

    private void RenderEvents()
    {
        if(eventList==null || calendar==null)return;
        var date=calendar.SelectedDate??DateTime.Today;var now=DateTimeOffset.Now;
        var selected=app.Calendar.Events.Where(e=>e.Start.LocalDateTime.Date<=date && e.End.LocalDateTime.Date>=date).OrderBy(e=>e.Start).ToArray();
        var upcoming=app.Calendar.Events.Where(e=>e.End>now).OrderBy(e=>e.Start).Take(5).ToArray();
        string key=date.ToString("O")+string.Join("|",selected.Concat(upcoming).Select(e=>e.Id+e.Title+e.Start+e.End+e.MeetingUrl));if(key==calendarKey)return;calendarKey=key;eventList.Children.Clear();
        eventList.Children.Add(UI.Label(date.ToString("ddd, d MMM")));if(selected.Length==0)eventList.Children.Add(UI.Text("No events on this day",12,UI.Muted));foreach(var item in selected)AddEvent(item);
        var label=UI.Label("Upcoming");label.Margin=new(0,12,0,8);eventList.Children.Add(label);if(upcoming.Length==0)eventList.Children.Add(UI.Text("No upcoming events",12,UI.Muted));foreach(var item in upcoming)AddEvent(item);
    }
    private void AddEvent(CalendarEvent item)
    {
        var row=new StackPanel{Margin=new(0,0,0,10)};row.Children.Add(UI.Text(item.Title,13));row.Children.Add(UI.Text(item.Start.ToLocalTime().ToString("ddd, d MMM · HH:mm"),11,UI.Muted));
        var actions=UI.Row(Action("Event details",openMain));if(item.MeetingUrl.Length>0)actions.Children.Add(Action("Join meeting",()=>{try{ShortcutService.Open(new(){Target=item.MeetingUrl});}catch(Exception ex){app.Store.Log(ex);app.Notify("Could not open meeting",ex.Message);}}));row.Children.Add(actions);eventList!.Children.Add(row);
    }
    private void BuildSystem()
    {
        title=UI.Text("CPU",14);Add(title);progress=new ProgressBar{Minimum=0,Maximum=100,Height=5,Background=UI.Brush("#34363C"),Foreground=Brushes.White};Add(progress);detail=UI.Text("RAM",14);Add(detail);memory=new ProgressBar{Minimum=0,Maximum=100,Height=5,Background=UI.Brush("#34363C"),Foreground=Brushes.White};Add(memory);Add(UI.Text("Uses the island’s shared system sample.",11,UI.Muted));
    }
    private void Refresh()
    {
        if(!visible)return;updating=true;
        try
        {
            if(kind=="media")
            {
                var media=app.Media;title!.Text=media.Title;detail!.Text=media.Artist;artwork!.Source=media.Artwork;artwork.Visibility=media.Artwork==null?Visibility.Collapsed:Visibility.Visible;
                var key=string.Join("|",media.Sessions.Select(s=>s.SourceAppUserModelId));if(key!=sessionKey || sessions!.Items.Count!=media.Sessions.Count){sessionKey=key;sessions!.Items.Clear();foreach(var session in media.Sessions)sessions.Items.Add(MediaService.SessionName(session));}
                sessions!.Visibility=media.Sessions.Count>1?Visibility.Visible:Visibility.Collapsed;
                sessions!.SelectedIndex=media.Sessions.FindIndex(s=>s.SourceAppUserModelId==media.Selected?.SourceAppUserModelId);
                previous!.IsEnabled=media.CanPrevious;next!.IsEnabled=media.CanNext;toggle!.IsEnabled=media.CanToggle;toggle.Content=media.Playing?"Pause":"Play";
                volume!.IsEnabled=app.Audio.Available;if(!volume.IsMouseCaptureWithin && !volume.IsKeyboardFocusWithin)volume.Value=app.Audio.Volume*100;
                var timeline=media.Timeline();seek!.IsEnabled=media.CanSeek && timeline.duration>0;if(!seek.IsMouseCaptureWithin && !seek.IsKeyboardFocusWithin)seek.Value=timeline.duration>0?timeline.elapsed/timeline.duration:0;
            }
            else if(kind is "timer" or "focus")
            {
                var now=DateTimeOffset.UtcNow;double remaining=Clock.Remaining(now);time!.Text=remaining>0?Clock.Display(now):int.TryParse(minutes!.Text,out int ready) && ready>=1 && ready<=1440?new Countdown{RemainingSeconds=ready*60}.Display(now):"—";progress!.Value=remaining>0?Clock.Progress(now):0;toggle!.Content=Clock.Running?"Pause":Clock.Remaining(now)>0?"Resume":"Start";minutes!.IsEnabled=!Clock.Running && Clock.Remaining(now)<=0;detail!.Text=kind=="focus"?(Clock.Phase=="break"?"Break":"Focus session"):"Countdown";
            }
            else if(kind=="notes")SyncNotes();else if(kind=="files")RefreshShelf();else if(kind=="calendar")RenderEvents();
            else if(kind=="system")
            {
                var usage=app.Usage;title!.Text=double.IsFinite(usage.cpu)?$"CPU  {usage.cpu:0}%":"CPU unavailable";progress!.Value=double.IsFinite(usage.cpu)?Math.Clamp(usage.cpu,0,100):0;
                detail!.Text=usage.total>0?$"RAM  {usage.used:0.0} / {usage.total:0.0} GB":"RAM unavailable";memory!.Value=usage.total>0?Math.Clamp(usage.used/usage.total*100,0,100):0;
            }
        }
        finally{updating=false;}
    }
}
