using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace NabyScape;
internal sealed class LyricsSourcesControl : StackPanel
{
    private static int openPopups;
    internal static bool MenuOpen=>System.Threading.Volatile.Read(ref openPopups)>0;
    private readonly App app;
    private readonly Popup popup=new(){StaysOpen=false,AllowsTransparency=true,Placement=PlacementMode.Bottom,PopupAnimation=PopupAnimation.None};
    private readonly StackPanel menu=new();
    private readonly Button trigger;
    private readonly Action retry,local;
    private CancellationTokenSource? pending;
    internal LyricsSourcesControl(App app,Action retry,Action local)
    {
        this.app=app;this.retry=retry;this.local=local;
        trigger=UI.Button(Localization.Text("Choose lyrics provider"),()=>{if(popup.IsOpen){popup.IsOpen=false;return;}ShowSources();popup.IsOpen=true;});
        var musicIcon=Icons.Create("music",13);musicIcon.Margin=new(0,0,6,0);var chevron=Icons.Create("chevron",8,UI.Muted);chevron.Margin=new(7,0,0,0);trigger.Content=UI.Row(musicIcon,UI.Text(Localization.Text("Lyrics"),11),chevron);trigger.MinHeight=28;trigger.Padding=new(8,4,8,4);trigger.Margin=new(0);trigger.HorizontalAlignment=HorizontalAlignment.Left;
        trigger.Background=UI.Brush("#24262B");Children.Add(trigger);
        var viewport=new ScrollViewer{Content=menu,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=330};
        var border=new Border{Child=viewport,Background=UI.Brush("#111216"),BorderBrush=UI.Brush("#53565D"),BorderThickness=new(.7),CornerRadius=new(10),Padding=new(8),Width=264};
        UI.Named(border,Localization.Text("Lyrics source popover"));popup.Child=border;popup.PlacementTarget=trigger;popup.VerticalOffset=6;
        popup.Opened+=(_,_)=>{System.Threading.Interlocked.Increment(ref openPopups);border.Opacity=Motion.Enabled?0:1;Motion.Fade(border,1,160);border.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));};
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(border,System.Windows.Input.KeyboardNavigationMode.Cycle);
        border.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){popup.IsOpen=false;trigger.Focus();e.Handled=true;}};
        popup.Closed+=(_,_)=>{System.Threading.Interlocked.Decrement(ref openPopups);pending?.Cancel();menu.Children.Clear();AutomationPropertiesStatus(false);};Unloaded+=(_,_)=>{popup.IsOpen=false;pending?.Cancel();};
        viewport.PreviewMouseWheel+=(_,e)=>{viewport.ScrollToVerticalOffset(viewport.VerticalOffset-e.Delta*.35);e.Handled=true;};
    }
    private void AutomationPropertiesStatus(bool open)=>System.Windows.Automation.AutomationProperties.SetItemStatus(trigger,open?Localization.Text("Expanded"):Localization.Text("Collapsed"));
    private void Heading(string text){var label=UI.Text(text,11,UI.Muted);label.Margin=new(4,2,4,8);menu.Children.Add(label);}
    private Button Row(string name,string detail,Action click,bool selected=false)
    {
        var button=UI.Button(name,click);button.Margin=new(0,1,0,1);button.Padding=new(7,6,7,6);button.Background=selected?UI.Brush("#303238"):Brushes.Transparent;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        var row=new DockPanel();var check=UI.Text(selected?"✓":"",13);check.Width=20;DockPanel.SetDock(check,Dock.Right);row.Children.Add(check);
        var labels=new StackPanel();labels.Children.Add(UI.Text(name,12));if(detail.Length>0)labels.Children.Add(UI.Text(detail,10,UI.Muted));row.Children.Add(labels);button.Content=row;menu.Children.Add(button);return button;
    }
    private void ShowSources()
    {
        pending?.Cancel();menu.Children.Clear();Heading(Localization.Text("Lyrics source"));AutomationPropertiesStatus(true);
        Row(Localization.Text("Lyric text"),app.Settings.LyricsTextMode=="Romaji"?Localization.Text("Japanese Romaji"):Localization.Text("Original script"),ShowTextChoices);
        menu.Children.Add(new Separator{Margin=new(0,6,0,6)});
        Row(Localization.Text("Automatic"),Localization.Text("All languages"),()=>{popup.IsOpen=false;app.Lyrics.SelectedSource="Automatic";retry();},app.Lyrics.SelectedSource=="Automatic");
        menu.Children.Add(new Separator{Margin=new(0,6,0,6)});
        foreach(string source in LyricsProviders.Names)
        {
            bool key=LyricsProviders.NeedsKey(source);bool connector=LyricsProviders.NeedsConnector(source);bool video=LyricsProviders.NeedsVideo(source);
            bool ready=video?app.Lyrics.HasVideo(app.Media.Title,app.Media.Artist):connector?app.Lyrics.HasConnector(source):!key || new AssistantKeys().Exists(LyricsProviders.KeyFor(source));
            Row(Localization.Text(source),ready?"":video?Localization.Text("Add video link"):key?Localization.Text("Connect API key"):Localization.Text("Connect lyrics server"),()=>{if(!ready){Configure(source,key);return;}_=Fetch(source);},app.Lyrics.SelectedSource==source);
        }
        menu.Children.Add(new Separator{Margin=new(0,6,0,6)});
        Row(Localization.Text("Retry lyrics"),"",()=>{popup.IsOpen=false;retry();});Row(Localization.Text("Local LRC…"),"",()=>{popup.IsOpen=false;local();});
        Row(Localization.Text("Custom API…"),Localization.Text("LRCLIB-compatible server"),()=>{popup.IsOpen=false;new LyricsSearchWindow(app){Owner=Window.GetWindow(this)}.ShowDialog();});
    }
    private void Configure(string source,bool key)
    {
        bool video=LyricsProviders.NeedsVideo(source);menu.Children.Clear();Heading(Localization.Text(source));var status=UI.Text(video?Localization.Text("Paste the YouTube link for this track. Windows media metadata does not contain its video ID."):key?Localization.Text("Save your ")+(source=="Musixmatch"?Localization.Text("Musixmatch developer"):"PaxSenix")+Localization.Text(" API key in Windows Credential Manager."):Localization.Text("This source needs an authorized lyrics connector with a LRCLIB-compatible /api/search endpoint. Native account login is not included."),11,UI.Muted);status.Margin=new(4,0,4,10);menu.Children.Add(status);
        var secret=new PasswordBox{MaxLength=2560,Margin=new(4,0,4,8)};var endpoint=UI.Input(video?Localization.Text("YouTube video link"):Localization.Text("HTTPS connector base URL"),"");
        if(key){UI.Named(secret,source+Localization.Text(" API key"));menu.Children.Add(secret);}else menu.Children.Add(endpoint);
        Row(Localization.Text("Save connection"),"",()=>
        {
            try{if(key){new AssistantKeys().Write(LyricsProviders.KeyFor(source),secret.Password);secret.Clear();}else if(video)app.Lyrics.ConfigureVideo(endpoint.Text,app.Media.Title,app.Media.Artist);else app.Lyrics.ConfigureConnector(source,new Uri(endpoint.Text.Trim(),UriKind.Absolute));ShowSources();}
            catch(Exception ex){status.Text=ex.Message;}
        });
        if(key)Row(Localization.Text("Remove saved key"),"",()=>{try{new AssistantKeys().Remove(LyricsProviders.KeyFor(source));ShowSources();}catch(Exception ex){status.Text=ex.Message;}});
        Row(Localization.Text("Back"),"",ShowSources);
    }
    private void ShowTextChoices()
    {
        menu.Children.Clear();Heading(Localization.Text("Lyric text"));
        void Choose(string mode){app.Settings.LyricsTextMode=mode;app.Lyrics.SetTextMode(mode);app.ChangeSettings();popup.IsOpen=false;}
        Row(Localization.Text("Original"),Localization.Text("Keep the original writing"),()=>Choose("Original"),app.Settings.LyricsTextMode=="Original");
        Row(Localization.Text("Japanese Romaji"),Localization.Text("Japanese lyrics in Latin letters · offline"),()=>Choose("Romaji"),app.Settings.LyricsTextMode=="Romaji");
        var note=UI.Text(Localization.Text("Automatic readings may differ for names or creative kanji. Timing stays unchanged."),11,UI.Muted);note.Margin=new(7,8,7,8);menu.Children.Add(note);
        Row(Localization.Text("Back to sources"),"",ShowSources);
    }
    private async Task Fetch(string source)
    {
        pending?.Cancel();pending?.Dispose();var operation=new CancellationTokenSource();pending=operation;
        string title=app.Media.Title,artist=app.Media.Artist;double duration=app.Media.Timeline().duration;
        menu.Children.Clear();Heading(Localization.Text(source));var status=UI.Text(Localization.Text("Fetching lyrics…"),12,UI.Muted);status.Margin=new(5);menu.Children.Add(status);Row(Localization.Text("Back"),"",ShowSources);
        try
        {
            var choices=await app.Lyrics.FetchProvider(source,title,artist,duration,operation.Token);
            if(operation.IsCancellationRequested || !popup.IsOpen)return;
            menu.Children.Clear();Heading(Localization.Text(source));Row(Localization.Text("Back to sources"),"",ShowSources);
            if(choices.Length==0){var empty=UI.Text(Localization.Text("No synced lyrics found. Try another source."),12,UI.Muted);empty.Margin=new(5);menu.Children.Add(empty);}
            foreach(var choice in choices.Take(30))Row(choice.Title+" · "+choice.Artist,choice.Album,()=>
            {
                if(app.Media.Title!=title || app.Media.Artist!=artist || Math.Round(app.Media.Timeline().duration)!=Math.Round(duration)){menu.Children.Clear();Heading(Localization.Text("Track changed — fetch again"));Row(Localization.Text("Sources"),"",ShowSources);return;}
                try{app.Lyrics.SelectedSource=source;if(choice.Plain.Length>0)app.Lyrics.ImportPlain(title,artist,duration,choice.Plain,Localization.Text("Plain lyrics · ")+source+" · "+choice.Album);else app.Lyrics.Import(title,artist,duration,choice.Lrc,Localization.Text("Selected lyrics · ")+source);popup.IsOpen=false;}
                catch(Exception ex){status.Text=ex.Message;}
            });
        }
        catch(OperationCanceledException){if(!operation.IsCancellationRequested)status.Text=Localization.Text("Request timed out. Try another source.");}
        catch(Exception ex){if(!operation.IsCancellationRequested)status.Text=ex.Message;}
    }
}
