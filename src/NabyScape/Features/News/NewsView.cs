using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NabyScape;

// Content-first summary: readable headlines, real source avatars, one quiet footer.
internal sealed class NewsView : UserControl
{
    private readonly App app;
    private readonly Action<Uri> openLink;
    private readonly Grid root=new(){ClipToBounds=true};
    private readonly StackPanel stories=new(),connection=new(),reading=new();
    private readonly TextBlock status=UI.Text("",11,UI.Muted),edition=UI.Caption("News summary",14),updated=UI.Text("",11,UI.Muted);
    private readonly Button refresh,allNewsButton;
    private readonly ScrollViewer storyScroll;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMinutes(1)};
    private readonly List<(Image Image,string Path)> avatars=new();
    private CancellationTokenSource? request;
    private bool busy;
    private bool? narrowFooter;
    private string configuration="";
    private Border? poster;
    private Image? cover;
    internal event Action? ContentSizeChanged;
    internal bool Busy=>busy;
    internal NewsSnapshot? Shown {get;private set;}
    internal Action<Uri>? LinkObserver {get;set;}
    private void Navigate(Uri uri){if(LinkObserver!=null)LinkObserver(uri);else openLink(uri);}
    internal void FitTo(double height){MaxHeight=Math.Max(120,height);if(poster!=null)poster.Height=PosterHeight;}
    private double PosterHeight=>Math.Clamp((root.ActualWidth>0?root.ActualWidth:300)*.30,76,96);

    internal NewsView(App app,Action<Uri>? open=null)
    {
        this.app=app;openLink=open??OpenBrowser;
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        edition.FontWeight=FontWeights.SemiBold;
        var header=new Grid{Margin=new(0,0,0,12)};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        header.Children.Add(edition);updated.Margin=new(0,0,6,0);Grid.SetColumn(updated,1);header.Children.Add(updated);
        refresh=NewsIcons.Action("refresh","Refresh news",()=>_=Reload(true));Grid.SetColumn(refresh,2);header.Children.Add(refresh);root.Children.Add(header);
        status.Margin=new(0,0,0,10);status.Visibility=Visibility.Collapsed;reading.Children.Add(status);reading.Children.Add(connection);reading.Children.Add(stories);
        storyScroll=UI.Scroll(reading);storyScroll.ClipToBounds=true;storyScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;storyScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;UI.LocalizedName(storyScroll,"Anyway News summary");Grid.SetRow(storyScroll,1);root.Children.Add(storyScroll);
        // Keep the overflow affordance visible, including before the first scroll.
        ScrollChrome.SetEnabled(storyScroll,false);storyScroll.Loaded+=(_,_)=>{if(storyScroll.Template.FindName("PART_VerticalScrollBar",storyScroll) is System.Windows.Controls.Primitives.ScrollBar bar)bar.Opacity=.55;};
        var footer=new Grid{Margin=new(0,12,0,0)};footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var logo=new Image{Width=12,Height=12,Source=new BitmapImage(new Uri("pack://application:,,,/Assets/anyway-logo.jpg")),Stretch=Stretch.Uniform};ThemeInk.SetTone(logo,Tone.Fixed);
        var signature=UI.Text("by Anyway News",11,UI.Muted);signature.Margin=new(5,0,0,0);
        var brand=UI.CaptionButton("Open Anyway News",()=>Navigate(NewsService.AppLink));brand.Content=UI.Row(logo,signature);brand.Background=Brushes.Transparent;brand.BorderThickness=new(0);brand.Padding=new(0);brand.MinHeight=30;brand.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(brand);
        // Full reading continues in the mini app.
        allNewsButton=UI.CaptionButton("Open all news in the mini app",()=>Navigate(NewsService.AppLink));allNewsButton.Background=Brushes.Transparent;allNewsButton.BorderThickness=new(0);allNewsButton.Padding=new(4,0,6,0);allNewsButton.MinHeight=30;allNewsButton.Tag=new CornerRadius(7);
        var settings=NewsIcons.Action("settings","News connection",()=>{connection.Visibility=connection.IsVisible?Visibility.Collapsed:Visibility.Visible;storyScroll.ScrollToTop();});
        var actions=UI.Row(allNewsButton,settings);Grid.SetColumn(actions,1);footer.Children.Add(actions);Grid.SetRow(footer,2);root.Children.Add(footer);
        BuildConnection();
        var card=new Border{Child=root,Background=UI.Brush("#15171B"),BorderBrush=UI.Brush("#2B2F36"),BorderThickness=new(.7),CornerRadius=new(16),Padding=new(14,12,14,12)};
        card.SizeChanged+=(_,_)=>card.Clip=new RectangleGeometry(new Rect(card.RenderSize),16,16);Content=card;
        root.SizeChanged+=(_,_)=>{if(poster!=null)poster.Height=PosterHeight;ArrangeFooter();ContentSizeChanged?.Invoke();};ArrangeFooter();
        Loaded+=(_,_)=>{configuration=Configuration;app.SettingsChanged-=Preferences;app.SettingsChanged+=Preferences;if(IsVisible){timer.Start();_=Reload();}};
        IsVisibleChanged+=(_,_)=>{if(IsVisible && IsLoaded){timer.Start();_=Reload();}else Stop();};
        Unloaded+=(_,_)=>{app.SettingsChanged-=Preferences;Stop();};timer.Tick+=(_,_)=>{if(!busy && IsVisible)_=Reload();};
    }
    private void ArrangeFooter()
    {
        // The brand already opens the same app; keep it intact when space is tight.
        double width=root.ActualWidth/Accessibility.TextScale;
        allNewsButton.Visibility=width>0 && width<220?Visibility.Collapsed:Visibility.Visible;
        bool narrow=width>0 && width<250;if(narrowFooter==narrow)return;narrowFooter=narrow;
        var icon=NewsIcons.Create("open",16);icon.Margin=new(narrow?0:6,0,0,0);
        allNewsButton.Content=narrow?icon:UI.Row(UI.Caption("All news",11,UI.Muted),icon);
    }
    private void Stop(){timer.Stop();request?.Cancel();}
    private string Configuration=>app.Settings.NewsConnected+"|"+app.Settings.NewsApiBaseUrl;
    private void Preferences(){if(configuration==Configuration)return;configuration=Configuration;BuildConnection();if(IsVisible && IsLoaded)_=Reload();}
    private void OpenBrowser(Uri uri)
    {try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException){Message(Localization.Text("Could not open Anyway News."));}}
    private void Message(string text){status.Text=text;status.Visibility=Visibility.Visible;}
    private void BuildConnection()
    {
        connection.Children.Clear();connection.Visibility=app.Settings.NewsConnected?Visibility.Collapsed:Visibility.Visible;connection.Margin=new(0,0,0,12);
        connection.Children.Add(UI.Caption("API address",11,UI.Muted));var input=UI.Input(Localization.Text("News API address"),app.Settings.NewsApiBaseUrl);input.Margin=new(0,6,0,8);connection.Children.Add(input);
        connection.Children.Add(UI.Caption("Connect to load the public news summary. No account or token is required.",11,UI.Muted));
        var connect=UI.CaptionButton("Connect",()=>{try{app.Settings.NewsApiBaseUrl=NewsService.BaseUri(input.Text).AbsoluteUri.TrimEnd('/');app.Settings.NewsConnected=true;app.ChangeSettings();}catch(ArgumentException e){Message(e.Message);}});connect.Margin=new(0,8,0,0);connection.Children.Add(connect);
        if(app.Settings.NewsConnected)connection.Children.Add(UI.CaptionButton("Disconnect",()=>{app.Settings.NewsConnected=false;app.ChangeSettings();}));
    }
    internal async Task Reload(bool force=false)
    {
        request?.Cancel();request?.Dispose();request=new();var current=request;
        if(!app.Settings.NewsConnected){busy=false;Shown=null;stories.Children.Clear();Message(Localization.Text("Connect Anyway News to load the summary."));refresh.IsEnabled=false;return;}
        string address=app.Settings.NewsApiBaseUrl;busy=true;refresh.IsEnabled=false;
        var saved=app.News.Saved(address);if(!ReferenceEquals(Shown,saved))Render(saved);if(saved==null)Message(Localization.Text("Loading news…"));
        try
        {
            var snapshot=await app.News.Get(address,force,current.Token);if(current!=request || !IsLoaded || !IsVisible)return;
            if(!ReferenceEquals(Shown,snapshot))Render(snapshot);updated.Text=snapshot.FetchedAt.ToLocalTime().ToString("HH:mm");status.Visibility=Visibility.Collapsed;
            if(snapshot.Items.FirstOrDefault() is {} first && cover is {Source:null} target)
            {
                try{var bytes=await app.News.Cover(address,first.Cover,current.Token);if(bytes!=null && current==request && ReferenceEquals(cover,target))target.Source=Decode(bytes,800);}
                catch(Exception ex) when(ex is OperationCanceledException or System.Net.Http.HttpRequestException or IOException or NotSupportedException or FormatException or System.Runtime.InteropServices.COMException){/* A failed image does not remove the story. */}
            }
            await Task.WhenAll(avatars.Where(slot=>slot.Image.Source==null).ToArray().Select(slot=>LoadAvatar(slot.Image,slot.Path,address,current)));
        }
        catch(OperationCanceledException){if(current==request && IsVisible)Message(Localization.Text("The news request timed out. Try refreshing."));}
        catch(Exception ex) when(ex is System.Net.Http.HttpRequestException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            if(current==request && IsVisible)
            {
                string detail=ex switch {
                    System.Net.Http.HttpRequestException {StatusCode:{} code}=>Localization.Format("News API is unavailable ({0}). Try again later.",(int)code),
                    System.Net.Http.HttpRequestException=>Localization.Text("News could not be loaded. Check your connection and try again."),
                    System.Text.Json.JsonException=>Localization.Text("The news API returned an unknown format."),
                    _=>ex.Message
                };
                Message(saved!=null?Localization.Format("Showing saved news. {0}",detail):detail);
            }
        }
        finally{if(current==request){busy=false;refresh.IsEnabled=app.Settings.NewsConnected;_ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>ContentSizeChanged?.Invoke());}}
    }
    private static BitmapImage Decode(byte[] bytes,int width)
    {using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=width;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;}
    internal void Render(NewsSnapshot? snapshot)
    {
        Shown=snapshot;stories.Children.Clear();avatars.Clear();poster=null;cover=null;edition.Text=Localization.Text("News summary");
        if(snapshot==null)return;
        if(snapshot.Items.Length==0){stories.Children.Add(UI.Caption("No news summary is available yet.",13,UI.Muted));return;}
        stories.Children.Add(LeadStory(snapshot.Items[0]));
        foreach(var item in snapshot.Items.Skip(1).Take(3))stories.Children.Add(CompactStory(item));
    }
    private FrameworkElement LeadStory(NewsItem item)
    {
        var lead=new StackPanel{Margin=new(0,0,0,12)};
        if(item.Cover.Length>0)
        {
            cover=new Image{Stretch=Stretch.UniformToFill};RenderOptions.SetBitmapScalingMode(cover,BitmapScalingMode.HighQuality);ThemeInk.SetTone(cover,Tone.Fixed);
            var frame=new Border{Child=cover,Height=PosterHeight,Background=UI.Brush("#252A32"),CornerRadius=new(10),Margin=new(0,0,0,10)};poster=frame;
            frame.SizeChanged+=(_,_)=>frame.Clip=new RectangleGeometry(new Rect(frame.RenderSize),10,10);lead.Children.Add(frame);
        }
        var title=UI.Text(item.Title,15);title.FontWeight=FontWeights.SemiBold;title.LineStackingStrategy=LineStackingStrategy.MaxHeight;title.LineHeight=20;title.Margin=new(0,0,0,8);lead.Children.Add(title);
        var meta=new Grid();meta.ColumnDefinitions.Add(new(){Width=GridLength.Auto});meta.ColumnDefinitions.Add(new());meta.Children.Add(AvatarGroup(item));
        var sources=UI.Text(item.SourceCount>0?Localization.Format("Sources: {0}",item.SourceCount):item.Source,11,UI.Muted);sources.Margin=new(8,0,0,0);Grid.SetColumn(sources,1);meta.Children.Add(sources);lead.Children.Add(meta);
        foreach(var note in item.Notes){var label=UI.Text(note,11,UI.Muted);label.Margin=new(0,6,0,0);lead.Children.Add(label);}
        return new Border{Child=StoryButton(item,lead),BorderBrush=UI.Brush("#2B2F36"),BorderThickness=new(0,0,0,.5)};
    }
    private Button StoryButton(NewsItem item,FrameworkElement content)
    {var button=UI.Button(item.Title,()=>{if(item.Url!=null)Navigate(item.Url);});button.Content=content;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.Padding=new(0);button.Margin=new(0);button.Tag=new CornerRadius(7);return button;}
    private FrameworkElement CompactStory(NewsItem item)
    {
        var row=new Grid{Margin=new(0,8,0,8)};row.ColumnDefinitions.Add(new(){Width=new GridLength(48)});row.ColumnDefinitions.Add(new());
        row.Children.Add(AvatarGroup(item));
        var copy=new StackPanel();var title=UI.Text(item.Title,13);title.FontWeight=FontWeights.Medium;title.LineStackingStrategy=LineStackingStrategy.MaxHeight;title.LineHeight=18;copy.Children.Add(title);
        var sources=UI.Text(item.SourceCount>0?Localization.Format("Sources: {0}",item.SourceCount):item.Source,11,UI.Muted);sources.Margin=new(0,4,0,0);copy.Children.Add(sources);foreach(var note in item.Notes)copy.Children.Add(UI.Text(note,11,UI.Muted));Grid.SetColumn(copy,1);row.Children.Add(copy);
        return new Border{Child=StoryButton(item,row),BorderBrush=UI.Brush("#2B2F36"),BorderThickness=new(0,0,0,.5)};
    }
    private FrameworkElement AvatarGroup(NewsItem item)
    {
        var known=app.News.SourcesFor(item,app.Settings.NewsApiBaseUrl);var group=new Grid{Width=known.Length>1?40:24,Height=24,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
        Brush background=Accessibility.HighContrast?SystemColors.WindowBrush:UI.Brush("#30343D");
        if(known.Length==0){group.Children.Add(new System.Windows.Shapes.Ellipse{Width=24,Height=24,Fill=background,Stroke=Accessibility.HighContrast?SystemColors.WindowTextBrush:null,StrokeThickness=1});return group;}
        for(int index=0;index<known.Length;index++)
        {
            var source=known[index];var face=new Grid{Width=24,Height=24,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(index*16,0,0,0),UseLayoutRounding=true};UI.Named(face,source.Name);
            face.Children.Add(new System.Windows.Shapes.Ellipse{Fill=background});
            var initials=UI.Text(source.Name.Length>0?source.Name[..1].ToUpperInvariant():"•",11);initials.HorizontalAlignment=HorizontalAlignment.Center;face.Children.Add(initials);
            var image=new Image{Width=24,Height=24,Stretch=Stretch.UniformToFill,Clip=new EllipseGeometry(new Point(12,12),12,12),Tag="news-source-avatar"};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);ThemeInk.SetTone(image,Tone.Fixed);UI.Named(image,source.Name);face.Children.Add(image);
            face.Children.Add(new System.Windows.Shapes.Ellipse{Stroke=Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush("#15171B"),StrokeThickness=1.5,IsHitTestVisible=false});group.Children.Add(face);if(source.Avatar.Length>0)avatars.Add((image,source.Avatar));
        }
        return group;
    }
    private async Task LoadAvatar(Image target,string path,string address,CancellationTokenSource current)
    {
        try{var bytes=await app.News.Cover(address,path,current.Token);if(bytes!=null && current==request && IsVisible)target.Source=Decode(bytes,64);}
        catch(Exception ex) when(ex is OperationCanceledException or System.Net.Http.HttpRequestException or IOException or NotSupportedException or FormatException or System.Runtime.InteropServices.COMException){/* Keep source initials when its image is unavailable. */}
    }
}
