using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace NabyScape;

internal sealed class WeatherView : UserControl
{
    private readonly App app;
    private readonly StackPanel page=new(),content=new(),location=new(),results=new();
    private readonly TextBlock city=UI.Text("",11,UI.Muted),status=UI.Text("",10,UI.Muted);
    private readonly TextBox search=UI.Input("Weather city search", "", "Search for your city");
    private readonly Button find,retry;
    private CancellationTokenSource? lifetime,searchRequest;
    private string shownKey="";
    private bool busy;
    private int revision;
    private DateTimeOffset checkedAt;
    private WeatherSnapshot? shown;
    internal WeatherView(App app,bool configure=false)
    {
        this.app=app;
        var header=new DockPanel{Margin=new(0,0,0,8)};
        var choose=Icons.Button("settings","Choose weather city and units",()=>location.Visibility=location.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible,false,28);choose.Background=Brushes.Transparent;choose.Margin=new(0);DockPanel.SetDock(choose,Dock.Right);header.Children.Add(choose);
        var title=new StackPanel();title.Children.Add(UI.Heading(configure?"Location & forecast":"Weather",18));city.Margin=new(0,3,0,0);city.TextTrimming=TextTrimming.CharacterEllipsis;city.TextWrapping=TextWrapping.NoWrap;title.Children.Add(city);header.Children.Add(title);page.Children.Add(header);
        var searchRow=new DockPanel();find=UI.Button("Search",()=>_=Search());find.Padding=new(12,6,12,6);find.Margin=new(8,0,0,0);DockPanel.SetDock(find,Dock.Right);searchRow.Children.Add(find);search.Margin=new(0);search.MaxLength=120;var searchHost=new Grid();searchHost.Children.Add(search);var searchHint=UI.Text("Search city or townâ€¦",12,UI.Muted);searchHint.Margin=new(11,0,0,0);searchHint.IsHitTestVisible=false;searchHost.Children.Add(searchHint);search.TextChanged+=(_,_)=>searchHint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;searchRow.Children.Add(searchHost);location.Children.Add(searchRow);
        search.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){_=Search();e.Handled=true;}};
        var options=new ComboBox{ItemsSource=new[]{"Celsius","Fahrenheit"},SelectedItem=app.Settings.WeatherUnits,MinHeight=30,Margin=new(0,8,0,8)};UI.Named(options,"Weather temperature units");options.SelectionChanged+=(_,_)=>{if(options.SelectedItem is string unit && app.Settings.WeatherUnits!=unit){app.Settings.WeatherUnits=unit;app.ChangeSettings();}};location.Children.Add(options);
        var list=UI.Scroll(results);list.Tag="panel-content-scroll";list.MaxHeight=150;location.Children.Add(list);location.Margin=new(0,0,0,8);page.Children.Add(location);page.Children.Add(content);
        var footer=new DockPanel{Margin=new(0,6,0,0)};retry=Icons.Button("reset","Refresh weather",()=>_=Reload(true),false,26);retry.Margin=new(0);retry.Background=Brushes.Transparent;DockPanel.SetDock(retry,Dock.Right);footer.Children.Add(retry);footer.Children.Add(status);page.Children.Add(footer);
        var credit=UI.Button("Open-Meteo",()=>ShortcutService.Open(new(){Target="https://open-meteo.com/"}));credit.FontSize=9;credit.Foreground=UI.Muted;credit.Background=Brushes.Transparent;credit.BorderThickness=new(0);credit.Padding=new(4,0,4,0);credit.MinHeight=20;credit.Margin=new(0);DockPanel.SetDock(credit,Dock.Right);footer.Children.Insert(1,credit);
        Content=page;IsVisibleChanged+=(_,_)=>{if(IsLoaded && IsVisible)_=Reload();};Loaded+=LoadedView;Unloaded+=UnloadedView;location.Visibility=configure || WeatherService.Key(app.Settings).Length==0?Visibility.Visible:Visibility.Collapsed;
        city.Text=app.Settings.WeatherCity.Length>0?app.Settings.WeatherCity:"Your daily outlook";Render(null);
    }
    internal void OpenLocationEditor(){location.Visibility=Visibility.Visible;}
    private void LoadedView(object sender,RoutedEventArgs e)
    {
        lifetime=new();app.Updated+=Heartbeat;app.SettingsChanged+=Preferences;_=Reload();
    }
    private void UnloadedView(object sender,RoutedEventArgs e)
    {
        app.Updated-=Heartbeat;app.SettingsChanged-=Preferences;lifetime?.Cancel();lifetime?.Dispose();lifetime=null;searchRequest?.Cancel();searchRequest?.Dispose();searchRequest=null;revision++;busy=false;
    }
    private void Preferences()
    {
        foreach(var art in Artworks(page)){art.ReduceMotion=app.Settings.ReduceMotion;art.RefreshMotion();}
        if(IsVisible && shownKey!=WeatherService.Key(app.Settings))_=Reload();
    }
    private static System.Collections.Generic.IEnumerable<WeatherArtwork> Artworks(DependencyObject node)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()){if(child is WeatherArtwork art)yield return art;foreach(var nested in Artworks(child))yield return nested;}
    }
    private void Heartbeat()
    {
        if(!IsVisible || (app.CurrentUpdate&RuntimeChange.Clock)==0 || Window.GetWindow(this)?.WindowState==WindowState.Minimized || DateTimeOffset.UtcNow-checkedAt<TimeSpan.FromMinutes(1))return;
        checkedAt=DateTimeOffset.UtcNow;
        if(!busy && DateTimeOffset.UtcNow-app.Weather.LastAttempt>TimeSpan.FromMinutes(3) && (shown==null || DateTimeOffset.UtcNow-shown.Updated>TimeSpan.FromMinutes(20)))_=Reload();
    }
    private async Task Search()
    {
        if(lifetime==null || search.Text.Trim().Length<2){status.Text="Enter at least two letters of your city.";return;}
        searchRequest?.Cancel();searchRequest?.Dispose();searchRequest=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var request=searchRequest;find.IsEnabled=false;results.Children.Clear();status.Text="Looking for citiesâ€¦";
        try
        {
            var places=await app.Weather.Search(search.Text,request.Token);if(!IsLoaded || request!=searchRequest)return;
            foreach(var place in places)
            {
                var button=UI.Button(place.Name,()=>{app.Settings.WeatherCity=place.Name;app.Settings.WeatherLatitude=place.Latitude;app.Settings.WeatherLongitude=place.Longitude;location.Visibility=Visibility.Collapsed;app.ChangeSettings();});button.FontSize=11;button.Margin=new(0,2,0,2);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Content=new TextBlock{Text=place.Name,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=Brushes.White};results.Children.Add(button);
            }
            status.Text=places.Length==0?"No cities found. Try a nearby city or include the country.":"Choose your city above.";
        }
        catch(OperationCanceledException){}
        catch(Exception){if(IsLoaded)status.Text="City search is unavailable. Please try again.";}
        finally{if(IsLoaded && request==searchRequest)find.IsEnabled=true;}
    }
    private async Task Reload(bool force=false)
    {
        if(lifetime==null)return;string key=WeatherService.Key(app.Settings);int version=++revision;shownKey=key;city.Text=app.Settings.WeatherCity.Length>0?app.Settings.WeatherCity:"Your daily outlook";
        if(key.Length==0){shown=null;busy=false;Render(null);status.Text="Choose a city to see today's weather.";retry.IsEnabled=false;return;}
        if(shown?.Key!=key){shown=app.Weather.Cached?.Key==key?app.Weather.Cached:null;Render(shown);}
        busy=true;retry.IsEnabled=false;status.Text="Updating weatherâ€¦";
        try
        {
            var snapshot=await app.Weather.Get(app.Settings,force,lifetime.Token);if(!IsLoaded || version!=revision)return;
            shown=snapshot;Render(snapshot);status.Text="Updated "+snapshot.Updated.ToLocalTime().ToString("HH:mm");
        }
        catch(OperationCanceledException){}
        catch(Exception){if(IsLoaded && version==revision)status.Text=shown==null?"Weather unavailable. Refresh to try again.":"Saved outlook Â· connection unavailable";}
        finally{if(IsLoaded && version==revision){busy=false;retry.IsEnabled=true;}}
    }
    private void Render(WeatherSnapshot? data)
    {
        content.Children.Clear();bool day=data?.Day??true;int code=data?.Code??0;
        if(data==null)
        {var hint=UI.Text("Your daily outlook starts with a city. Search above to get started.",11,UI.Muted);hint.Margin=new(0,8,0,8);content.Children.Add(hint);return;}
        var scene=new Grid();scene.Children.Add(new WeatherSky{Day=day,Code=code});var panel=new StackPanel{Margin=new(16,10,14,10)};
        var hero=new Grid();hero.ColumnDefinitions.Add(new());hero.ColumnDefinitions.Add(new(){Width=new GridLength(108)});
        var readings=new StackPanel();var today=UI.Text(day?"TODAY · DAYLIGHT":"TONIGHT",9,UI.Brush("#B6C7D9"));readings.Children.Add(today);
        var temperature=UI.Text($"{data.Temperature:0}°",44);temperature.FontWeight=FontWeights.Light;temperature.TextWrapping=TextWrapping.NoWrap;readings.Children.Add(temperature);
        readings.Children.Add(UI.Text(WeatherService.Condition(code),12));var range=UI.Text($"Feels {data.FeelsLike:0}°  ·  H {data.High:0}°  L {data.Low:0}°",10,UI.Brush("#B7CADC"));range.Margin=new(0,4,0,0);readings.Children.Add(range);hero.Children.Add(readings);
        var art=new WeatherArtwork{Code=code,Day=day,Width=104,Height=92,ReduceMotion=app.Settings.ReduceMotion};Grid.SetColumn(art,1);hero.Children.Add(art);panel.Children.Add(hero);
        var metrics=new System.Windows.Controls.Primitives.UniformGrid{Columns=3,Rows=1,Margin=new(0,10,0,0)};
        void Metric(string label,string value){var p=new StackPanel{Orientation=Orientation.Horizontal};p.Children.Add(UI.Text(label+" ",9,UI.Brush("#97ADC2")));p.Children.Add(UI.Text(value,10,UI.Brush("#DFE8EF")));metrics.Children.Add(p);}
        Metric("Rain",$"{data.RainChance:0}%");Metric("Humidity",$"{data.Humidity:0}%");Metric("Wind",$"{data.Wind:0} km/h");panel.Children.Add(metrics);scene.Children.Add(panel);
        content.Children.Add(new Border{CornerRadius=new(16),BorderBrush=UI.Brush(day?"#3C5268":"#354364"),BorderThickness=new(.7),Child=scene});
        var forecast=new Grid{Margin=new(12,8,12,6)};forecast.RowDefinitions.Add(new());forecast.RowDefinitions.Add(new(){Height=new GridLength(16)});var hourly=new System.Windows.Controls.Primitives.UniformGrid{Columns=Math.Max(1,data.Hours.Count),Rows=1};
        foreach(var hour in data.Hours)
        {
            var column=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};column.Children.Add(UI.Text(hour.Time,9,UI.Muted));column.Children.Add(new WeatherArtwork{Code=hour.Code,Day=hour.Day,Width=25,Height=22,Margin=new(0,3,0,1),ReduceMotion=true,DecorativeMotion=false});var value=UI.Text($"{hour.Temperature:0}°",11);value.HorizontalAlignment=HorizontalAlignment.Center;column.Children.Add(value);column.ToolTip=$"{WeatherService.Condition(hour.Code)} · {hour.RainChance:0}% rain";hourly.Children.Add(column);
        }
        forecast.Children.Add(hourly);var trend=new WeatherTrend{Height=16};Grid.SetRow(trend,1);forecast.Children.Add(trend);
        string Clock(string value)=>value.Length>=16?value.Substring(11,5):"—";
        content.Children.Add(new Border{Child=forecast,Background=UI.Brush("#111820"),BorderBrush=UI.Brush("#273440"),BorderThickness=new(.6),CornerRadius=new(12),Margin=new(0,8,0,0),ToolTip="Next hours · Sunrise "+Clock(data.Sunrise)+" · Sunset "+Clock(data.Sunset)});
    }
}
