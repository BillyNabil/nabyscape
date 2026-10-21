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
    public List<WeatherHour> Hours {get;set;}=new();
}
internal sealed class WeatherService : IDisposable
{
    private static readonly HttpClient client=new(){Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=1024*1024};
    private readonly Store store;
    private readonly CancellationTokenSource shutdown=new();
    private Task<WeatherSnapshot>? pending;
    private string pendingKey="";
    internal WeatherSnapshot? Cached {get;private set;}
    internal DateTimeOffset LastAttempt {get;private set;}
    internal WeatherService(Store store){this.store=store;Cached=store.Load<WeatherSnapshot>("weather");}
    internal static string Key(Settings s)=>s.WeatherLatitude is {} lat && s.WeatherLongitude is {} lon?FormattableString.Invariant($"{lat:F5},{lon:F5}|{s.WeatherUnits}"):"";
    internal async Task<WeatherPlace[]> Search(string city,CancellationToken token)
    {
        city=city.Trim();if(city.Length<2)return Array.Empty<WeatherPlace>();if(city.Length>120)city=city[..120];
        using var doc=await Fetch("https://geocoding-api.open-meteo.com/v1/search?name="+Uri.EscapeDataString(city)+"&count=8&language=en&format=json",token);
        if(!doc.RootElement.TryGetProperty("results",out var results))return Array.Empty<WeatherPlace>();
        return results.EnumerateArray().Select(place=>new WeatherPlace(string.Join(", ",new[]{Text(place,"name"),Text(place,"admin1"),Text(place,"country")}.Where(v=>v.Length>0).Distinct()),Number(place,"latitude"),Number(place,"longitude"))).Where(p=>Math.Abs(p.Latitude)<=90 && Math.Abs(p.Longitude)<=180).ToArray();
    }
    internal Task<WeatherSnapshot> Get(Settings s,bool force,CancellationToken token)
    {
        string key=Key(s);if(key.Length==0)throw new InvalidOperationException("Choose a city first.");
        if(!force && Cached is {} saved && saved.Key==key && DateTimeOffset.UtcNow-saved.Updated<TimeSpan.FromMinutes(20))return Task.FromResult(saved);
        if(pending is {IsCompleted:false} && pendingKey==key)return pending.WaitAsync(token);
        pendingKey=key;LastAttempt=DateTimeOffset.UtcNow;pending=Download(s.WeatherLatitude!.Value,s.WeatherLongitude!.Value,s.WeatherUnits,key);return pending.WaitAsync(token);
    }
    private async Task<WeatherSnapshot> Download(double lat,double lon,string units,string key)
    {
        string coordinates=FormattableString.Invariant($"latitude={lat}&longitude={lon}");
        string url="https://api.open-meteo.com/v1/forecast?"+coordinates+"&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m&hourly=temperature_2m,weather_code,is_day,precipitation_probability&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset&timezone=auto&forecast_days=2&temperature_unit="+(units=="Fahrenheit"?"fahrenheit":"celsius");
        using var doc=await Fetch(url,shutdown.Token);var root=doc.RootElement;var current=root.GetProperty("current");var daily=root.GetProperty("daily");var hourly=root.GetProperty("hourly");
        var snapshot=new WeatherSnapshot{Key=key,Updated=DateTimeOffset.UtcNow,Temperature=Number(current,"temperature_2m"),FeelsLike=Number(current,"apparent_temperature"),Wind=Number(current,"wind_speed_10m"),Humidity=Number(current,"relative_humidity_2m"),Code=(int)Number(current,"weather_code"),Day=Number(current,"is_day")==1,High=At(daily,"temperature_2m_max",0),Low=At(daily,"temperature_2m_min",0),RainChance=At(daily,"precipitation_probability_max",0),Sunrise=daily.GetProperty("sunrise")[0].GetString()??"",Sunset=daily.GetProperty("sunset")[0].GetString()??""};
        string now=Text(current,"time");var times=hourly.GetProperty("time");
        for(int i=0;i<times.GetArrayLength() && snapshot.Hours.Count<6;i++)
        {
            string time=times[i].GetString()??"";if(string.CompareOrdinal(time,now)<0)continue;
            snapshot.Hours.Add(new(){Time=time.Length>=16?time.Substring(11,5):time,Temperature=At(hourly,"temperature_2m",i),Code=(int)At(hourly,"weather_code",i),Day=At(hourly,"is_day",i)==1,RainChance=At(hourly,"precipitation_probability",i)});
        }
        if(!shutdown.IsCancellationRequested && pendingKey==key){Cached=snapshot;try{store.Save("weather",snapshot);}catch(Exception ex){store.Log(ex);}}return snapshot;
    }
    private static async Task<JsonDocument> Fetch(string url,CancellationToken token)
    {
        using var response=await client.GetAsync(url,token);response.EnsureSuccessStatusCode();return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    private static double Number(JsonElement value,string name)=>value.TryGetProperty(name,out var field) && field.ValueKind==JsonValueKind.Number && field.TryGetDouble(out var n) && double.IsFinite(n)?n:0;
    private static double At(JsonElement value,string name,int index)=>value.TryGetProperty(name,out var field) && field.ValueKind==JsonValueKind.Array && index<field.GetArrayLength() && field[index].ValueKind==JsonValueKind.Number && field[index].TryGetDouble(out var n) && double.IsFinite(n)?n:0;
    private static string Text(JsonElement value,string name)=>value.TryGetProperty(name,out var field) && field.ValueKind==JsonValueKind.String?field.GetString()??"":"";
    internal static string Condition(int code)=>code switch{0=>"Clear skies",1=>"Mostly clear",2=>"Partly cloudy",3=>"Cloudy",45 or 48=>"Foggy",>=51 and <=57=>"Drizzle",>=61 and <=67=>"Rain",>=71 and <=77=>"Snow",>=80 and <=82=>"Rain showers",85 or 86=>"Snow showers",>=95=>"Thunderstorms",_=>"Weather"};
    public void Dispose(){shutdown.Cancel();shutdown.Dispose();}
}
