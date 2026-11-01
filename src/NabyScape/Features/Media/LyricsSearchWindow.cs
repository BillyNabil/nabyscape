using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;
internal sealed class LyricsSearchWindow : Window
{
    private readonly CancellationTokenSource lifetime=new();
    internal LyricsSearchWindow(App app)
    {
        Title="Choose lyrics provider";Width=560;Height=520;MinWidth=360;MinHeight=320;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=UI.Brush("#101114");Foreground=Brushes.White;
        string title=app.Media.Title,artist=app.Media.Artist;double duration=app.Media.Timeline().duration;
        var panel=new StackPanel{Margin=new(20)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(UI.Heading("Choose lyrics provider",21));
        panel.Children.Add(UI.Text("Tap a provider to fetch lyrics, then choose a synced version.",12,UI.Muted));
        int selectedSource=app.Lyrics.Result.Status.Contains("BiniLyrics",StringComparison.Ordinal)?1:app.Lyrics.Result.Status.Contains("custom API",StringComparison.Ordinal)?2:0;Button? search=null;
        var endpoint=UI.Input("Compatible API base URL","");endpoint.MaxLength=2048;endpoint.Visibility=Visibility.Collapsed;
        string[] sources={"LRCLIB","BiniLyrics","Custom LRCLIB-compatible API"};
        for(int i=0;i<sources.Length;i++)
        {
            int selected=i;var labels=new StackPanel();labels.Children.Add(UI.Text(sources[i],14));labels.Children.Add(UI.Text(i==selectedSource && app.Lyrics.Result.Lines.Length>0?"Current provider":i==2?"Enter your server URL":"Tap to fetch",11,UI.Muted));
            var row=new RadioButton{Content=labels,GroupName="LyricsProviders",IsChecked=i==selectedSource,Padding=new(12),Margin=new(0,8,0,0),HorizontalContentAlignment=HorizontalAlignment.Stretch};UI.Named(row,"Fetch lyrics from "+sources[i]);
            row.Click+=(_,_)=>{selectedSource=selected;endpoint.Visibility=selected==2?Visibility.Visible:Visibility.Collapsed;if(selected!=2)search?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};panel.Children.Add(row);
        }
        panel.Children.Add(endpoint);
        var query=UI.Input("Song title",title);query.MaxLength=300;panel.Children.Add(query);
        var singer=UI.Input("Artist",artist);singer.MaxLength=300;panel.Children.Add(singer);
        var status=UI.Text("Search sends the title and artist to your selected source.",12,UI.Muted);status.Margin=new(0,10,0,10);
        var results=new StackPanel();
        search=UI.Button("Search synced lyrics",async()=>
        {
            if(!search!.IsEnabled)return;search.IsEnabled=false;foreach(var row in panel.Children){if(row is RadioButton radio)radio.IsEnabled=false;}results.Children.Clear();status.Text="Searching…";
            try
            {
                Uri? custom=selectedSource==2?new Uri(endpoint.Text.Trim(),UriKind.Absolute):null;
                int requestedSource=selectedSource;
                var choices=requestedSource==1?await app.Lyrics.SearchBini(query.Text.Trim(),singer.Text.Trim(),lifetime.Token):await app.Lyrics.Search(query.Text.Trim(),singer.Text.Trim(),lifetime.Token,custom);
                if(lifetime.IsCancellationRequested)return;
                status.Text=choices.Length==0?"No synced results. Try a shorter title, another artist spelling, or Local LRC.":$"{choices.Length} synced versions · select one to apply";
                foreach(var choice in choices)
                {
                    string label=choice.Title+" · "+choice.Artist+"\n"+choice.Album+$" · {choice.Duration:0}s";
                    var select=UI.Button(label,()=>
                    {
                        if(app.Media.Title!=title || app.Media.Artist!=artist || Math.Round(app.Media.Timeline().duration)!=Math.Round(duration)){status.Text="The track changed. Reopen Find lyrics for the current song.";return;}
                        try{app.Lyrics.Import(title,artist,duration,choice.Lrc,requestedSource==1?"Selected lyrics · BiniLyrics":custom==null?"Selected lyrics · LRCLIB":"Selected lyrics · custom API");Close();}catch(Exception ex){status.Text=ex.Message;}
                    });
                    select.Content=UI.Text(label,12);select.Margin=new(0,0,0,8);results.Children.Add(select);
                }
            }
            catch(OperationCanceledException){if(!lifetime.IsCancellationRequested)status.Text="Search timed out. Try again.";}
            catch(Exception ex){if(!lifetime.IsCancellationRequested)status.Text="Could not search: "+ex.Message;}
            finally{if(!lifetime.IsCancellationRequested){search!.IsEnabled=true;foreach(var row in panel.Children){if(row is RadioButton radio)radio.IsEnabled=true;}}}
        });
        panel.Children.Add(search);panel.Children.Add(status);panel.Children.Add(results);
        Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();};
    }
}
