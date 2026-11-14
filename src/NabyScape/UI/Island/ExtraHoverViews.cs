using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class OverlayWindow
{
    private readonly Dictionary<string,TextBlock> extraPeekLabels=new();
    private FrameworkElement BuildExtraPeek(string id)
    {
        if(id=="weather")return new WeatherGlance(app);
        var page=new StackPanel();page.Children.Add(UI.Heading(PeekName(id),13));var detail=UI.Text("",id=="clock"?30:12,UI.Muted);detail.Margin=new(0,9,0,8);page.Children.Add(detail);extraPeekLabels[id]=detail;
        if(id=="volume")
        {
            var slider=new Slider{Minimum=0,Maximum=100,Height=26,Margin=new(0,4,0,4)};UI.Named(slider,"Hover output volume");bool sync=false;slider.ValueChanged+=(_,_)=>{if(!sync)app.Audio.SetVolume(slider.Value/100);};
            page.IsVisibleChanged+=(_,_)=>{if(page.IsVisible){sync=true;slider.Value=app.Audio.Volume*100;sync=false;}};page.Children.Add(slider);
        }
        else if(id=="shortcuts")
        {
            var apps=new WrapPanel();foreach(var shortcut in app.Settings.Shortcuts.Take(4)){var target=shortcut;var b=PlainPeekButton("Launch "+target.Name,()=>ShortcutService.Open(target));b.Content=UI.Text(target.Name,11);b.Padding=new(7,3,7,3);apps.Children.Add(b);}page.Children.Add(apps);
        }
        var open=PlainPeekButton("Open "+PeekName(id),OpenPeekPage);open.Content="Open "+PeekName(id);open.FontSize=10;open.Foreground=UI.Muted;open.HorizontalAlignment=HorizontalAlignment.Left;page.Children.Add(open);return page;
    }
    private void UpdateExtraPeek()
    {
        // Keep hidden previews dormant; values update when their page is selected.
        if(!extraPeekLabels.TryGetValue(peekId,out var label))return;
        string text=peekId switch
        {
            "clock"=>DateTime.Now.ToString(app.Settings.Clock24Hour?"HH:mm":"h:mm tt"),
            "volume"=>app.Audio.Available?$"{app.Audio.Volume*100:0}% · {(app.Audio.Muted?"Muted":"Output volume")}":"No output device",
            "battery"=>System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)?"Connected to power":$"{System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent*100:0}% battery",
            "shortcuts"=>"Your favorite apps, one click away",
            "power"=>"Open to choose a Windows power plan",
            "awake"=>"Keep your computer awake while you work",
            "links"=>"Remove tracking parameters from a link",
            "voice"=>app.Assistant.VoiceConnected?"Voice session active":"A dedicated space to talk with Naby",
            _=>""
        };
        Motion.Text(label,text);
    }
}

internal sealed class WeatherGlance : UserControl
{
    private readonly App app;
    private readonly Grid scene=new();
    private readonly TextBlock temperature=UI.Text("—°",34),detail=UI.Text("Choose a city in Weather",11,UI.Muted);
    private readonly WeatherArtwork art=new(){Width=84,Height=82,DecorativeMotion=false};
    private System.Threading.CancellationTokenSource? lifetime;
    private WeatherSnapshot? shown;
    private DateTimeOffset shownAt;
    private bool busy;
    internal WeatherGlance(App app)
    {
        this.app=app;var content=new Grid{Margin=new(14,8,12,8)};content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new(){Width=new GridLength(90)});var info=new StackPanel();info.Children.Add(UI.Text("Weather",10,UI.Muted));info.Children.Add(temperature);info.Children.Add(detail);content.Children.Add(info);Grid.SetColumn(art,1);content.Children.Add(art);scene.Children.Add(content);Content=scene;
        Loaded+=(_,_)=>{lifetime??=new();app.Updated-=Update;app.SettingsChanged-=Changed;app.Updated+=Update;app.SettingsChanged+=Changed;Watch(IsVisible);Changed();};
        Unloaded+=(_,_)=>{Watch(false);app.Updated-=Update;app.SettingsChanged-=Changed;lifetime?.Cancel();lifetime?.Dispose();lifetime=null;};
        IsVisibleChanged+=(_,_)=>{if(!IsLoaded)return;Watch(IsVisible);if(IsVisible)Changed();};
    }
    private void Watch(bool on){app.Weather.SetVisible(this,on);app.Weather.Changed-=ServiceChanged;if(on)app.Weather.Changed+=ServiceChanged;}
    private void ServiceChanged()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(ServiceChanged);return;}
        if(IsLoaded && IsVisible && app.Weather.Saved(app.Settings) is {} saved)Show(saved);
    }
    // The minute tick refreshes only the age label; downloads follow the app's weather schedule.
    private void Update(){if(IsVisible && shown!=null && (app.CurrentUpdate&RuntimeChange.Clock)!=0 && DateTimeOffset.UtcNow-shownAt>=TimeSpan.FromMinutes(1))Show(shown);}
    private void Show(WeatherSnapshot data)
    {
        shownAt=DateTimeOffset.UtcNow;string? stale=WeatherSchedule.StaleLabel(shownAt,data.Updated);
        Motion.Text(temperature,$"{data.Temperature:0}°");Motion.Text(detail,WeatherService.Condition(data.Code)+(stale!=null?" · "+stale:$" · H {data.High:0}° L {data.Low:0}°"));
        if(ReferenceEquals(data,shown))return;shown=data;art.Code=data.Code;art.Day=data.Day;art.ReduceMotion=true;art.InvalidateVisual();
        if(scene.Children[0] is WeatherSky old)scene.Children.Remove(old);scene.Children.Insert(0,new WeatherSky{Code=data.Code,Day=data.Day});
    }
    private async void Changed()
    {
        if(!IsVisible || lifetime==null || busy)return;string key=WeatherService.Key(app.Settings);
        if(shown!=null && shown.Key!=key){shown=null;Motion.Text(temperature,"—°");}
        if(key.Length==0){Motion.Text(detail,"Choose a city in Weather");return;}
        // Show the saved outlook at once instead of a blank glance while a due refresh runs.
        if(app.Weather.Saved(app.Settings) is {} saved)Show(saved);
        busy=true;var token=lifetime.Token;
        try
        {
            var data=await app.Weather.Get(app.Settings,false,token);if(!IsLoaded || key!=WeatherService.Key(app.Settings))return;
            Show(data);
        }
        catch(System.OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception){if(IsLoaded && shown==null)Motion.Text(detail,"Open Weather for your outlook");}
        finally{busy=false;}
    }
}
