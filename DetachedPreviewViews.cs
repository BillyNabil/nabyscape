using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        if(value){app.Updated+=Refresh;app.Productivity.Changed+=DataChanged;app.Calendar.Changed+=DataChanged;Refresh();}
        else{app.Updated-=Refresh;app.Productivity.Changed-=DataChanged;app.Calendar.Changed-=DataChanged;Flush();}
    }
    internal void ApplySettings(){if(visible)Refresh();}
    private void DataChanged(){if(!visible)return;Refresh();}
    internal void Flush(){noteSave.Stop();if(dirty){dirty=!app.Productivity.Save();if(status!=null)status.Text=dirty?"Could not save. Try Save again.":"Saved locally";}}
    public void Dispose(){SetVisible(false);Flush();noteSave.Stop();}
    private void Add(FrameworkElement element){element.Margin=new(0,0,0,9);panel.Children.Add(element);}
    private Button Action(string label,Action action){var button=UI.Button(label,action);button.Margin=new(0,0,6,0);return button;}
    private void BuildMusic()
    {
        artwork=new Image{Height=86,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};UI.Named(artwork,"Current album artwork");Add(artwork);
        title=UI.Text("Nothing playing",17);title.FontWeight=FontWeights.SemiBold;Add(title);detail=UI.Text("",12,UI.Muted);Add(detail);
        sessions=new ComboBox{MinHeight=28};UI.Named(sessions,"Media session");sessions.SelectionChanged+=(_,_)=>{if(!updating && sessions.SelectedIndex>=0 && sessions.SelectedIndex<app.Media.Sessions.Count)app.Media.Select(app.Media.Sessions[sessions.SelectedIndex]);};Add(sessions);
        previous=Action("Previous",()=>_=app.Media.Command("previous"));toggle=Action("Play",()=>_=app.Media.Command("toggle"));next=Action("Next",()=>_=app.Media.Command("next"));Add(UI.Row(previous,toggle,next));
        seek=new Slider{Minimum=0,Maximum=1};UI.Named(seek,"Track position");seek.ValueChanged+=(_,_)=>{if(!updating)_=app.Media.Seek(seek.Value);};Add(seek);
        volume=new Slider{Minimum=0,Maximum=100};UI.Named(volume,"Output volume");volume.ValueChanged+=(_,_)=>{if(!updating)app.Audio.SetVolume(volume.Value/100);};Add(UI.Row(UI.Text("Volume",11,UI.Muted),volume));volume.Width=170;
        Add(Action("Mute / unmute",()=>app.Audio.ToggleMute()));
    }
    private Countdown Clock=>kind=="timer"?app.Session.Timer:app.Session.Focus;
    private void BuildClock()
    {
        detail=UI.Text(kind=="focus"?"Focus session":"Countdown",12,UI.Muted);Add(detail);
        time=UI.Text("00:00",38);Typography.SetNumeralAlignment(time,FontNumeralAlignment.Tabular);Add(time);
        progress=new ProgressBar{Minimum=0,Maximum=1,Height=4,Foreground=Brushes.White};Add(progress);
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
        calendar=new Calendar{SelectedDate=DateTime.Today,DisplayDate=DateTime.Today};UI.Named(calendar,"Calendar month and event day");calendar.SelectedDatesChanged+=(_,_)=>{calendarKey="";RenderEvents();};Add(calendar);
        eventList=new StackPanel();Add(eventList);
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
        title=UI.Text("CPU",14);Add(title);progress=new ProgressBar{Minimum=0,Maximum=100,Height=5};Add(progress);detail=UI.Text("RAM",14);Add(detail);memory=new ProgressBar{Minimum=0,Maximum=100,Height=5};Add(memory);Add(UI.Text("Uses the island’s shared system sample.",11,UI.Muted));
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
                sessions!.SelectedIndex=media.Sessions.FindIndex(s=>s.SourceAppUserModelId==media.Selected?.SourceAppUserModelId);
                previous!.IsEnabled=media.CanPrevious;next!.IsEnabled=media.CanNext;toggle!.IsEnabled=media.CanToggle;toggle.Content=media.Playing?"Pause":"Play";
                volume!.IsEnabled=app.Audio.Available;if(!volume.IsMouseCaptureWithin)volume.Value=app.Audio.Volume*100;
                var timeline=media.Timeline();seek!.IsEnabled=media.CanSeek && timeline.duration>0;if(!seek.IsMouseCaptureWithin)seek.Value=timeline.duration>0?timeline.elapsed/timeline.duration:0;
            }
            else if(kind is "timer" or "focus")
            {
                var now=DateTimeOffset.UtcNow;time!.Text=Clock.Display(now);progress!.Value=Clock.Progress(now);toggle!.Content=Clock.Running?"Pause":Clock.Remaining(now)>0?"Resume":"Start";minutes!.IsEnabled=!Clock.Running && Clock.Remaining(now)<=0;detail!.Text=kind=="focus"?(Clock.Phase=="break"?"Break":"Focus session"):"Countdown";
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

