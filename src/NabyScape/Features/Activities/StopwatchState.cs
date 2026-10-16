using System;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace NabyScape;

public sealed class StopwatchState
{
    public double ElapsedSeconds {get;set;}
    public DateTimeOffset? UtcAnchor {get;set;}
    public bool Running {get;set;}
    private Func<DateTimeOffset> utc=()=>DateTimeOffset.UtcNow;
    private Func<long> monotonic=()=>Environment.TickCount64; // Windows uptime includes suspend time.
    private long stamp;
    private bool recovered;
    internal void UseClock(Func<DateTimeOffset> wall,Func<long> steady){utc=wall;monotonic=steady;recovered=false;}
    public void Recover()
    {
        if(recovered)return;
        ElapsedSeconds=double.IsFinite(ElapsedSeconds)?Math.Clamp(ElapsedSeconds,0,TimeSpan.MaxValue.TotalSeconds/2):0;
        if(Running && UtcAnchor.HasValue)ElapsedSeconds+=Math.Max(0,(utc()-UtcAnchor.Value).TotalSeconds);
        stamp=monotonic();recovered=true;UtcAnchor=Running?utc():null;
    }
    [JsonIgnore] public double Elapsed {get{Recover();return ElapsedSeconds+(Running?Math.Max(0,(monotonic()-stamp)/1000d):0);}}
    [JsonIgnore] public string Display {get{var seconds=Math.Floor(Elapsed);long total=(long)Math.Min(seconds,long.MaxValue/2);return total>=3600?$"{total/3600}:{total/60%60:00}:{total%60:00}":$"{total/60:00}:{total%60:00}";}}
    public void Checkpoint(){ElapsedSeconds=Elapsed;stamp=monotonic();UtcAnchor=Running?utc():null;}
    public void Toggle(){Checkpoint();Running=!Running;UtcAnchor=Running?utc():null;}
    public void Reset(){ElapsedSeconds=0;Running=false;UtcAnchor=null;stamp=monotonic();recovered=true;}
}

// The same compact control is used in the island, Settings, hover and detached views.
internal sealed class StopwatchView : StackPanel
{
    private readonly App app;
    internal int Refreshes {get;private set;}
    private readonly TextBlock digits=UI.Text("00:00",29);
    private readonly Button toggle;
    internal StopwatchView(App app)
    {
        this.app=app;Children.Add(UI.Heading("Stopwatch",13));digits.Margin=new(0,10,0,8);Typography.SetNumeralAlignment(digits,FontNumeralAlignment.Tabular);Children.Add(digits);
        toggle=UI.Button("Start",()=>{app.Session.Stopwatch.Toggle();app.SaveSession();});UI.Named(toggle,"Start, pause or resume stopwatch");
        var reset=UI.Button("Reset",()=>{app.Session.Stopwatch.Reset();app.SaveSession();});UI.Named(reset,"Reset stopwatch");
        foreach(var button in new[]{toggle,reset}){button.FontSize=11;button.Padding=new(9,4,9,4);button.MinHeight=30;button.ToolTip="Tab to select; Space or Enter to activate";}
        Children.Add(UI.Row(toggle,reset));
        IsVisibleChanged+=(_,_)=>{app.Updated-=Refresh;if(IsVisible){app.Updated+=Refresh;Refresh();}};
        Refresh();
    }
    private void Refresh(){if(Window.GetWindow(this)?.WindowState==WindowState.Minimized)return;Refreshes++;digits.Text=app.Session.Stopwatch.Display;toggle.Content=app.Session.Stopwatch.Running?"Pause":app.Session.Stopwatch.Elapsed>0?"Resume":"Start";}
}
