using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;

internal sealed record WeatherPlace(string Name,double Latitude,double Longitude);
internal sealed class WeatherHour
{
    public string Time {get;set;}="";
    // Location-local "yyyy-MM-ddTHH:mm"; empty in outlooks saved before 2.27.
    public string Stamp {get;set;}="";
    public double Temperature {get;set;}
    public int Code {get;set;}
    public bool Day {get;set;}
    public double RainChance {get;set;}
}
internal sealed class WeatherSnapshot
{
    public string Key {get;set;}="";
    public DateTimeOffset Updated {get;set;}
    public double Temperature {get;set;}
    public double FeelsLike {get;set;}
    public double Wind {get;set;}
    public double Humidity {get;set;}
    public double High {get;set;}
    public double Low {get;set;}
    public double RainChance {get;set;}
    public int Code {get;set;}
    public bool Day {get;set;}
    public string Sunrise {get;set;}="";
    public string Sunset {get;set;}="";
    public int UtcOffsetSeconds {get;set;}
    public List<WeatherHour> Hours {get;set;}=new();
}
internal sealed class WeatherService : IDisposable
{
    private static readonly HttpClient client=new(){Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=1024*1024};
    private readonly Store store;
    private readonly Func<string,CancellationToken,Task<string>> fetch;
    private readonly CancellationTokenSource shutdown=new();
    private readonly HashSet<object> visible=new();
    private CancellationTokenSource? download;
    private Task<WeatherSnapshot>? pending;
    private string pendingKey="";
    private bool updating;
    private DateTimeOffset? nudgeAt;
    internal WeatherSnapshot? Cached {get;private set;}
    internal DateTimeOffset LastAttempt {get;private set;}
    // Consecutive failed downloads for the current location; drives the retry backoff.
    internal int Failures {get;private set;}
    internal bool Updating=>updating;
    internal bool AnyVisible=>visible.Count>0;
    internal Func<DateTimeOffset> Now {get;set;}=()=>DateTimeOffset.UtcNow;
    // Raised on the calling (UI) thread when a download starts or finishes.
    internal event Action? Changed;
    internal WeatherService(Store store,Func<string,CancellationToken,Task<string>>? fetch=null){this.store=store;this.fetch=fetch??FetchText;Cached=store.Load<WeatherSnapshot>("weather");}
    internal static string Key(Settings s)=>s.WeatherLatitude is {} lat && s.WeatherLongitude is {} lon?FormattableString.Invariant($"{lat:F5},{lon:F5}|{s.WeatherUnits}"):"";
    internal WeatherSnapshot? Saved(Settings s){string key=Key(s);return key.Length>0 && Cached?.Key==key?Cached:null;}
    internal bool FailedFor(Settings s)=>Failures>0 && pendingKey==Key(s);
    internal bool UpdatingFor(Settings s)=>updating && pendingKey==Key(s);
    internal void SetVisible(object owner,bool shown){if(shown)visible.Add(owner);else visible.Remove(owner);}
    // Resume from sleep, unlock or a regained network: check again shortly, once, without the failure backoff.
    internal void Nudge(DateTimeOffset now)=>nudgeAt=now+TimeSpan.FromSeconds(10);
    internal WeatherPlan Plan(Settings s,WeatherDemand demand,DateTimeOffset now,bool away=false)
    {
        bool same=pendingKey==Key(s);
        return new(demand,Saved(s)?.Updated,same?LastAttempt:default,same?Failures:0,updating,away,nudgeAt is {} at && now>=at);
    }
    // Called from the app clock. Downloads only while weather is in use and the saved outlook is due.
    internal bool Tick(Settings s,WeatherDemand demand,DateTimeOffset now,bool away,Func<bool> online)
    {
        if(Key(s).Length==0){nudgeAt=null;return false;}
        var plan=Plan(s,demand,now,away);if(plan.Nudged)nudgeAt=null;
        if(!WeatherSchedule.Due(plan,now) || !online())return false;
        _=Refresh(s);return true;
    }
    private async Task Refresh(Settings s){try{await Start(s,Key(s));}catch(Exception){}}
    internal async Task<WeatherPlace[]> Search(string city,CancellationToken token)
    {
        city=city.Trim();if(city.Length<2)return Array.Empty<WeatherPlace>();if(city.Length>120)city=city[..120];
        using var doc=JsonDocument.Parse(await fetch("https://geocoding-api.open-meteo.com/v1/search?name="+Uri.EscapeDataString(city)+"&count=8&language=en&format=json",token));
        if(!doc.RootElement.TryGetProperty("results",out var results))return Array.Empty<WeatherPlace>();
        return results.EnumerateArray().Select(place=>new WeatherPlace(string.Join(", ",new[]{Text(place,"name"),Text(place,"admin1"),Text(place,"country")}.Where(v=>v.Length>0).Distinct()),Number(place,"latitude"),Number(place,"longitude"))).Where(p=>Math.Abs(p.Latitude)<=90 && Math.Abs(p.Longitude)<=180).ToArray();
    }
    // A view leaving early never cancels the shared download: it finishes in the background (15 s limit)
    // and is saved, so the next glance is current. Only a different location or app exit cancels it.
    internal async Task<WeatherSnapshot> Get(Settings s,bool force,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();string key=Key(s);if(key.Length==0)throw new InvalidOperationException("Choose a city first.");
        if(updating && pendingKey==key && pending!=null)return await pending.WaitAsync(token);
        var now=Now();var saved=Saved(s);
        if(!force && saved!=null && !WeatherSchedule.Due(Plan(s,WeatherDemand.Visible,now),now))return saved;
        if(!force && saved==null && pendingKey==key && WeatherSchedule.Waiting(now,LastAttempt,Failures))throw new HttpRequestException("Weather is unavailable right now.");
        return await Start(s,key).WaitAsync(token);
    }
    private Task<WeatherSnapshot> Start(Settings s,string key)
    {
        if(updating && pendingKey==key && pending!=null)return pending;
        if(shutdown.IsCancellationRequested)return Task.FromCanceled<WeatherSnapshot>(new CancellationToken(true));
        download?.Cancel();if(pendingKey!=key)Failures=0;
        var cancellation=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);download=cancellation;
        pendingKey=key;LastAttempt=Now();updating=true;
        pending=Download(s.WeatherLatitude!.Value,s.WeatherLongitude!.Value,s.WeatherUnits,key,cancellation);
        Changed?.Invoke();return pending;
    }
    private async Task<WeatherSnapshot> Download(double lat,double lon,string units,string key,CancellationTokenSource cancellation)
    {
        bool Current()=>ReferenceEquals(download,cancellation);
        try
        {
            string coordinates=FormattableString.Invariant($"latitude={lat}&longitude={lon}");
            string url="https://api.open-meteo.com/v1/forecast?"+coordinates+"&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m&hourly=temperature_2m,weather_code,is_day,precipitation_probability&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset&timezone=auto&forecast_days=2&temperature_unit="+(units=="Fahrenheit"?"fahrenheit":"celsius");
            var snapshot=Parse(await fetch(url,cancellation.Token),key,Now());
            if(!cancellation.IsCancellationRequested && Current()){Cached=snapshot;Failures=0;try{store.Save("weather",snapshot);}catch(Exception ex){store.Log(ex);}}
            return snapshot;
        }
        // An HttpClient timeout also surfaces as cancellation; only our own cancellation is not a failure.
        catch(Exception) when(!cancellation.IsCancellationRequested){if(Current())Failures++;throw;}
        finally
        {
            if(Current()){download=null;updating=false;}
            cancellation.Dispose();if(!shutdown.IsCancellationRequested)Changed?.Invoke();
        }
    }
    internal static WeatherSnapshot Parse(string json,string key,DateTimeOffset updated)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;var current=root.GetProperty("current");var daily=root.GetProperty("daily");var hourly=root.GetProperty("hourly");
        var snapshot=new WeatherSnapshot{Key=key,Updated=updated,UtcOffsetSeconds=(int)Math.Clamp(Number(root,"utc_offset_seconds"),-18*3600,18*3600),Temperature=Number(current,"temperature_2m"),FeelsLike=Number(current,"apparent_temperature"),Wind=Number(current,"wind_speed_10m"),Humidity=Number(current,"relative_humidity_2m"),Code=(int)Number(current,"weather_code"),Day=Number(current,"is_day")==1,High=At(daily,"temperature_2m_max",0),Low=At(daily,"temperature_2m_min",0),RainChance=At(daily,"precipitation_probability_max",0),Sunrise=daily.GetProperty("sunrise")[0].GetString()??"",Sunset=daily.GetProperty("sunset")[0].GetString()??""};
        // Keep the coming day and a half so a saved outlook can still show "next hours" later.
        string now=Text(current,"time");var times=hourly.GetProperty("time");
        for(int i=0;i<times.GetArrayLength() && snapshot.Hours.Count<36;i++)
        {
            string time=times[i].GetString()??"";if(string.CompareOrdinal(time,now)<0)continue;
            snapshot.Hours.Add(new(){Time=time.Length>=16?time.Substring(11,5):time,Stamp=WeatherSchedule.LocalStamp(time)!=null?time:"",Temperature=At(hourly,"temperature_2m",i),Code=(int)At(hourly,"weather_code",i),Day=At(hourly,"is_day",i)==1,RainChance=At(hourly,"precipitation_probability",i)});
        }
        return snapshot;
    }
    private static async Task<string> FetchText(string url,CancellationToken token)
    {
        using var response=await client.GetAsync(url,token);response.EnsureSuccessStatusCode();return await response.Content.ReadAsStringAsync(token);
    }
    private static double Number(JsonElement value,string name)=>value.TryGetProperty(name,out var field) && field.ValueKind==JsonValueKind.Number && field.TryGetDouble(out var n) && double.IsFinite(n)?n:0;
    private static double At(JsonElement value,string name,int index)=>value.TryGetProperty(name,out var field) && field.ValueKind==JsonValueKind.Array && index<field.GetArrayLength() && field[index].ValueKind==JsonValueKind.Number && field[index].TryGetDouble(out var n) && double.IsFinite(n)?n:0;
    private static string Text(JsonElement value,string name)=>value.TryGetProperty(name,out var field) && field.ValueKind==JsonValueKind.String?field.GetString()??"":"";
    internal static string Condition(int code)=>code switch{0=>"Clear skies",1=>"Mostly clear",2=>"Partly cloudy",3=>"Cloudy",45 or 48=>"Foggy",>=51 and <=57=>"Drizzle",>=61 and <=67=>"Rain",>=71 and <=77=>"Snow",>=80 and <=82=>"Rain showers",85 or 86=>"Snow showers",>=95=>"Thunderstorms",_=>"Weather"};
    public void Dispose(){if(shutdown.IsCancellationRequested)return;shutdown.Cancel();visible.Clear();}
}
